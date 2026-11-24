using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

public class FichaLoaderUI : MonoBehaviour
{
    [Header("UI / Prefab")]
    public GameObject fichaPrefab;
    public Transform contentParent; // Onde as fichas serão criadas (o Content do ScrollView)

    [Header("Opções")]
    public bool clearBeforeInstantiate = true;

    [Header("Paginação")]
    [SerializeField] private IndicadorPaginasUI indicadorPaginas;
    [SerializeField, Min(1)] private int itensPorPagina = 9;
    [SerializeField] private Button botaoAnterior;
    [SerializeField] private Button botaoProximo;

    [Header("Filtro por Sistema (opcional)")]
    [Tooltip("Este loader é reaproveitado em vários lugares (dashboard, sala, etc) e nem todo lugar " +
        "precisa de filtro — liga/desliga a seção inteira (painelFiltroSistema) sem precisar de " +
        "outra versão do prefab. Segue a mesma base de chip (toggle+texto) que FichaGeralManager já " +
        "usa pra filtros de ações/habilidades/magias/inventário — reaproveita o FiltroChipToggleUI.")]
    [SerializeField] private bool habilitarFiltroSistema = false;
    [SerializeField] private GameObject painelFiltroSistema;
    [SerializeField] private Transform  conteudoFiltroSistema; // Content onde os chips são instanciados
    [SerializeField] private GameObject filtroChipPrefab;      // prefab com FiltroChipToggleUI
    [SerializeField] private Button     btnExpandirFiltroSistema;
    [SerializeField] private Button     btnColapsarFiltroSistema;

    [Header("Busca")]
    [Tooltip("Filtra por nome da ficha, raça ou classe — busca em texto livre, case-insensitive, " +
        "combinada com o filtro de sistema (se ligado).")]
    [SerializeField] private TMP_InputField inputBusca;

    [Header("Telas (para redirecionamento de fichas incompletas)")]
    public GameObject telaCriacaoFichaDnd; // Tela de criação de ficha D&D
    public GameObject telaFichaDnd;        // Tela da ficha pronta (visualização/edição)

    [Header("Navegação")]
    [SerializeField] private ToggleObjects toggleObjects;

    // Disparado após os itens de ficha serem criados com sucesso no contentParent
    public event Action OnPopuladoComSucesso;

    private ScrollRect parentScrollRect;
    private bool inscritoEmDataManager;
    private Coroutine initializeRoutineCoroutine;
    private Coroutine populateRoutineCoroutine;
    private FichaData[] todasFichas = new FichaData[0];
    private FichaData[] fichasFiltradas = new FichaData[0];
    private int paginaAtual;

    // ── Filtro por sistema ───────────────────────────────────────────────────
    private readonly Dictionary<int, string> sistemaPorFichaDndId = new Dictionary<int, string>();
    private readonly HashSet<string> sistemasAtivos = new HashSet<string>();
    private readonly List<GameObject> chipsFiltroSistemaInstanciados = new List<GameObject>();

    // ── Busca ────────────────────────────────────────────────────────────────
    private string textoBuscaAtual = "";

    private void Awake()
    {
        if (toggleObjects == null)
            toggleObjects = FindFirstObjectByType<ToggleObjects>();

        if (painelFiltroSistema != null)
            painelFiltroSistema.SetActive(habilitarFiltroSistema);
    }

    private void OnEnable()
    {
        Debug.Log($"[FichaLoaderUI] OnEnable '{gameObject.name}' — DataManager.Instance={(DataManager.Instance != null)}, SteamManager.Instance={(SteamManager.Instance != null)}, CurrentSteamId='{SteamManager.Instance?.CurrentSteamId}'");

        // Busca o ScrollRect pai para pausá-lo durante atualizações (evita erro de MissingReference)
        if (contentParent != null)
            parentScrollRect = contentParent.GetComponentInParent<ScrollRect>();

        // NÃO inscreve em DataManager.OnDataUpdated aqui — confirmado em log real que
        // DataManager.Instance pode ainda estar nulo neste exato ponto (Awake() de "Main Camera"
        // não necessariamente já rodou quando o OnEnable deste objeto roda, dependendo da ordem de
        // processamento da cena). Isso fazia a inscrição ser pulada em silêncio pra sempre — os dados
        // carregavam certinho (visível no log do DataManager), mas este painel nunca era avisado. A
        // inscrição real acontece em TentarIniciarBuscaRoutine, depois de esperar um frame.

        botaoAnterior?.onClick.AddListener(PaginaAnterior);
        botaoProximo?.onClick.AddListener(ProximaPagina);

        inputBusca?.onValueChanged.AddListener(OnBuscaAlterada);

        if (habilitarFiltroSistema)
        {
            btnExpandirFiltroSistema?.onClick.AddListener(ExpandirFiltroSistema);
            btnColapsarFiltroSistema?.onClick.AddListener(ColapsarFiltroSistema);
            ColapsarFiltroSistema(); // começa recolhido, mesmo padrão de FichaGeralManager
        }

        TentarIniciarBusca();
    }

    private void OnDisable()
    {
        Debug.Log($"[FichaLoaderUI] OnDisable '{gameObject.name}'");

        if (DataManager.Instance != null && inscritoEmDataManager)
            DataManager.Instance.OnDataUpdated -= OnDataUpdated;
        inscritoEmDataManager = false;

        DesinscreverEventosSteam();

        botaoAnterior?.onClick.RemoveListener(PaginaAnterior);
        botaoProximo?.onClick.RemoveListener(ProximaPagina);

        inputBusca?.onValueChanged.RemoveListener(OnBuscaAlterada);

        btnExpandirFiltroSistema?.onClick.RemoveListener(ExpandirFiltroSistema);
        btnColapsarFiltroSistema?.onClick.RemoveListener(ColapsarFiltroSistema);

        StopAllCoroutines();
    }

    // ── Espera pela conexão Steam antes do primeiro fetch ───────────────────
    //
    // DataManager.GetCurrentSteamId() só retorna o Steam ID real depois que SteamManager conecta —
    // antes disso cai num placeholder ("0" em build, "editor_X" no Editor), sob o qual nenhuma ficha
    // real está salva. Isso vale TAMBÉM no Editor: se o cliente Steam está rodando, SteamManager
    // conecta de verdade durante o Play Mode (confirmado em log real — steam_id passa de "editor_1"
    // pro ID real do Steam assim que a conexão termina), então o placeholder do Editor não é estável
    // o suficiente pra pular a espera — só pula se não há SteamManager na cena (sem integração Steam)
    // ou se ele já conectou antes disso rodar.
    //
    // ÚNICO ponto de entrada pra iniciar uma busca — usado tanto por OnEnable() (automático) quanto
    // por OpenFichas() (manual, ex: botão de refresh).
    private void TentarIniciarBusca()
    {
        if (!gameObject.activeInHierarchy)
        {
            Debug.Log($"[FichaLoaderUI] TentarIniciarBusca '{gameObject.name}' — abortado, GameObject inativo.");
            return;
        }

        StartCoroutine(TentarIniciarBuscaRoutine());
    }

    private IEnumerator TentarIniciarBuscaRoutine()
    {
        // Espera pelo menos um frame antes de tocar em DataManager.Instance/SteamManager.Instance —
        // confirmado em log real que os dois podem ainda estar NULOS no frame síncrono deste
        // OnEnable (Awake() de "Main Camera", dono dos dois singletons, nem sempre já rodou nesse
        // exato instante, dependendo da ordem de processamento da cena). Sem essa espera: (a) a
        // inscrição em OnDataUpdated era pulada em silêncio pra sempre, e (b) PrecisaEsperarSteamConectar()
        // sempre concluía "sem SteamManager, prossiga" mesmo com o Steam ainda conectando — o jogo
        // carregava as fichas de verdade (visível no log do DataManager), mas este painel nunca era
        // avisado e nunca tentava de novo.
        yield return null;

        if (!gameObject.activeInHierarchy) yield break;

        if (!inscritoEmDataManager && DataManager.Instance != null)
        {
            DataManager.Instance.OnDataUpdated += OnDataUpdated;
            inscritoEmDataManager = true;
            Debug.Log($"[FichaLoaderUI] TentarIniciarBuscaRoutine '{gameObject.name}' — inscrito em DataManager.OnDataUpdated.");
        }
        else if (!inscritoEmDataManager)
        {
            Debug.LogWarning($"[FichaLoaderUI] TentarIniciarBuscaRoutine '{gameObject.name}' — DataManager.Instance CONTINUA nulo mesmo depois de esperar um frame!");
        }

        bool precisaEsperar = PrecisaEsperarSteamConectar();
        Debug.Log($"[FichaLoaderUI] TentarIniciarBuscaRoutine '{gameObject.name}' — precisaEsperarSteam={precisaEsperar}, " +
            $"SteamManager.Instance={(SteamManager.Instance != null)}, CurrentSteamId='{SteamManager.Instance?.CurrentSteamId}'");

        if (precisaEsperar)
        {
            // Remove antes de adicionar — chamar isso de novo (ex: usuário clicou refresh 2x)
            // enquanto ainda espera não pode duplicar a inscrição.
            DesinscreverEventosSteam();
            SteamManager.Instance.OnConectadoComSucesso += OnSteamConectadoAntesDoFetch;
            SteamManager.Instance.OnFalhaAoConectar      += OnSteamFalhouAntesDoFetch;
            Debug.Log($"[FichaLoaderUI] TentarIniciarBuscaRoutine '{gameObject.name}' — inscrito nos eventos do SteamManager, aguardando.");
        }
        else
        {
            Debug.Log($"[FichaLoaderUI] TentarIniciarBuscaRoutine '{gameObject.name}' — Steam já resolvido (ou sem SteamManager), iniciando busca já.");
            StartInitializeRoutine();
        }
    }

    private static bool PrecisaEsperarSteamConectar()
    {
        return SteamManager.Instance != null
            && (string.IsNullOrWhiteSpace(SteamManager.Instance.CurrentSteamId) || SteamManager.Instance.CurrentSteamId == "0");
    }

    private void OnSteamConectadoAntesDoFetch()
    {
        Debug.Log($"[FichaLoaderUI] OnSteamConectadoAntesDoFetch '{gameObject.name}' — CurrentSteamId='{SteamManager.Instance?.CurrentSteamId}', activeInHierarchy={gameObject.activeInHierarchy}");
        DesinscreverEventosSteam();
        if (gameObject.activeInHierarchy) StartInitializeRoutine();
    }

    private void OnSteamFalhouAntesDoFetch(string mensagem)
    {
        Debug.Log($"[FichaLoaderUI] OnSteamFalhouAntesDoFetch '{gameObject.name}' — mensagem='{mensagem}', activeInHierarchy={gameObject.activeInHierarchy}");
        DesinscreverEventosSteam();
        // Mesmo sem conectar, segue com o fluxo normal — GetCurrentSteamId() cai pro placeholder
        // ("0" em build, "editor_X" no Editor), que continua útil pra testar localmente sem o
        // cliente Steam rodando (fichas salvas sob esse ID de debug continuam aparecendo). O que
        // importa é não ter feito essa busca ANTES de saber se o Steam ia conectar ou não.
        if (gameObject.activeInHierarchy) StartInitializeRoutine();
    }

    private void DesinscreverEventosSteam()
    {
        if (SteamManager.Instance != null)
        {
            SteamManager.Instance.OnConectadoComSucesso -= OnSteamConectadoAntesDoFetch;
            SteamManager.Instance.OnFalhaAoConectar      -= OnSteamFalhouAntesDoFetch;
        }
    }

    private IEnumerator InitializeRoutine()
    {
        Debug.Log($"[FichaLoaderUI] InitializeRoutine '{gameObject.name}' — início, aguardando fim do frame...");
        yield return new WaitForEndOfFrame();

        if (DataManager.Instance == null)
        {
            Debug.LogWarning($"[FichaLoaderUI] InitializeRoutine '{gameObject.name}' — DataManager.Instance é null, abortando com popup de erro.");
            PopupManager.Instance?.MostrarErro("Erro", "Gerenciador de dados não encontrado.");
            yield break;
        }

        Debug.Log($"[FichaLoaderUI] InitializeRoutine '{gameObject.name}' — DataManager.Instance.HasData={DataManager.Instance.HasData}");

        if (DataManager.Instance.HasData)
        {
            FichaData[] copia = DataManager.Instance.GetFichasCopy();
            Debug.Log($"[FichaLoaderUI] InitializeRoutine '{gameObject.name}' — usando dados já em cache, {copia.Length} ficha(s).");
            DefinirDados(copia);
        }
        else
        {
            Debug.Log($"[FichaLoaderUI] InitializeRoutine '{gameObject.name}' — sem dados em cache, mostrando popup e disparando fetch. " +
                $"PopupManager.Instance={(PopupManager.Instance != null)}");
            PopupManager.Instance?.MostrarCarregamento("Carregando fichas", "Buscando seus personagens...");

            int pid = (PlayerSession.Instance != null) ? PlayerSession.Instance.playerId : 1;
            Debug.Log($"[FichaLoaderUI] Buscando fichas para Player ID: {pid} (PlayerSession.Instance={(PlayerSession.Instance != null)}, " +
                $"DataManager.GetCurrentSteamId()='{DataManager.Instance.GetCurrentSteamId()}')");
            DataManager.Instance.StartFetch(pid, false);
        }
    }

    private void OnDataUpdated(FichaData[] newData)
    {
        Debug.Log($"[FichaLoaderUI] OnDataUpdated '{gameObject.name}' — recebido {(newData?.Length ?? -1)} ficha(s), " +
            $"activeInHierarchy={gameObject.activeInHierarchy}, fechando popup se houver algum aberto.");

        // Fecha o popup de carregamento sempre que os dados chegarem, mesmo que este
        // painel não esteja ativo no momento — do contrário o popup (compartilhado
        // globalmente via PopupManager) fica preso na tela indefinidamente.
        PopupManager.Instance?.Fechar();

        if (!gameObject.activeInHierarchy)
        {
            Debug.Log($"[FichaLoaderUI] OnDataUpdated '{gameObject.name}' — GameObject inativo, ignorando os dados recebidos.");
            return;
        }
        if (newData == null || newData.Length == 0)
        {
            Debug.Log($"[FichaLoaderUI] OnDataUpdated '{gameObject.name}' — dados vazios, não populando a lista.");
            return;
        }

        DefinirDados(newData);
    }

    public void OpenFichas()
    {
        TentarIniciarBusca();
    }

    private void StartInitializeRoutine()
    {
        if (initializeRoutineCoroutine != null)
        {
            StopCoroutine(initializeRoutineCoroutine);
            initializeRoutineCoroutine = null;
        }

        initializeRoutineCoroutine = StartCoroutine(InitializeRoutineWrapper());
    }

    private IEnumerator InitializeRoutineWrapper()
    {
        yield return InitializeRoutine();
        initializeRoutineCoroutine = null;
    }

    // Novo conjunto de dados (fetch inicial ou atualização) — reseta a paginação para a primeira página.
    private void DefinirDados(FichaData[] data)
    {
        Debug.Log($"[FichaLoaderUI] DefinirDados '{gameObject.name}' — {(data?.Length ?? 0)} ficha(s) recebida(s).");
        todasFichas = data ?? new FichaData[0];

        if (habilitarFiltroSistema)
        {
            AtualizarSistemaPorFicha();
            PopularChipsFiltroSistema();
        }

        AplicarFiltroEIrParaPagina(0);
    }

    // ── Filtro por sistema ───────────────────────────────────────────────────
    //
    // Reaproveita o mesmo modelo de chip (toggle+texto) que FichaGeralManager usa pra
    // ações/habilidades/magias/inventário: nenhuma tag ativa = mostra tudo (sem filtro), qualquer
    // tag ativa restringe à lista. A única diferença é que aqui as tags (nomes de sistema) não são
    // fixas em código — são descobertas a partir dos dados carregados, já que qualquer sistema
    // (incluindo os criados na Forja) pode aparecer.

    // FichaData não carrega o sistema — só existe no snapshot local (LocalFichaStorage), então
    // resolve e cacheia uma vez por ficha aqui (evita reler o disco toda vez que o filtro muda).
    private void AtualizarSistemaPorFicha()
    {
        sistemaPorFichaDndId.Clear();

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        foreach (FichaData d in todasFichas)
        {
            if (d == null) continue;

            string sistema = "Desconhecido";
            if (LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId, d.fichas_general_id, d.fichas_dnd_id, out LocalFichaData snap)
                && !string.IsNullOrWhiteSpace(snap.sistema_nome))
                sistema = snap.sistema_nome;

            sistemaPorFichaDndId[d.fichas_dnd_id] = sistema;
        }
    }

    private void PopularChipsFiltroSistema()
    {
        foreach (GameObject go in chipsFiltroSistemaInstanciados) if (go != null) Destroy(go);
        chipsFiltroSistemaInstanciados.Clear();
        sistemasAtivos.Clear();

        if (conteudoFiltroSistema == null || filtroChipPrefab == null) return;

        var sistemasDisponiveis = new List<string>(new HashSet<string>(sistemaPorFichaDndId.Values));
        sistemasDisponiveis.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (string sistema in sistemasDisponiveis)
        {
            GameObject go = Instantiate(filtroChipPrefab, conteudoFiltroSistema, false);
            go.SetActive(true);
            chipsFiltroSistemaInstanciados.Add(go);

            FiltroChipToggleUI chip = go.GetComponent<FiltroChipToggleUI>();
            if (chip == null) continue;

            chip.Configurar(sistema, (tagAlterada, isOn) =>
            {
                if (isOn) sistemasAtivos.Add(tagAlterada);
                else sistemasAtivos.Remove(tagAlterada);
                AplicarFiltroEIrParaPagina(0);
            });
        }

        if (conteudoFiltroSistema is RectTransform rt)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    private void ExpandirFiltroSistema()
    {
        if (conteudoFiltroSistema != null) conteudoFiltroSistema.gameObject.SetActive(true);
        if (btnExpandirFiltroSistema != null) btnExpandirFiltroSistema.gameObject.SetActive(false);
        if (btnColapsarFiltroSistema != null) btnColapsarFiltroSistema.gameObject.SetActive(true);
    }

    private void ColapsarFiltroSistema()
    {
        if (conteudoFiltroSistema != null) conteudoFiltroSistema.gameObject.SetActive(false);
        if (btnColapsarFiltroSistema != null) btnColapsarFiltroSistema.gameObject.SetActive(false);
        if (btnExpandirFiltroSistema != null) btnExpandirFiltroSistema.gameObject.SetActive(true);
    }

    private void AplicarFiltroEIrParaPagina(int pagina)
    {
        fichasFiltradas = FiltrarPorBusca(FiltrarPorSistema(todasFichas));
        indicadorPaginas?.ConfigurarPaginas(fichasFiltradas.Length, itensPorPagina);
        IrParaPagina(pagina);
    }

    private FichaData[] FiltrarPorSistema(FichaData[] origem)
    {
        if (!habilitarFiltroSistema || sistemasAtivos.Count == 0) return origem;

        var resultado = new List<FichaData>();
        foreach (FichaData d in origem)
        {
            if (d == null) continue;
            string sistema = sistemaPorFichaDndId.TryGetValue(d.fichas_dnd_id, out string s) ? s : "Desconhecido";
            if (sistemasAtivos.Contains(sistema)) resultado.Add(d);
        }
        return resultado.ToArray();
    }

    // ── Busca (nome / raça / classe) ─────────────────────────────────────────

    private void OnBuscaAlterada(string texto)
    {
        textoBuscaAtual = texto?.Trim() ?? "";
        AplicarFiltroEIrParaPagina(0);
    }

    private FichaData[] FiltrarPorBusca(FichaData[] origem)
    {
        if (string.IsNullOrWhiteSpace(textoBuscaAtual)) return origem;

        string termo = textoBuscaAtual.ToLowerInvariant();
        var resultado = new List<FichaData>();
        foreach (FichaData d in origem)
        {
            if (d == null) continue;
            if (ContemTermo(d.name_pc, termo) || ContemTermo(d.race_pc, termo) || ContemTermo(d.class_pc, termo))
                resultado.Add(d);
        }
        return resultado.ToArray();
    }

    private static bool ContemTermo(string valor, string termoEmMinusculas)
        => !string.IsNullOrEmpty(valor) && valor.ToLowerInvariant().Contains(termoEmMinusculas);

    // Setadas por ProximaPagina/PaginaAnterior logo antes de chamar IrParaPagina, e consumidas
    // (resetadas) na mesma chamada — assim só a navegação explícita por botão anima com slide;
    // trocas de página vindas de filtro/busca ou da carga inicial (AplicarFiltroEIrParaPagina)
    // continuam instantâneas, mesmo padrão de CarrosselNoticias só animar em ProximaNoticia/
    // NoticiaAnterior, nunca na montagem inicial.
    private bool proximaNavegacaoDeveAnimar;
    private int  direcaoProximaNavegacao = 1;

    public void IrParaPagina(int pagina)
    {
        int totalPaginas = Mathf.Max(1, Mathf.CeilToInt((float)fichasFiltradas.Length / itensPorPagina));
        int novaPagina = Mathf.Clamp(pagina, 0, totalPaginas - 1);
        bool animar = proximaNavegacaoDeveAnimar && novaPagina != paginaAtual;
        int  direcao = direcaoProximaNavegacao;
        proximaNavegacaoDeveAnimar = false;

        paginaAtual = novaPagina;

        int inicio = paginaAtual * itensPorPagina;
        int fim = Mathf.Min(inicio + itensPorPagina, fichasFiltradas.Length);
        int quantidade = Mathf.Max(0, fim - inicio);

        FichaData[] dadosDaPagina = new FichaData[quantidade];
        Array.Copy(fichasFiltradas, inicio, dadosDaPagina, 0, quantidade);

        indicadorPaginas?.AtualizarPaginaAtiva(paginaAtual);
        PopulateFromData(dadosDaPagina, animar, direcao);
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

    private void PopulateFromData(FichaData[] data, bool animarTransicao = false, int direcaoTransicao = 1)
    {
        if (this == null || !gameObject.activeInHierarchy)
        {
            Debug.Log($"[FichaLoaderUI] PopulateFromData — abortado, componente destruído ou GameObject inativo.");
            return;
        }
        if (contentParent == null || fichaPrefab == null)
        {
            Debug.LogWarning($"[FichaLoaderUI] PopulateFromData '{gameObject.name}' — abortado, contentParent={(contentParent != null)}, fichaPrefab={(fichaPrefab != null)} (confira as referências no Inspector).");
            return;
        }
        if (data == null) data = new FichaData[0];
        Debug.Log($"[FichaLoaderUI] PopulateFromData '{gameObject.name}' — {data.Length} ficha(s) nesta página.");

        if (populateRoutineCoroutine != null)
        {
            StopCoroutine(populateRoutineCoroutine);
            populateRoutineCoroutine = null;
        }

        populateRoutineCoroutine = StartCoroutine(PopulateRoutineWrapper(data, animarTransicao, direcaoTransicao));
    }

    private IEnumerator PopulateRoutineWrapper(FichaData[] data, bool animarTransicao, int direcaoTransicao)
    {
        // O ScrollRect é desabilitado pra toda a sequência (slide + repopulação, ou só a
        // repopulação instantânea) — se ficasse ativo no meio, ele brigaria com o
        // reposicionamento manual do slide (ScrollRect também escreve em content.anchoredPosition).
        bool scrollEstavaAtivo = parentScrollRect != null && parentScrollRect.enabled;
        if (parentScrollRect != null) parentScrollRect.enabled = false;

        if (animarTransicao)
            yield return TransicaoDeslizantePagina.ExecutarAsync(contentParent as RectTransform, direcaoTransicao, () => SafePopulateRoutine(data));
        else
            yield return SafePopulateRoutine(data);

        if (parentScrollRect != null)
        {
            parentScrollRect.enabled = scrollEstavaAtivo;
            parentScrollRect.verticalNormalizedPosition = 1f;
        }

        populateRoutineCoroutine = null;
    }

    private string ObterCaminhoImagemFicha(int generalId, int dndId)
    {
        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        
        LocalFichaData snapshot;
        if (LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId, generalId, dndId, out snapshot))
        {
            return snapshot.token_image_path;
        }
        
        return "";
    }

    private IEnumerator SafePopulateRoutine(FichaData[] data)
    {
        bool sucesso = false;
        try
        {
            // O ScrollRect (se houver) já foi pausado pelo chamador (PopulateRoutineWrapper) por
            // toda a sequência — inclusive durante o slide de troca de página, se houver — não
            // pausa/reativa aqui de novo pra não reativá-lo cedo demais no meio do slide.

            // 2. Limpeza segura
            if (clearBeforeInstantiate)
            {
                for (int i = contentParent.childCount - 1; i >= 0; i--)
                {
                    Transform child = contentParent.GetChild(i);
                    if (child != null)
                    {
                        child.gameObject.SetActive(false);
                        Destroy(child.gameObject);
                    }
                }
                contentParent.DetachChildren();
            }
            sucesso = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FichaLoaderUI] Erro ao limpar lista: {e.Message}");
            PopupManager.Instance?.MostrarErro("Erro", "Falha ao carregar a lista de fichas.");
            yield break;
        }

        if (!sucesso) yield break;

        yield return null; // aguarda os Destroys

        // 3. Criação
        int criados = 0;
        foreach (var d in data)
        {
            if (d == null) continue;
            try
            {
                GameObject go = Instantiate(fichaPrefab, contentParent);
                go.SetActive(true);

                var ui = go.GetComponent<FichaUI>();
                if (ui != null)
                {
                    string caminhoImagem = ObterCaminhoImagemFicha(d.fichas_general_id, d.fichas_dnd_id);
                    ui.SetFicha(d.name_pc, d.level_pc, d.race_pc, d.class_pc, d.fichas_general_id, d.fichas_dnd_id, caminhoImagem);
                }
                criados++;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FichaLoaderUI] Falha ao criar item de ficha: {e.Message}");
            }
        }

        // 4. Força atualização do Canvas
        Canvas.ForceUpdateCanvases();
        if (contentParent is RectTransform rt)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

        // ScrollRect reativado pelo chamador (PopulateRoutineWrapper), não aqui.

        Debug.Log($"[FichaLoaderUI] SafePopulateRoutine '{gameObject.name}' — concluído, {criados}/{data.Length} card(s) instanciado(s) em '{contentParent.name}'.");

        if (criados == 0 && data.Length > 0)
            PopupManager.Instance?.MostrarErro("Erro", "Não foi possível exibir as fichas. Tente novamente.");

        if (criados > 0)
            OnPopuladoComSucesso?.Invoke();
    }

    /// <summary>
    /// Popula todos os campos de restauração a partir de um snapshot de ficha.
    /// Deve ser chamado ANTES de ativar a tela de criação para que os seletores
    /// encontrem os dados no OnEnable.
    /// </summary>
    public static void CarregarRestauracaoDoSnapshot(LocalFichaData snap)
    {
        if (snap == null) return;

        GlobalFichaData.nomePersonagem  = snap.name_pc ?? "";
        GlobalFichaData.levelPersonagem = snap.level_pc > 0 ? snap.level_pc : 1;

        // Preferir novo campo raca para IDs de restauração (fallback para campos legados)
        bool temRacaNova = snap.raca != null && !string.IsNullOrEmpty(snap.raca.raca_id);
        GlobalFichaData.raceIdRestauracao  = snap.raca?.raca_id ?? "";
        GlobalFichaData.classIdRestauracao = snap.classes?.PrimaryClass()?.class_id ?? "";
        if (temRacaNova && !string.IsNullOrEmpty(snap.raca.raca_nome))
            GlobalFichaData.racaPersonagem = snap.raca.raca_nome;

        // Subraça — preferir snap.raca se disponível, fallback para race_info_json
        // Subraça — vem de snap.raca (novo)
        GlobalFichaData.subraceIdRestauracao = snap.raca?.subraca_id ?? "";

        // Raça — RaceSelector.RestauracaoInfo constrói-se a partir de snap.raca
        RaceSelector.RestauracaoInfo = null;
        if (temRacaNova)
        {
            RaceSelector.RestauracaoInfo = new RaceSelector.RacaInfoData
            {
                subrace_id   = snap.raca.subraca_id   ?? "",
                subrace_name = snap.raca.subraca_nome ?? "",
                skills    = snap.raca.skills    ?? new System.Collections.Generic.List<string>(),
                armors    = snap.raca.armors    ?? new System.Collections.Generic.List<string>(),
                weapons   = snap.raca.weapons   ?? new System.Collections.Generic.List<string>(),
                tools     = snap.raca.tools     ?? new System.Collections.Generic.List<string>(),
                languages = snap.raca.languages ?? new System.Collections.Generic.List<string>(),
            };
        }

        // Classe + subclasse — lê de snap.classes (campos de proficiência já incluídos em ClasseData)
        ClassSelector.RestauracaoInfo = null;
        ClassSelector.RestauracaoInfoPorClasse.Clear();
        GlobalFichaData.subclassIdRestauracao = "";

        if (snap.classes != null && snap.classes.Count > 0)
        {
            foreach (var cd in snap.classes)
            {
                if (cd == null || string.IsNullOrEmpty(cd.class_id)) continue;

                // Constrói ClasseInfoData totalmente a partir dos campos diretos de ClasseData
                var info = new ClassSelector.ClasseInfoData
                {
                    subclass_id   = cd.subclass_id   ?? "",
                    subclass_name = cd.subclass_name ?? "",
                    saves     = cd.saves     ?? new System.Collections.Generic.List<string>(),
                    skills    = cd.skills    ?? new System.Collections.Generic.List<string>(),
                    armors    = cd.armors    ?? new System.Collections.Generic.List<string>(),
                    weapons   = cd.weapons   ?? new System.Collections.Generic.List<string>(),
                    tools     = cd.tools     ?? new System.Collections.Generic.List<string>(),
                    languages = cd.languages ?? new System.Collections.Generic.List<string>(),
                    equipment = cd.equipment ?? new System.Collections.Generic.List<string>(),
                    asi_feats = cd.asi_feats ?? new System.Collections.Generic.List<string>(),
                    class_powers        = new System.Collections.Generic.List<ClassSelector.ClassPowerData>(),
                    subclass_powers     = new System.Collections.Generic.List<ClassSelector.ClassPowerData>(),
                    class_power_choices = new System.Collections.Generic.List<ClassSelector.ClassPowerChoiceData>(),
                };
                foreach (var p in cd.class_powers ?? new System.Collections.Generic.List<ClassPowerEntry>())
                    info.class_powers.Add(new ClassSelector.ClassPowerData { id = p.id, name = p.name, level = p.level, description = p.description });
                foreach (var p in cd.subclass_powers ?? new System.Collections.Generic.List<ClassPowerEntry>())
                    info.subclass_powers.Add(new ClassSelector.ClassPowerData { id = p.id, name = p.name, level = p.level, description = p.description });
                foreach (var c in cd.class_power_choices ?? new System.Collections.Generic.List<ClassPowerChoiceEntry>())
                    info.class_power_choices.Add(new ClassSelector.ClassPowerChoiceData { feature_id = c.feature_id, feature_name = c.feature_name, level = c.level, selected_option = c.selected_option });

                ClassSelector.RestauracaoInfoPorClasse[cd.class_id] = info;

                if (cd.is_primary || ClassSelector.RestauracaoInfo == null)
                {
                    ClassSelector.RestauracaoInfo = info;
                    GlobalFichaData.subclassIdRestauracao = cd.subclass_id ?? "";
                    GlobalFichaData.classIdRestauracao    = cd.class_id;
                }
            }
        }

        // Multiclasse — deriva de snap.classes (levels per class)
        GlobalFichaData.modoMulticlasse = true;
        GlobalFichaData.multiclassesSelecionadas = new System.Collections.Generic.List<MulticlassData>();
        if (snap.classes != null && snap.classes.Count > 1)
        {
            foreach (var cd in snap.classes)
                if (cd != null)
                    GlobalFichaData.multiclassesSelecionadas.Add(new MulticlassData { className = cd.class_name, level = cd.level });
        }

        // Fallback: se classes tem ≤ 1 entrada mas level_pc_detalhe indica multiclasse (ex: "Clérigo 3 / Bárbaro 3"),
        // parseia o detalhe para recuperar todas as classes e seus níveis.
        if (GlobalFichaData.multiclassesSelecionadas.Count <= 1
            && !string.IsNullOrWhiteSpace(snap.level_pc_detalhe)
            && snap.level_pc_detalhe.Contains("/"))
        {
            var partes = snap.level_pc_detalhe.Split('/');
            var recuperadas = new System.Collections.Generic.List<MulticlassData>();
            bool parseOk = true;
            foreach (var parte in partes)
            {
                var tokens = parte.Trim().Split(' ');
                if (tokens.Length < 2 || !int.TryParse(tokens[tokens.Length - 1], out int lvl))
                { parseOk = false; break; }
                string className = string.Join(" ", System.Linq.Enumerable.Take(tokens, tokens.Length - 1)).Trim();
                if (string.IsNullOrEmpty(className)) { parseOk = false; break; }
                recuperadas.Add(new MulticlassData { className = className, level = lvl });
            }
            if (parseOk && recuperadas.Count > 1)
            {
                GlobalFichaData.multiclassesSelecionadas = recuperadas;
                Debug.Log($"[FichaLoaderUI] Multiclasse recuperado de level_pc_detalhe: {snap.level_pc_detalhe}");
            }
        }

        // Antecedente (usa nome como chave estável)
        BackgroundSelector.RestauracaoInfo = null;
        if (!string.IsNullOrEmpty(snap.background_info_json))
            try { BackgroundSelector.RestauracaoInfo = JsonUtility.FromJson<BackgroundSelector.DetalhesUpdate>(snap.background_info_json)?.background_info; }
            catch { }
        GlobalFichaData.backgroundIdRestauracao = BackgroundSelector.RestauracaoInfo?.nome ?? snap.background_id ?? "";

        // Sinaliza que os seletores devem reiniciar o fluxo com os dados restaurados
        GlobalFichaData.precisaRestaurarFluxo = true;
    }

    /// <summary>
    /// Trata o clique em uma ficha, redirecionando para criação (se incompleta) ou visualização (se completa)
    /// </summary>
    public void TratarCliqueFicha(int fichasGeneralId, int fichasDndId, string nomePersonagem, int levelPersonagem, bool fichaIncompleta)
    {
        // Salva a ficha em criação antes de navegar (se houver uma ativa)
        if (GlobalFichaData.fichasDndId > 0 && GlobalFichaData.fichasDndId != fichasDndId)
        {
            FichaSaveManager.Instance?.SalvarLocalSincronamente();
            // Limpa o cache de JSON para que os seletores recarreguem do disco ao abrir a nova ficha
            GlobalFichaData.inventarioJson  = null;
            GlobalFichaData.magiasJson      = null;
            GlobalFichaData.slotsMagiasJson = "";
            EquipmentSelector.PrecisaCarregarEquipamentos = true;
            SpellSelector.PrecisaCarregarMagias           = true;
        }

        GlobalFichaData.fichasGeneralId = fichasGeneralId;
        GlobalFichaData.fichasDndId = fichasDndId;

        if (fichaIncompleta)
        {
            Debug.Log($"[FichaLoaderUI] Ficha incompleta. Exibindo popup de escolha.");

            int    pid  = DataManager.Instance != null ? DataManager.Instance.GetCurrentId()      : 0;
            string sid  = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
            LocalFichaStorage.TryGetFichaSnapshot(sid, pid, fichasGeneralId, fichasDndId, out LocalFichaData snap);

            int   gId   = fichasGeneralId;
            int   dId   = fichasDndId;
            string nome = nomePersonagem;
            int   lvl   = levelPersonagem;

            PopupManager.Instance?.MostrarEscolha(
                "Ficha Incompleta",
                "Esta ficha ainda está em criação. Como deseja continuar?",
                labelOpcao1: "Continuar Criação",
                onOpcao1: () =>
                {
                    // Portão central: reseta tudo primeiro, incondicionalmente. Reset e
                    // restauração não são mais mutuamente exclusivos (como este caminho tratava
                    // antes, evitando de propósito qualquer reset pra não apagar a restauração) —
                    // o portão reseta e a restauração abaixo sobrescreve por cima do estado limpo
                    // com os dados reais desta ficha.
                    GlobalFichaData.IniciarSessaoParaFicha(gId, dId);
                    if (snap != null) CarregarRestauracaoDoSnapshot(snap);
                    // MostrarTelaDiretamente (não NavegarParaTela/ShowScreen): evita chamar
                    // FichaEditorManager.OpenForNewFicha(), que resetaria de novo por cima da
                    // restauração acima.
                    toggleObjects?.MostrarTelaDiretamente(telaCriacaoFichaDnd);
                },
                labelOpcao2: "Abrir Ficha",
                onOpcao2: () =>
                {
                    GlobalFichaData.IniciarSessaoParaFicha(gId, dId);
                    GlobalFichaData.nomePersonagem = nome;
                    GlobalFichaData.levelPersonagem = lvl;
                    // MostrarTelaDiretamente (não NavegarParaTela/ShowScreen): ShowScreen chama
                    // FichaEditorManager.ClearSelectionContext() sempre que o índice não é
                    // criarFichaIndex, que zera GlobalFichaData.fichasDndId/fichasGeneralId
                    // (via ResetSelectionIds()) — exatamente os IDs setados acima. Isso fazia a
                    // ficha ficar presa em "Carregando..." pra sempre, sem erro.
                    toggleObjects?.MostrarTelaDiretamente(telaFichaDnd);
                }
            );
        }
        else
        {
            Debug.Log($"[FichaLoaderUI] Ficha completa. Abrindo para visualização...");
            GlobalFichaData.IniciarSessaoParaFicha(fichasGeneralId, fichasDndId);
            GlobalFichaData.nomePersonagem  = nomePersonagem;
            GlobalFichaData.levelPersonagem = levelPersonagem;
            FichaLogManager.IniciarParaFicha(fichasDndId);
            // MostrarTelaDiretamente, mesmo motivo dos outros casos acima: ShowScreen limparia
            // GlobalFichaData.fichasDndId/fichasGeneralId via ClearSelectionContext.
            toggleObjects?.MostrarTelaDiretamente(telaFichaDnd);
        }
    }
}