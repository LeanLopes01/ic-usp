using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class EquipmentSelector : MonoBehaviour
{
    // A cena tem múltiplos GameObjects com este script (uma por tela/contexto). FindObjectOfType
    // é não-determinístico entre eles, e já causou perda de itens: FichaSaveManager podia salvar
    // o meuInventario de uma instância errada (sem as compras do jogador) por cima da correta.
    // Instance rastreia a última instância habilitada — normalmente só uma tela de equipamento
    // fica ativa por vez, então isso reflete de forma confiável "a que o jogador está usando".
    public static EquipmentSelector Instance { get; private set; }

    [Header("Modo Delegado (painel 'Adicionar Item' da visualização de ficha)")]
    [Tooltip("Se definido, este EquipmentSelector vira só um catálogo de busca/compra: nunca carrega o inventário existente da ficha nem salva por conta própria — cada compra chama FichaGeralManager.AdicionarItemAoInventario diretamente, então só existe UMA fonte de verdade para o inventário. A lista 'inventário' deste painel passa a mostrar apenas os itens adicionados nesta sessão (não o inventário completo da ficha).")]
    public FichaGeralManager fichaGeralManagerAlvo;

    /// <summary>
    /// Resolve a instância "real" (não-delegada) de EquipmentSelector desta cena. Prefere Instance
    /// (setada em OnEnable), mas cai numa busca completa — incluindo objetos inativos, filtrando
    /// SEMPRE qualquer instância delegada (fichaGeralManagerAlvo != null) — pro caso de Instance
    /// ainda não ter sido setada. Bug real que isto corrige: ClassSelector/BackgroundSelector
    /// chamam isto (pra importar equipamento de classe/antecedente) ANTES do jogador ter aberto a
    /// aba "Equipamento" nesta sessão, ou seja, antes do OnEnable da instância principal
    /// (TelaEquipamentos) sequer rodar uma vez — Instance fica null, e o antigo fallback
    /// `FindObjectOfType&lt;EquipmentSelector&gt;(true)` (sem filtro) podia escolher a instância
    /// delegada (embutida no painel "Adicionar Item" de FichaGeralManager) por pura ordem interna
    /// do Unity. Itens importados nela ficam em memória normalmente, mas
    /// RotinaSalvarInventario() nela é sempre um no-op (yield break) — nada nunca chega no disco,
    /// e o inventário final da ficha salva vazio mesmo com os itens certos aparecendo em tela
    /// durante a criação.
    /// </summary>
    public static EquipmentSelector ResolveInstanciaPrincipal()
    {
        if (Instance != null) return Instance;

        EquipmentSelector fallback = null;
        foreach (var e in FindObjectsByType<EquipmentSelector>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (e.fichaGeralManagerAlvo == null) return e;
            fallback = e;
        }
        return fallback;
    }

    public string pastaItensLocalRelativa = "sistemas/quinta-edicao-srd/itens";

    [Header("UI - Listas (Tabelas)")]
    public Transform itemsContent;       
    public Transform inventoryContent;  

    [Header("UI - Painel de Pesquisa")]
    public GameObject searchContentPanel;
    public Button expandButton;
    public Button collapseButton;

    [Header("UI - Filtros (Catálogo)")]
    public TMP_InputField filtroPesquisaInput;
    public Button pesquisarButton;
    [Tooltip("Prefab de chip (toggle+texto) reutilizado nos dois painéis de filtro abaixo — mesmo componente FiltroChipToggleUI que FichaGeralManager.cs já usa.")]
    public GameObject filtroChipPrefab;
    public Button btnExpandirFiltroCategoria;
    public Button btnColapsarFiltroCategoria;
    public Transform conteudoFiltroCategoria;

    [Header("UI - Filtros (Inventário)")]
    public TMP_InputField filtroInventarioInput;
    public Button         pesquisarInventarioButton;
    public Button btnExpandirFiltroCategoriaInventario;
    public Button btnColapsarFiltroCategoriaInventario;
    public Transform conteudoFiltroCategoriaInventario;

    [Header("UI - Prefabs")]
    public GameObject itemPrefab;
    public GameObject inventoryPrefab;

    [Header("UI - Paginação (Catálogo)")]
    [Tooltip("Prefab simples de texto (TMP_Text) com o número da página. Se o prefab também tiver um componente Button, clicar nele navega direto para aquela página.")]
    public GameObject paginaPrefab;
    public Transform paginacaoContent;

    [Header("UI - Detalhes e Controles")]
    public TMP_Text detailsText;
    public TMP_Text feedbackText;

    [Header("UI - Tooltip (Prefab)")]
    [Tooltip("Mesmo componente TooltipUI usado em FichaGeralManager.cs — mostra nome/categoria e descrição ao passar o mouse sobre um item, aqui na tela de criação.")]
    public GameObject tooltipPrefab;
    private TooltipUI tooltipInstancia;

    [Header("Riqueza e Dinheiro")]
    public TMP_InputField ouroInputField; 
    public Button rolarRiquezaButton; 

    // Evento para notificar mudanças de ouro
    public static Action<float> OnOuroMudou;

    private Dictionary<int, ItemData> todosOsItens = new Dictionary<int, ItemData>();
    private Dictionary<string, List<ItemData>> itensPorCategoria = new Dictionary<string, List<ItemData>>();
    private List<InventorySaveItem> meuInventario = new List<InventorySaveItem>();
    private readonly List<CategoriaPaiData> categoriasHierarquicas = new List<CategoriaPaiData>();
    
    private float ouroPersonagem = 0f;
    private string ultimoDetalheRolagemOuro = "Ainda não rolado.";
    
    private List<EquipmentRowUI> listaUI_ItensVisiveis = new List<EquipmentRowUI>();

    private const int ITENS_POR_PAGINA_CATALOGO = 25;
    private int paginaAtualCatalogo = 0;
    private readonly List<GameObject> paginasCatalogoInstanciadas = new List<GameObject>();
    
    private string filtroPesquisaAtual = "";
    private string filtroInventarioAtual = "";
    // Multi-seleção (chips) — vazio = mostra tudo, sem filtro de categoria.
    private readonly HashSet<string> categoriasAtivas = new HashSet<string>();
    private readonly HashSet<string> categoriasInventarioAtivas = new HashSet<string>();
    private readonly List<GameObject> chipsFiltroCategoriaInstanciados = new List<GameObject>();
    private readonly List<GameObject> chipsFiltroCategoriaInventarioInstanciados = new List<GameObject>();

    private readonly List<CanvasGroup> _canvasGroupsTmp = new List<CanvasGroup>();
    private Coroutine _reativacaoItensCoroutine;

    // Evita recarregar o banco de dados se já estiver carregado
    [HideInInspector] public bool isLoaded = false;
    private int lastLoadedFichaDndId = -1;

    // --- CORREIO LOCAL (ANTECEDENTE) ---
    public static List<string> EquipamentosAntecedenteLocal = new List<string>();
    public static bool ImportacaoLocalPendente = false;
    
    // --- FLAG PARA CARREGAMENTO DEFERIDO ---
    public static bool PrecisaCarregarEquipamentos = false;

    [Serializable] private class ItemList { public ItemData[] items; }
    [Serializable] public class ItemData { public int id; public string nome; public string categoria; public string xml; public string idLogico; }
    [Serializable] private class CategoriaFilhoData { public string nome; public List<ItemData> itens = new List<ItemData>(); }
    [Serializable] private class CategoriaPaiData { public string nome; public bool expandido = true; public List<CategoriaFilhoData> filhos = new List<CategoriaFilhoData>(); }
    
    // InventoryWrapper/InventorySaveItem agora são as classes canônicas e públicas de
    // LocalFichaStorage.cs — não são mais declaradas aqui (evita a duplicata privada que só
    // "funcionava" por coincidência de nomes de campo via JsonUtility).


    void Awake()
    {
        if (ouroInputField != null) ouroInputField.onValueChanged.AddListener(OnOuroInputMudou);
        if (rolarRiquezaButton != null) rolarRiquezaButton.onClick.AddListener(RolarRiquezaInicial);
        if (filtroPesquisaInput != null)
        {
            filtroPesquisaInput.onValueChanged.AddListener(OnFiltroPesquisaMudou);
            filtroPesquisaInput.onSubmit.AddListener(_ => AplicarFiltroPesquisaAtual());
        }
        if (pesquisarButton != null) pesquisarButton.onClick.AddListener(AplicarFiltroPesquisaAtual);
        if (filtroInventarioInput != null)
        {
            filtroInventarioInput.onValueChanged.AddListener(OnFiltroInventarioMudou);
            filtroInventarioInput.onSubmit.AddListener(_ => AplicarFiltroInventario());
        }
        if (pesquisarInventarioButton != null) pesquisarInventarioButton.onClick.AddListener(AplicarFiltroInventario);
        // Os chips de categoria (catálogo/inventário) se auto-configuram via
        // FiltroChipsHelper.ConfigurarExpansao, chamado de ConfigurarFiltroChipsCategoria()/
        // AtualizarListaInventario() — não precisam de wiring manual aqui.
        if (expandButton != null) expandButton.onClick.AddListener(ExpandirPainelPesquisa);
        if (collapseButton != null) collapseButton.onClick.AddListener(RecolherPainelPesquisa);

        // Estado inicial: painel de pesquisa expandido
        if (searchContentPanel != null) searchContentPanel.SetActive(true);
        if (expandButton != null) expandButton.gameObject.SetActive(false);
        if (collapseButton != null) collapseButton.gameObject.SetActive(true);

        if (tooltipPrefab != null && tooltipInstancia == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            if (canvas != null)
            {
                GameObject tGo = Instantiate(tooltipPrefab, canvas.transform);
                tooltipInstancia = tGo.GetComponent<TooltipUI>();
                tooltipInstancia?.Esconder();
                tGo.transform.SetAsLastSibling();
            }
        }
    }

    void OnEnable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada += OnSessaoDeFichaIniciada;

        // Instâncias delegadas (fichaGeralManagerAlvo != null) NUNCA podem virar Instance — são só um
        // catálogo de busca embutido na visualização da ficha, nunca a dona real do inventário do
        // personagem, e RotinaSalvarInventario() nelas é sempre um no-op (yield break). Confirmado em
        // cena real (Desktop Principal.unity): a instância delegada fica habilitada ao mesmo tempo que
        // a instância real do fluxo de criação — sem esta checagem, Instance podia acabar apontando
        // pra delegada por pura ordem de OnEnable, fazendo ClassSelector/BackgroundSelector importarem
        // equipamento nela e nunca ser salvo em disco (itens de classe/antecedente somem da ficha).
        if (fichaGeralManagerAlvo == null)
            Instance = this;

        LimparDetalhes();
        EsconderTooltip();

        Debug.Log($"[EquipmentSelector][DIAG] OnEnable — instanceId={GetInstanceID()} delegado={(fichaGeralManagerAlvo != null)} " +
            $"activeInHierarchy={gameObject.activeInHierarchy} isLoaded={isLoaded} PrecisaCarregarEquipamentos={PrecisaCarregarEquipamentos} " +
            $"lastLoadedFichaDndId={lastLoadedFichaDndId} GlobalFichaData.fichasDndId={GlobalFichaData.fichasDndId} meuInventario.Count={meuInventario.Count}");

        // Só inicia coroutine se o GameObject está realmente ativo na hierarquia
        if (gameObject.activeInHierarchy) TentarCarregarOuAtualizar();
    }

    void OnDisable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada -= OnSessaoDeFichaIniciada;

        // Limpa TUDO que estava renderizado/carregado nesta instância ao sair da tela — pedido
        // explícito do usuário. Esta instância NUNCA é destruída entre fichas (só desativada/
        // reativada — ver comentário de Instance no topo do arquivo), então sem isto, linhas do
        // catálogo/inventário e o próprio meuInventario de uma ficha continuavam em memória mesmo
        // depois do jogador sair da criação — se o gatilho de "ficha mudou"
        // (lastLoadedFichaDndId vs GlobalFichaData.fichasDndId, ver CarregarTudo) por qualquer
        // motivo não disparasse corretamente entre uma ficha e outra, a PRÓXIMA ficha aberta nesta
        // mesma instância herdava esse estado (catálogo nunca reconstruído, itens de inventário de
        // uma ficha vazando pra outra). Forçar isLoaded=false garante que CarregarTudo() rode por
        // inteiro (recarrega inventário do disco + re-renderiza tudo do zero) na próxima vez que
        // esta tela abrir, mesmo que fichaMudou seja calculado errado por algum outro motivo.
        Debug.Log($"[EquipmentSelector][DIAG] OnDisable — instanceId={GetInstanceID()} limpando estado. " +
            $"meuInventario.Count(antes)={meuInventario.Count} itemsContent.childCount={(itemsContent != null ? itemsContent.childCount : -1)} " +
            $"inventoryContent.childCount={(inventoryContent != null ? inventoryContent.childCount : -1)}");

        LimparConteudoRenderizado(itemsContent);
        LimparConteudoRenderizado(inventoryContent);
        listaUI_ItensVisiveis.Clear();

        LimparEstadoInventarioLocal();
        isLoaded = false;

        // Nada resetava searchContentPanel — se o jogador expandia o painel de pesquisa numa ficha,
        // ele continuava expandido pra sempre depois (mesma instância nunca destruída, ver acima),
        // inclusive na primeira tela de uma ficha nova. Volta pro estado padrão (recolhido) ao sair.
        RecolherPainelPesquisa();
    }

    private static void LimparConteudoRenderizado(Transform content)
    {
        if (content == null) return;
        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);
    }

    /// <summary>
    /// Gatilho independente de OnEnable/Start, pro caso deste passo virar um GameObject sempre
    /// ativo (mesma correção já aplicada em ClassSelector pro bug de CanvasGroup/layout preso após
    /// SetActive(false)-&gt;(true)) — nesse cenário OnEnable nunca mais dispara de novo, e sem isto
    /// o inventário/equipamento de uma ficha ANTERIOR continuaria em memória mesmo depois de
    /// criar/abrir outra ficha. GlobalFichaData.OnSessaoDeFichaIniciada dispara em TODA troca de
    /// ficha, independente do ciclo de vida do GameObject.
    /// </summary>
    private void OnSessaoDeFichaIniciada()
    {
        PrecisaCarregarEquipamentos = true;
        Debug.Log($"[EquipmentSelector][DIAG] OnSessaoDeFichaIniciada — instanceId={GetInstanceID()} " +
            $"activeInHierarchy={gameObject.activeInHierarchy} GlobalFichaData.fichasDndId={GlobalFichaData.fichasDndId} " +
            $"lastLoadedFichaDndId={lastLoadedFichaDndId} meuInventario.Count={meuInventario.Count}");
        if (gameObject.activeInHierarchy) TentarCarregarOuAtualizar();
    }

    private void TentarCarregarOuAtualizar()
    {
        // Se PrecisaCarregarEquipamentos está true, carregar agora
        // Se estiver false, verificar se já foi carregado antes
        if (PrecisaCarregarEquipamentos || !isLoaded)
        {
            Debug.Log($"[EquipmentSelector][DIAG] TentarCarregarOuAtualizar — ramo StartCoroutine(CarregarTudo()) " +
                $"(PrecisaCarregarEquipamentos={PrecisaCarregarEquipamentos} isLoaded={isLoaded})");
            PrecisaCarregarEquipamentos = false;
            StartCoroutine(CarregarTudo());
        }
        else if (ImportacaoLocalPendente)
        {
            Debug.Log("[EquipmentSelector][DIAG] TentarCarregarOuAtualizar — ramo AplicarImportacaoAntecedenteLocal (ImportacaoLocalPendente=true)");
            // Aplica o import do antecedente sobre o inventário já em memória,
            // sem recarregar do disco (evita perder itens de raça/classe).
            StartCoroutine(AplicarImportacaoAntecedenteLocal());
        }
        else
        {
            Debug.Log($"[EquipmentSelector][DIAG] TentarCarregarOuAtualizar — ramo AtualizarListaInventario direto " +
                $"(nada mudou: PrecisaCarregarEquipamentos=false isLoaded=true ImportacaoLocalPendente=false) meuInventario.Count={meuInventario.Count}");
            GarantirCatalogoRenderizado();
            AtualizarListaInventario();
        }
    }

    // OnDisable agora limpa itemsContent (ver comentário lá) — qualquer caminho que NÃO passe pelo
    // corpo completo de CarregarTudo (o "ramo direto" acima, ou a saída antecipada dentro de
    // CarregarTudo) precisa checar isto antes de só atualizar o inventário, senão o catálogo fica
    // vazio pra sempre depois de um ciclo OnDisable->OnEnable da MESMA ficha (ex: navegar entre
    // passos do assistente de criação sem trocar de ficha) — reproduzido no log: TentarCarregarOuAtualizar
    // caindo direto no ramo "nada mudou" ou CarregarTudo na saída antecipada, ambos sem nenhuma
    // chamada a RecarregarVisualizacaoCategoriasEItens(), com itemsContent já vazio por causa do
    // OnDisable anterior.
    private void GarantirCatalogoRenderizado()
    {
        if (itemsContent == null || todosOsItens.Count == 0) return;
        if (itemsContent.childCount > 0) return;

        Debug.Log("[EquipmentSelector][DIAG] GarantirCatalogoRenderizado — itemsContent estava vazio, re-renderizando catálogo.");
        RecarregarVisualizacaoCategoriasEItens();
    }

    private IEnumerator CarregarTudo()
    {
        int fichaAtualId = GlobalFichaData.fichasDndId;
        bool fichaMudou = lastLoadedFichaDndId != fichaAtualId;

        Debug.Log($"[EquipmentSelector][DIAG] CarregarTudo início — instanceId={GetInstanceID()} fichaAtualId={fichaAtualId} " +
            $"lastLoadedFichaDndId={lastLoadedFichaDndId} fichaMudou={fichaMudou} isLoaded={isLoaded} " +
            $"ImportacaoLocalPendente={ImportacaoLocalPendente} meuInventario.Count(antes)={meuInventario.Count}");

        if (fichaMudou)
        {
            LimparEstadoInventarioLocal();
        }

        if (isLoaded && !fichaMudou && ImportacaoLocalPendente == false)
        {
            Debug.Log("[EquipmentSelector][DIAG] CarregarTudo — saída antecipada (já carregado, mesma ficha, sem import pendente): " +
                "GarantirCatalogoRenderizado() + AtualizarListaInventario()");
            GarantirCatalogoRenderizado();
            AtualizarListaInventario();
            yield break;
        }

        // 1. Carrega o catálogo local e monta a hierarquia de categorias/chips de filtro — dado
        // ESTÁTICO (conteúdo do SRD, independe de qual ficha está sendo criada), então só precisa
        // rodar uma vez por sessão do app.
        if (todosOsItens.Count == 0)
        {
            CarregarCatalogoItensLocal();
            Debug.Log("[EquipmentSelector] Catálogo local: Itens=" + todosOsItens.Count + ", Categorias=" + itensPorCategoria.Count);
            ConstruirCategoriasHierarquicas();
            ConfigurarFiltroChipsCategoria();
        }

        // A RENDERIZAÇÃO da lista, ao contrário do carregamento acima, precisa rodar TODA VEZ que
        // esta corrotina executa (cada ficha nova, ou reabrir esta tela) — antes, isso ficava preso
        // dentro do "if (todosOsItens.Count == 0)" acima (via InicializarListasFixas(), removido),
        // então só rodava UMA VEZ EM TODA A SESSÃO DO APP: da segunda ficha em diante o catálogo
        // nunca mais era reconstruído, e o jogador sempre via as MESMAS linhas da primeiríssima
        // renderização daquela sessão — inclusive quando essa primeira renderização acontecia cedo
        // demais (ex: disparada de forma incidental por FichaCreator.SetScreenVisibility durante o
        // salvamento da ficha, antes da tela de equipamento estar de fato visível/ativa na
        // hierarquia), o que deixava as linhas com o texto padrão da prefab (nome/custo/peso nunca
        // atualizados visualmente) — sem nenhuma renderização seguinte pra corrigir isso.
        RecarregarVisualizacaoCategoriasEItens();

        // Modo delegado: não carrega o inventário real da ficha (FichaGeralManager é quem manda
        // nele) — este painel fica só como catálogo de busca, com a lista "inventário" vazia até
        // o jogador comprar algo nesta sessão.
        if (fichaGeralManagerAlvo != null)
        {
            lastLoadedFichaDndId = fichaAtualId;
            isLoaded = true;
            AtualizarListaInventario();
            yield break;
        }

        // 2. Carrega inventário salvo localmente
        InventoryWrapper inv = GlobalFichaData.inventarioJson;
        if (inv == null && DataManager.Instance != null)
        {
            string steamId = DataManager.Instance.GetCurrentSteamId();
            int playerId = DataManager.Instance.GetCurrentId();
            inv = LocalFichaStorage.LoadInventory(steamId, playerId, GlobalFichaData.fichasGeneralId, fichaAtualId);
        }

        // Distingue "não tem inventário salvo ainda" (ficha nova de verdade) de "tinha inventário
        // carregado" — só o primeiro caso deve cair no fluxo de RolarRiquezaInicial() abaixo. Antes,
        // quando este campo era uma string, um erro de parse conseguia apagar o inventário real (ver
        // comentário histórico em EquipmentSelector.cs sobre "Itens perdidos"); agora que
        // inventario_json é o objeto já desserializado por TryGetFichaSnapshot (não há mais uma
        // etapa de parse separada aqui pra falhar), essa classe de bug deixa de existir.
        bool inventarioExistia = false;
        if (inv?.itens != null)
        {
            meuInventario = inv.itens;
            ouroPersonagem = inv.gp;
            OnOuroMudou?.Invoke(ouroPersonagem);
            inventarioExistia = true;
        }

        // 3. Importação local pendente (antecedentes e outros)
        if (ImportacaoLocalPendente)
        {
            ImportacaoLocalPendente = false;
            meuInventario.RemoveAll(i => i.instanceId.StartsWith("LOCAL_BG_"));
            foreach (string cmd in EquipamentosAntecedenteLocal)
                ImportarComandoLocal(cmd, "LOCAL_BG_");
            yield return RotinaSalvarInventario();
        }

        lastLoadedFichaDndId = fichaAtualId;
        isLoaded = true;

        Debug.Log($"[EquipmentSelector][DIAG] CarregarTudo fim — instanceId={GetInstanceID()} lastLoadedFichaDndId(novo)={lastLoadedFichaDndId} " +
            $"inventarioExistia={inventarioExistia} meuInventario.Count(depois)={meuInventario.Count} ouroPersonagem={ouroPersonagem}");

        // Rola ouro automaticamente só pra personagem REALMENTE novo (sem inventário salvo).
        if (!inventarioExistia)
            RolarRiquezaInicial();
        else
            AtualizarListaInventario();
    }

    private IEnumerator AplicarImportacaoAntecedenteLocal()
    {
        ImportacaoLocalPendente = false;
        meuInventario.RemoveAll(i => i.instanceId.StartsWith("LOCAL_BG_"));
        foreach (string cmd in EquipamentosAntecedenteLocal)
            ImportarComandoLocal(cmd, "LOCAL_BG_");
        AtualizarListaInventario();
        yield return RotinaSalvarInventario();
    }

    public void ExpandirPainelPesquisa()
    {
        searchContentPanel?.SetActive(true);
        expandButton?.gameObject.SetActive(false);
        collapseButton?.gameObject.SetActive(true);
    }

    public void RecolherPainelPesquisa()
    {
        searchContentPanel?.SetActive(false);
        expandButton?.gameObject.SetActive(true);
        collapseButton?.gameObject.SetActive(false);
    }

    private void LimparEstadoInventarioLocal()
    {
        Debug.Log($"[EquipmentSelector][DIAG] LimparEstadoInventarioLocal — instanceId={GetInstanceID()} " +
            $"meuInventario.Count(antes)={meuInventario.Count} ouroPersonagem(antes)={ouroPersonagem}");
        meuInventario.Clear();
        ouroPersonagem = 0f;
    }

    private void CarregarCatalogoItensLocal()
    {
        try
        {
            todosOsItens.Clear();
            itensPorCategoria.Clear();

            string pasta = DndSrdLocalPath.Resolver(pastaItensLocalRelativa);
            if (!Directory.Exists(pasta))
            {
                Debug.LogError("[EquipmentSelector] Pasta local de itens não encontrada: " + pasta);
                return;
            }

            foreach (string arquivo in Directory.GetFiles(pasta, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    string xml = File.ReadAllText(arquivo, Encoding.UTF8).Trim().Replace("\uFEFF", "");
                    XDocument doc = XDocument.Parse(xml);
                    XElement root = doc.Element("item") ?? doc.Root;
                    if (root == null) continue;

                    string nome = root.Element("name")?.Value?.Trim();
                    if (string.IsNullOrEmpty(nome)) continue;

                    string categoria = root.Element("metadata")?.Element("category")?.Value?.Trim();
                    if (string.IsNullOrEmpty(categoria)) categoria = "Outros";

                    string idLogico = root.Attribute("id")?.Value;
                    int id = GerarIdDeterministico(idLogico, nome);

                    ItemData item = new ItemData
                    {
                        id = id,
                        nome = nome,
                        categoria = categoria,
                        xml = xml,
                        idLogico = idLogico
                    };

                    // Existem arquivos duplicados com o mesmo id lógico (ex: escudos/Escudo.xml e
                    // Escudo_2.xml, ambos id="shield"): um traz o bloco <armor>/<weapon> completo,
                    // o outro é um placeholder "Item importado do SRD" sem stats. Sem esta checagem,
                    // o placeholder podia sobrescrever a versão completa dependendo da ordem de leitura.
                    if (todosOsItens.TryGetValue(id, out ItemData existente) && existente != null)
                    {
                        bool existenteTemStats = existente.xml.Contains("<armor>") || existente.xml.Contains("<weapon>");
                        bool novoTemStats = xml.Contains("<armor>") || xml.Contains("<weapon>");
                        if (existenteTemStats && !novoTemStats) continue;
                    }

                    todosOsItens[id] = item;
                    if (!itensPorCategoria.ContainsKey(categoria)) itensPorCategoria[categoria] = new List<ItemData>();
                    itensPorCategoria[categoria].Add(item);
                }
                catch (Exception eArquivo)
                {
                    Debug.LogWarning("[EquipmentSelector] Falha ao ler item local em '" + arquivo + "': " + eArquivo.Message);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[EquipmentSelector] Erro no fallback local de itens: " + e.Message);
        }
    }

    private static int GerarIdDeterministico(string idLogico, string nome)
    {
        string chave = (idLogico ?? "") + "|" + (nome ?? "");
        unchecked
        {
            int hash = 23;
            for (int i = 0; i < chave.Length; i++) hash = (hash * 31) + chave[i];
            if (hash == int.MinValue) hash = int.MaxValue;
            hash = Mathf.Abs(hash);
            return hash == 0 ? 1 : hash;
        }
    }

    private void ConstruirCategoriasHierarquicas()
    {
        categoriasHierarquicas.Clear();

        Dictionary<string, CategoriaPaiData> porNomePai = new Dictionary<string, CategoriaPaiData>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in todosOsItens.Values.OrderBy(i => i.nome))
        {
            ObterCategoriaHierarquica(item, out string pai, out string filho);
            if (string.IsNullOrWhiteSpace(pai)) pai = "Outros";
            if (string.IsNullOrWhiteSpace(filho)) filho = "Geral";

            if (!porNomePai.TryGetValue(pai, out CategoriaPaiData parent))
            {
                parent = new CategoriaPaiData { nome = pai, expandido = true };
                porNomePai[pai] = parent;
                categoriasHierarquicas.Add(parent);
            }

            CategoriaFilhoData child = parent.filhos.FirstOrDefault(c => string.Equals(c.nome, filho, StringComparison.OrdinalIgnoreCase));
            if (child == null)
            {
                child = new CategoriaFilhoData { nome = filho };
                parent.filhos.Add(child);
            }

            child.itens.Add(item);
        }

        categoriasHierarquicas.Sort((a, b) => string.Compare(a.nome, b.nome, StringComparison.OrdinalIgnoreCase));
        foreach (var parent in categoriasHierarquicas)
        {
            parent.filhos.Sort((a, b) => string.Compare(a.nome, b.nome, StringComparison.OrdinalIgnoreCase));
            foreach (var child in parent.filhos)
            {
                child.itens.Sort((a, b) => string.Compare(a.nome, b.nome, StringComparison.OrdinalIgnoreCase));
            }
        }

    }

    /// <summary>Constrói/reconstrói os chips de categoria do catálogo a partir das categorias-pai
    /// reais já produzidas por ObterCategoriaHierarquica (via categoriasHierarquicas) — substitui o
    /// antigo categoriaDropdown (seleção única) por multi-seleção.</summary>
    private void ConfigurarFiltroChipsCategoria()
    {
        FiltroChipsHelper.ConfigurarExpansao(
            btnExpandirFiltroCategoria, btnColapsarFiltroCategoria, conteudoFiltroCategoria,
            filtroChipPrefab, categoriasHierarquicas.Select(c => c.nome),
            chipsFiltroCategoriaInstanciados, categoriasAtivas, RecarregarVisualizacaoCategoriasEItens);
    }

    private void OnFiltroPesquisaMudou(string texto)
    {
        filtroPesquisaAtual = NormalizarChave(texto);
        AplicarFiltroPesquisaAtual();
    }

    private void OnFiltroInventarioMudou(string texto)
    {
        filtroInventarioAtual = NormalizarChave(texto);
        AtualizarListaInventario();
    }

    private void AplicarFiltroInventario()
    {
        if (filtroInventarioInput != null)
            filtroInventarioAtual = NormalizarChave(filtroInventarioInput.text);
        AtualizarListaInventario();
    }

    // Categoria virtual usada nos chips de filtro do inventário pra itens customizados (id=-1/-2)
    // ou cujo id não bate mais com o catálogo carregado — sem categoria real no banco, mas ainda
    // precisam aparecer em algum chip, senão qualquer filtro de categoria ativo os esconde sempre.
    private const string CategoriaInventarioOutros = "Outros";

    private bool InventarioItemCombinaFiltro(InventorySaveItem item, string filtro)
    {
        if (item.id <= 0 || !todosOsItens.TryGetValue(item.id, out ItemData dbItem))
        {
            // Item customizado (id=-1/-2) ou cujo id não bate mais com o catálogo carregado: entra
            // na categoria virtual "Outros" — só some se o jogador excluir essa categoria dos chips
            // (antes, qualquer filtro de categoria ativo escondia esses itens incondicionalmente).
            if (categoriasInventarioAtivas.Count > 0 && !categoriasInventarioAtivas.Contains(CategoriaInventarioOutros)) return false;
            return string.IsNullOrWhiteSpace(filtro) || NormalizarChave(item.customName ?? "").Contains(filtro);
        }

        if (categoriasInventarioAtivas.Count > 0)
        {
            ObterCategoriaHierarquica(dbItem, out string pai, out _);
            if (!categoriasInventarioAtivas.Contains(pai)) return false;
        }

        return string.IsNullOrWhiteSpace(filtro) || NormalizarChave(dbItem.nome).Contains(filtro);
    }

    /// <summary>Reconstrói os chips de categoria do inventário da sessão — dinâmico, só mostra
    /// categorias realmente presentes em meuInventario (mesmo comportamento do antigo
    /// categoriaInventarioDropdown). Chamado a cada AtualizarListaInventario().</summary>
    private void ConfigurarFiltroChipsCategoriaInventario()
    {
        var categoriasPresentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var invItem in meuInventario)
        {
            if (invItem.id == -999) continue; // item de controle oculto (import_flag) — nunca vira chip

            if (invItem.id <= 0 || !todosOsItens.TryGetValue(invItem.id, out ItemData dbItem))
            {
                categoriasPresentes.Add(CategoriaInventarioOutros);
                continue;
            }

            ObterCategoriaHierarquica(dbItem, out string pai, out _);
            categoriasPresentes.Add(pai);
        }

        // Preserva os toggles ativos que ainda fazem sentido (categoria continua presente) —
        // FiltroChipsHelper.PopularChips reseta tagsAtivas ao reconstruir os chips, então captura
        // antes e restaura os que sobrevivem.
        var ativasAntes = new HashSet<string>(categoriasInventarioAtivas, StringComparer.OrdinalIgnoreCase);

        FiltroChipsHelper.ConfigurarExpansao(
            btnExpandirFiltroCategoriaInventario, btnColapsarFiltroCategoriaInventario, conteudoFiltroCategoriaInventario,
            filtroChipPrefab, categoriasPresentes.OrderBy(c => c, StringComparer.OrdinalIgnoreCase),
            chipsFiltroCategoriaInventarioInstanciados, categoriasInventarioAtivas, AtualizarListaInventario);

        if (ativasAntes.Count > 0)
        {
            foreach (var go in chipsFiltroCategoriaInventarioInstanciados)
            {
                var chip = go != null ? go.GetComponent<FiltroChipToggleUI>() : null;
                if (chip == null || !ativasAntes.Contains(chip.Tag)) continue;
                if (chip.toggle != null) chip.toggle.SetIsOnWithoutNotify(true);
                categoriasInventarioAtivas.Add(chip.Tag);
            }
        }
    }

    private void AplicarFiltroPesquisaAtual()
    {
        if (filtroPesquisaInput != null)
            filtroPesquisaAtual = NormalizarChave(filtroPesquisaInput.text);

        RecarregarVisualizacaoCategoriasEItens();
    }

    private void RecarregarVisualizacaoCategoriasEItens()
    {
        // Filtro/categoria mudou -> o conjunto de resultados mudou, então a página atual pode nem
        // existir mais no novo conjunto. Sempre volta pra primeira página nesse caso.
        paginaAtualCatalogo = 0;
        GarantirVisibilidadeContainer(itemsContent, "EquipmentItems");
        RenderizarItensCategoriaAtual();
    }

    private void RenderizarItensCategoriaAtual()
    {
        foreach (Transform child in itemsContent) Destroy(child.gameObject);
        listaUI_ItensVisiveis.Clear();

        string filtroNormalizado = filtroPesquisaAtual ?? string.Empty;

        // Monta a lista completa (já filtrada por categoria/pesquisa) antes de decidir o que exibir
        // — a paginação fatia esta lista, não a criação de UI diretamente.
        List<ItemData> itensFiltrados = new List<ItemData>();
        foreach (var parent in categoriasHierarquicas)
        {
            if (categoriasAtivas.Count > 0 && !categoriasAtivas.Contains(parent.nome)) continue;

            foreach (var child in parent.filhos)
                foreach (var item in child.itens)
                    if (ItemCombinaFiltro(item, filtroNormalizado))
                        itensFiltrados.Add(item);
        }

        int totalPaginas = Mathf.Max(1, Mathf.CeilToInt(itensFiltrados.Count / (float)ITENS_POR_PAGINA_CATALOGO));
        paginaAtualCatalogo = Mathf.Clamp(paginaAtualCatalogo, 0, totalPaginas - 1);

        int indiceInicial = paginaAtualCatalogo * ITENS_POR_PAGINA_CATALOGO;
        int quantidadeNestaPagina = Mathf.Clamp(itensFiltrados.Count - indiceInicial, 0, ITENS_POR_PAGINA_CATALOGO);

        for (int i = 0; i < quantidadeNestaPagina; i++)
        {
            ItemData item = itensFiltrados[indiceInicial + i];

            GameObject go = Instantiate(itemPrefab, itemsContent, false);
            go.SetActive(true);
            go.transform.localScale = Vector3.one;
            EquipmentRowUI itemRow = go.GetComponent<EquipmentRowUI>();

            ExtractItemDetails(item.xml, out string cost, out string weight, out string origin);
            itemRow.SetupItem(item.id, item.nome, cost, weight, origin, false, string.Empty, string.Empty, OnClickUnicoItem, OnClickDuploItem, null, ExtrairEstatisticasBasicas(item.xml));
            listaUI_ItensVisiveis.Add(itemRow);

            ConfigurarHoverTooltip(go, item.nome, item.categoria, ExtrairDescricaoXML(item.xml));
        }

        AtualizarPaginacaoCatalogo(itensFiltrados.Count, totalPaginas);

        Debug.Log("[EquipmentSelector] Categorias='" + string.Join(",", categoriasAtivas) + "' Itens filtrados=" + itensFiltrados.Count + " Página=" + (paginaAtualCatalogo + 1) + "/" + totalPaginas + " Filtro='" + filtroPesquisaAtual + "'");

        Canvas.ForceUpdateCanvases();
        var rect = itemsContent as RectTransform;
        if (rect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

        if (gameObject.activeInHierarchy)
        {
            if (_reativacaoItensCoroutine != null) StopCoroutine(_reativacaoItensCoroutine);
            _reativacaoItensCoroutine = StartCoroutine(ReforcarAtivacaoItensPorFrames());
        }
    }

    /// <summary>Recria os indicadores de página (prefab simples de texto) em paginacaoContent — um
    /// por página, numerados a partir de 1. A página atual aparece em negrito/sublinhada. Se o
    /// prefab tiver um Button, clicar nele chama IrParaPaginaCatalogo.</summary>
    private void AtualizarPaginacaoCatalogo(int totalItensFiltrados, int totalPaginas)
    {
        foreach (var go in paginasCatalogoInstanciadas)
            if (go != null) Destroy(go);
        paginasCatalogoInstanciadas.Clear();

        if (paginacaoContent == null || paginaPrefab == null) return;
        // Nada pra paginar (catálogo vazio pro filtro atual) -> não mostra nem a página "1" vazia.
        if (totalItensFiltrados == 0) return;

        for (int p = 0; p < totalPaginas; p++)
        {
            int paginaCapturada = p;
            GameObject go = Instantiate(paginaPrefab, paginacaoContent, false);
            go.SetActive(true);
            go.transform.localScale = Vector3.one;
            paginasCatalogoInstanciadas.Add(go);

            TMP_Text texto = go.GetComponentInChildren<TMP_Text>();
            if (texto != null)
                texto.text = (p == paginaAtualCatalogo) ? $"<b><u>{p + 1}</u></b>" : (p + 1).ToString();

            Button botao = go.GetComponentInChildren<Button>();
            if (botao != null)
            {
                botao.onClick.RemoveAllListeners();
                botao.onClick.AddListener(() => IrParaPaginaCatalogo(paginaCapturada));
            }
        }

        Canvas.ForceUpdateCanvases();
        var rectPaginacao = paginacaoContent as RectTransform;
        if (rectPaginacao != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rectPaginacao);
    }

    /// <summary>Navega para uma página específica do catálogo (0-indexada). Chamado pelo clique nos
    /// indicadores de página instanciados por AtualizarPaginacaoCatalogo.</summary>
    public void IrParaPaginaCatalogo(int pagina)
    {
        if (pagina == paginaAtualCatalogo) return;
        paginaAtualCatalogo = pagina;
        RenderizarItensCategoriaAtual();
    }

    private bool ItemCombinaFiltro(ItemData item, string filtroNormalizado)
    {
        if (string.IsNullOrWhiteSpace(filtroNormalizado)) return true;

        return NormalizarChave(item.nome).Contains(filtroNormalizado)
            || NormalizarChave(item.categoria).Contains(filtroNormalizado)
            || NormalizarChave(item.xml).Contains(filtroNormalizado);
    }

    private void ObterCategoriaHierarquica(ItemData item, out string pai, out string filho)
    {
        pai = "Equipamento de Aventura";
        filho = "Equipamentos Gerais";

        string categoria = item?.categoria ?? string.Empty;
        string nome = item?.nome ?? string.Empty;
        string categoriaNormalizada = NormalizarChave(categoria);
        string nomeNormalizado = NormalizarChave(nome);

        if (TentarDividirCategoriaExplicita(categoria, out string paiExplicito, out string filhoExplicito))
        {
            pai = string.IsNullOrWhiteSpace(paiExplicito) ? pai : paiExplicito;
            filho = string.IsNullOrWhiteSpace(filhoExplicito) ? filho : filhoExplicito;
            return;
        }

        if (EhArma(categoriaNormalizada, nomeNormalizado))
        {
            pai = "Armas";
            filho = InferirSubcategoriaArma(categoriaNormalizada, nomeNormalizado);
            return;
        }

        if (EhArmaduraOuEscudo(categoriaNormalizada, nomeNormalizado))
        {
            pai = "Armaduras e Escudos";
            filho = InferirSubcategoriaArmadura(categoriaNormalizada, nomeNormalizado);
            return;
        }

        if (EhItemMagico(categoriaNormalizada, nomeNormalizado))
        {
            pai = "Itens Mágicos";
            filho = InferirSubcategoriaMagica(categoriaNormalizada, nomeNormalizado);
            return;
        }

        if (EhFerramenta(categoriaNormalizada, nomeNormalizado))
        {
            pai = "Ferramentas";
            filho = InferirSubcategoriaFerramenta(categoriaNormalizada, nomeNormalizado);
            return;
        }

        if (EhMontariaOuVeiculo(categoriaNormalizada, nomeNormalizado))
        {
            pai = "Montarias e Veículos";
            filho = InferirSubcategoriaMontariaVeiculo(categoriaNormalizada, nomeNormalizado);
            return;
        }

        if (EhRiquezaOuCommodities(categoriaNormalizada, nomeNormalizado))
        {
            pai = "Riquezas e Bens de Comércio";
            filho = InferirSubcategoriaRiqueza(categoriaNormalizada, nomeNormalizado);
            return;
        }

        pai = "Equipamento de Aventura";
        filho = InferirSubcategoriaAventura(categoriaNormalizada, nomeNormalizado);
    }

    private bool TentarDividirCategoriaExplicita(string categoria, out string pai, out string filho)
    {
        pai = string.Empty;
        filho = string.Empty;

        if (string.IsNullOrWhiteSpace(categoria)) return false;

        string[] separadores = { " / ", " > ", " | ", " - ", ":" };
        foreach (string separador in separadores)
        {
            int indice = categoria.IndexOf(separador, StringComparison.OrdinalIgnoreCase);
            if (indice > 0 && indice < categoria.Length - separador.Length)
            {
                pai = categoria.Substring(0, indice).Trim();
                filho = categoria.Substring(indice + separador.Length).Trim();
                if (!string.IsNullOrWhiteSpace(pai) && !string.IsNullOrWhiteSpace(filho)) return true;
            }
        }

        return false;
    }

    private string InferirSubcategoriaArma(string categoriaNormalizada, string nomeNormalizado)
    {
        bool distancia = ContemAlgum(nomeNormalizado, "bow", "crossbow", "arcolongo", "arcocurto", "besta", "dardo", "funda", "blowgun", "zarabatana", "net", "rede")
            || ContemAlgum(categoriaNormalizada, "ranged", "distance", "distancia");

        bool simples = ContemAlgum(categoriaNormalizada, "simple", "simples")
            || ContemAlgum(nomeNormalizado, "adaga", "dagger", "clava", "club", "mace", "lanca", "spear", "javelin", "azagaia", "foice", "sickle", "cajado", "quarterstaff", "marteloleve", "lighthammer", "machadinha", "handaxe", "arcocurto", "shortbow", "dardo", "funda", "sling", "lightcrossbow", "bestaleve", "blowgun", "zarabatana");

        bool marciais = ContemAlgum(categoriaNormalizada, "martial", "marcial")
            || ContemAlgum(nomeNormalizado, "espadalonga", "longsword", "greatsword", "espadagrande", "battleaxe", "greataxe", "machadogrande", "halberd", "alabarda", "glaive", "maul", "warhammer", "rapier", "scimitar", "flail", "whip", "trident", "pike", "lance", "morningstar", "longbow", "arcolongo", "heavycrossbow", "bestapesada", "handcrossbow", "bestademao");

        if (distancia && simples) return "Armas Simples - À Distância";
        if (distancia && marciais) return "Armas Marciais - À Distância";
        if (!distancia && marciais) return "Armas Marciais - Corpo a Corpo";
        return "Armas Simples - Corpo a Corpo";
    }

    private string InferirSubcategoriaArmadura(string categoriaNormalizada, string nomeNormalizado)
    {
        if (ContemAlgum(categoriaNormalizada, "shield", "escudo") || ContemAlgum(nomeNormalizado, "shield", "escudo")) return "Escudos";
        if (ContemAlgum(categoriaNormalizada, "light", "leve") || ContemAlgum(nomeNormalizado, "light", "leve", "couro", "padded")) return "Armaduras Leves";
        if (ContemAlgum(categoriaNormalizada, "medium", "media") || ContemAlgum(nomeNormalizado, "medium", "media", "peitoral", "breastplate", "halfplate", "camisadecota")) return "Armaduras Médias";
        if (ContemAlgum(categoriaNormalizada, "heavy", "pesada") || ContemAlgum(nomeNormalizado, "heavy", "pesada", "placas", "plate", "chainmail", "cotademalha")) return "Armaduras Pesadas";
        return "Escudos";
    }

    private string InferirSubcategoriaMagica(string categoriaNormalizada, string nomeNormalizado)
    {
        if (ContemAlgum(categoriaNormalizada, "armor", "armadura") || ContemAlgum(nomeNormalizado, "armor", "armadura")) return "Armaduras Mágicas";
        if (ContemAlgum(categoriaNormalizada, "weapon", "arma") || ContemAlgum(nomeNormalizado, "weapon", "arma")) return "Armas Mágicas";
        if (ContemAlgum(categoriaNormalizada, "potion", "oleo", "oil") || ContemAlgum(nomeNormalizado, "potion", "pocao", "oleo", "oil")) return "Poções e Óleos";
        if (ContemAlgum(categoriaNormalizada, "scroll", "pergaminho") || ContemAlgum(nomeNormalizado, "scroll", "pergaminho")) return "Pergaminhos";
        if (ContemAlgum(categoriaNormalizada, "ring", "anel") || ContemAlgum(nomeNormalizado, "ring", "anel")) return "Anéis";
        if (ContemAlgum(categoriaNormalizada, "wand", "staff", "rod", "varinha", "cajado", "bastao") || ContemAlgum(nomeNormalizado, "wand", "staff", "rod", "varinha", "cajado", "bastao")) return "Varinhas, Cajados e Bastões";
        return "Itens Maravilhosos";
    }

    private string InferirSubcategoriaAventura(string categoriaNormalizada, string nomeNormalizado)
    {
        if (ContemAlgum(categoriaNormalizada, "ammo", "ammunition", "municao") || ContemAlgum(nomeNormalizado, "arrow", "bolt", "flecha", "virote", "agulha", "dart")) return "Munição";
        if (ContemAlgum(categoriaNormalizada, "arcanefocus", "focusarcano") || ContemAlgum(nomeNormalizado, "focoarcano", "cristal", "orb", "varinha", "cajado")) return "Focos Mágicos - Foco Arcano";
        if (ContemAlgum(categoriaNormalizada, "druidicfocus", "focodruidico") || ContemAlgum(nomeNormalizado, "focodruidico", "visco", "totem")) return "Focos Mágicos - Foco Druídico";
        if (ContemAlgum(categoriaNormalizada, "holysymbol", "simbolosagrado") || ContemAlgum(nomeNormalizado, "simbolosagrado", "amuleto", "emblema")) return "Focos Mágicos - Símbolo Sagrado";
        if (ContemAlgum(categoriaNormalizada, "consumable", "alchemical", "consumivel") || ContemAlgum(nomeNormalizado, "fogoalquimico", "aguabenta", "veneno")) return "Consumíveis Mundanos";
        if (ContemAlgum(categoriaNormalizada, "pack", "pacote") || ContemAlgum(nomeNormalizado, "pacote", "explorador", "estudioso", "dungeoneer", "scholar")) return "Pacotes de Classe";
        return "Equipamentos Gerais";
    }

    private string InferirSubcategoriaFerramenta(string categoriaNormalizada, string nomeNormalizado)
    {
        if (ContemAlgum(categoriaNormalizada, "artisan", "artesao") || ContemAlgum(nomeNormalizado, "ferramentasde", "suprimentosde", "smith", "alchemist")) return "Ferramentas de Artesão";
        if (ContemAlgum(categoriaNormalizada, "gaming", "jogo") || ContemAlgum(nomeNormalizado, "baralho", "dados", "xadrez")) return "Conjuntos de Jogos";
        if (ContemAlgum(categoriaNormalizada, "musical", "instrument") || ContemAlgum(nomeNormalizado, "alaude", "flauta", "lira", "tambor", "viol")) return "Instrumentos Musicais";
        return "Ferramentas Específicas";
    }

    private string InferirSubcategoriaMontariaVeiculo(string categoriaNormalizada, string nomeNormalizado)
    {
        if (ContemAlgum(categoriaNormalizada, "vehiclewater", "water", "aquatico") || ContemAlgum(nomeNormalizado, "barco", "gale", "galera", "navio", "ship")) return "Veículos Aquáticos";
        if (ContemAlgum(categoriaNormalizada, "vehicleland", "sela", "arreio") || ContemAlgum(nomeNormalizado, "carroca", "charrete", "sela", "arreio", "wagon", "cart")) return "Veículos Terrestres e Arreios";
        return "Animais";
    }

    private string InferirSubcategoriaRiqueza(string categoriaNormalizada, string nomeNormalizado)
    {
        if (ContemAlgum(categoriaNormalizada, "coin", "moeda") || ContemAlgum(nomeNormalizado, "cp", "sp", "gp", "pp", "moeda", "ouro", "prata", "platina", "cobre")) return "Moedas";
        if (ContemAlgum(categoriaNormalizada, "gem", "art", "gema", "obra") || ContemAlgum(nomeNormalizado, "gema", "joia", "obra", "arte")) return "Gemas e Obras de Arte";
        return "Commodities";
    }

    private bool EhArma(string categoriaNormalizada, string nomeNormalizado)
    {
        return ContemAlgum(categoriaNormalizada, "weapon", "arma", "simple", "martial")
            || ContemAlgum(nomeNormalizado, "sword", "espada", "axe", "machado", "spear", "lanca", "dagger", "adaga", "mace", "clava", "bow", "arco", "crossbow", "besta", "halberd", "alabarda", "glaive", "trident", "pike", "lance", "flail", "whip");
    }

    private bool EhArmaduraOuEscudo(string categoriaNormalizada, string nomeNormalizado)
    {
        return ContemAlgum(categoriaNormalizada, "armor", "armadura", "shield", "escudo")
            || ContemAlgum(nomeNormalizado, "armor", "armadura", "shield", "escudo", "chainmail", "plate", "couro");
    }

    private bool EhItemMagico(string categoriaNormalizada, string nomeNormalizado)
    {
        return ContemAlgum(categoriaNormalizada, "magic", "magi", "wondrous", "rarity")
            || ContemAlgum(nomeNormalizado, "potion", "pocao", "scroll", "pergaminho", "wand", "varinha", "ring", "anel", "attunement", "sintonizacao");
    }

    private bool EhFerramenta(string categoriaNormalizada, string nomeNormalizado)
    {
        return ContemAlgum(categoriaNormalizada, "tool", "ferrament", "artisan", "gaming", "instrument")
            || ContemAlgum(nomeNormalizado, "kit", "ferramenta", "suprimentos", "instrumento", "baralho", "alaude", "flauta");
    }

    private bool EhMontariaOuVeiculo(string categoriaNormalizada, string nomeNormalizado)
    {
        return ContemAlgum(categoriaNormalizada, "mount", "vehicle", "veiculo", "montaria")
            || ContemAlgum(nomeNormalizado, "cavalo", "mastim", "camelo", "carroca", "charrete", "sela", "barco", "navio", "galera", "camel", "horse", "dog", "wagon", "cart", "ship", "boat");
    }

    private bool EhRiquezaOuCommodities(string categoriaNormalizada, string nomeNormalizado)
    {
        return ContemAlgum(categoriaNormalizada, "coin", "currency", "trade", "commodity", "gem", "art")
            || ContemAlgum(nomeNormalizado, "cobre", "prata", "ouro", "platina", "cp", "sp", "gp", "pp", "gema", "joia", "arte", "trigo", "ovelha", "ferro", "barra");
    }

    // Normaliza um id lógico (ex: "explorer-pack", "scholars-pack") para comparação
    // tolerante a variações de singular/plural entre a definição de equipamento da
    // classe e o id real do item no catálogo (ex: "explorers-pack" no arquivo do item).
    private static string NormalizarIdLogico(string idLogico)
    {
        if (string.IsNullOrWhiteSpace(idLogico)) return string.Empty;

        string valor = idLogico.Trim().ToLowerInvariant();
        string[] partes = valor.Split('-');
        for (int i = 0; i < partes.Length; i++)
        {
            if (partes[i].Length > 1 && partes[i].EndsWith('s'))
                partes[i] = partes[i][..^1];
        }
        return string.Join("-", partes);
    }

    private static bool ContemAlgum(string texto, params string[] termos)
    {
        if (string.IsNullOrWhiteSpace(texto) || termos == null || termos.Length == 0) return false;
        for (int i = 0; i < termos.Length; i++)
        {
            string termo = termos[i];
            if (string.IsNullOrWhiteSpace(termo)) continue;
            if (texto.Contains(NormalizarChave(termo))) return true;
        }
        return false;
    }

    private static string NormalizarChave(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return string.Empty;

        string semAcento = valor.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder(semAcento.Length);
        foreach (char c in semAcento)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    // ==========================================================
    // MÉTODO ACESSADO PELO BACKGROUND SELECTOR PARA AUTO-SAVE
    // ==========================================================
    public IEnumerator RotinaReceberESalvarEquipamentosAntecedente(List<string> equipamentos)
    {
        VTTLog.Save($"[DIAG] RotinaReceberESalvarEquipamentosAntecedente chamada. isLoaded={isLoaded} catalogo={todosOsItens.Count} comandos={equipamentos?.Count ?? -1} inventarioAntes={meuInventario.Count} instanceId(this)={GetInstanceID()}");
        if (!isLoaded || todosOsItens.Count == 0) yield return CarregarTudo();

        ImportacaoLocalPendente = false;
        meuInventario.RemoveAll(i => i.instanceId.StartsWith("LOCAL_BG_"));
        foreach (string cmd in equipamentos)
            ImportarComandoLocal(cmd, "LOCAL_BG_");

        VTTLog.Save($"[DIAG] Após importar antecedente: inventarioDepois={meuInventario.Count} itens=[{string.Join(", ", meuInventario.Select(i => i.instanceId + ":" + i.id))}]");

        if (gameObject.activeInHierarchy) AtualizarListaInventario();
        yield return RotinaSalvarInventario();
    }

    // ==========================================================
    // MÉTODO ACESSADO PELO CLASS SELECTOR PARA AUTO-SAVE
    // ==========================================================
    public IEnumerator RotinaReceberESalvarEquipamentos(List<string> equipamentos)
    {
        VTTLog.Save($"[DIAG] RotinaReceberESalvarEquipamentos chamada. isLoaded={isLoaded} catalogo={todosOsItens.Count} comandos={equipamentos?.Count ?? -1} [{string.Join(" | ", equipamentos ?? new List<string>())}] inventarioAntes={meuInventario.Count} instanceId(this)={GetInstanceID()} fichasDndId={GlobalFichaData.fichasDndId}");

        // Removemos o StartCoroutine daqui. Agora ele apenas retorna o IEnumerator direto!
        if (!isLoaded || todosOsItens.Count == 0) yield return CarregarTudo();

        // Remove itens antigos da classe (para não duplicar caso ele mude de classe)
        meuInventario.RemoveAll(i => i.instanceId.StartsWith("CLASS_"));

        // Adiciona os novos itens
        foreach (string cmd in equipamentos)
        {
            ImportarComandoLocal(cmd, "CLASS_");
        }

        VTTLog.Save($"[DIAG] Após importar classe: inventarioDepois={meuInventario.Count} itens=[{string.Join(", ", meuInventario.Select(i => i.instanceId + ":" + i.id))}]");

        // Só atualiza a interface se a aba estiver ativa para evitar erros visuais
        if (gameObject.activeInHierarchy) AtualizarListaInventario();

        // Removemos o StartCoroutine daqui também!
        yield return RotinaSalvarInventario();
    }

    // BUSCA AVANÇADA NO BANCO DE DADOS — prioriza o id lógico exato (ou tolerante a
    // singular/plural, ex: "explorer-pack" vs "explorers-pack" no catálogo) antes de
    // cair em comparações de nome mais frágeis.
    private ItemData EncontrarItemLocal(string searchId, string fallbackName)
    {
        string searchIdNormalizado = NormalizarIdLogico(searchId);
        var dbItem = todosOsItens.Values.FirstOrDefault(i =>
            !string.IsNullOrEmpty(i.idLogico) && !string.IsNullOrEmpty(searchIdNormalizado) && NormalizarIdLogico(i.idLogico) == searchIdNormalizado
        );

        if (dbItem == null)
        {
            dbItem = todosOsItens.Values.FirstOrDefault(i =>
                (!string.IsNullOrEmpty(i.nome) && i.nome.Equals(searchId, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(i.nome) && i.nome.Equals(fallbackName, StringComparison.OrdinalIgnoreCase)) ||
                // Se o ID existir dentro do XML do banco de dados (Ex: id="greataxe" ou <id>greataxe</id>)
                (!string.IsNullOrEmpty(i.xml) && (i.xml.Contains($"\"{searchId}\"") || i.xml.Contains($">{searchId}<"))) ||
                // Se o nome for parecido
                (!string.IsNullOrEmpty(i.nome) && fallbackName.Length > 4 && i.nome.IndexOf(fallbackName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                (!string.IsNullOrEmpty(i.nome) && i.nome.Length > 4 && fallbackName.IndexOf(i.nome, StringComparison.OrdinalIgnoreCase) >= 0)
            );
        }

        return dbItem;
    }

    /// <summary>
    /// Descrição real (nome, categoria, resumo, custo, peso) de um item ou pacote do catálogo
    /// local, a partir de um "comando" de equipamento (mesmo formato usado em equipamentoEscolhido
    /// no ClassSelector/BackgroundSelector: "idLogico|Nome", "Nome" simples, "OURO:x" ou "CUSTOM:x").
    /// Usado por outras telas para mostrar a descrição real da opção que o jogador está vendo/selecionando.
    /// Retorna null se não encontrar nenhum item correspondente (chamador deve usar um texto genérico).
    /// </summary>
    public string ObterDescricaoParaComando(string comando)
    {
        if (string.IsNullOrEmpty(comando)) return null;
        if (comando.StartsWith("OURO:")) return null;

        if (todosOsItens.Count == 0) CarregarCatalogoItensLocal();

        string searchId = comando;
        string fallbackName = comando;
        if (comando.StartsWith("CUSTOM:"))
        {
            // "CUSTOM:x" não é necessariamente um item de verdade sem correspondente no catálogo —
            // é só o formato usado quando quem gerou a lista (ex: antecedente) não anexou um id
            // lógico junto do nome. Tenta casar pelo nome mesmo assim (ver ImportarComandoLocal).
            fallbackName = comando.Substring("CUSTOM:".Length);
            searchId = fallbackName;
        }
        else if (comando.Contains("|"))
        {
            string[] parts = comando.Split('|');
            searchId = parts[0];
            fallbackName = parts[1];
        }

        ItemData dbItem = EncontrarItemLocal(searchId, fallbackName);
        return dbItem != null ? FormatarDescricaoItem(dbItem) : null;
    }

    private void ImportarComandoLocal(string comando, string prefixo)
    {
        if (string.IsNullOrEmpty(comando)) return;

        if (comando.StartsWith("OURO:"))
        {
            if (float.TryParse(comando.Replace("OURO:", ""), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float valorExtra)) {
                ouroPersonagem += valorExtra;
                OnOuroMudou?.Invoke(ouroPersonagem);
            }
            return;
        }

        if (comando.StartsWith("CUSTOM:"))
        {
            // "CUSTOM:x" não tem id lógico anexado (ex: equipamento de antecedente, escrito como
            // texto livre), mas o nome muitas vezes bate com um item real do catálogo (ex: "Um kit
            // de disfarça" -> Disguise Kit) — antes isto desistia direto e virava um item fantasma
            // sem peso/custo/categoria reais. Tenta casar pelo nome antes de cair no fallback.
            string nomeCustom = comando.Substring("CUSTOM:".Length);
            ItemData dbItemCustom = EncontrarItemLocal(nomeCustom, nomeCustom);

            if (dbItemCustom != null)
            {
                meuInventario.Add(new InventorySaveItem {
                    instanceId = prefixo + Guid.NewGuid().ToString(), id = dbItemCustom.id, equipSlot = "Desequipado"
                });
                VTTLog.Save($"[DIAG] ImportarComandoLocal('{comando}') -> MATCH (custom) id={dbItemCustom.id} nome='{dbItemCustom.nome}' (prefixo={prefixo}) inventarioAgora={meuInventario.Count}");
            }
            else
            {
                meuInventario.Add(new InventorySaveItem {
                    instanceId = prefixo + Guid.NewGuid().ToString(), id = -2, equipSlot = "Desequipado", customName = nomeCustom
                });
                VTTLog.Save($"[DIAG] ImportarComandoLocal('{comando}') -> SEM MATCH (custom), salvo como custom '{nomeCustom}' (prefixo={prefixo}) inventarioAgora={meuInventario.Count}");
            }
            return;
        }

        // Separa o ID lógico (ex: greataxe) do texto do jogador (ex: Um machado grande)
        string searchId = comando;
        string fallbackName = comando;

        if (comando.Contains("|"))
        {
            string[] parts = comando.Split('|');
            searchId = parts[0];
            fallbackName = parts[1];
        }

        ItemData dbItem = EncontrarItemLocal(searchId, fallbackName);

        if (dbItem != null)
        {
            meuInventario.Add(new InventorySaveItem {
                instanceId = prefixo + Guid.NewGuid().ToString(), id = dbItem.id, equipSlot = "Desequipado"
            });
            VTTLog.Save($"[DIAG] ImportarComandoLocal('{comando}') -> MATCH id={dbItem.id} nome='{dbItem.nome}' (prefixo={prefixo}) inventarioAgora={meuInventario.Count}");
        }
        else
        {
            // Se não encontrar nada, salva como item customizado usando o nome de fallback
            meuInventario.Add(new InventorySaveItem {
                instanceId = prefixo + Guid.NewGuid().ToString(), id = -2, equipSlot = "Desequipado", customName = fallbackName
            });
            VTTLog.Save($"[DIAG] ImportarComandoLocal('{comando}') -> SEM MATCH, salvo como custom '{fallbackName}' (prefixo={prefixo}) inventarioAgora={meuInventario.Count}");
        }
    }

    private void ExtractItemDetails(string xml, out string cost, out string weight, out string origin)
    {
        cost = "-"; weight = "-"; origin = "-";
        if (string.IsNullOrEmpty(xml)) return;
        try {
            XDocument doc = XDocument.Parse(xml);
            XElement root = doc.Root;
            if (root != null) {
                var meta = root.Element("metadata");
                if (meta != null) {
                    cost = meta.Element("cost")?.Value ?? cost;
                    weight = meta.Element("weight") != null ? meta.Element("weight").Value + " lb" : weight;
                }
            }
        } catch { }
    }

    // Mesma prioridade description -> summary usada em FichaGeralManager.ExtrairDescricaoXML —
    // texto mostrado no tooltip de hover (ver ConfigurarHoverTooltip).
    private string ExtrairDescricaoXML(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "Sem detalhes.";
        try
        {
            XDocument doc = XDocument.Parse(xml);
            return doc.Descendants("description").FirstOrDefault()?.Value ?? doc.Descendants("summary").FirstOrDefault()?.Value ?? "Sem detalhes no XML.";
        }
        catch { return "Erro ao ler detalhes."; }
    }

    // Mesmo mecanismo de FichaGeralManager.ConfigurarHoverTooltip/MostrarTooltip/EsconderTooltip —
    // reutiliza o componente TooltipUI (segue o mouse, já cuida de não vazar pra fora da tela).
    private void ConfigurarHoverTooltip(GameObject go, string titulo, string subtitulo, string descricao)
    {
        EventTrigger trigger = go.GetComponent<EventTrigger>();
        if (trigger == null) trigger = go.AddComponent<EventTrigger>();

        EventTrigger.Entry entryEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entryEnter.callback.AddListener((data) => MostrarTooltip(titulo, subtitulo, descricao));
        trigger.triggers.Add(entryEnter);

        EventTrigger.Entry entryExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        entryExit.callback.AddListener((data) => EsconderTooltip());
        trigger.triggers.Add(entryExit);
    }

    private void MostrarTooltip(string titulo, string subtitulo, string descricao)
    {
        if (tooltipInstancia != null) tooltipInstancia.Mostrar(titulo, subtitulo, descricao);
    }

    private void EsconderTooltip()
    {
        if (tooltipInstancia != null) tooltipInstancia.Esconder();
    }

    // Extrai dano (arma), CA (armadura) e efeito básico (poções, itens mágicos, etc.) do
    // XML do item, para exibir na linha do EquipmentRowUI além de custo/peso.
    private static string ExtrairEstatisticasBasicas(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return string.Empty;
        try
        {
            XDocument doc = XDocument.Parse(xml);
            XElement root = doc.Root;
            if (root == null) return string.Empty;

            var partes = new List<string>();

            XElement weaponEl = root.Element("weapon");
            if (weaponEl != null)
            {
                string dano = FormatarDanoArma(weaponEl.Element("damage"));
                string tipoDano = weaponEl.Element("damage-type")?.Value?.Trim();
                if (!string.IsNullOrEmpty(dano))
                    partes.Add(string.IsNullOrEmpty(tipoDano) ? $"Dano: {dano}" : $"Dano: {dano} ({tipoDano})");
            }

            XElement armorEl = root.Element("armor");
            if (armorEl != null)
            {
                string ca = armorEl.Element("armor-class")?.Value?.Trim();
                if (!string.IsNullOrEmpty(ca))
                {
                    partes.Add($"CA: {ca}");
                }
                else
                {
                    // Escudos não têm CA fixa: usam armor-class-bonus (ex: +2) em vez de armor-class.
                    string acTexto = armorEl.Element("ac-text")?.Value?.Trim();
                    string bonusCa = armorEl.Element("armor-class-bonus")?.Value?.Trim();
                    if (!string.IsNullOrEmpty(acTexto)) partes.Add(acTexto);
                    else if (!string.IsNullOrEmpty(bonusCa)) partes.Add($"CA: +{bonusCa}");
                }
            }

            string efeito = root.Element("properties")?.Element("effect")?.Value?.Trim();
            if (!string.IsNullOrEmpty(efeito)) partes.Add(efeito);

            return string.Join(" | ", partes);
        }
        catch { return string.Empty; }
    }

    // Armas versáteis (ex: Espada Longa, Machado, Martelo de Guerra) guardam o dano em
    // <damage><one-handed>/<two-handed></damage> em vez de texto direto. Ler .Value nesse
    // caso concatena os dois valores sem separação (ex: "1d81d10") — trata os dois formatos.
    private static string FormatarDanoArma(XElement damageEl)
    {
        if (damageEl == null) return "";

        string umaMao = damageEl.Element("one-handed")?.Value?.Trim();
        string duasMaos = damageEl.Element("two-handed")?.Value?.Trim();
        if (!string.IsNullOrEmpty(umaMao) || !string.IsNullOrEmpty(duasMaos))
            return $"{umaMao} (1 mão) / {duasMaos} (2 mãos)";

        return damageEl.Value?.Trim() ?? "";
    }

    public void RolarRiquezaInicial()
    {
        int idClasse = GlobalFichaData.currentClassId; 
        int numDados = 4;
        int multiplicador = 10;

        switch (idClasse)
        {
            case 1: numDados = 2; multiplicador = 10; break;
            case 6: numDados = 5; multiplicador = 1; break;
            case 2: numDados = 3; multiplicador = 10; break;
            case 3: case 4: case 5: case 7: numDados = 5; multiplicador = 10; break;
            default: numDados = 4; multiplicador = 10; break; 
        }

        int soma = 0;
        List<int> rolagens = new List<int>();
        for (int i = 0; i < numDados; i++) {
            int valor = UnityEngine.Random.Range(1, 5);
            rolagens.Add(valor);
            soma += valor;
        }

        float totalGerado = soma * multiplicador;
        ouroPersonagem += totalGerado;
        OnOuroMudou?.Invoke(ouroPersonagem);
        ultimoDetalheRolagemOuro = $"Riqueza inicial: {numDados}d4 x {multiplicador} | Rolagens: {string.Join(" + ", rolagens)} = {soma} | Resultado: {totalGerado:0.##} GP";
        
        AtualizarListaInventario();
        MostrarFeedback($"Você rolou {numDados}d4 x {multiplicador} e obteve {totalGerado} GP!", false);
        StartCoroutine(RotinaSalvarInventario());
    }

    /// <summary>
    /// Retorna o peso total do inventário em libras
    /// </summary>
    public float ObterPesoTotal()
    {
        float pesoTotal = 0f;
        foreach (var invItem in meuInventario)
        {
            if (invItem.id <= 0 || invItem.id == -999) continue;
            
            if (todosOsItens.TryGetValue(invItem.id, out ItemData dbItem))
            {
                ExtractItemDetails(dbItem.xml, out _, out string weight, out _);
                if (!string.IsNullOrEmpty(weight) && weight != "-")
                {
                    float numPeso = ConverterPesoParaFloat(weight);
                    pesoTotal += numPeso;
                }
            }
        }
        return pesoTotal;
    }

    /// <summary>
    /// Retorna o ouro total do personagem
    /// </summary>
    public float ObterOuroTotal()
    {
        return ouroPersonagem;
    }

    private float ConverterCustoParaOuro(string costStr)
    {
        if (string.IsNullOrEmpty(costStr) || costStr == "-") return 0f;
        string lowerCost = costStr.ToLower().Trim();
        string numStr = new string(lowerCost.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray()).Replace(',', '.');
        if (!float.TryParse(numStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float valorOriginal)) return 0f;

        if (lowerCost.Contains("cp") || lowerCost.Contains("pc") || lowerCost.Contains("cobre")) return valorOriginal / 100f;
        if (lowerCost.Contains("sp") || lowerCost.Contains("pp") || lowerCost.Contains("prata")) return valorOriginal / 10f; 
        if (lowerCost.Contains("ep") || lowerCost.Contains("pe") || lowerCost.Contains("electro")) return valorOriginal / 2f;
        if (lowerCost.Contains("platina") || lowerCost.Contains("pl")) return valorOriginal * 10f; 
        
        return valorOriginal; 
    }

    private void OnOuroInputMudou(string novoValor)
    {
        if (float.TryParse(novoValor, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out float novoOuro))
        {
            ouroPersonagem = novoOuro;
            OnOuroMudou?.Invoke(ouroPersonagem);
        }
    }

    private void AtualizarListaInventario()
    {
        ConfigurarFiltroChipsCategoriaInventario();
        foreach (Transform child in inventoryContent) Destroy(child.gameObject);

        if (todosOsItens.Count == 0) CarregarCatalogoItensLocal();

        float pesoCalculado = 0f;

        foreach (var invItem in meuInventario)
        {
            if (invItem.id == -999) continue; // item de controle oculto (import_flag)
            if (!InventarioItemCombinaFiltro(invItem, filtroInventarioAtual)) continue;

            GameObject go = Instantiate(inventoryPrefab, inventoryContent, false);
            go.transform.localScale = Vector3.one;
            go.SetActive(true);
            EquipmentRowUI row = go.GetComponent<EquipmentRowUI>();

            if (invItem.id > 0 && todosOsItens.TryGetValue(invItem.id, out ItemData dbItem))
            {
                ExtractItemDetails(dbItem.xml, out string cost, out string weight, out string origin);
                pesoCalculado += ConverterPesoParaFloat(weight);
                row.SetupItem(dbItem.id, dbItem.nome, cost, weight, origin, true, invItem.instanceId, invItem.equipSlot, OnClickUnicoItem, OnClickDuploItem, OnMudarSlotEquipamento, ExtrairEstatisticasBasicas(dbItem.xml));
                ConfigurarHoverTooltip(go, dbItem.nome, dbItem.categoria, ExtrairDescricaoXML(dbItem.xml));
            }
            else
            {
                // Item customizado (id=-1/-2) ou cujo id não bate mais com o catálogo
                // carregado: nunca esconder — mostra pelo nome salvo, senão o jogador não
                // vê que o item existe no inventário.
                string nomeExibicao = string.IsNullOrWhiteSpace(invItem.customName) ? $"Item desconhecido (id {invItem.id})" : invItem.customName;
                row.SetupItem(-1, nomeExibicao, "-", "-", "Personalizado", true, invItem.instanceId, invItem.equipSlot, OnClickUnicoVirtual, OnClickDuploItem, OnMudarSlotEquipamento);
                ConfigurarHoverTooltip(go, nomeExibicao, "Personalizado", "Item personalizado — sem dados de catálogo.");
            }
        }

        if (ouroInputField != null && !ouroInputField.isFocused)
            ouroInputField.SetTextWithoutNotify(ouroPersonagem.ToString("0.##", CultureInfo.InvariantCulture));
        
        // Garantir que todos os dropdowns estão fechados no final
        var allDropdowns = inventoryContent.GetComponentsInChildren<TMP_Dropdown>();
        foreach (var dropdown in allDropdowns)
        {
            dropdown.Hide();
        }
    }

    private float ConverterPesoParaFloat(string weightString)
    {
        if (string.IsNullOrEmpty(weightString) || weightString == "-") return 0f;
        string num = new string(weightString.Where(c => char.IsDigit(c) || c == '.' || c == ',').ToArray()).Replace(',', '.');
        if (float.TryParse(num, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float res)) return res;
        return 0f;
    }

    private IEnumerator ReforcarAtivacaoItensPorFrames()
    {
        for (int frame = 0; frame < 8; frame++)
        {
            yield return null;
            for (int i = 0; i < listaUI_ItensVisiveis.Count; i++)
            {
                var row = listaUI_ItensVisiveis[i];
                if (row == null) continue;

                bool estavaInativo = !row.gameObject.activeSelf;
                row.ForceVisibleState();

                if (estavaInativo && row.gameObject.activeSelf)
                {
                    Debug.Log("[EquipmentSelector] Row reativado no frame " + frame + " idx=" + i + " nome='" + row.name + "'");
                }
            }
        }
    }

    private void GarantirVisibilidadeContainer(Transform content, string tag)
    {
        if (content == null) return;
        if (!content.gameObject.activeSelf) content.gameObject.SetActive(true);

        Transform atual = content;
        int nivel = 0;
        while (atual != null && nivel < 8)
        {
            atual.GetComponents(_canvasGroupsTmp);
            for (int i = 0; i < _canvasGroupsTmp.Count; i++)
            {
                var cg = _canvasGroupsTmp[i];
                if (cg == null) continue;
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }
            _canvasGroupsTmp.Clear();
            atual = atual.parent;
            nivel++;
        }

        Debug.Log("[" + tag + "] Container visibilidade reforçada.");
    }

    private void OnClickUnicoVirtual(EquipmentRowUI row)
    {
        var invItem = meuInventario.FirstOrDefault(i => i.instanceId == row.InstanceId);
        if (invItem != null) detailsText.text = $"<size=150%><b>ESCOLHA PENDENTE</b></size>\n\n<color=#FFAA00>{invItem.customName}</color>\n\n<i>Pesquise e adicione o item correto manualmente e dê duplo clique nisto para remover.</i>";
    }

    private void OnClickUnicoItem(EquipmentRowUI row) { MostrarDetalhes(row.ItemId); }

    private void OnClickDuploItem(EquipmentRowUI row)
    {
        // Modo delegado: FichaGeralManager é a única fonte de verdade do inventário — este
        // painel só chama os métodos dele e espelha o resultado na sua própria lista local
        // (que só existe pra mostrar "o que foi adicionado nesta sessão"), sem tocar em
        // ouro/RotinaSalvarInventario, que não fazem sentido fora da criação de ficha.
        if (fichaGeralManagerAlvo != null)
        {
            if (row.IsInventory)
            {
                fichaGeralManagerAlvo.RemoverItemDoInventario(row.InstanceId);
                meuInventario.RemoveAll(i => i.instanceId == row.InstanceId);
            }
            else if (todosOsItens.ContainsKey(row.ItemId))
            {
                string instanceId = fichaGeralManagerAlvo.AdicionarItemAoInventario(row.ItemId, 1);
                meuInventario.Add(new InventorySaveItem { instanceId = instanceId, id = row.ItemId, equipSlot = "Desequipado" });
            }

            AtualizarListaInventario();
            return;
        }

        if (row.IsInventory)
        {
            var invItem = meuInventario.FirstOrDefault(i => i.instanceId == row.InstanceId);
            if (invItem != null && todosOsItens.TryGetValue(invItem.id, out ItemData dbItem))
            {
                ExtractItemDetails(dbItem.xml, out string cost, out _, out _);
                ouroPersonagem += ConverterCustoParaOuro(cost);
                OnOuroMudou?.Invoke(ouroPersonagem);
            }
            meuInventario.RemoveAll(i => i.instanceId == row.InstanceId);
        }
        else
        {
            if (todosOsItens.TryGetValue(row.ItemId, out ItemData dbItem))
            {
                ExtractItemDetails(dbItem.xml, out string cost, out _, out _);
                float valorParaDescontar = ConverterCustoParaOuro(cost);

                if (ouroPersonagem - valorParaDescontar < 0)
                {
                    MostrarFeedback($"Ouro insuficiente! Você tem {ouroPersonagem:0.##}gp e o item custa {valorParaDescontar:0.##}gp.", true);
                    return;
                }

                ouroPersonagem -= valorParaDescontar;
                OnOuroMudou?.Invoke(ouroPersonagem);
                meuInventario.Add(new InventorySaveItem { instanceId = Guid.NewGuid().ToString(), id = row.ItemId, equipSlot = "Desequipado" });
            }
        }

        AtualizarListaInventario();
        // Compras/remoções manuais só ficavam em memória — se o jogador saísse da tela de
        // criação sem clicar em "Finalizar", o GlobalFichaData.inventarioJson continuava com
        // o valor antigo e os itens comprados aqui eram perdidos no auto-save do OnDisable.
        StartCoroutine(RotinaSalvarInventario());
    }

    private void OnMudarSlotEquipamento(string instanceId, string novoSlot)
    {
        var item = meuInventario.FirstOrDefault(i => i.instanceId == instanceId);
        if (item != null)
        {
            item.equipSlot = novoSlot;
            AtualizarListaInventario();
            StartCoroutine(RotinaSalvarInventario());
        }
    }

    private void MostrarDetalhes(int id)
    {
        if (!todosOsItens.TryGetValue(id, out ItemData item)) return;
        detailsText.text = FormatarDescricaoItem(item);
    }

    private string FormatarDescricaoItem(ItemData item)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"<size=150%><b>{item.nome.ToUpper()}</b></size>");
        sb.AppendLine($"<color=#777777><i>{item.categoria}</i></color>\n");
        if (!string.IsNullOrEmpty(item.xml))
        {
            try {
                XDocument doc = XDocument.Parse(item.xml);
                string summary = doc.Root.Element("summary")?.Value ?? doc.Root.Element("description")?.Value;
                if (!string.IsNullOrEmpty(summary)) sb.AppendLine(summary + "\n");
                var meta = doc.Root.Element("metadata") ?? doc.Root;
                string c = meta.Element("cost")?.Value; string w = meta.Element("weight")?.Value;
                if (!string.IsNullOrEmpty(c)) sb.AppendLine($"<b>Custo:</b> {c}");
                if (!string.IsNullOrEmpty(w)) sb.AppendLine($"<b>Peso:</b> {w} lb");
            } catch { }
        }
        return sb.ToString();
    }

    public void SalvarInventarioGeral() { StartCoroutine(RotinaSalvarInventario()); }

    public IEnumerator RotinaSalvarInventario()
    {
        // Modo delegado: FichaGeralManager é o único dono do inventário desta ficha. meuInventario
        // aqui só reflete os itens adicionados nesta sessão do painel (ver CarregarTudo), então
        // deixar este método gravar sobrescreveria o inventário completo da ficha com essa lista
        // parcial. Guarda única na fonte em vez de checar fichaGeralManagerAlvo em cada botão que
        // pode chamar isto (OnMudarSlotEquipamento, SalvarInventarioGeral, etc.) — mais seguro e
        // não exige tocar em mais nada deste script pra criação de ficha continuar intacta.
        if (fichaGeralManagerAlvo != null) yield break;

        if (GlobalFichaData.fichasDndId <= 0)
        {
            VTTLog.SaveWarn($"[DIAG] RotinaSalvarInventario ABORTOU: fichasDndId={GlobalFichaData.fichasDndId} <= 0. Itens perdidos={meuInventario.Count}");
            MostrarFeedback("Erro: Ficha não carregada.", true);
            yield break;
        }

        if (!meuInventario.Any(i => i.id == -999)) meuInventario.Add(new InventorySaveItem { id = -999, instanceId = "import_flag", equipSlot = "Hidden" });

        // DataManager.Instance é usado logo abaixo (GetCurrentId/GetCurrentSteamId) só pros logs de
        // diagnóstico — sem ele, evita a NRE em vez de arriscar salvar sem esse contexto.
        if (DataManager.Instance == null) yield break;

        InventoryWrapper inv = new InventoryWrapper { itens = meuInventario, gp = ouroPersonagem };
        GlobalFichaData.inventarioJson = inv;

        int playerId = DataManager.Instance.GetCurrentId();
        string steamId = DataManager.Instance.GetCurrentSteamId();
        VTTLog.Save($"[DIAG] RotinaSalvarInventario gravando. steamId={steamId} playerId={playerId} fichasGeneralId={GlobalFichaData.fichasGeneralId} fichasDndId={GlobalFichaData.fichasDndId} json={JsonUtility.ToJson(inv)}");
        bool ok = LocalFichaStorage.SaveInventory(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            inv
        );

        VTTLog.Save($"[DIAG] RotinaSalvarInventario resultado ok={ok}");

        if (ok) MostrarFeedback("Inventário salvo localmente!", false);
        else MostrarFeedback("Erro ao salvar inventário local.", true);

        yield return null;
    }

    private void LimparDetalhes() { if (detailsText) detailsText.text = "Selecione um item."; }
    private void MostrarFeedback(string msg, bool err) {
        if (feedbackText) { feedbackText.text = msg; feedbackText.color = err ? Color.red : Color.green; feedbackText.gameObject.SetActive(true); Invoke("EsconderFeedback", 3f); }
    }
    private void EsconderFeedback() { if (feedbackText) feedbackText.gameObject.SetActive(false); }
}