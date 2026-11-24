using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lista paginada dos sistemas de RPG locais (Application.persistentDataPath/sistemas), para a
/// aba "Sistemas" da tela inicial. Só lê e exibe — criação/edição continua em SystemManager.
/// </summary>
public class SistemasListManager : MonoBehaviour
{
    private const string SistemasFolderName = "sistemas";
    private const string MetadadosFileName = "regras_jogando.json";

    [Header("UI")]
    [SerializeField] private Transform systemsContent;
    [SerializeField] private GameObject systemItemPrefab;

    [Header("Paginação")]
    [SerializeField] private IndicadorPaginasUI indicadorPaginas;
    [SerializeField, Min(1)] private int itensPorPagina = 9;
    [SerializeField] private Button botaoAnterior;
    [SerializeField] private Button botaoProximo;

    [Header("Navegação")]
    [SerializeField] private ToggleObjects toggleObjects;
    [Tooltip("Tela onde os detalhes do sistema selecionado são exibidos (SystemManager vive nela).")]
    [SerializeField] private GameObject telaSistemas;
    [SerializeField] private SystemManager systemManager;

    // Disparado quando o usuário clica num sistema da lista.
    public event Action<RPGSystemData> OnSistemaClicado;

    private readonly List<RPGSystemData> systems = new List<RPGSystemData>();
    private int paginaAtual;

    private string SistemasRootPath => Path.Combine(Application.persistentDataPath, SistemasFolderName);

    private void Awake()
    {
        if (toggleObjects == null)
            toggleObjects = FindFirstObjectByType<ToggleObjects>();

        if (systemManager == null)
            systemManager = FindFirstObjectByType<SystemManager>();
    }

    private void OnEnable()
    {
        botaoAnterior?.onClick.AddListener(PaginaAnterior);
        botaoProximo?.onClick.AddListener(ProximaPagina);

        RecarregarLista();
    }

    private void OnDisable()
    {
        botaoAnterior?.onClick.RemoveListener(PaginaAnterior);
        botaoProximo?.onClick.RemoveListener(ProximaPagina);
    }

    /// <summary>Reescaneia as subpastas e recria os itens de UI a partir da primeira página.</summary>
    public void RecarregarLista()
    {
        systems.Clear();
        CarregarSistemasDoDisco();

        VTTLog.System($"[SistemasListManager] RecarregarLista — {systems.Count} sistema(s) encontrado(s).");
        indicadorPaginas?.ConfigurarPaginas(systems.Count, itensPorPagina);
        IrParaPagina(0);
    }

    // Ver mesmo raciocínio em FichaLoaderUI.cs: só a navegação explícita por botão anima com
    // slide — trocas de página vindas da carga inicial (RecarregarLista) continuam instantâneas.
    private bool proximaNavegacaoDeveAnimar;
    private int  direcaoProximaNavegacao = 1;
    private Coroutine transicaoPaginaCoroutine;

    public void IrParaPagina(int pagina)
    {
        int totalPaginas = Mathf.Max(1, Mathf.CeilToInt((float)systems.Count / itensPorPagina));
        int novaPagina = Mathf.Clamp(pagina, 0, totalPaginas - 1);
        bool animar = proximaNavegacaoDeveAnimar && novaPagina != paginaAtual;
        int  direcao = direcaoProximaNavegacao;
        proximaNavegacaoDeveAnimar = false;

        paginaAtual = novaPagina;

        indicadorPaginas?.AtualizarPaginaAtiva(paginaAtual);

        if (transicaoPaginaCoroutine != null) StopCoroutine(transicaoPaginaCoroutine);

        if (animar && systemsContent is RectTransform rt)
            transicaoPaginaCoroutine = StartCoroutine(TransicaoDeslizantePagina.Executar(rt, direcao, RenderizarPagina));
        else
            RenderizarPagina();
    }

    public void ProximaPagina()
    {
        proximaNavegacaoDeveAnimar = true;
        direcaoProximaNavegacao = 1;
        IrParaPagina(paginaAtual + 1);
    }

    public void PaginaAnterior()
    {
        proximaNavegacaoDeveAnimar = true;
        direcaoProximaNavegacao = -1;
        IrParaPagina(paginaAtual - 1);
    }

    private void RenderizarPagina()
    {
        if (systemsContent == null)
        {
            VTTLog.SystemWarn("[SistemasListManager] systemsContent não definido no Inspector.");
            return;
        }

        if (systemItemPrefab == null)
        {
            VTTLog.SystemWarn("[SistemasListManager] systemItemPrefab não definido no Inspector.");
            return;
        }

        for (int i = systemsContent.childCount - 1; i >= 0; i--)
            Destroy(systemsContent.GetChild(i).gameObject);

        int inicio = paginaAtual * itensPorPagina;
        int fim = Mathf.Min(inicio + itensPorPagina, systems.Count);

        for (int i = inicio; i < fim; i++)
        {
            RPGSystemData data = systems[i];
            GameObject itemGo = Instantiate(systemItemPrefab, systemsContent, false);
            itemGo.SetActive(true);

            SystemUIItem uiItem = itemGo.GetComponent<SystemUIItem>();
            if (uiItem == null)
            {
                VTTLog.SystemWarn("[SistemasListManager] Prefab instanciado não possui componente SystemUIItem.");
                continue;
            }

            uiItem.Setup(data, OnClicarSistema, isSelected: false);
        }
    }

    private void OnClicarSistema(RPGSystemData data)
    {
        if (data == null) return;

        toggleObjects?.NavegarParaTela(telaSistemas);
        systemManager?.SelecionarSistemaPorPasta(data.folderName);
        OnSistemaClicado?.Invoke(data);
    }

    private void CarregarSistemasDoDisco()
    {
        if (!Directory.Exists(SistemasRootPath))
        {
            VTTLog.SystemWarn($"[SistemasListManager] Pasta raiz não existe: {SistemasRootPath}");
            return;
        }

        string[] subpastas = Directory.GetDirectories(SistemasRootPath);
        Array.Sort(subpastas, StringComparer.OrdinalIgnoreCase);

        foreach (string pasta in subpastas)
        {
            string folderName = Path.GetFileName(pasta);
            SistemaMetadados meta = LerMetadadosSistema(pasta);

            RPGSystemData data = new RPGSystemData
            {
                folderName = folderName,
                systemName = !string.IsNullOrWhiteSpace(meta?.nome) ? meta.nome : FormatarNomeExibicao(folderName),
                description = LerDescricaoSistema(pasta),
                descricaoDetalhe = !string.IsNullOrWhiteSpace(meta?.descricao) ? meta.descricao : "",
                icon = CarregarTokenSistema(pasta),
                isEditable = true
            };

            systems.Add(data);
        }
    }

    // Mesmo formato aceito por SystemManager: objeto { "nome", "descricao" } ou array de regras.
    private SistemaMetadados LerMetadadosSistema(string pastaSistema)
    {
        string[] arquivos = Directory.GetFiles(pastaSistema, MetadadosFileName, SearchOption.AllDirectories);
        if (arquivos.Length == 0) return null;

        string json;
        try { json = File.ReadAllText(arquivos[0]); }
        catch (Exception e)
        {
            VTTLog.SystemWarn($"[SistemasListManager] Falha ao ler '{arquivos[0]}': {e.Message}");
            return null;
        }

        if (json.TrimStart().StartsWith("{"))
        {
            SistemaMetadados meta = JsonUtility.FromJson<SistemaMetadados>(json);
            return string.IsNullOrWhiteSpace(meta?.nome) ? null : meta;
        }

        if (json.TrimStart().StartsWith("["))
        {
            RegraJogandoItem[] regras = JsonHelper.FromJson<RegraJogandoItem>(json);
            if (regras == null || regras.Length == 0) return null;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (RegraJogandoItem regra in regras)
                AppendRegraItem(sb, regra);

            return new SistemaMetadados { nome = "", descricao = sb.ToString().TrimEnd() };
        }

        return null;
    }

    private static void AppendRegraItem(System.Text.StringBuilder sb, RegraJogandoItem regra)
    {
        if (!string.IsNullOrWhiteSpace(regra.topico)) sb.AppendLine(regra.topico);
        if (!string.IsNullOrWhiteSpace(regra.descricao)) sb.AppendLine(regra.descricao);

        if (regra.subtopicos != null)
            foreach (SubtopicoRegra sub in regra.subtopicos)
                AppendSubtopicoItem(sb, sub);

        sb.AppendLine();
    }

    private static void AppendSubtopicoItem(System.Text.StringBuilder sb, SubtopicoRegra sub)
    {
        if (!string.IsNullOrWhiteSpace(sub.topico)) sb.AppendLine(sub.topico);
        if (!string.IsNullOrWhiteSpace(sub.descricao)) sb.AppendLine(sub.descricao);

        if (sub.subtopicos != null)
        {
            foreach (PassoRegra passo in sub.subtopicos)
            {
                if (!string.IsNullOrWhiteSpace(passo.nome)) sb.AppendLine(passo.nome);
                if (!string.IsNullOrWhiteSpace(passo.descricao)) sb.AppendLine(passo.descricao);
            }
        }
    }

    private static string FormatarNomeExibicao(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return "Sistema sem nome";

        string nome = folderName.Replace('-', ' ').Replace('_', ' ').Trim();
        return char.ToUpper(nome[0]) + nome.Substring(1);
    }

    // Lê regras/Descricao.xml (ex: <disclaimer><text>...</text></disclaimer>). Aceita qualquer
    // nome de elemento raiz — usa <text> se existir, senão cai no texto puro da raiz.
    private static string LerDescricaoSistema(string pastaSistema)
    {
        string caminho = Path.Combine(pastaSistema, "regras", "Descricao.xml");
        if (!File.Exists(caminho))
            return "";

        try
        {
            XDocument doc = XDocument.Parse(File.ReadAllText(caminho, System.Text.Encoding.UTF8));
            XElement root = doc.Root;
            string texto = root?.Element("text")?.Value ?? root?.Value;
            return string.IsNullOrWhiteSpace(texto) ? "" : texto.Trim();
        }
        catch (Exception e)
        {
            VTTLog.SystemWarn($"[SistemasListManager] Falha ao ler descrição do sistema '{pastaSistema}': {e.Message}");
            return "";
        }
    }

    // Procura um arquivo "*-token.*" (jpg/png/etc.) direto na raiz da pasta do sistema.
    private static Sprite CarregarTokenSistema(string pastaSistema)
    {
        string[] candidatos = Directory.GetFiles(pastaSistema, "*-token.*", SearchOption.TopDirectoryOnly);
        if (candidatos.Length == 0)
            return null;

        try
        {
            byte[] bytes = File.ReadAllBytes(candidatos[0]);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }

            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
        catch (Exception e)
        {
            VTTLog.SystemWarn($"[SistemasListManager] Falha ao carregar token do sistema '{pastaSistema}': {e.Message}");
            return null;
        }
    }
}
