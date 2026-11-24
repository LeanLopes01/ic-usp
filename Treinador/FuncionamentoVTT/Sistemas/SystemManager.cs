using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum TipoFerramentaSistema
{
    CriarNovoSistema,
    VisualizarSistema,
    EditarSistema
}

[Serializable]
public class FerramentaSistemaUI
{
    [Header("Base")]
    public string nomeFerramenta;
    public TipoFerramentaSistema tipoFerramenta = TipoFerramentaSistema.VisualizarSistema;

    [Header("Referências Fixas")]
    public Button botao;
    public TMP_Text texto;
    public Image imagemBotao;

    [Header("Regras")]
    public bool requerSistemaSelecionado = true;

    [Header("Visual")]
    [Range(0f, 1f)] public float alphaHabilitado = 1f;
    [Range(0f, 1f)] public float alphaDesabilitado = 0.3f;
}

/// <summary>
/// Gerencia os sistemas de RPG locais em Application.persistentDataPath/sistemas.
/// </summary>
public class SystemManager : MonoBehaviour
{
    public static SystemManager Instance { get; private set; }

    // Públicas — CustomSystemManager (auto-save de nome/imagem) referencia as mesmas constantes em
    // vez de duplicar os literais, pra nunca desalinhar do caminho que RecarregarLista/
    // LerMetadadosSistema/CarregarTokenSistema realmente leem aqui.
    public const string SistemasFolderName = "sistemas";
    public const string DadosFolderName    = "dados";
    public const string MetadadosFileName  = "regras_jogando.json";
    private const string NovoSistemaBaseName = "novo-sistema";

    [Header("UI - Sistemas")]
    [SerializeField] private Transform systemsContent;
    [SerializeField] private GameObject systemItemPrefab;
    [SerializeField] private GameObject createSystemScreen;
    [SerializeField] private GameObject systemsListScreen;
    [SerializeField] private CustomSystemManager customSystemManager;

    [Header("UI - Ferramentas")]
    [SerializeField] private bool atualizarVisualFerramentasNoStart = true;

    [Header("Configuração das Ferramentas")]
    [SerializeField] private List<FerramentaSistemaUI> ferramentas = new List<FerramentaSistemaUI>();

    [Header("UI - Detalhes")]
    [SerializeField] private TMP_Text selectedSystemNameText;
    [SerializeField] private TMP_Text selectedSystemDescriptionText;

    [Header("UI - Sumário de Regras")]
    [SerializeField] private Transform regrasContent;
    [SerializeField] private GameObject regraButtonPrefab;
    [SerializeField] private TMP_Text textoVisualizacaoRegra;

    [Header("Opcional")]
    [SerializeField] private TMP_Text feedbackText;

    private readonly List<RPGSystemData> systems = new List<RPGSystemData>();
    private RPGSystemData selectedSystem;

    private string SistemasRootPath
    {
        get { return Path.Combine(Application.persistentDataPath, SistemasFolderName); }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            VTTLog.SystemWarn("SystemManager: instância duplicada detectada. Mantendo a primeira instância ativa.");
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        if (createSystemScreen != null)
            createSystemScreen.SetActive(false);

        if (systemsListScreen != null)
            systemsListScreen.SetActive(true);

        GarantirConfiguracaoPadraoFerramentas();
        RecarregarLista();
        RegistrarBotoesFerramentas();

        if (atualizarVisualFerramentasNoStart)
            AtualizarEstadoFerramentas();
    }

    /// <summary>
    /// Reescaneia as subpastas e recria os itens de UI.
    /// </summary>
    public void RecarregarLista()
    {
        systems.Clear();
        CarregarSistemasDoDisco();

        if (selectedSystem != null)
        {
            selectedSystem = systems.Find(s => string.Equals(s.folderName, selectedSystem.folderName, StringComparison.OrdinalIgnoreCase));
        }

        if (selectedSystem == null && systems.Count > 0)
        {
            selectedSystem = systems[0];
        }

        VTTLog.System($"RecarregarLista — {systems.Count} sistema(s) | selecionado='{selectedSystem?.folderName ?? "nenhum"}'");
        RenderizarListaSistemas();
        AtualizarContextoSistemaSelecionado();
    }

    /// <summary>
    /// Seleciona e exibe um sistema pelo nome da pasta — usado pela listagem de sistemas do
    /// dashboard (SistemasListManager) ao navegar para esta tela após o usuário clicar um sistema.
    /// </summary>
    public void SelecionarSistemaPorPasta(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return;

        RPGSystemData alvo = systems.Find(s => string.Equals(s.folderName, folderName, StringComparison.OrdinalIgnoreCase));

        if (alvo == null)
        {
            RecarregarLista();
            alvo = systems.Find(s => string.Equals(s.folderName, folderName, StringComparison.OrdinalIgnoreCase));
        }

        if (alvo == null)
        {
            VTTLog.SystemWarn($"SelecionarSistemaPorPasta: sistema '{folderName}' não encontrado.");
            return;
        }

        OnSistemaSelecionado(alvo);
    }

    private void CarregarSistemasDoDisco()
    {
        if (!Directory.Exists(SistemasRootPath))
        {
            VTTLog.SystemWarn($"Pasta raiz não existe: {SistemasRootPath}");
            return;
        }

        string[] subpastas = Directory.GetDirectories(SistemasRootPath);
        VTTLog.System($"Pastas encontradas em '{SistemasRootPath}': {subpastas.Length}");
        Array.Sort(subpastas, StringComparer.OrdinalIgnoreCase);

        foreach (string pasta in subpastas)
        {
            string folderName = Path.GetFileName(pasta);
            SistemaMetadados meta = LerMetadadosSistema(pasta);
            VTTLog.System($"Sistema: '{folderName}' | nome={meta?.nome ?? "(sem metadata)"}");

            RPGSystemData data = new RPGSystemData
            {
                folderName       = folderName,
                systemName       = !string.IsNullOrWhiteSpace(meta?.nome) ? meta.nome : FormatarNomeExibicao(folderName),
                description      = LerDescricaoSistema(pasta),
                descricaoDetalhe = !string.IsNullOrWhiteSpace(meta?.descricao) ? meta.descricao : "",
                icon             = CarregarTokenSistema(pasta),
                isEditable       = true
            };

            systems.Add(data);
        }
    }

    /// <summary>
    /// Busca regras_jogando.json recursivamente dentro da pasta do sistema.
    /// Suporta dois formatos:
    ///   - Objeto: { "nome": "...", "descricao": "..." }
    ///   - Array:  [{ "id": "...", "topico": "...", "descricao": "..." }, ...]
    /// No formato array, gera uma descrição a partir do número de regras encontradas.
    /// </summary>
    private SistemaMetadados LerMetadadosSistema(string pastaSistema)
    {
        string[] arquivos = Directory.GetFiles(pastaSistema, MetadadosFileName, SearchOption.AllDirectories);

        if (arquivos.Length == 0)
            return null;

        string json;
        try { json = File.ReadAllText(arquivos[0]); }
        catch (Exception e)
        {
            VTTLog.SystemWarn($"Falha ao ler '{arquivos[0]}': {e.Message}");
            return null;
        }

        // Formato objeto
        if (json.TrimStart().StartsWith("{"))
        {
            SistemaMetadados meta = JsonUtility.FromJson<SistemaMetadados>(json);
            return string.IsNullOrWhiteSpace(meta?.nome) ? null : meta;
        }

        // Formato array de regras — percorre topicos e subtopicos recursivamente
        if (json.TrimStart().StartsWith("["))
        {
            RegraJogandoItem[] regras = JsonHelper.FromJson<RegraJogandoItem>(json);
            if (regras == null || regras.Length == 0)
                return null;

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (RegraJogandoItem regra in regras)
                AppendRegraItem(sb, regra);

            return new SistemaMetadados
            {
                nome      = "",
                descricao = sb.ToString().TrimEnd()
            };
        }

        return null;
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
            VTTLog.SystemWarn($"Falha ao ler descrição do sistema '{pastaSistema}': {e.Message}");
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
            VTTLog.SystemWarn($"Falha ao carregar token do sistema '{pastaSistema}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Cria dados/regras_jogando.json dentro da pasta do sistema com valores padrão.
    /// Não sobrescreve se já existir.
    /// </summary>
    private void CriarMetadadosPadrao(string pastaSistema, string nomeSistema, string descricao = "")
    {
        string pastaD = Path.Combine(pastaSistema, DadosFolderName);
        Directory.CreateDirectory(pastaD);

        string caminho = Path.Combine(pastaD, MetadadosFileName);
        if (File.Exists(caminho))
            return;

        SistemaMetadados meta = new SistemaMetadados
        {
            nome     = nomeSistema,
            descricao = string.IsNullOrWhiteSpace(descricao) ? "Sistema personalizado local." : descricao,
            versao   = "1.0",
            autor    = ""
        };

        File.WriteAllText(caminho, JsonUtility.ToJson(meta, prettyPrint: true));
        VTTLog.System($"Metadados criados: {caminho}");
    }

    private void RenderizarListaSistemas()
    {
        if (systemsContent == null)
        {
            VTTLog.SystemWarn("systemsContent não definido no Inspector.");
            return;
        }

        if (systemItemPrefab == null)
        {
            VTTLog.SystemWarn("systemItemPrefab não definido no Inspector.");
            return;
        }

        for (int i = systemsContent.childCount - 1; i >= 0; i--)
        {
            Destroy(systemsContent.GetChild(i).gameObject);
        }

        foreach (RPGSystemData data in systems)
        {
            GameObject itemGo = Instantiate(systemItemPrefab, systemsContent, false);
            itemGo.SetActive(true);

            SystemUIItem uiItem = itemGo.GetComponent<SystemUIItem>();
            if (uiItem == null)
            {
                VTTLog.SystemWarn("Prefab instanciado não possui componente SystemUIItem.");
                continue;
            }

            bool isSelected = selectedSystem != null && string.Equals(selectedSystem.folderName, data.folderName, StringComparison.OrdinalIgnoreCase);
            uiItem.Setup(data, OnSistemaSelecionado, isSelected);
        }
    }

    private void OnSistemaSelecionado(RPGSystemData data)
    {
        selectedSystem = data;
        RenderizarListaSistemas();
        AtualizarContextoSistemaSelecionado();
    }

    private void AtualizarContextoSistemaSelecionado()
    {
        if (selectedSystemNameText != null)
        {
            selectedSystemNameText.text = selectedSystem != null ? selectedSystem.systemName : "Nenhum sistema selecionado";
        }

        if (selectedSystemDescriptionText != null)
        {
            selectedSystemDescriptionText.text = selectedSystem != null
                ? selectedSystem.descricaoDetalhe
                : "Selecione um sistema para visualizar detalhes.";
        }

        AtualizarEstadoFerramentas();
        CarregarSumarioDeRegras();
    }

    private void RegistrarBotoesFerramentas()
    {
        for (int i = 0; i < ferramentas.Count; i++)
        {
            FerramentaSistemaUI ferramenta = ferramentas[i];
            if (ferramenta == null || ferramenta.botao == null)
            {
                continue;
            }

            if (ferramenta.texto != null)
            {
                ferramenta.texto.text = ObterNomeFerramenta(ferramenta);
            }

            ferramenta.botao.onClick.RemoveAllListeners();
            ferramenta.botao.onClick.AddListener(() => ExecutarFerramenta(ferramenta));
        }
    }

    private void AtualizarEstadoFerramentas()
    {
        for (int i = 0; i < ferramentas.Count; i++)
        {
            FerramentaSistemaUI ferramenta = ferramentas[i];
            if (ferramenta == null || ferramenta.botao == null)
            {
                continue;
            }

            bool podeUsar = PodeUsarFerramenta(ferramenta);
            ferramenta.botao.interactable = podeUsar;

            float alphaAlvo = podeUsar ? ferramenta.alphaHabilitado : ferramenta.alphaDesabilitado;

            if (ferramenta.texto != null)
            {
                Color c = ferramenta.texto.color;
                c.a = alphaAlvo;
                ferramenta.texto.color = c;
            }

            if (ferramenta.imagemBotao != null)
            {
                Color c = ferramenta.imagemBotao.color;
                c.a = alphaAlvo;
                ferramenta.imagemBotao.color = c;
            }
        }
    }

    private bool PodeUsarFerramenta(FerramentaSistemaUI ferramenta)
    {
        if (ferramenta == null)
        {
            return false;
        }

        if (ferramenta.requerSistemaSelecionado && selectedSystem == null)
        {
            return false;
        }

        return true;
    }

    private void ExecutarFerramenta(FerramentaSistemaUI ferramenta)
    {
        switch (ferramenta.tipoFerramenta)
        {
            case TipoFerramentaSistema.CriarNovoSistema:
                CriarNovoSistema();
                break;
            case TipoFerramentaSistema.VisualizarSistema:
                VisualizarSistemaSelecionado();
                break;
            case TipoFerramentaSistema.EditarSistema:
                EditarSistema();
                break;
            default:
                VTTLog.SystemWarn("Ferramenta não mapeada: " + ferramenta.tipoFerramenta);
                break;
        }
    }

    private string ObterNomeFerramenta(FerramentaSistemaUI ferramenta)
    {
        if (ferramenta == null)
        {
            return "Ferramenta";
        }

        if (!string.IsNullOrWhiteSpace(ferramenta.nomeFerramenta))
        {
            return ferramenta.nomeFerramenta;
        }

        switch (ferramenta.tipoFerramenta)
        {
            case TipoFerramentaSistema.CriarNovoSistema:
                return "Criar Sistema";
            case TipoFerramentaSistema.EditarSistema:
                return "Editar Sistema";
            case TipoFerramentaSistema.VisualizarSistema:
            default:
                return "Visualizar Sistema";
        }
    }

    private void GarantirConfiguracaoPadraoFerramentas()
    {
        if (ferramentas != null && ferramentas.Count > 0)
        {
            return;
        }

        ferramentas = new List<FerramentaSistemaUI>
        {
            new FerramentaSistemaUI
            {
                nomeFerramenta = "Criar Sistema",
                tipoFerramenta = TipoFerramentaSistema.CriarNovoSistema,
                requerSistemaSelecionado = false
            },
            new FerramentaSistemaUI
            {
                nomeFerramenta = "Visualizar Sistema",
                tipoFerramenta = TipoFerramentaSistema.VisualizarSistema,
                requerSistemaSelecionado = true
            },
            new FerramentaSistemaUI
            {
                nomeFerramenta = "Editar Sistema",
                tipoFerramenta = TipoFerramentaSistema.EditarSistema,
                requerSistemaSelecionado = true
            }
        };
    }

    /// <summary>
    /// Cria uma nova pasta de sistema com nome genérico único e atualiza a lista.
    /// </summary>
    public void CriarNovoSistema()
    {
        if (createSystemScreen != null)
            createSystemScreen.SetActive(true);

        if (systemsListScreen != null)
            systemsListScreen.SetActive(false);
    }

    public void OnSistemaCriado(string caminhoSistema, string nomeSistema)
    {
        if (systemsListScreen != null)
        {
            systemsListScreen.SetActive(true);
        }

        if (createSystemScreen != null)
        {
            createSystemScreen.SetActive(false);
        }

        CriarMetadadosPadrao(caminhoSistema, nomeSistema);
        RecarregarLista();
        DefinirFeedback("Sistema salvo: " + nomeSistema);
        VTTLog.System("Sistema criado e retornado à lista: " + caminhoSistema);

        PostHogTracker.TrackSistemaCriado(nomeSistema);
    }

    public void EditarSistema()
    {
        if (selectedSystem == null)
        {
            DefinirFeedback("Selecione um sistema para editar.");
            return;
        }

        if (createSystemScreen != null) createSystemScreen.SetActive(true);
        if (systemsListScreen  != null) systemsListScreen.SetActive(false);

        if (customSystemManager != null)
        {
            string caminho = Path.Combine(SistemasRootPath, selectedSystem.folderName);
            customSystemManager.OnSistemaSalvo -= VoltarParaLista;
            customSystemManager.OnSistemaSalvo += VoltarParaLista;
            customSystemManager.CarregarSistemaExistente(caminho, selectedSystem.systemName);
        }

        DefinirFeedback("Editando sistema: " + selectedSystem.systemName);
    }

    private void VoltarParaLista()
    {
        if (customSystemManager != null)
            customSystemManager.OnSistemaSalvo -= VoltarParaLista;

        if (systemsListScreen  != null) systemsListScreen.SetActive(true);
        if (createSystemScreen != null) createSystemScreen.SetActive(false);

        RecarregarLista();
    }

    public void VoltarParaListaSistemas()
    {
        if (systemsListScreen  != null) systemsListScreen.SetActive(true);
        if (createSystemScreen != null) createSystemScreen.SetActive(false);

        RecarregarLista();
    }

    public void VisualizarSistemaSelecionado()
    {
        if (selectedSystem == null)
        {
            DefinirFeedback("Selecione um sistema para visualizar.");
            return;
        }

        VTTLog.System("Visualizar sistema: " + selectedSystem.folderName);
        DefinirFeedback("Visualizando sistema: " + selectedSystem.systemName);

        CarregarSumarioDeRegras();
    }

    /// <summary>
    /// Lê TODAS as pastas na raiz do sistema selecionado e cria os botões do sumário.
    /// Ignora a pasta de configurações "dados". É totalmente genérico.
    /// </summary>
    public void CarregarSumarioDeRegras()
    {
        if (selectedSystem == null)
        {
            DefinirFeedback("Nenhum sistema selecionado para carregar o sumário.");
            return;
        }

        if (regrasContent != null)
        {
            for (int i = regrasContent.childCount - 1; i >= 0; i--)
                Destroy(regrasContent.GetChild(i).gameObject);
        }

        if (textoVisualizacaoRegra != null)
            textoVisualizacaoRegra.text = "Selecione uma categoria no sumário para ler.";

        string pastaSistema = Path.Combine(SistemasRootPath, selectedSystem.folderName);
        VTTLog.System($"CarregarSumario — sistema='{selectedSystem.folderName}' | path='{pastaSistema}'");

        if (!Directory.Exists(pastaSistema))
        {
            VTTLog.SystemWarn($"Pasta do sistema não encontrada: '{pastaSistema}'");
            DefinirFeedback("Pasta do sistema não encontrada.");
            return;
        }

        string[] subpastas = Directory.GetDirectories(pastaSistema);
        VTTLog.System($"Pastas raiz encontradas: {subpastas.Length}");

        if (subpastas.Length == 0)
        {
            VTTLog.SystemWarn("Nenhuma subpasta encontrada.");
            if (textoVisualizacaoRegra != null)
                textoVisualizacaoRegra.text = "Nenhuma pasta encontrada neste sistema.";
            return;
        }

        if (regraButtonPrefab == null)
        {
            VTTLog.SystemWarn("regraButtonPrefab não atribuído no Inspector.");
            return;
        }

        if (regrasContent == null)
        {
            VTTLog.SystemWarn("regrasContent não atribuído no Inspector.");
            return;
        }

        int pastasAdicionadas = 0;

        foreach (string pasta in subpastas)
        {
            string nomePasta = Path.GetFileName(pasta);

            if (string.Equals(nomePasta, DadosFolderName, StringComparison.OrdinalIgnoreCase))
            {
                VTTLog.System($"Pasta '{nomePasta}' ignorada (pasta interna).");
                continue;
            }

            pastasAdicionadas++;
            string tituloBotao  = FormatarNomeExibicao(nomePasta);
            string pastaCaptura = pasta;
            VTTLog.System($"Criando botão: '{tituloBotao}' → '{pasta}'");

            GameObject btnObj = Instantiate(regraButtonPrefab, regrasContent, false);
            btnObj.SetActive(true);

            TMP_Text t = btnObj.GetComponentInChildren<TMP_Text>();
            if (t != null) t.text = tituloBotao;

            if (btnObj.TryGetComponent(out Button b))
            {
                b.onClick.RemoveAllListeners();
                b.onClick.AddListener(() => MostrarConteudoDaPasta(pastaCaptura, tituloBotao));
            }
        }

        if (pastasAdicionadas == 0 && textoVisualizacaoRegra != null)
        {
            VTTLog.SystemWarn("Nenhuma pasta de conteúdo válida (apenas 'dados' encontrada).");
            textoVisualizacaoRegra.text = "Nenhuma categoria de conteúdo válida encontrada (apenas pastas internas).";
        }
    }

    /// <summary>
    /// Lê todos os arquivos XML de uma subpasta específica e exibe no painel de texto.
    /// </summary>
    private void MostrarConteudoDaPasta(string caminhoPasta, string tituloPasta)
    {
        if (textoVisualizacaoRegra == null) return;

        string[] arquivosXml = Directory.GetFiles(caminhoPasta, "*.xml");
        VTTLog.System($"Lendo pasta='{caminhoPasta}' | Arquivos XML={arquivosXml.Length}");

        if (arquivosXml.Length == 0)
        {
            textoVisualizacaoRegra.text = $"<size=150%><b>{tituloPasta}</b></size>\n\n<i>Nenhum arquivo XML encontrado dentro de {Path.GetFileName(caminhoPasta)}.</i>";
            return;
        }

        System.Text.StringBuilder textoFinal = new();
        textoFinal.AppendLine($"<size=150%><b>{tituloPasta}</b></size>");
        textoFinal.AppendLine("<line-height=50%>\n</line-height>");

        foreach (string arquivo in arquivosXml)
        {
            try
            {
                XDocument doc = XDocument.Parse(File.ReadAllText(arquivo, System.Text.Encoding.UTF8));
                XElement root = doc.Root;

                string titulo = root?.Element("name")?.Value
                             ?? root?.Element("Titulo")?.Value
                             ?? Path.GetFileNameWithoutExtension(arquivo);

                VTTLog.System($"XML lido: titulo='{titulo}'");

                textoFinal.AppendLine($"<size=130%><b><color=#FFD700>{titulo}</color></b></size>");

                if (root != null)
                    textoFinal.Append(ConverterXmlParaTextoFormatado(root));

                textoFinal.AppendLine("\n<color=#888888>──────────────────────────────────────</color>\n");
            }
            catch (Exception e)
            {
                VTTLog.SystemWarn($"Erro ao ler XML '{arquivo}': {e.Message}");
                textoFinal.AppendLine($"<size=120%><b>{Path.GetFileNameWithoutExtension(arquivo)}</b></size>");
                textoFinal.AppendLine("<i>[Erro ao carregar ou formatar este arquivo]</i>\n");
            }
        }

        textoVisualizacaoRegra.text = textoFinal.ToString();
    }

    /// <summary>
    /// Interpreta a raiz do XML e inicia a formatação recursiva.
    /// </summary>
    private static string ConverterXmlParaTextoFormatado(XElement root)
    {
        System.Text.StringBuilder sb = new();

        string summary = root.Element("summary")?.Value ?? root.Element("Descricao")?.Value;
        if (!string.IsNullOrWhiteSpace(summary))
            sb.AppendLine($"<i>{summary.Trim()}</i>\n");

        foreach (XElement child in root.Elements())
        {
            string nomeTag = child.Name.LocalName.ToLower();
            if (nomeTag == "name" || nomeTag == "titulo" || nomeTag == "summary" || nomeTag == "descricao")
                continue;

            FormatarNoXml(sb, child, 0);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Função recursiva que formata cada nó do XML em texto legível.
    /// </summary>
    private static void FormatarNoXml(System.Text.StringBuilder sb, XElement node, int indent)
    {
        string espaco      = new(' ', indent * 4);
        string tag         = node.Name.LocalName.ToLower();
        string tagFormatada = FormatarNomeExibicaoTag(node.Name.LocalName);

        // Nó com <name> + <description> diretos → enciclopédia clássica
        if (node.Element("name") != null && node.Element("description") != null)
        {
            sb.AppendLine($"{espaco}<b>{node.Element("name").Value}:</b> {node.Element("description").Value.Trim()}");
            return;
        }

        // Lista de itens → bullet points
        if (tag == "option" || tag == "item" || tag == "skill"    || tag == "tool" ||
            tag == "language" || tag == "save" || tag == "armor"  || tag == "weapon")
        {
            sb.AppendLine($"{espaco}• {node.Value.Trim()}");
            return;
        }

        if (node.HasElements)
        {
            sb.AppendLine();
            sb.AppendLine($"{espaco}<b><color=#AADDFF>{tagFormatada}</color></b>");

            if (node.Attribute("text") != null)
                sb.AppendLine($"{espaco}<i>{node.Attribute("text").Value}</i>");

            foreach (XElement child in node.Elements())
                FormatarNoXml(sb, child, indent + 1);
        }
        else
        {
            string valor = node.Value.Trim();
            if (!string.IsNullOrWhiteSpace(valor))
                sb.AppendLine($"{espaco}<b>{tagFormatada}:</b> {valor}");
        }
    }

    /// <summary>
    /// Converte nomes de tags XML em texto legível (ex: "skill-proficiencies" → "Skill proficiencies").
    /// </summary>
    private static string FormatarNomeExibicaoTag(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return "";
        string s = tag.Replace("-", " ").Replace("_", " ");
        return char.ToUpper(s[0]) + s[1..];
    }

    private static void AppendRegraItem(System.Text.StringBuilder sb, RegraJogandoItem regra)
    {
        if (!string.IsNullOrWhiteSpace(regra.topico))
            sb.AppendLine(regra.topico);
        if (!string.IsNullOrWhiteSpace(regra.descricao))
            sb.AppendLine(regra.descricao);

        if (regra.subtopicos != null)
        {
            foreach (SubtopicoRegra sub in regra.subtopicos)
                AppendSubtopicoItem(sb, sub);
        }

        sb.AppendLine();
    }

    private static void AppendSubtopicoItem(System.Text.StringBuilder sb, SubtopicoRegra sub)
    {
        if (!string.IsNullOrWhiteSpace(sub.topico))
            sb.AppendLine(sub.topico);
        if (!string.IsNullOrWhiteSpace(sub.descricao))
            sb.AppendLine(sub.descricao);

        if (sub.subtopicos != null)
        {
            foreach (PassoRegra passo in sub.subtopicos)
            {
                if (!string.IsNullOrWhiteSpace(passo.nome))
                    sb.AppendLine(passo.nome);
                if (!string.IsNullOrWhiteSpace(passo.descricao))
                    sb.AppendLine(passo.descricao);
            }
        }
    }

    private string GerarNomeUnicoNovoSistema()
    {
        int index = 1;

        while (true)
        {
            string nome = NovoSistemaBaseName + "-" + index.ToString("000");
            string caminho = Path.Combine(SistemasRootPath, nome);
            if (!Directory.Exists(caminho))
            {
                return nome;
            }

            index++;
        }
    }

    private static string FormatarNomeExibicao(string folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return "Sistema sem nome";
        }

        string nome = folderName.Replace('-', ' ').Replace('_', ' ').Trim();
        return char.ToUpper(nome[0]) + nome.Substring(1);
    }

    public void DefinirFeedbackPublico(string mensagem) => DefinirFeedback(mensagem);

    private void DefinirFeedback(string mensagem)
    {
        if (feedbackText != null)
        {
            feedbackText.text = mensagem;
        }
    }
}
