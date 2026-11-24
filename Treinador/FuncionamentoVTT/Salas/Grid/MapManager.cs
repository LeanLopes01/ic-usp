using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using SimpleFileBrowser;
using Steamworks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class MapManager : MonoBehaviour, IGerenciadorDeParedes, IGerenciadorDeTokens
{
    [System.Serializable]
    public class TokenCatalogItem
    {
        public string nome;
        public Sprite sprite;
        [HideInInspector] public string imagemPath;
        // Link com a ficha de origem (0 = token sem ficha linkada, ex: monstro genérico)
        [HideInInspector] public int fichasGeneralId;
        [HideInInspector] public int fichasDndId;
        [HideInInspector] public string ownerSteamId;
        [HideInInspector] public int ownerPlayerId;
        // Link com o monstro de origem (catálogo local) — mutuamente exclusivo com o link de ficha
        [HideInInspector] public string monstroId;
    }

    [Header("Referências da Cena")]
    public Grid gridPrincipal;
    public SpriteRenderer mapaRenderer;

    [Header("Configuração Atual")]
    public MapConfig mapaAtivo;

    // Listagem/UI da galeria de mapas e do catálogo de tokens NÃO vive mais aqui — ver
    // Salas/BarraFerramentasManager.cs (FerramentaGaleriaSlot). MapManager só guarda os DADOS
    // (todosOsMapas/todosOsTokens) e as AÇÕES (trocar de mapa, criar mapa/token, soltar no grid);
    // a barra de ferramentas assina os eventos abaixo pra saber quando repopular a UI dela.

    [Header("Dados")]
    public List<MapConfig> todosOsMapas = new List<MapConfig>();
    [SerializeField] private List<TokenCatalogItem> todosOsTokens = new List<TokenCatalogItem>();

    /// <summary>Somente leitura pra fora — quem quiser adicionar/remover usa AdicionarTokenAoCatalogo,
    /// nunca mexe na lista direto (ver comentário de ChaveCatalogo sobre deduplicação).</summary>
    public IReadOnlyList<TokenCatalogItem> TodosOsTokens => todosOsTokens;

    /// <summary>Disparado sempre que todosOsMapas muda (criar mapa, carregar estado, resetar sala) —
    /// Salas/BarraFerramentasManager.cs assina isso pra repopular o slot de Cena.</summary>
    public event System.Action OnCatalogoDeMapasAlterado;
    /// <summary>Mesma ideia de OnCatalogoDeMapasAlterado, pro slot de Token.</summary>
    public event System.Action OnCatalogoDeTokensAlterado;
    /// <summary>Disparado quando a aba de mapa fica inativa (ver AplicarEstadoVisualGrid) — a barra
    /// de ferramentas deve fechar todos os painéis de galeria (nenhum sentido em ficar aberto sobre
    /// um grid escondido).</summary>
    public event System.Action OnDevoFecharGalerias;

    [Header("Novo Mapa (UI)")]
    public GameObject prefabPopupCriarMapa;
    public Transform parentPopup;

    [Header("Galeria de Tokens (UI)")]
    [SerializeField] private GameObject prefabTokenNoMapa;
    [SerializeField] private GameObject prefabPopupCriarToken;

    [Header("Lore de NPC — somente leitura (Fase 7, ver SalaNpcStatBlockLoreUI)")]
    [Tooltip("Prefab com SalaNpcStatBlockLoreUI — aberto pela opção 'Lore' do menu de atalho de um " +
        "token com NpcIdCampanha (NPC enviado de Campanhas). Autoria continua exclusiva de Campanhas; " +
        "aqui só exibe o EntryData completo (stat block + lore) somente-leitura.")]
    [SerializeField] private GameObject prefabLoreNpc;

    [Header("Grid Automático")]
    public bool criarGridAutomatico = true;
    public Color corLinhasGrid = new Color(1f, 1f, 1f, 0.22f);
    [Range(0.001f, 0.5f)]
    [Tooltip("Espessura das linhas como fração do tamanho de uma célula (0.03 = 3% da célula).")]
    public float espessuraLinhasGrid = 0.03f;
    public int gridSortingOrderOffset = 1;
    [SerializeField] private Vector2 tamanhoMundoGridFixo = new Vector2(240f, 240f);
    [SerializeField] private Color corFundoGrid = new Color32(0x33, 0x33, 0x33, 0xFF);
    [SerializeField] private SpriteRenderer fundoGridRenderer;

    [Header("Renderização")]
    [Tooltip("Força mapa e grid para trás da interface (UI).")]
    [SerializeField] private bool forcarMapaAtrasDaUI = true;
    [SerializeField] private string sortingLayerMapa = "Default";
    [SerializeField] private int sortingOrderMapa = -200;

    [Header("Ajuste de Escala do Battle Map")]
    [SerializeField] private float multiplicadorTamanhoGrid = 2f;
    [SerializeField] private float escalaInicialMapa = 2f;
    [SerializeField] private float passoTamanhoGrid = 0.1f;
    [SerializeField] private float multiplicadorMinimoGrid = 0.35f;
    [SerializeField] private float multiplicadorMaximoGrid = 3f;
    [SerializeField] private TMP_Text textoEscalaGrid;
    [SerializeField, Min(0.5f)] private float larguraMinimaPixelsGrid = 1.5f;
    [Tooltip("Teto de segurança pra espessura da linha do grid, como MÚLTIPLO da espessura base (não " +
        "em pixels de tela!) — protege contra Screen.height vir errado no build (ex: Windows sem DPI " +
        "awareness correta, reportando altura menor que a física real, o que infla mundoPorPixel e " +
        "larguraMinMundo). Um teto expresso em pixels sofreria do MESMO erro, porque usaria o mesmo " +
        "Screen.height errado pra se converter — por isso aqui é relativo à espessura base, que é " +
        "puramente em unidades de mundo e não depende de tela. Editor Play normalmente não sofre esse " +
        "desvio; só o build standalone. Ver AtualizarEspessuraLinhasPorZoom.")]
    [SerializeField, Min(1f)] private float fatorMaximoEspessuraLinhasGrid = 5f;

    [Header("Estado da Sala")]
    [SerializeField] private SteamLobbyManager steamLobbyManager;

    [Header("Câmera VTT")]
    public VTTCamController cameraVTT;

    [Header("Paredes (vetoriais, livres de grid — port de Campanhas/Grid/CampanhaMapManager.cs)")]
    [Tooltip("Cor do overlay de depuração — só a linha fina desenhada sobre cada parede detectada, " +
        "não afeta o colisor real.")]
    [SerializeField] private Color corOverlayParede = new Color(0f, 1f, 1f, 1f);
    [Tooltip("Roda CampanhaMapaVetorizador de novo com a calibração atual dos sliders abaixo sobre o " +
        "mapa ativo — só o host pode acionar.")]
    [SerializeField] private Button botaoRevetorizarParedes;
    [SerializeField, Range(0.005f, 0.2f)] private float espessuraOverlayParede = 0.05f;
    [Tooltip("Distância normalizada (0..1) usada pra reconhecer que a ponta de um WallLine é 'a mesma' " +
        "que a ponta de outro, ao reencadear a lista plana em cadeias contíguas (ver AgruparEmCadeias).")]
    [SerializeField] private float toleranciaEncadeamentoParedes = 0.002f;
    [Tooltip("Layer dedicada às paredes, pra Raycasts de visão colidirem só com elas — precisa existir " +
        "em Edit > Project Settings > Tags and Layers (Unity não permite criar layers em runtime).")]
    [SerializeField] private string nomeLayerVisionBlocker = "VisionBlocker";
    [Tooltip("Liga/desliga o GameObject com as ferramentas de edição manual de paredes (WallEditorTools) " +
        "— só abre pra quem é host da sala.")]
    [SerializeField] private Button botaoFerramentasParede;
    [SerializeField] private GameObject painelFerramentasParede;

    [Header("Calibração da Vetorização (ver CampanhaMapaVetorizadorConfig)")]
    [SerializeField] private Slider sliderLimiarLuminancia;
    [SerializeField] private TMP_Text textoLimiarLuminancia;
    [SerializeField] private Slider sliderRaioDilatacao;
    [SerializeField] private TMP_Text textoRaioDilatacao;
    [SerializeField] private Slider sliderComprimentoMinimoContorno;
    [SerializeField] private TMP_Text textoComprimentoMinimoContorno;
    [SerializeField] private Slider sliderEpsilonRDP;
    [SerializeField] private TMP_Text textoEpsilonRDP;

    [Header("Visão Dinâmica (Fog of War) — port de CampanhaMapManager")]
    [Tooltip("Raios base distribuídos uniformemente em 360° — só suavizam o limite CIRCULAR do raio de " +
        "visão em áreas abertas sem parede por perto (cantos de parede/mapa já ficam exatos via " +
        "raycasting orientado a vértice).")]
    [SerializeField, Range(16, 360)] private int raiosBaseVisao = 48;
    [Tooltip("Alpha máximo da escuridão dinâmica (spotlight do host e fog-of-war automático do " +
        "jogador) — antes era sempre 100% opaco; agora é o teto que o gradiente atinge a partir de " +
        "transicaoGradienteEmCelulas da borda da visão.")]
    [SerializeField, Range(0f, 1f)] private float alphaMaximoVisaoDinamica = 0.85f;
    [Tooltip("Distância (em células de grid) a partir da borda da visão até a escuridão atingir o " +
        "alpha máximo — 0 volta ao comportamento antigo (opaco logo na borda, sem gradiente).")]
    [SerializeField, Min(0f)] private float transicaoGradienteEmCelulas = 2f;
    [Tooltip("Slider de raio de visão (em METROS) do token com a visão spotlight ativa no momento — " +
        "ferramenta do HOST, só fica interagível enquanto 'Selecionar Visão' estiver ligado num token.")]
    [SerializeField] private Slider sliderRaioVisao;
    [SerializeField] private TMP_Text textoRaioVisao;
    [Tooltip("Alterna a visão combinada de TODOS os tokens da mesa ao mesmo tempo — ferramenta do HOST " +
        "(área fora da visão combinada só fica ESCURECIDA, não totalmente preta).")]
    [SerializeField] private Button botaoVisaoTodosOsTokens;
    [SerializeField, Range(0f, 1f)] private float alphaVisaoGeralTokens = 0.6f;

    [Header("Fog Manual (retângulo preso ao grid — ver FogRect.cs, FogEditorTools.cs)")]
    [Tooltip("Alpha da área de fog manual pro HOST — jogador comum sempre vê 100% opaco (fica " +
        "totalmente escondido, não importa a visão dos tokens); o host continua vendo a área " +
        "semitransparente, pra não perder a preparação do mapa de vista.")]
    [SerializeField, Range(0f, 1f)] private float alphaFogParaHost = 0.5f;

    [Header("Cor da Luz (filtro sutil sobre a área visível — ver BarraFerramentasManager, roleta de cor)")]
    [Tooltip("Cor do 'filtro de luz' aplicado sobre a área que os tokens enxergam (spotlight/visão " +
        "geral/fog automático do jogador) — o Mestre escolhe só o MATIZ na roleta de cores " +
        "(FlexibleColorPicker); a intensidade é sempre baixa (ver alphaLuzPersonagem, não exposto " +
        "ao Mestre), pra ficar 'um filtro bem claro' (amarelado, azulado, etc.), não uma cor sólida. " +
        "Color.white é o sentinel de 'sem filtro' — é o valor default, então mapas salvos antes " +
        "deste campo existir continuam sem nenhuma mudança visual (ver LuzVisivel).")]
    [SerializeField] private Color corLuzVisao = Color.white;
    [Tooltip("Alpha fixo do filtro de cor de luz — deliberadamente BAIXO e não exposto ao Mestre (só " +
        "a cor é escolhível, ver corLuzVisao) pra garantir que o filtro fique sempre sutil.")]
    [SerializeField, Range(0f, 0.4f)] private float alphaLuzPersonagem = 0.15f;

    // Color.white == sem filtro (sentinel, ver corLuzVisao) — usado tanto pra decidir se o mesh de
    // luz deve ficar habilitado quanto pra pular a construção dele à toa quando desligado.
    //
    // Forçado sempre false por enquanto (feature desativada a pedido do usuário — ver
    // BarraFerramentasManager.RegistrarSlidersDeIluminacao). Sem isso, um corLuzVisao não-branco
    // salvo ANTES da UI ser escondida (SalaDataStorage local ou payload de rede de outro jogador)
    // continuaria colorindo a visão pra sempre, já que esconder o campo de hex não limpa dado já
    // persistido — só bloqueia editar um novo. Pra religar: reverter esta linha pra
    // "corLuzVisao != Color.white" junto com o bloco comentado em BarraFerramentasManager.
    private bool LuzVisivel => false;

    [Header("UI - Tooltip de Token (Prefab)")]
    [SerializeField] private GameObject tokenTooltipPrefab;

    private readonly List<LineRenderer> linhasGridInstanciadas = new List<LineRenderer>();
    private readonly List<UnityEngine.Object> assetsRuntimeCriados = new List<UnityEngine.Object>();

    private Material materialLinhaGrid;
    private Texture2D texturaTracejadaGrid;
    private Sprite spriteFundoBranco;
    private Transform containerLinhasGrid;
    private Transform containerTokensNoMapa;
    // Incrementa a cada token spawnado — evita que dois tokens na mesma célula fiquem com
    // sortingOrder idêntico (ver SpawnTokenNoMapa).
    private int proximaOrdemToken;

    // Espaçamento entre sortingOrder de tokens consecutivos (não mais +1) — abre uma "folga"
    // (FolgaMascaraToken pra cada lado) usada pelo range customizado do SpriteMask de cada token,
    // sem risco de um token vizinho cair dentro dela (ver bloco da máscara em SpawnTokenNoMapa).
    private const int IntervaloOrdemToken = 8;
    private const int FolgaMascaraToken = 3;
    private MapCreatePopupUI popupCriarMapaUI;
    private TokenCreatePopupUI popupCriarTokenUI;
    private SalaNpcStatBlockLoreUI loreNpcUI;
    private Sprite spriteImportadoPendente;
    private Sprite spriteTokenImportadoPendente;
    private float escalaBaseMapaAtual = 1f;
    private bool salaAtiva;
    private bool abaMapaAtiva;
    private float espessuraBaseLinhasMundo;
    private float espessuraLinhasAplicada = -1f;

    // ── Paredes (vetoriais — ver IGerenciadorDeParedes) ──────────────────────────
    // Começa visível: overlay de depuração é a regra durante preparação/edição de mapa, não a
    // exceção — ver WallEditorTools.AlternarVisibilidadeParedes, o botão que liga/desliga isso.
    private bool _mostrarParedesDetectadas = true;
    private readonly CampanhaMapaVetorizadorConfig _configVetorizacao = new CampanhaMapaVetorizadorConfig();
    private Material _materialLinhaParede;
    private Transform _containerParedes;
    private bool _vetorizacaoEmAndamento;

    // ── Visão Dinâmica (Fog of War) — ver região dedicada mais abaixo ────────────
    private const int MaxColisoresNoRaioDeVisao = 128;
    private readonly Collider2D[] _bufferColisoresNoRaio = new Collider2D[MaxColisoresNoRaioDeVisao];
    private readonly RaycastHit2D[] _bufferRaycastHit = new RaycastHit2D[1];
    private const float MetrosPorQuadrado = 1.5f;

    private Transform _containerVisao;
    private MeshFilter _meshFilterVisao;
    private MeshRenderer _meshRendererVisao;
    private Material _materialEscuridaoVisao;
    private MapTokenDraggable _tokenComVisaoAtiva;

    private bool _visaoTodosOsTokensAtiva;
    private Transform _containerVisaoGeral;
    private MeshFilter _meshFilterVisaoGeral;
    private MeshRenderer _meshRendererVisaoGeral;
    private Material _materialEscuridaoVisaoGeral;

    // Fog-of-war AUTOMÁTICO de jogador comum — sempre ativo pra quem não é host (ver
    // AtualizarVisaoAutomaticaDoJogador), diferente do spotlight/visão-geral acima, que são
    // ferramentas manuais do host. Opaco (igual ao spotlight), combinado de todos os tokens cujo
    // OwnerSteamId seja o meu.
    private Transform _containerVisaoJogador;
    private MeshFilter _meshFilterVisaoJogador;
    private MeshRenderer _meshRendererVisaoJogador;
    private Material _materialEscuridaoVisaoJogador;

    // Filtro de cor de luz (ver Header "Cor da Luz" abaixo, corLuzVisao) — cobre a área VISÍVEL
    // (interior do polígono de visão, o oposto da escuridão acima), um mesh-leque por modo de visão,
    // espelhando 1-pra-1 os 3 containers de escuridão acima (mesmo polígono "pontos", só que o leque
    // fecha no centro/origem em vez de formar um anel). corLuzVisao == Color.white é o sentinel de
    // "sem filtro" (default, mapas salvos antes deste campo existir) — nesse caso o mesh nem chega a
    // ser habilitado, ver LuzVisivel.
    private Transform _containerLuz;
    private MeshFilter _meshFilterLuz;
    private MeshRenderer _meshRendererLuz;
    private Material _materialLuz;

    private Transform _containerLuzGeral;
    private MeshFilter _meshFilterLuzGeral;
    private MeshRenderer _meshRendererLuzGeral;
    private Material _materialLuzGeral;

    private Transform _containerLuzJogador;
    private MeshFilter _meshFilterLuzJogador;
    private MeshRenderer _meshRendererLuzJogador;
    private Material _materialLuzJogador;

    // Modo mover mapa
    private bool modoMoverMapa = false;
    private bool arrastandomApa = false;
    private Vector3 posicaoMapaInicioDrag;
    private Vector3 mouseMundoInicioDrag;
    private bool dragMapaBloqueadoPorUI = false;

    // Persistência VTT
    private string salaIdAtual;
    private string pendenteCaminhoMapa;
    private string pendenteCaminhoToken;
    private bool   jaCarregouEstadoNaSessao;

    /// <summary>Workshop ID do mapa activo; 0 se for mapa local sem upload.</summary>
    public ulong MapaAtualWorkshopId { get; private set; }

    // Caminho local do mapa activo (usado para reenviar a novos jogadores)
    private string caminhoMapaAtualParaRede;

    // Geração (timestamp Steam) do mapa activo — usada pra descartar transferências P2P
    // desatualizadas que cheguem fora de ordem por trocas rápidas de mapa. Ver Bug C.2.
    private long geracaoMapaAtual;

    // tokenId → Workshop ID do token (populado após upload)
    private readonly Dictionary<string, ulong> workshopIdPorToken = new Dictionary<string, ulong>();

    // tokenIds cujo sprite ainda é o placeholder cinza (aguardando imagem P2P ou Workshop)
    private readonly HashSet<string> tokensPendentesSprite = new HashSet<string>();

    private void Start()
    {
        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();

        salaAtiva = steamLobbyManager != null && steamLobbyManager.SalaAtual.HasValue;
        abaMapaAtiva = true;

        if (cameraVTT == null)
            cameraVTT = FindFirstObjectByType<VTTCamController>();

        multiplicadorTamanhoGrid = Mathf.Clamp(escalaInicialMapa, multiplicadorMinimoGrid, multiplicadorMaximoGrid);

        InstanciarTokenTooltip();

        Religar(botaoRevetorizarParedes, RevetorizarMapaAtivo);
        Religar(botaoFerramentasParede, AlternarPainelFerramentasParede);
        if (painelFerramentasParede != null) painelFerramentasParede.SetActive(false);

        ConfigurarSlider(sliderLimiarLuminancia, 0f, 1f, _configVetorizacao.limiarLuminancia,
            (v) => { _configVetorizacao.limiarLuminancia = v; AtualizarTexto(textoLimiarLuminancia, "Limiar", v, "0.00"); });
        ConfigurarSlider(sliderRaioDilatacao, 0f, 5f, _configVetorizacao.raioDilatacao,
            (v) => { _configVetorizacao.raioDilatacao = Mathf.RoundToInt(v); AtualizarTexto(textoRaioDilatacao, "Dilatação", _configVetorizacao.raioDilatacao, "0"); },
            valoresInteiros: true);
        ConfigurarSlider(sliderComprimentoMinimoContorno, 0f, 200f, _configVetorizacao.comprimentoMinimoContorno,
            (v) => { _configVetorizacao.comprimentoMinimoContorno = Mathf.RoundToInt(v); AtualizarTexto(textoComprimentoMinimoContorno, "Contorno mín.", _configVetorizacao.comprimentoMinimoContorno, "0"); },
            valoresInteiros: true);
        ConfigurarSlider(sliderEpsilonRDP, 0.25f, 10f, _configVetorizacao.epsilonRDP,
            (v) => { _configVetorizacao.epsilonRDP = v; AtualizarTexto(textoEpsilonRDP, "Épsilon RDP", v, "0.00"); });

        Religar(botaoVisaoTodosOsTokens, AlternarVisaoDeTodosOsTokens);
        if (sliderRaioVisao != null)
        {
            sliderRaioVisao.minValue = MetrosPorQuadrado;
            sliderRaioVisao.maxValue = MetrosPorQuadrado * 40f; // 40 quadrados — generoso o bastante pra mapas grandes
            sliderRaioVisao.wholeNumbers = false;
            sliderRaioVisao.onValueChanged.AddListener(OnSliderRaioVisaoAlterado);
        }
        SincronizarSliderRaioVisao(); // estado inicial: nenhum token com visão spotlight ativa

        if (mapaAtivo != null)
            CarregarMapa(mapaAtivo);
        else
            AjustarCamera();

        AplicarEstadoVisualGrid();
    }

    private void OnEnable()
    {
        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();

        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso       += OnSalaAberta;
            steamLobbyManager.OnEntrouNaSalaComSucesso      += OnSalaAberta;
            steamLobbyManager.OnSaiuDaSalaComSucesso        += OnSalaFechada;
            steamLobbyManager.OnEntrouEmSalaLocalComSucesso += OnSalaLocalAberta;
        }
    }

    private void OnDisable()
    {
        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso       -= OnSalaAberta;
            steamLobbyManager.OnEntrouNaSalaComSucesso      -= OnSalaAberta;
            steamLobbyManager.OnSaiuDaSalaComSucesso        -= OnSalaFechada;
            steamLobbyManager.OnEntrouEmSalaLocalComSucesso -= OnSalaLocalAberta;
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < assetsRuntimeCriados.Count; i++)
        {
            if (assetsRuntimeCriados[i] != null)
                Destroy(assetsRuntimeCriados[i]);
        }
    }

    private void InstanciarTokenTooltip()
    {
        if (tokenTooltipPrefab == null || TokenTooltipUI.Instance != null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        canvas ??= FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject go = Instantiate(tokenTooltipPrefab, canvas.transform);
        go.transform.SetAsLastSibling();

        // Ativa para disparar Awake (que registra Instance e força enabled=true),
        // depois esconde — Mostrar() reativa quando necessário
        go.SetActive(true);
        TokenTooltipUI comp = go.GetComponent<TokenTooltipUI>();
        if (comp != null) comp.enabled = true;
        go.SetActive(false);
    }

    private void OnSalaAberta(Steamworks.Data.Lobby _)
    {
        // Só ativa a sala visualmente — o ID e o load são coordenados por TelaSalaUIManager
        // para garantir que o arquivo já existe antes de tentar carregar.
        DefinirSalaAtiva(true);
    }

    private void OnSalaLocalAberta(SalaLocalSnapshot _)
    {
        DefinirSalaAtiva(true);
    }

    private void OnSalaFechada()
    {
        SalvarEstadoVTT();
        DefinirSalaAtiva(false);
        salaIdAtual = string.Empty;
        jaCarregouEstadoNaSessao = false;
    }

    /// <summary>
    /// Chamado por TelaSalaUIManager depois que o arquivo de dados da sala já existe.
    /// Define o ID estável e carrega o estado VTT (apenas uma vez por sessão).
    /// </summary>
    public void IniciarSessaoComId(string localId)
    {
        if (string.IsNullOrWhiteSpace(localId))
            return;

        salaIdAtual = localId;

        if (jaCarregouEstadoNaSessao)
            return;

        if (!SalaDataStorage.HasSalaData(salaIdAtual))
            return;

        SalaData dados = SalaDataStorage.LoadSalaData(salaIdAtual);
        if (dados != null)
            CarregarEstadoVTT(dados);
    }

    // Identidade real de um item do catálogo: pela ficha ou monstro linkado quando existir, senão
    // pelo nome (tokens de imagem solta, sem link). Usar só o nome como chave (como era antes)
    // colapsava qualquer ficha/monstro com nome repetido (ex: duas fichas de teste "Teste 1", ou
    // vários monstros do mesmo tipo "Goblin") num ÚNICO item de catálogo — o segundo AdicionarToken
    // virava no-op silencioso, e arrastar da lista sempre resolvia pro PRIMEIRO cadastrado daquele
    // nome, entregando a imagem (e o link) errado pra token novo.
    private static string ChaveCatalogo(int fichasDndId, string monstroId, string nome)
    {
        if (fichasDndId > 0) return $"ficha:{fichasDndId}";
        if (!string.IsNullOrWhiteSpace(monstroId)) return $"monstro:{monstroId}";
        return $"nome:{nome}";
    }

    private static string ChaveCatalogo(TokenCatalogItem item) =>
        item == null ? null : ChaveCatalogo(item.fichasDndId, item.monstroId, item.nome);

    /// <summary>
    /// Adiciona um token ao catálogo a partir de um caminho de imagem.
    /// Ignora silenciosamente se o arquivo não existir ou se o token já estiver cadastrado (mesma
    /// ficha/monstro linkado, ou mesmo nome pra tokens sem link).
    /// </summary>
    public void AdicionarTokenAoCatalogo(string nome, string imagemPath,
        int fichasGeneralId = 0, int fichasDndId = 0, string ownerSteamId = "", int ownerPlayerId = 0,
        string monstroId = "")
    {
        if (string.IsNullOrWhiteSpace(imagemPath) || !File.Exists(imagemPath))
        {
            Debug.LogWarning($"[MapManager] Token '{nome}' não adicionado ao catálogo: imagem não encontrada em '{imagemPath}'.");
            return;
        }

        string chave = ChaveCatalogo(fichasDndId, monstroId, nome);
        for (int i = 0; i < todosOsTokens.Count; i++)
        {
            if (todosOsTokens[i] != null && ChaveCatalogo(todosOsTokens[i]) == chave)
            {
                Debug.Log($"[MapManager] Token '{nome}' já está no catálogo — ignorando duplicata.");
                return;
            }
        }

        Sprite sprite = CarregarSpriteDeArquivo(imagemPath);
        if (sprite == null)
        {
            Debug.LogWarning($"[MapManager] Token '{nome}' não adicionado ao catálogo: falha ao carregar sprite de '{imagemPath}'.");
            return;
        }

        todosOsTokens.Add(new TokenCatalogItem
        {
            nome = nome,
            sprite = sprite,
            imagemPath = imagemPath,
            fichasGeneralId = fichasGeneralId,
            fichasDndId = fichasDndId,
            ownerSteamId = ownerSteamId,
            ownerPlayerId = ownerPlayerId,
            monstroId = monstroId
        });
        OnCatalogoDeTokensAlterado?.Invoke();
        SalvarEstadoVTT();
    }

    public void CarregarMapa(MapConfig novaConfig)
    {
        if (novaConfig == null)
        {
            Debug.LogWarning("[MapManager] Config de mapa nula.");
            return;
        }

        // Antes de trocar: os tokens atualmente na tela pertencem ao mapa que está SAINDO —
        // snapshot pra dentro dele (em memória) antes de destruir os GameObjects, senão eles se
        // perdem ou (pior) continuam existindo por cima do mapa novo. Sem isso, trocar de mapa
        // deixava os tokens do mapa anterior sobrepostos no novo indefinidamente.
        if (mapaAtivo != null && mapaAtivo != novaConfig)
            SalvarTokensNoMapaAtivo();

        mapaAtivo = novaConfig;
        geracaoMapaAtual = ObterTimestampServidor();

        LimparTokensDoMapa();
        LimparParedes();
        EsconderVisaoDoToken();          // GameObjects do mapa anterior já foram destruídos por LimparTokensDoMapa
        DesligarVisaoDeTodosOsTokens();

        if (mapaRenderer != null)
        {
            mapaRenderer.sprite = novaConfig.imagemMapa;
            AplicarOrdemRenderizacaoMapa();
            AplicarEscalaNormalizadaDoMapa();
            AplicarMultiplicadorEscalaMapa();
        }
        else
        {
            Debug.LogWarning("[MapManager] SpriteRenderer do mapa não foi atribuído.");
        }

        AtualizarGridVisual();
        AjustarCamera();

        // Config de iluminação é POR MAPA (ver seção "Configuração de iluminação dinâmica" abaixo) —
        // sincroniza os campos locais a partir do MapConfig ANTES de recalcular a visão, senão
        // ReconstruirParedes/AtualizarMeshDeFogRects abaixo usariam os valores do mapa anterior.
        SincronizarConfigIluminacaoDoMapa(novaConfig);

        // Sempre chamado (não só quando vetorizado) — ReconstruirParedes também é o ponto que
        // recalcula/esconde qualquer visão dinâmica ativa (ver AtualizarVisaoDinamicaGeral no final
        // dela), então precisa rodar mesmo com paredes vazias pra não deixar fog do mapa anterior.
        ReconstruirParedes(novaConfig.paredes);
        AtualizarMeshDeFogRects();

        Debug.Log($"[MapManager] Mapa '{novaConfig.nomeDoMapa}' carregado.");

        // Rastreia o caminho local do mapa ativo INDEPENDENTE de ser host agora — necessário pra
        // conseguir reenviar caso este cliente vire host depois (migração de ownership do lobby).
        // Antes, esse campo só era escrito dentro do branch host-only abaixo, então um cliente que
        // recebeu o mapa via P2P (ou baixou do Workshop) nunca tinha nada pra reenviar se assumisse
        // o papel de host — EnviarMapaParaJogador fazia no-op silencioso pro próximo jogador a entrar.
        if (!string.IsNullOrWhiteSpace(novaConfig.imagemPath) && File.Exists(novaConfig.imagemPath))
            caminhoMapaAtualParaRede = novaConfig.imagemPath;

        // Auto-sync: só quem É host agora efetivamente ENVIA o mapa pros clientes via P2P chunking.
        if (SouHost() && !string.IsNullOrWhiteSpace(novaConfig.imagemPath) && File.Exists(novaConfig.imagemPath))
            MestreEnviaMapaLocalParaRede(novaConfig.imagemPath);

        // Restaura só os tokens DESTE mapa — depois de LimparTokensDoMapa acima já ter removido
        // qualquer sobra do mapa anterior, e depois do grid/câmera estarem prontos (RestaurarTokensDoMapa
        // usa ObterTamanhoAtualDaCelula/AjustarPosicaoTokenAoGrid, que dependem do mapa já carregado).
        RestaurarTokensDoMapa(novaConfig.tokensNoMapa);
    }

    /// <summary>Chamado por MapButtonUI após o jogador confirmar a troca de cena (popup de
    /// confirmação) — troca de mapa com popup de progresso (PopupManager), mesmo padrão de
    /// CampanhaMapManager.SalvarTudoCoroutine. No-op se o mapa clicado já for o ativo.</summary>
    public void TrocarMapaComProgresso(MapConfig novoMapa)
    {
        if (novoMapa == null || novoMapa == mapaAtivo) return;
        StartCoroutine(TrocarMapaCoroutine(novoMapa));
    }

    private IEnumerator TrocarMapaCoroutine(MapConfig novoMapa)
    {
        PopupManager.Instance?.MostrarCarregamento("Trocando de mapa", novoMapa.nomeDoMapa);
        yield return null;

        PopupManager.Instance?.AtualizarProgresso(0.3f, "Salvando mapa atual...");
        yield return null;

        // CarregarMapa já faz, internamente: snapshot dos tokens do mapa que está saindo, limpa a
        // tela, troca sprite/grid/câmera, e restaura os tokens do mapa novo — ver o método acima.
        PopupManager.Instance?.AtualizarProgresso(0.7f, "Carregando novo mapa...", novoMapa.nomeDoMapa);
        CarregarMapa(novoMapa);
        yield return null;

        PopupManager.Instance?.AtualizarProgresso(1f, "Concluído");
        yield return new WaitForSeconds(0.3f);
        PopupManager.Instance?.Fechar();
    }

    /// <summary>Recarrega o mapa atualmente ativo do zero (grid/paredes/tokens) — usado pelo botão
    /// de reload da barra superior (ver ToggleObjects.RecarregarTelaAtual). CarregarMapa(novaConfig)
    /// só salva os tokens do mapa que está SAINDO quando novaConfig é uma referência DIFERENTE da
    /// atual (troca de mapa de verdade) — chamando com a MESMA referência essa guarda nunca dispara,
    /// e LimparTokensDoMapa() destruiria os tokens sem salvar primeiro. Por isso aqui salva
    /// explicitamente ANTES de chamar CarregarMapa, cobrindo esse caso "mesmo mapa" que nenhum
    /// caminho existente precisava tratar até agora (TrocarMapaComProgresso inclusive trata
    /// mapa==mapaAtivo como no-op).</summary>
    public void RecarregarMapaAtual()
    {
        if (mapaAtivo == null) return;
        StartCoroutine(RecarregarMapaAtualCoroutine());
    }

    private IEnumerator RecarregarMapaAtualCoroutine()
    {
        var mapaParaRecarregar = mapaAtivo;

        PopupManager.Instance?.MostrarCarregamento("Recarregando mapa", mapaParaRecarregar.nomeDoMapa);
        yield return null;

        // Snapshot explícito ANTES de CarregarMapa — ver justificativa no comentário do método acima.
        SalvarTokensNoMapaAtivo();
        PopupManager.Instance?.AtualizarProgresso(0.5f, "Recarregando...", mapaParaRecarregar.nomeDoMapa);
        yield return null;

        CarregarMapa(mapaParaRecarregar);
        yield return null;

        PopupManager.Instance?.AtualizarProgresso(1f, "Concluído");
        yield return new WaitForSeconds(0.3f);
        PopupManager.Instance?.Fechar();
    }

    // Snapshot dos GameObjects atualmente sob containerTokensNoMapa pra dentro de mapaAtivo.tokensNoMapa
    // (em memória, não grava em disco — quem grava é SalvarEstadoVTT). Chamado ao trocar de mapa
    // (CarregarMapa) e também de dentro de SalvarEstadoVTT, pra garantir que o mapa ativo esteja
    // sempre com sua lista em memória sincronizada antes de qualquer serialização.
    private void SalvarTokensNoMapaAtivo()
    {
        if (mapaAtivo == null) return;
        mapaAtivo.tokensNoMapa = ColetarInstanciasDeTokensAtuais(null);
    }

    // Lê os GameObjects atualmente sob containerTokensNoMapa e serializa pra uma lista de
    // TokenInstanceSaveData, persistindo a imagem de cada um (se pathsPersistidos != null, registra
    // os caminhos gravados pra PurgarImagensOrfas). Extraído de SalvarEstadoVTT pra ser reaproveitado
    // também por SalvarTokensNoMapaAtivo (snapshot em memória, sem tocar no disco).
    private List<TokenInstanceSaveData> ColetarInstanciasDeTokensAtuais(HashSet<string> pathsPersistidos)
    {
        var instancias = new List<TokenInstanceSaveData>();
        if (containerTokensNoMapa == null) return instancias;

        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            Transform tokenT = containerTokensNoMapa.GetChild(i);
            if (tokenT == null) continue;

            MapTokenDraggable drag = tokenT.GetComponent<MapTokenDraggable>();
            if (drag == null) continue;

            string pathPersistida = string.IsNullOrWhiteSpace(drag.ImagemPath) || string.IsNullOrWhiteSpace(salaIdAtual)
                ? drag.ImagemPath ?? string.Empty
                : SalaDataStorage.PersistirImagem(drag.ImagemPath, salaIdAtual, $"inst_{drag.TokenId}");

            if (!string.IsNullOrWhiteSpace(pathPersistida)) pathsPersistidos?.Add(pathPersistida);
            instancias.Add(new TokenInstanceSaveData
            {
                nomeToken       = drag.NomeToken,
                tokenId         = drag.TokenId,
                imagemPath      = pathPersistida,
                posX            = tokenT.position.x,
                posY            = tokenT.position.y,
                posZ            = tokenT.position.z,
                fichasGeneralId = drag.FichasGeneralId,
                fichasDndId     = drag.FichasDndId,
                ownerSteamId    = drag.OwnerSteamId,
                ownerPlayerId   = drag.OwnerPlayerId,
                monstroId       = drag.MonstroId,
                npcIdCampanha   = drag.NpcIdCampanha,
                raioVisaoMetros = drag.RaioVisaoMetros,
                vidaAtual       = drag.VidaAtual,
                bloqueado       = drag.Bloqueado,
                escondido       = drag.Escondido,
            });
        }
        return instancias;
    }

    // Respawna os tokens de UM mapa específico — usado tanto ao trocar de mapa (CarregarMapa) quanto
    // ao carregar o estado inicial da sessão (CarregarEstadoVTT). Roda no frame seguinte (via
    // coroutine) porque depende do grid/câmera já terem sido ajustados pro mapa novo.
    private void RestaurarTokensDoMapa(List<TokenInstanceSaveData> instancias)
    {
        if (instancias == null || instancias.Count == 0) return;
        StartCoroutine(RestaurarTokensNoMapaAposFrame(instancias.ToArray()));
    }

    // Timestamp do servidor Steam (segundos desde epoch), sincronizado entre todos os peers —
    // NÃO usar DateTimeOffset.UtcNow local aqui: o relógio de sistema de cada jogador pode estar
    // dessincronizado (clock drift), o que faria a versão do mapa (Bug C.2) rejeitar mapas
    // legítimos por engano após uma migração de ownership do lobby.
    private static long ObterTimestampServidor()
    {
        // SteamServerTime é DateTime (UTC) nesse binding do Facepunch.Steamworks, não um long cru —
        // precisa converter via DateTimeOffset pra virar segundos unix.
        if (SteamClient.IsValid)
            return new System.DateTimeOffset(SteamUtils.SteamServerTime).ToUnixTimeSeconds();
        return System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public void AbrirPopupCriarMapa()
    {
        // Só o host (dono do lobby) pode importar mapas — um cliente fazendo isso cria um
        // MapConfig fantasma em todosOsMapas que nunca é broadcastado (CarregarMapa só propaga
        // pra rede quando SouHost()), e colide com a reconstrução de todosOsMapas vinda de rede.
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode importar mapas.");
            return;
        }

        if (prefabPopupCriarMapa == null)
        {
            Debug.LogWarning("[MapManager] Prefab do popup de criar mapa não foi atribuído.");
            return;
        }

        if (popupCriarMapaUI == null)
        {
            Transform pai = parentPopup != null ? parentPopup : transform;
            GameObject popupObj = UIUtils.Spawn(prefabPopupCriarMapa, pai);
            popupCriarMapaUI = popupObj.GetComponent<MapCreatePopupUI>();

            if (popupCriarMapaUI == null)
            {
                Debug.LogWarning("[MapManager] O prefab do popup não possui MapCreatePopupUI.");
                return;
            }

            popupCriarMapaUI.Setup(this);
        }

        spriteImportadoPendente = null;
        popupCriarMapaUI.LimparUI();
        popupCriarMapaUI.gameObject.SetActive(true);
    }

    public void AbrirPopupCriarToken()
    {
        if (prefabPopupCriarToken == null)
        {
            Debug.LogWarning("[MapManager] Prefab do popup de criar token não foi atribuído.");
            return;
        }

        if (popupCriarTokenUI == null)
        {
            Transform pai = parentPopup != null ? parentPopup : transform;
            GameObject popupObj = UIUtils.Spawn(prefabPopupCriarToken, pai);
            popupCriarTokenUI = popupObj.GetComponent<TokenCreatePopupUI>();

            if (popupCriarTokenUI == null)
            {
                Debug.LogWarning("[MapManager] O prefab do popup não possui TokenCreatePopupUI.");
                return;
            }

            popupCriarTokenUI.Setup(this);
        }

        spriteTokenImportadoPendente = null;
        popupCriarTokenUI.LimparUI();
        popupCriarTokenUI.gameObject.SetActive(true);
    }

    public void FecharPopupCriarMapa()
    {
        if (popupCriarMapaUI != null)
            popupCriarMapaUI.gameObject.SetActive(false);
    }

    public void FecharPopupCriarToken()
    {
        if (popupCriarTokenUI != null)
            popupCriarTokenUI.gameObject.SetActive(false);
    }

    public void ImportarImagemParaNovoMapa()
    {
        FileBrowser.SetFilters(true, new FileBrowser.Filter("Imagens", ".jpg", ".png", ".jpeg"));
        FileBrowser.ShowLoadDialog(
            (paths) =>
            {
                if (paths == null || paths.Length == 0) return;
                string caminho = paths[0];

                Sprite sprite = CarregarSpriteDeArquivo(caminho);
                if (sprite == null) return;

                spriteImportadoPendente = sprite;
                pendenteCaminhoMapa = caminho;
                if (popupCriarMapaUI != null)
                    popupCriarMapaUI.AtualizarPreview(spriteImportadoPendente);
            },
            null,
            FileBrowser.PickMode.Files, false, null, null, "Selecionar imagem do mapa", "Selecionar");
    }

    public void ImportarImagemParaNovoToken()
    {
        FileBrowser.SetFilters(true, new FileBrowser.Filter("Imagens", ".jpg", ".png", ".jpeg"));
        FileBrowser.ShowLoadDialog(
            (paths) =>
            {
                if (paths == null || paths.Length == 0) return;
                string caminho = paths[0];

                Sprite sprite = CarregarSpriteDeArquivo(caminho);
                if (sprite == null) return;

                spriteTokenImportadoPendente = sprite;
                pendenteCaminhoToken = caminho;
                if (popupCriarTokenUI != null)
                    popupCriarTokenUI.AtualizarPreview(spriteTokenImportadoPendente);
            },
            null,
            FileBrowser.PickMode.Files, false, null, null, "Selecionar imagem do token", "Selecionar");
    }

    private Sprite CarregarSpriteDeArquivo(string caminho)
    {
        if (!File.Exists(caminho))
        {
            Debug.LogWarning($"[MapManager] Arquivo não encontrado: {caminho}");
            return null;
        }

        byte[] bytes = File.ReadAllBytes(caminho);
        Texture2D textura = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!textura.LoadImage(bytes))
        {
            Debug.LogWarning("[MapManager] Falha ao decodificar imagem.");
            Destroy(textura);
            return null;
        }

        Sprite sprite = Sprite.Create(textura, new Rect(0, 0, textura.width, textura.height), new Vector2(0.5f, 0.5f), 100f);
        assetsRuntimeCriados.Add(textura);
        assetsRuntimeCriados.Add(sprite);
        return sprite;
    }

    public void ConfirmarCriacaoNovoMapa()
    {
        // Defesa em profundidade — AbrirPopupCriarMapa já bloqueia não-host antes de a UI abrir,
        // mas essa guarda protege contra qualquer outro caminho que chame isso diretamente no futuro.
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode criar mapas.");
            return;
        }

        if (spriteImportadoPendente == null)
        {
            Debug.LogWarning("[MapManager] Importe uma imagem antes de confirmar o novo mapa.");
            return;
        }

        MapConfig novoMapa = ScriptableObject.CreateInstance<MapConfig>();
        string nomePadrao = $"Mapa {todosOsMapas.Count + 1}";

        if (popupCriarMapaUI != null)
            novoMapa.nomeDoMapa = popupCriarMapaUI.ObterNomeMapa(nomePadrao);
        else
            novoMapa.nomeDoMapa = nomePadrao;

        novoMapa.imagemMapa  = spriteImportadoPendente;
        novoMapa.imagemPath  = pendenteCaminhoMapa;
        novoMapa.escalaVisual = 1f;
        novoMapa.unidadesPorCelula = 1.5f;

        ConfigurarGridInicialPorAspecto(novoMapa, spriteImportadoPendente);

        todosOsMapas.Add(novoMapa);
        OnCatalogoDeMapasAlterado?.Invoke();
        CarregarMapa(novoMapa);
        FecharPopupCriarMapa();
        pendenteCaminhoMapa = string.Empty;
        SalvarEstadoVTT();
    }

    public void ConfirmarCriacaoNovoToken()
    {
        if (spriteTokenImportadoPendente == null)
        {
            Debug.LogWarning("[MapManager] Importe uma imagem antes de confirmar o novo token.");
            return;
        }

        string nomePadrao = $"Token {todosOsTokens.Count + 1}";
        string nomeToken = popupCriarTokenUI != null
            ? popupCriarTokenUI.ObterNomeToken(nomePadrao)
            : nomePadrao;

        TokenCatalogItem novoToken = new TokenCatalogItem
        {
            nome       = nomeToken,
            sprite     = spriteTokenImportadoPendente,
            imagemPath = pendenteCaminhoToken
        };

        todosOsTokens.Add(novoToken);
        OnCatalogoDeTokensAlterado?.Invoke();
        FecharPopupCriarToken();
        pendenteCaminhoToken = string.Empty;
        SalvarEstadoVTT();
    }

    /// <summary>Importa uma cena do módulo Campanhas pro catálogo de mapas da sala ATUALMENTE ABERTA
    /// — usado pelo painel "Conteudo &gt; Cena de Campanha" (BarraFerramentasManager). Ao contrário de
    /// CampanhaParaSalaConverter.EnviarMapaParaSala (grava direto em SalaData no disco, pensado pra
    /// funcionar com a sala FECHADA), este método mexe em todosOsMapas EM MEMÓRIA e reaproveita
    /// SalvarEstadoVTT pra persistir — mesmo padrão de ConfirmarCriacaoNovoMapa — assim
    /// OnCatalogoDeMapasAlterado dispara na hora, mesmo com a sala já aberta, sem esperar o próximo
    /// CarregarEstadoVTT. Não troca automaticamente pro mapa importado (só adiciona ao catálogo).</summary>
    public bool ImportarCenaDeCampanha(CampanhaMapaData cena, string campanhaId)
    {
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode importar cenas de campanha.");
            return false;
        }

        if (cena == null || string.IsNullOrWhiteSpace(campanhaId) || string.IsNullOrWhiteSpace(salaIdAtual))
        {
            Debug.LogWarning("[MapManager] ImportarCenaDeCampanha: parâmetros inválidos ou nenhuma sala ativa.");
            return false;
        }

        Sprite sprite = CarregarSpriteDeArquivo(cena.imagemPath);
        if (sprite == null)
        {
            Debug.LogWarning($"[MapManager] ImportarCenaDeCampanha: falha ao carregar imagem '{cena.imagemPath}'.");
            return false;
        }

        MapConfig novoMapa = ScriptableObject.CreateInstance<MapConfig>();
        novoMapa.nomeDoMapa        = string.IsNullOrWhiteSpace(cena.nome) ? $"Mapa {todosOsMapas.Count + 1}" : cena.nome;
        novoMapa.imagemMapa        = sprite;
        novoMapa.imagemPath        = cena.imagemPath;
        novoMapa.gridColunas       = cena.gridColunas;
        novoMapa.gridLinhas        = cena.gridLinhas;
        novoMapa.unidadesPorCelula = cena.unidadesPorCelula;
        novoMapa.escalaVisual      = cena.escalaVisual;
        novoMapa.vetorizado        = cena.vetorizado;
        novoMapa.paredes           = cena.paredes != null ? new List<WallLine>(cena.paredes) : new List<WallLine>();
        novoMapa.fogRects          = cena.fogRects != null ? new List<FogRect>(cena.fogRects) : new List<FogRect>();
        novoMapa.mapaId            = Guid.NewGuid().ToString("N");
        novoMapa.tokensNoMapa      = CampanhaParaSalaConverter.ImportarTokensDeNpcs(cena, campanhaId, salaIdAtual);

        todosOsMapas.Add(novoMapa);
        OnCatalogoDeMapasAlterado?.Invoke();
        SalvarEstadoVTT();
        return true;
    }

    // Recebe o TokenCatalogItem diretamente (referência guardada pelo próprio TokenButtonUI desde
    // o Setup) em vez de re-resolver por nome: catálogo e lista de arrasto eram ligados só pelo
    // nome exibido, então duas fichas/monstros diferentes com o mesmo nome (ex: duas fichas de
    // teste "Teste 1") colapsavam num único item — o segundo virava no-op silencioso em
    // AdicionarTokenAoCatalogo, e arrastar da lista sempre resolvia pro PRIMEIRO cadastrado
    // daquele nome, entregando a imagem/link errado a cada novo token solto.
    public void TentarSoltarTokenNoMapa(TokenCatalogItem itemCatalogo, Vector2 posicaoTela, float escalaMultiplicador = 1f)
    {
        if (itemCatalogo == null)
        {
            Debug.LogWarning("[MapManager] TentarSoltarTokenNoMapa — abortado: itemCatalogo é null.");
            return;
        }
        if (itemCatalogo.sprite == null)
        {
            Debug.LogWarning($"[MapManager] TentarSoltarTokenNoMapa — abortado: itemCatalogo.sprite é null (nome='{itemCatalogo.nome}', imagemPath='{itemCatalogo.imagemPath}').");
            return;
        }
        bool podeInteragir = PodeInteragirComMapaParaToken();
        if (!podeInteragir)
        {
            Debug.LogWarning($"[MapManager] TentarSoltarTokenNoMapa — abortado: PodeInteragirComMapaParaToken()=false " +
                $"(mapaRenderer={(mapaRenderer != null)}, mapaRenderer.sprite={(mapaRenderer != null && mapaRenderer.sprite != null)}, " +
                $"abaMapaAtiva={abaMapaAtiva}, salaAtiva={salaAtiva}).");
            return;
        }

        Camera cam = null;
        if (cameraVTT != null)
            cam = cameraVTT.GetComponent<Camera>();

        if (cam == null)
            cam = Camera.main;

        if (cam == null)
        {
            Debug.LogWarning("[MapManager] TentarSoltarTokenNoMapa — abortado: nenhuma Camera encontrada (cameraVTT nulo e Camera.main nulo).");
            return;
        }

        Vector3 posicaoMundo = CamParaMundoToken(cam, posicaoTela);
        if (!PosicaoDentroDaAreaDeTokens(posicaoMundo))
        {
            Bounds areaDebug = ObterBoundsGridFixo();
            Debug.LogWarning($"[MapManager] TentarSoltarTokenNoMapa — abortado: posição {posicaoMundo} fora da área de tokens " +
                $"(bounds min={areaDebug.min}, max={areaDebug.max}, posicaoTela={posicaoTela}).");
            return;
        }

        Debug.Log($"[MapManager] TentarSoltarTokenNoMapa — soltando '{itemCatalogo.nome}' em {posicaoMundo}.");

        float tamanhoCelula = ObterTamanhoAtualDaCelula() * Mathf.Clamp(escalaMultiplicador, 0.1f, 10f);
        posicaoMundo = AjustarPosicaoTokenAoGrid(posicaoMundo);

        string nomeToken = itemCatalogo.nome;
        Sprite spriteToken = itemCatalogo.sprite;
        string imagemPath = itemCatalogo.imagemPath ?? string.Empty;
        string tokenId = System.Guid.NewGuid().ToString("N");
        int fichasGeneralId = itemCatalogo.fichasGeneralId;
        int fichasDndId = itemCatalogo.fichasDndId;
        string ownerSteamId = itemCatalogo.ownerSteamId ?? string.Empty;
        int ownerPlayerId = itemCatalogo.ownerPlayerId;
        string monstroId = itemCatalogo.monstroId ?? string.Empty;

        SpawnTokenNoMapa(nomeToken, spriteToken, posicaoMundo, tamanhoCelula, imagemPath, tokenId,
            fichasGeneralId, fichasDndId, ownerSteamId, ownerPlayerId, monstroId);
        SalvarEstadoVTT();
        AtualizarVisaoDinamicaGeral();

        // Broadcast pra todos os clientes — QUALQUER peer pode fazer isso, não só o host:
        // NetworkSyncManager.EnviarParaTodos/EnviarTokenImagemParaTodos são mesh P2P real (mandam
        // pacote direto pra cada membro do lobby, sem exigir ser host) e o upload de imagem pro
        // Workshop dentro de UploadEBroadcastToken é vinculado à conta Steam de quem chama, não ao
        // lobby. O gate "só host" aqui bloqueava sem necessidade — era por isso que o token de um
        // jogador comum nunca aparecia (nem a imagem) pra mais ninguém, nem pro mestre.
        UploadEBroadcastToken(tokenId, nomeToken, imagemPath, posicaoMundo,
            fichasGeneralId, fichasDndId, ownerSteamId, ownerPlayerId, monstroId);
    }

    private void AtualizarGridVisual()
    {
        if (!DeveExibirGridAgora())
        {
            OcultarGridVisual();
            return;
        }

        LimparLinhasGrid();
        GarantirFundoGridVisual();

        if (!criarGridAutomatico)
            return;

        Bounds bounds = ObterBoundsGridFixo();
        float largura = bounds.size.x;
        float altura = bounds.size.y;

        if (largura < 0.001f || altura < 0.001f)
            return;

        float unidadesPorCelula = ObterTamanhoAtualDaCelula();
        int colunas = Mathf.Max(1, Mathf.RoundToInt(largura / unidadesPorCelula));
        int linhas = Mathf.Max(1, Mathf.RoundToInt(altura / unidadesPorCelula));
        float larguraUsada = colunas * unidadesPorCelula;
        float alturaUsada = linhas * unidadesPorCelula;

        // Atualiza o cellSize do Grid da Unity para refletir a célula real.
        if (gridPrincipal != null)
            gridPrincipal.cellSize = new Vector3(unidadesPorCelula, unidadesPorCelula, 1f);

        float minX = bounds.center.x - (larguraUsada * 0.5f);
        float maxX = bounds.center.x + (larguraUsada * 0.5f);
        float minY = bounds.center.y - (alturaUsada * 0.5f);
        float maxY = bounds.center.y + (alturaUsada * 0.5f);
        float z = mapaRenderer != null ? mapaRenderer.transform.position.z - 0.01f : -0.01f;

        Transform pai = ObterOuCriarContainerGridVisual();

        float espessuraMundo = Mathf.Clamp(unidadesPorCelula * espessuraLinhasGrid, 0.006f, unidadesPorCelula * 0.12f);
        espessuraBaseLinhasMundo = espessuraMundo;

        for (int c = 0; c <= colunas; c++)
        {
            float x = Mathf.Lerp(minX, maxX, (float)c / colunas);
            CriarLinha(new Vector3(x, minY, z), new Vector3(x, maxY, z), pai, espessuraMundo, unidadesPorCelula);
        }

        for (int l = 0; l <= linhas; l++)
        {
            float y = Mathf.Lerp(minY, maxY, (float)l / linhas);
            CriarLinha(new Vector3(minX, y, z), new Vector3(maxX, y, z), pai, espessuraMundo, unidadesPorCelula);
        }

        AtualizarEspessuraLinhasPorZoom();
    }

    private Transform ObterOuCriarContainerGridVisual()
    {
        if (containerLinhasGrid != null)
        {
            containerLinhasGrid.gameObject.SetActive(true);
            return containerLinhasGrid;
        }

        Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;
        Transform existente = pai.Find("Grid_Linhas_Automatico");
        if (existente != null)
        {
            containerLinhasGrid = existente;
            containerLinhasGrid.gameObject.SetActive(true);
            return containerLinhasGrid;
        }

        GameObject go = new GameObject("Grid_Linhas_Automatico");
        go.transform.SetParent(pai, false);
        containerLinhasGrid = go.transform;
        return containerLinhasGrid;
    }

    private Transform ObterOuCriarContainerTokensNoMapa()
    {
        if (containerTokensNoMapa != null)
            return containerTokensNoMapa;

        Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;
        Transform existente = pai.Find("Tokens_No_Mapa");
        if (existente != null)
        {
            containerTokensNoMapa = existente;
            return containerTokensNoMapa;
        }

        GameObject go = new GameObject("Tokens_No_Mapa");
        go.transform.SetParent(pai, false);
        containerTokensNoMapa = go.transform;
        return containerTokensNoMapa;
    }

    private void SpawnTokenNoMapa(string nomeToken, Sprite spriteToken, Vector3 posicaoMundo, float tamanhoCelula, string imagemPath = "", string tokenId = "",
        int fichasGeneralId = 0, int fichasDndId = 0, string ownerSteamId = "", int ownerPlayerId = 0, string monstroId = "",
        string npcIdCampanha = "", float raioVisaoMetros = 18f,
        int vidaAtualSalva = -1, bool bloqueadoSalvo = false, bool escondidoSalvo = false)
    {
        Transform parentTokens = ObterOuCriarContainerTokensNoMapa();
        string nomeFinal = string.IsNullOrWhiteSpace(nomeToken) ? "Token" : nomeToken;

        GameObject tokenGo;
        SpriteRenderer sr;

        if (prefabTokenNoMapa != null)
        {
            tokenGo = Instantiate(prefabTokenNoMapa, parentTokens, true);
            tokenGo.name = $"Token_{nomeFinal}";
            tokenGo.transform.position = posicaoMundo;

            // Busca o SpriteRenderer no FILHO (Imagem_Sprite)
            sr = tokenGo.GetComponentInChildren<SpriteRenderer>(true);
            if (sr == null)
            {
                GameObject childSprite = new GameObject("Imagem_Sprite");
                childSprite.transform.SetParent(tokenGo.transform, false);
                sr = childSprite.AddComponent<SpriteRenderer>();
            }
            sr.sprite = spriteToken;
        }
        else
        {
            tokenGo = new GameObject($"Token_{nomeFinal}");
            tokenGo.transform.SetParent(parentTokens, true);
            tokenGo.transform.position = posicaoMundo;

            GameObject childSprite = new GameObject("Imagem_Sprite");
            childSprite.transform.SetParent(tokenGo.transform, false);
            sr = childSprite.AddComponent<SpriteRenderer>();
            sr.sprite = spriteToken;
        }

        // Camadas de Renderização da Imagem
        // sortingOrder incrementa por token (em vez de uma constante fixa pra todos): se dois
        // tokens caem na mesma célula/posição (o que acontece com facilidade já que a posição é
        // encaixada no grid), com sortingOrder igual eles ficam empilhados pixel-a-pixel e só o de
        // cima aparece — dava a impressão de "o token novo roubou a imagem do antigo", quando na
        // verdade os dois sempre tiveram sprites corretos e distintos, só um tampando o outro.
        sr.sortingLayerID = mapaRenderer != null ? mapaRenderer.sortingLayerID : SortingLayer.NameToID(sortingLayerMapa);
        sr.sortingOrder   = (mapaRenderer != null ? mapaRenderer.sortingOrder + 4 : sortingOrderMapa + 4) + (proximaOrdemToken++) * IntervaloOrdemToken;

        // Camadas de Renderização da UI (Para ficar SEMPRE na frente do monstro)
        Canvas tokenCanvas = tokenGo.GetComponentInChildren<Canvas>(true);
        if (tokenCanvas != null)
        {
            tokenCanvas.sortingLayerID = sr.sortingLayerID;
            tokenCanvas.sortingOrder = sr.sortingOrder + 1;
        }

        // === A MÁGICA DA ESCALA ===
        // 1. A Raiz (onde o Canvas está) NUNCA muda de tamanho, fica sempre 1x1.
        tokenGo.transform.localScale = Vector3.one;

        // 2. Calcula a proporção APENAS para a imagem caber certinho no grid.
        Vector2 tamanhoLocal = spriteToken.bounds.size;
        float maiorDimensao = Mathf.Max(0.0001f, Mathf.Max(tamanhoLocal.x, tamanhoLocal.y));
        float escalaImagem = tamanhoCelula / maiorDimensao;

        // 3. Aplica a escala exclusivamente no SpriteRenderer.
        sr.transform.localScale = new Vector3(escalaImagem, escalaImagem, 1f);

        // Deixa o token redondo mesmo que a imagem de origem seja um quadrado 1:1 sem bordas
        // transparentes: um SpriteMask circular (gerado em código, sem depender de asset) recorta
        // o SpriteRenderer. Fica na raiz (escala sempre 1x1) e é dimensionado direto pelo tamanho
        // da célula, então não depende da escala variável do SpriteRenderer da imagem.
        Transform mascaraExistente = tokenGo.transform.Find("Mascara_Redonda");
        GameObject mascaraGo = mascaraExistente != null ? mascaraExistente.gameObject : new GameObject("Mascara_Redonda");
        if (mascaraExistente == null) mascaraGo.transform.SetParent(tokenGo.transform, false);
        mascaraGo.transform.localScale = new Vector3(tamanhoCelula, tamanhoCelula, 1f);
        SpriteMask mascara = mascaraGo.GetComponent<SpriteMask>();
        if (mascara == null) mascara = mascaraGo.AddComponent<SpriteMask>();
        mascara.sprite = TokenMascaraCircular.Obter();
        sr.maskInteraction = SpriteMaskInteraction.VisibleInsideMask;

        // Restringe a máscara a uma FAIXA em torno do sortingOrder deste token (±FolgaMascaraToken) —
        // sem isso, o SpriteMask afeta a Sorting Layer INTEIRA (todo token está nela), causando
        // cross-masking entre tokens que piora dependendo do que a câmera está renderizando (zoom
        // muda o que está em view/culled). Usa uma faixa LARGA (não front==back exatamente igual a
        // sortingOrder) de propósito: a documentação da Unity não deixa claro se front==back produz
        // uma faixa de um único valor válida ou uma faixa vazia (nesse segundo caso, o token nunca
        // ficaria dentro da própria máscara e sumiria por completo — foi exatamente isso que
        // aconteceu na primeira tentativa). Com IntervaloOrdemToken=8 entre tokens consecutivos e
        // ±3 de folga aqui, a faixa de um token nunca alcança a de outro (folga de 2 unidades sobra
        // de cada lado), então a ambiguidade não importa: seja qual for a semântica exata, o único
        // sortingOrder que pode cair nessa faixa é o do próprio token.
        mascara.isCustomRangeActive = true;
        mascara.frontSortingLayerID = sr.sortingLayerID;
        mascara.frontSortingOrder   = sr.sortingOrder + FolgaMascaraToken;
        mascara.backSortingLayerID  = sr.sortingLayerID;
        mascara.backSortingOrder    = sr.sortingOrder - FolgaMascaraToken;

        // O Colisor fica na raiz, com o tamanho exato da célula, para o Drag funcionar.
        BoxCollider2D col = tokenGo.GetComponent<BoxCollider2D>();
        if (col == null) col = tokenGo.AddComponent<BoxCollider2D>();
        col.size = new Vector2(tamanhoCelula, tamanhoCelula);

        // Configuração dos Scripts
        MapTokenDraggable arrastavel = tokenGo.GetComponent<MapTokenDraggable>();
        if (arrastavel == null) arrastavel = tokenGo.AddComponent<MapTokenDraggable>();
        arrastavel.Setup(this, nomeFinal, imagemPath, tokenId, donoSteamId: string.Empty,
            fichasGeneralId: fichasGeneralId, fichasDndId: fichasDndId, ownerSteamId: ownerSteamId, ownerPlayerId: ownerPlayerId,
            monstroId: monstroId, npcIdCampanha: npcIdCampanha, raioVisaoMetros: raioVisaoMetros,
            vidaAtualSalva: vidaAtualSalva, bloqueadoSalvo: bloqueadoSalvo, escondidoSalvo: escondidoSalvo);

        TokenButtonUI tokenUI = tokenGo.GetComponent<TokenButtonUI>();
        if (tokenUI == null) tokenUI = tokenGo.GetComponentInChildren<TokenButtonUI>(true);
        if (tokenUI != null)
        {
            tokenUI.Setup(this, nomeFinal, spriteToken);
            tokenUI.InicializarComoTokenDoMapa();
        }
    }

    // Extraído de AjustarPosicaoTokenAoGrid pra ser reaproveitado também por MundoParaCelula/
    // CelulaParaBoundsMundo (fog manual — ver FogRect.cs) sem duplicar a mesma matemática de
    // origem/dimensões do grid em três lugares.
    private void ObterGridDimensoesEOrigem(out int colunas, out int linhas, out float minX, out float minY)
    {
        Bounds area = ObterBoundsGridFixo();
        float tamanhoCelula = ObterTamanhoAtualDaCelula();

        colunas = Mathf.Max(1, Mathf.RoundToInt(area.size.x / tamanhoCelula));
        linhas  = Mathf.Max(1, Mathf.RoundToInt(area.size.y / tamanhoCelula));

        // Usa a mesma origem que AtualizarGridVisual para que o snap caia no centro das células
        minX = area.center.x - (colunas * tamanhoCelula * 0.5f);
        minY = area.center.y - (linhas  * tamanhoCelula * 0.5f);
    }

    public Vector3 AjustarPosicaoTokenAoGrid(Vector3 posicaoMundo)
    {
        ObterGridDimensoesEOrigem(out int colunas, out int linhas, out float minX, out float minY);
        float tamanhoCelula = ObterTamanhoAtualDaCelula();

        int indiceColuna = Mathf.Clamp(Mathf.FloorToInt((posicaoMundo.x - minX) / tamanhoCelula), 0, colunas - 1);
        int indiceLinha  = Mathf.Clamp(Mathf.FloorToInt((posicaoMundo.y - minY) / tamanhoCelula), 0, linhas  - 1);

        float x = minX + ((indiceColuna + 0.5f) * tamanhoCelula);
        float y = minY + ((indiceLinha  + 0.5f) * tamanhoCelula);
        float z = mapaRenderer != null ? mapaRenderer.transform.position.z - 0.02f : posicaoMundo.z;
        return new Vector3(x, y, z);
    }

    /// <summary>Converte uma posição de mundo na célula do grid (col, lin) que a contém — ver
    /// IGerenciadorDeParedes.MundoParaCelula.</summary>
    public (int col, int lin) MundoParaCelula(Vector3 posicaoMundo)
    {
        ObterGridDimensoesEOrigem(out int colunas, out int linhas, out float minX, out float minY);
        float tamanhoCelula = ObterTamanhoAtualDaCelula();

        int col = Mathf.Clamp(Mathf.FloorToInt((posicaoMundo.x - minX) / tamanhoCelula), 0, colunas - 1);
        int lin = Mathf.Clamp(Mathf.FloorToInt((posicaoMundo.y - minY) / tamanhoCelula), 0, linhas - 1);
        return (col, lin);
    }

    /// <summary>Bounds de mundo (centro/tamanho) da célula (col, lin) — inverso de MundoParaCelula.
    /// Ver IGerenciadorDeParedes.CelulaParaBoundsMundo.</summary>
    public Bounds CelulaParaBoundsMundo(int col, int lin)
    {
        ObterGridDimensoesEOrigem(out _, out _, out float minX, out float minY);
        float tamanhoCelula = ObterTamanhoAtualDaCelula();

        float x = minX + (col + 0.5f) * tamanhoCelula;
        float y = minY + (lin + 0.5f) * tamanhoCelula;
        float z = mapaRenderer != null ? mapaRenderer.transform.position.z : 0f;
        return new Bounds(new Vector3(x, y, z), new Vector3(tamanhoCelula, tamanhoCelula, 0.1f));
    }

    // Pathfinding automático — TerrainManager não está configurado em NENHUMA cena de Salas hoje (só
    // existe, vazio, em Campanhas) — sem uma instância arrastada aqui, CustoDeMovimentoDaCelula abaixo
    // sempre trata tudo como custo 1 (sem terreno difícil).
    [SerializeField] private TerrainManager terrainManager;

    /// <summary>Menor caminho entre duas células do grid de combate (mesma convenção (col,lin) já
    /// usada por paredes/MundoParaCelula — não Vector3Int/Tilemap, que é a convenção separada do
    /// TerrainManager). Sem diagonais, evita paredes, penaliza/evita terreno difícil — ver
    /// PathfindingGrid.CalcularCaminho (Dijkstra, não BFS puro: o pedido inclui custo variável, que
    /// BFS não modela; Dijkstra se reduz a BFS quando todo custo é 1, zero regressão no caso simples).
    /// Retorna null se não houver caminho.</summary>
    public List<(int col, int lin)> CalcularCaminhoDeToken((int col, int lin) origem, (int col, int lin) destino)
    {
        ObterGridDimensoesEOrigem(out int colunas, out int linhas, out _, out _);
        return PathfindingGrid.CalcularCaminho(origem, destino, colunas, linhas, TemParedeEntreCelulas, CustoDeMovimentoDaCelula);
    }

    /// <summary>Reaproveita a MESMA layer VisionBlocker e o MESMO buffer pré-alocado
    /// _bufferRaycastHit já usados por CalcularPoligonoDeVisao (ver ObterPontoDeImpacto) — por isso
    /// vive aqui dentro, com acesso direto aos membros privados, em vez de função solta.</summary>
    private bool TemParedeEntreCelulas((int col, int lin) a, (int col, int lin) b)
    {
        int layer = ObterLayerVisionBlocker();
        if (layer < 0) return false; // layer não configurada no projeto — mesmo fallback de ObterLayerVisionBlocker

        Vector3 centroA = CelulaParaBoundsMundo(a.col, a.lin).center;
        Vector3 centroB = CelulaParaBoundsMundo(b.col, b.lin).center;
        Vector3 direcao = centroB - centroA;
        float distancia = direcao.magnitude;
        if (distancia <= 0f) return false;

        int mascara = 1 << layer;
        int qtd = Physics2D.RaycastNonAlloc(centroA, direcao.normalized, _bufferRaycastHit, distancia, mascara);
        return qtd > 0 && _bufferRaycastHit[0].collider != null;
    }

    /// <summary>Ponte entre a convenção de célula (col,lin) do grid de combate e a convenção
    /// Vector3Int/Tilemap do TerrainManager — converte o centro da célula pra mundo e consulta
    /// TerrainManager.MundoParaCelula/ObterTerrenoEm (ambos já existentes, sem mudança). Mapeamento
    /// default (fácil de ajustar depois): Lava intransponível (igual parede), Água penaliza mas
    /// permite, Nenhum custa 1. Sem terrainManager configurado, tudo custa 1 (fallback seguro).</summary>
    private float CustoDeMovimentoDaCelula((int col, int lin) celula)
    {
        if (terrainManager == null) return 1f;

        Vector3 centro = CelulaParaBoundsMundo(celula.col, celula.lin).center;
        Vector3Int celulaTerreno = terrainManager.MundoParaCelula(centro);
        TerrainManager.TerrainType tipo = terrainManager.ObterTerrenoEm(celulaTerreno);

        return tipo switch
        {
            TerrainManager.TerrainType.Lava => float.PositiveInfinity,
            TerrainManager.TerrainType.Agua => 2f,
            _ => 1f,
        };
    }

    public bool PosicaoDentroDaAreaDeTokens(Vector3 posicaoMundo)
    {
        Bounds area = ObterBoundsGridFixo();
        return posicaoMundo.x >= area.min.x
            && posicaoMundo.x <= area.max.x
            && posicaoMundo.y >= area.min.y
            && posicaoMundo.y <= area.max.y;
    }

    public Vector3 CamParaMundoToken(Camera cam, Vector2 posicaoTela)
    {
        if (cam == null)
            return Vector3.zero;

        Vector3 mundo = cam.ScreenToWorldPoint(new Vector3(posicaoTela.x, posicaoTela.y, Mathf.Abs(cam.transform.position.z)));
        mundo.z = mapaRenderer != null ? mapaRenderer.transform.position.z - 0.02f : 0f;
        return mundo;
    }

    private bool PodeInteragirComMapaParaToken()
    {
        if (mapaRenderer == null || mapaRenderer.sprite == null)
            return false;

        if (!salaAtiva && steamLobbyManager != null && steamLobbyManager.SalaAtual.HasValue)
            salaAtiva = true;

        return abaMapaAtiva || salaAtiva;
    }

    private void AjustarCamera()
    {
        if (cameraVTT == null)
            return;

        if (mapaRenderer != null && mapaRenderer.sprite != null)
            cameraVTT.FitToSprite(mapaRenderer);

        cameraVTT.DefinirLimitesMovimento(ObterBoundsGridFixo(), ObterTamanhoAtualDaCelula() * 2f);
    }

    private void AplicarOrdemRenderizacaoMapa()
    {
        if (mapaRenderer == null || !forcarMapaAtrasDaUI)
            return;

        if (!string.IsNullOrWhiteSpace(sortingLayerMapa))
            mapaRenderer.sortingLayerName = sortingLayerMapa;

        mapaRenderer.sortingOrder = sortingOrderMapa;
    }

    private void AplicarEscalaNormalizadaDoMapa()
    {
        if (mapaRenderer == null || mapaRenderer.sprite == null || mapaAtivo == null)
            return;

        int colunas = Mathf.Max(1, mapaAtivo.gridColunas);
        int linhas = Mathf.Max(1, mapaAtivo.gridLinhas);
        float unidadesPorCelula = Mathf.Max(0.01f, mapaAtivo.unidadesPorCelula);
        float escalaVisual = Mathf.Max(0.01f, mapaAtivo.escalaVisual);

        Vector2 tamanhoLocal = mapaRenderer.sprite.bounds.size;
        if (tamanhoLocal.x < 0.001f || tamanhoLocal.y < 0.001f)
            return;

        float larguraAlvo = colunas * unidadesPorCelula * escalaVisual;
        float alturaAlvo = linhas * unidadesPorCelula * escalaVisual;
        float escalaUniforme = Mathf.Min(larguraAlvo / tamanhoLocal.x, alturaAlvo / tamanhoLocal.y);

        escalaBaseMapaAtual = Mathf.Max(0.0001f, escalaUniforme);
        mapaRenderer.transform.localScale = new Vector3(escalaBaseMapaAtual, escalaBaseMapaAtual, 1f);
    }

    private void AplicarMultiplicadorEscalaMapa()
    {
        if (mapaRenderer == null || mapaRenderer.sprite == null)
            return;

        float escalaFinal = Mathf.Max(0.0001f, escalaBaseMapaAtual * multiplicadorTamanhoGrid);
        mapaRenderer.transform.localScale = new Vector3(escalaFinal, escalaFinal, 1f);
    }

    public void DiminuirTamanhoGrid()
    {
        multiplicadorTamanhoGrid = Mathf.Clamp(multiplicadorTamanhoGrid - passoTamanhoGrid, multiplicadorMinimoGrid, multiplicadorMaximoGrid);
        AplicarMultiplicadorEscalaMapa();
        AjustarCamera();
        AtualizarEspessuraLinhasPorZoom();
        AtualizarTextoEscalaGrid();
    }

    public void AumentarTamanhoGrid()
    {
        multiplicadorTamanhoGrid = Mathf.Clamp(multiplicadorTamanhoGrid + passoTamanhoGrid, multiplicadorMinimoGrid, multiplicadorMaximoGrid);
        AplicarMultiplicadorEscalaMapa();
        AjustarCamera();
        AtualizarEspessuraLinhasPorZoom();
        AtualizarTextoEscalaGrid();
    }

    public void DefinirMultiplicadorTamanhoGrid(float novoMultiplicador)
    {
        multiplicadorTamanhoGrid = Mathf.Clamp(novoMultiplicador, multiplicadorMinimoGrid, multiplicadorMaximoGrid);
        AplicarMultiplicadorEscalaMapa();
        AjustarCamera();
        AtualizarEspessuraLinhasPorZoom();
        AtualizarTextoEscalaGrid();
    }

    public void DefinirSalaAtiva(bool ativa)
    {
        salaAtiva = ativa;
        AplicarEstadoVisualGrid();
    }

    /// <summary>Id da sala com sessão carregada em memória agora (vazio se nenhuma) — usado por
    /// CampanhaParaSalaConverter pra saber se deve gravar direto em disco ou delegar pro
    /// MapManager já aberto (ver SessaoAtivaPara).</summary>
    public string SalaIdAtual => salaIdAtual;

    /// <summary>True se ESTE MapManager já carregou e mantém em memória a sessão da sala com este
    /// localId — usado por CampanhaParaSalaConverter.EnviarMapaParaSala pra evitar gravar direto em
    /// disco numa sala que já está aberta (SalvarEstadoVTT sobrescreveria com o que está em memória
    /// na próxima chamada, perdendo o que foi gravado direto).</summary>
    public bool SessaoAtivaPara(string localId) =>
        jaCarregouEstadoNaSessao && !string.IsNullOrWhiteSpace(salaIdAtual) &&
        string.Equals(salaIdAtual, localId, StringComparison.OrdinalIgnoreCase);

    public void DefinirAbaMapaAtiva(bool ativa)
    {
        abaMapaAtiva = ativa;
        AplicarEstadoVisualGrid();
    }

    public bool ModoMoverMapaAtivo => modoMoverMapa;

    public void ToggleModoMoverMapa()
    {
        modoMoverMapa = !modoMoverMapa;
        // Cancela arrasto pendente ao desativar
        if (!modoMoverMapa)
            arrastandomApa = false;
    }

    public float ObterMultiplicadorTamanhoGrid()
    {
        return multiplicadorTamanhoGrid;
    }

    public float ObterMultiplicadorMinimoGrid()
    {
        return multiplicadorMinimoGrid;
    }

    public float ObterMultiplicadorMaximoGrid()
    {
        return multiplicadorMaximoGrid;
    }

    public float ObterTamanhoAtualDaCelulaEmMetros()
    {
        return ObterTamanhoAtualDaCelula();
    }

    private float ObterTamanhoAtualDaCelula()
    {
        if (mapaAtivo != null && mapaAtivo.unidadesPorCelula > 0f)
            return mapaAtivo.unidadesPorCelula;
        return 1.5f;
    }

    private void ConfigurarGridInicialPorAspecto(MapConfig config, Sprite sprite)
    {
        if (config == null || sprite == null)
            return;

        float larguraPx = Mathf.Max(1f, sprite.rect.width);
        float alturaPx = Mathf.Max(1f, sprite.rect.height);
        float aspect = larguraPx / alturaPx;
        int dimensaoBase = mapaAtivo != null
            ? Mathf.Max(1, Mathf.Max(mapaAtivo.gridColunas, mapaAtivo.gridLinhas))
            : 20;

        if (aspect >= 1f)
        {
            config.gridColunas = dimensaoBase;
            config.gridLinhas = Mathf.Max(1, Mathf.RoundToInt(dimensaoBase / aspect));
        }
        else
        {
            config.gridLinhas = dimensaoBase;
            config.gridColunas = Mathf.Max(1, Mathf.RoundToInt(dimensaoBase * aspect));
        }
    }

    private void CriarLinha(Vector3 inicio, Vector3 fim, Transform pai, float espessura, float tamanhoCelula)
    {
        GameObject go = new GameObject("GridLine");
        go.transform.SetParent(pai, false);

        LineRenderer lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = 2;
        lr.SetPosition(0, inicio);
        lr.SetPosition(1, fim);
        Material linhaMaterial = new Material(ObterMaterialLinhaGrid());
        assetsRuntimeCriados.Add(linhaMaterial);
        lr.material = linhaMaterial;
        lr.startColor = corLinhasGrid;
        lr.endColor = corLinhasGrid;
        lr.startWidth = espessura;
        lr.endWidth = espessura;
        lr.textureMode = LineTextureMode.Tile;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.generateLightingData = false;
        lr.alignment = LineAlignment.View;
        lr.sortingLayerID = mapaRenderer != null ? mapaRenderer.sortingLayerID : 0;
        lr.sortingOrder = mapaRenderer != null ? mapaRenderer.sortingOrder + gridSortingOrderOffset : gridSortingOrderOffset;
        lr.numCapVertices = 0;

        float comprimentoLinha = Vector3.Distance(inicio, fim);
        float tamanhoTraco = Mathf.Max(espessura * 2.5f, tamanhoCelula * 0.10f);
        lr.material.mainTextureScale = new Vector2(Mathf.Max(1f, comprimentoLinha / tamanhoTraco), 1f);

        linhasGridInstanciadas.Add(lr);
    }

    private Material ObterMaterialLinhaGrid()
    {
        if (materialLinhaGrid != null)
            return materialLinhaGrid;

        Shader shader = Shader.Find("Sprites/Default");
        materialLinhaGrid = new Material(shader);
        materialLinhaGrid.mainTexture = ObterTexturaTracejadaGrid();
        assetsRuntimeCriados.Add(materialLinhaGrid);
        return materialLinhaGrid;
    }

    private Texture2D ObterTexturaTracejadaGrid()
    {
        if (texturaTracejadaGrid != null)
            return texturaTracejadaGrid;

        texturaTracejadaGrid = new Texture2D(32, 1, TextureFormat.RGBA32, true);
        texturaTracejadaGrid.wrapMode = TextureWrapMode.Repeat;
        texturaTracejadaGrid.filterMode = FilterMode.Bilinear;
        texturaTracejadaGrid.anisoLevel = 8;
        Color[] pixels = new Color[32];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = (i % 4 == 0)
                ? new Color(1f, 1f, 1f, 0.9f)
                : Color.clear;
        }

        texturaTracejadaGrid.SetPixels(pixels);
        texturaTracejadaGrid.Apply(true, false);
        assetsRuntimeCriados.Add(texturaTracejadaGrid);
        return texturaTracejadaGrid;
    }

    private static void ConfigurarSlider(Slider slider, float min, float max, float valorInicial, System.Action<float> aoAlterar, bool valoresInteiros = false)
    {
        if (slider == null) { aoAlterar(valorInicial); return; }

        slider.minValue = min;
        slider.maxValue = max;
        slider.wholeNumbers = valoresInteiros;
        slider.value = valorInicial;
        slider.onValueChanged.AddListener((v) => aoAlterar(v));
        aoAlterar(valorInicial); // garante que o texto inicie sincronizado com o slider
    }

    private static void AtualizarTexto(TMP_Text texto, string rotulo, float valor, string formato)
    {
        if (texto != null) texto.text = $"{rotulo}: {valor.ToString(formato)}";
    }

    private static void Religar(Button btn, System.Action acao)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => acao());
    }

    // -----------------------------------------------------------------------
    // Paredes (modelo VETORIAL livre — ver WallLine) — port de
    // Campanhas/Grid/CampanhaMapManager.cs, IGerenciadorDeParedes
    // -----------------------------------------------------------------------
    //
    // Mesmo modelo de Campanhas: cada WallLine guarda pontoA/pontoB em coordenadas NORMALIZADAS
    // (0..1) relativas ao tamanho da imagem do mapa, sem nenhuma dependência do grid de combate — ver
    // NormalizadoParaMundo. AgruparEmCadeias reconecta os segmentos planos/desordenados de
    // mapaAtivo.paredes que compartilham ponta em polilinhas contíguas, cada uma virando UM único
    // EdgeCollider2D. Mutação (desenhar/apagar/vetorizar) é sempre gated a SouHost() — igual outras
    // ações de mestre já existentes neste arquivo (AbrirPopupCriarMapa, ConfirmarCriacaoNovoMapa).

    private void ReconstruirParedes(List<WallLine> paredes)
    {
        LimparParedes();
        if (paredes != null && paredes.Count > 0 && mapaRenderer != null && mapaRenderer.sprite != null)
        {
            int layer = ObterLayerVisionBlocker();
            foreach (CadeiaParede cadeia in AgruparEmCadeias(paredes, toleranciaEncadeamentoParedes))
                InstanciarCadeiaDeParede(cadeia, layer);
        }

        // Paredes mudaram (edição manual, revetorização, ou sync de rede) — qualquer visão dinâmica
        // ativa (spotlight/geral do host, fog automático do jogador) precisa recalcular os obstáculos.
        AtualizarVisaoDinamicaGeral();
    }

    private class CadeiaParede
    {
        public readonly List<Vector2> Pontos = new List<Vector2>();
        public readonly List<WallLine> Origens = new List<WallLine>();
    }

    private static List<CadeiaParede> AgruparEmCadeias(List<WallLine> linhas, float tolerancia)
    {
        var restantes = new List<WallLine>();
        foreach (WallLine l in linhas)
            if (l != null && Vector2.Distance(l.pontoA, l.pontoB) > 0.0001f)
                restantes.Add(l);

        var cadeias = new List<CadeiaParede>();

        while (restantes.Count > 0)
        {
            WallLine primeira = restantes[0];
            restantes.RemoveAt(0);

            var cadeia = new CadeiaParede();
            cadeia.Pontos.Add(primeira.pontoA);
            cadeia.Pontos.Add(primeira.pontoB);
            cadeia.Origens.Add(primeira);

            EstenderCadeia(cadeia, restantes, tolerancia, noFim: true);
            EstenderCadeia(cadeia, restantes, tolerancia, noFim: false);

            cadeias.Add(cadeia);
        }

        return cadeias;
    }

    private static void EstenderCadeia(CadeiaParede cadeia, List<WallLine> restantes, float tolerancia, bool noFim)
    {
        bool avancou = true;
        while (avancou)
        {
            avancou = false;
            Vector2 ponta = noFim ? cadeia.Pontos[cadeia.Pontos.Count - 1] : cadeia.Pontos[0];

            for (int i = 0; i < restantes.Count; i++)
            {
                WallLine l = restantes[i];
                Vector2 novoPonto;
                if (Vector2.Distance(l.pontoA, ponta) <= tolerancia) novoPonto = l.pontoB;
                else if (Vector2.Distance(l.pontoB, ponta) <= tolerancia) novoPonto = l.pontoA;
                else continue;

                if (noFim) cadeia.Pontos.Add(novoPonto);
                else cadeia.Pontos.Insert(0, novoPonto);
                cadeia.Origens.Add(l);

                restantes.RemoveAt(i);
                avancou = true;
                break;
            }
        }
    }

    private void InstanciarCadeiaDeParede(CadeiaParede cadeia, int layer)
    {
        if (cadeia == null || cadeia.Pontos.Count < 2) return;

        var mundo = new Vector3[cadeia.Pontos.Count];
        for (int i = 0; i < cadeia.Pontos.Count; i++)
            mundo[i] = NormalizadoParaMundo(cadeia.Pontos[i]);

        Vector3 centro = Vector3.zero;
        foreach (Vector3 p in mundo) centro += p;
        centro /= mundo.Length;
        centro.z = mapaRenderer.transform.position.z - 0.015f;

        var pontosLocais = new Vector2[mundo.Length];
        for (int i = 0; i < mundo.Length; i++)
            pontosLocais[i] = (Vector2)mundo[i] - (Vector2)centro;

        Transform pai = ObterOuCriarContainerParedes();
        GameObject go = new GameObject("Parede");
        go.transform.SetParent(pai, false);
        go.transform.position = centro;
        if (layer >= 0) go.layer = layer;

        EdgeCollider2D col = go.AddComponent<EdgeCollider2D>();
        col.points = pontosLocais;

        CampanhaParedeInstancia instancia = go.AddComponent<CampanhaParedeInstancia>();
        instancia.LinhasOrigem = cadeia.Origens;

        LineRenderer overlay = go.AddComponent<LineRenderer>();
        overlay.useWorldSpace = false;
        overlay.positionCount = pontosLocais.Length;
        for (int i = 0; i < pontosLocais.Length; i++)
            overlay.SetPosition(i, pontosLocais[i]);
        overlay.material = ObterMaterialLinhaParede();
        overlay.startColor = corOverlayParede;
        overlay.endColor = corOverlayParede;
        overlay.startWidth = espessuraOverlayParede;
        overlay.endWidth = espessuraOverlayParede;
        overlay.numCapVertices = 2;
        overlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        overlay.receiveShadows = false;
        overlay.generateLightingData = false;
        overlay.alignment = LineAlignment.View;
        overlay.sortingLayerID = mapaRenderer.sortingLayerID;
        overlay.sortingOrder = mapaRenderer.sortingOrder + 3; // abaixo dos tokens (+4), acima do mapa/grid
        overlay.enabled = _mostrarParedesDetectadas;
    }

    /// <summary>Converte um ponto normalizado (0..1, relativo ao tamanho da imagem) pra posição de
    /// mundo atual do mapa — acompanha automaticamente qualquer posição/escala que o mapaRenderer
    /// tiver no momento, sem nenhuma dependência de unidadesPorCelula/grid.</summary>
    private Vector3 NormalizadoParaMundo(Vector2 normalizado)
    {
        Bounds b = mapaRenderer.sprite.bounds;
        Vector3 local = new Vector3(
            b.min.x + normalizado.x * b.size.x,
            b.min.y + normalizado.y * b.size.y,
            0f);
        return mapaRenderer.transform.TransformPoint(local);
    }

    /// <summary>Inverso de NormalizadoParaMundo — usado por WallEditorTools (via IGerenciadorDeParedes)
    /// pra converter um clique (em posição de mundo) na coordenada normalizada que WallLine guarda.</summary>
    public Vector2 MundoParaNormalizado(Vector3 mundo)
    {
        if (mapaRenderer == null || mapaRenderer.sprite == null) return Vector2.zero;

        Vector3 local = mapaRenderer.transform.InverseTransformPoint(mundo);
        Bounds b = mapaRenderer.sprite.bounds;
        return new Vector2(
            (local.x - b.min.x) / Mathf.Max(0.0001f, b.size.x),
            (local.y - b.min.y) / Mathf.Max(0.0001f, b.size.y));
    }

    private Camera ObterCamera()
        => cameraVTT != null ? cameraVTT.GetComponent<Camera>() : Camera.main;

    /// <summary>Câmera do grid ativo — reaproveitada por WallEditorTools (via IGerenciadorDeParedes)
    /// pra não precisar configurar a mesma referência de câmera duas vezes no Inspector.</summary>
    public Camera CameraAtiva => ObterCamera();

    // ── Edição manual (WallEditorTools) ──────────────────────────────────────

    /// <summary>Adiciona uma parede desenhada manualmente ao mapa ativo, reconstrói e persiste
    /// imediatamente — só o host pode desenhar paredes.</summary>
    public void AdicionarParedeManual(WallLine linha)
    {
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode editar paredes.");
            return;
        }
        if (mapaAtivo == null || linha == null) return;

        mapaAtivo.paredes ??= new List<WallLine>();
        mapaAtivo.paredes.Add(linha);
        mapaAtivo.vetorizado = true;

        ReconstruirParedes(mapaAtivo.paredes);
        PersistirMapaAtivo();
    }

    /// <summary>Apaga só a porção de cada WallLine candidata que cai dentro do círculo da borracha
    /// (centroMundo, raioMundo) — não persiste sozinho (ver PersistirMapaAtivo). Retorna true se algo
    /// mudou. Só o host pode apagar paredes.</summary>
    public bool ApagarParedesNoRaio(IEnumerable<WallLine> candidatas, Vector3 centroMundo, float raioMundo)
    {
        if (!SouHost()) return false;
        if (mapaAtivo == null || mapaAtivo.paredes == null || candidatas == null) return false;

        bool alterou = false;
        var pedacos = new List<(Vector3 a, Vector3 b)>();
        var novasLinhas = new List<WallLine>();

        foreach (WallLine linha in candidatas)
        {
            if (!mapaAtivo.paredes.Contains(linha)) continue;

            Vector3 mundoA = NormalizadoParaMundo(linha.pontoA);
            Vector3 mundoB = NormalizadoParaMundo(linha.pontoB);

            pedacos.Clear();
            if (!RecortarSegmentoForaDoCirculo(mundoA, mundoB, centroMundo, raioMundo, pedacos))
                continue;

            mapaAtivo.paredes.Remove(linha);
            alterou = true;

            foreach ((Vector3 a, Vector3 b) in pedacos)
                novasLinhas.Add(new WallLine { pontoA = MundoParaNormalizado(a), pontoB = MundoParaNormalizado(b) });
        }

        if (!alterou) return false;

        mapaAtivo.paredes.AddRange(novasLinhas);
        ReconstruirParedes(mapaAtivo.paredes);
        return true;
    }

    private static bool RecortarSegmentoForaDoCirculo(Vector3 a, Vector3 b, Vector3 centro, float raio, List<(Vector3, Vector3)> pedacosRestantes)
    {
        Vector2 p0 = a;
        Vector2 p1 = b;
        Vector2 c = centro;

        Vector2 d = p1 - p0;
        Vector2 f = p0 - c;

        float coefA = Vector2.Dot(d, d);
        if (coefA < 1e-10f) return false;

        float coefB = 2f * Vector2.Dot(f, d);
        float coefC = Vector2.Dot(f, f) - raio * raio;
        float discriminante = coefB * coefB - 4f * coefA * coefC;

        if (discriminante < 0f) return false;

        float raizDisc = Mathf.Sqrt(discriminante);
        float t1 = (-coefB - raizDisc) / (2f * coefA);
        float t2 = (-coefB + raizDisc) / (2f * coefA);

        float tDentroInicio = Mathf.Max(0f, t1);
        float tDentroFim = Mathf.Min(1f, t2);

        if (tDentroInicio >= tDentroFim) return false;

        const float margem = 0.0005f;
        if (tDentroInicio > margem)
            pedacosRestantes.Add((a, Vector3.Lerp(a, b, tDentroInicio)));
        if (tDentroFim < 1f - margem)
            pedacosRestantes.Add((Vector3.Lerp(a, b, tDentroFim), b));

        return true;
    }

    /// <summary>Persiste a sala inteira (incluindo o mapa ativo) e, se este cliente for host,
    /// propaga as paredes do mapa ativo pra rede — chamado direto por AdicionarParedeManual, e por
    /// WallEditorTools ao concluir um arrasto de borracha.</summary>
    public void PersistirMapaAtivo()
    {
        SalvarEstadoVTT();
        if (SouHost() && mapaAtivo != null)
            EnviarParedesDoMapaParaTodos(mapaAtivo);
    }

    // ── Fog manual (ver Salas/Grid/FogRect.cs, FogEditorTools.cs) ───────────────────────────────
    //
    // Retângulos presos a células do grid (não à imagem do mapa) que ficam SEMPRE 100% pretos pro
    // jogador comum — diferente da escuridão dinâmica (gradiente, ver seção de Visão Dinâmica), não
    // depende de nenhum token pra existir. O host continua vendo a área semitransparente
    // (alphaFogParaHost), mesmo raciocínio já usado em MapTokenDraggable.AtualizarVisualEscondido
    // pro token Escondido.

    private Transform _containerFog;
    private MeshFilter _meshFilterFog;
    private MeshRenderer _meshRendererFog;
    private Material _materialFog;

    /// <summary>Adiciona um retângulo de fog manual ao mapa ativo, reconstrói a visualização e
    /// persiste imediatamente (e propaga pra rede via PersistirMapaAtivo, se for host). Só o host
    /// pode desenhar — mesmo padrão de AdicionarParedeManual.</summary>
    public void AdicionarFogRect(FogRect rect)
    {
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode editar fog manual.");
            return;
        }
        if (mapaAtivo == null || rect == null) return;

        mapaAtivo.fogRects ??= new List<FogRect>();
        mapaAtivo.fogRects.Add(rect);

        AtualizarMeshDeFogRects();
        PersistirMapaAtivo();
    }

    /// <summary>Remove fog manual dentro do retângulo de células informado — SUBTRAI essa área de
    /// qualquer FogRect já existente que a sobreponha, mantendo o resto de cada retângulo afetado
    /// (ver SubtrairRetangulo), em vez de apagar um retângulo inteiro de um clique só — mesma
    /// interação de arrastar de AdicionarFogRect, só que soltar REMOVE em vez de adicionar (ver
    /// FogEditorTools). Retorna true se algo mudou (e já persistiu/propagou). Só o host pode apagar.</summary>
    public bool ApagarFogNoRetangulo(int colMin, int linMin, int colMax, int linMax)
    {
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode editar fog manual.");
            return false;
        }
        if (mapaAtivo?.fogRects == null || mapaAtivo.fogRects.Count == 0) return false;

        var novaLista = new List<FogRect>(mapaAtivo.fogRects.Count);
        bool alterou = false;

        foreach (FogRect rect in mapaAtivo.fogRects)
        {
            if (rect == null) continue;

            List<FogRect> restantes = SubtrairRetangulo(rect, colMin, linMin, colMax, linMax);
            if (restantes == null) { novaLista.Add(rect); continue; } // sem sobreposição — mantém intacto

            alterou = true;
            novaLista.AddRange(restantes); // 0 a 4 pedaços (0 = retângulo original apagado por inteiro)
        }

        if (!alterou) return false;

        mapaAtivo.fogRects = novaLista;
        AtualizarMeshDeFogRects();
        PersistirMapaAtivo();
        return true;
    }

    // Subtrai o retângulo de células [eColMin..eColMax, eLinMin..eLinMax] de rect — devolve null se
    // não há sobreposição (rect fica intacto), ou a lista dos pedaços de rect que sobram FORA da
    // área subtraída (0 a 4 retângulos). Decomposição em "cruz": faixas de cima/baixo cobrem a
    // LARGURA TOTAL de rect, faixas de esquerda/direita cobrem só a ALTURA da interseção — evita
    // contar os cantos duas vezes, resultado são retângulos disjuntos que cobrem exatamente rect
    // menos a interseção.
    private static List<FogRect> SubtrairRetangulo(FogRect rect, int eColMin, int eLinMin, int eColMax, int eLinMax)
    {
        int iColMin = Mathf.Max(rect.colMin, eColMin);
        int iLinMin = Mathf.Max(rect.linMin, eLinMin);
        int iColMax = Mathf.Min(rect.colMax, eColMax);
        int iLinMax = Mathf.Min(rect.linMax, eLinMax);

        if (iColMin > iColMax || iLinMin > iLinMax) return null; // sem sobreposição

        var restantes = new List<FogRect>(4);

        if (iLinMax < rect.linMax) // faixa acima da interseção
            restantes.Add(new FogRect { colMin = rect.colMin, colMax = rect.colMax, linMin = iLinMax + 1, linMax = rect.linMax });
        if (iLinMin > rect.linMin) // faixa abaixo da interseção
            restantes.Add(new FogRect { colMin = rect.colMin, colMax = rect.colMax, linMin = rect.linMin, linMax = iLinMin - 1 });
        if (iColMin > rect.colMin) // faixa à esquerda, só na altura da interseção
            restantes.Add(new FogRect { colMin = rect.colMin, colMax = iColMin - 1, linMin = iLinMin, linMax = iLinMax });
        if (iColMax < rect.colMax) // faixa à direita, só na altura da interseção
            restantes.Add(new FogRect { colMin = iColMax + 1, colMax = rect.colMax, linMin = iLinMin, linMax = iLinMax });

        return restantes;
    }

    // ── Fontes de luz estática (tocha — ver Salas/Grid/LightSource.cs, LightEditorTools.cs) ──────
    //
    // Matematicamente idêntica à visão de um token PARADO — mesmo polígono de visão/bloqueio por
    // parede (ver AtualizarVisaoDeTodosOsTokens/AtualizarVisaoAutomaticaDoJogador, que dobram um
    // loop sobre mapaAtivo.fontesDeLuz igual ao loop que já existe pra tokens). Posição normalizada
    // (0..1, relativa à imagem do mapa — mesma convenção de WallLine), não presa a célula de grid.

    // Raio (em unidades de mundo) do marcador de edição de uma luz já plantada — usado tanto pro
    // desenho do marcador (ver LightEditorTools) quanto pro alvo de clique de RemoverFonteDeLuzEm,
    // então "o que você vê é o que você clica".
    private float ObterRaioMarcadorLuz() => ObterTamanhoAtualDaCelula() * 0.3f;

    /// <summary>Adiciona uma fonte de luz ao mapa ativo, recalcula a visão dinâmica e persiste
    /// imediatamente (e propaga pra rede via PersistirMapaAtivo, se for host). Só o host pode
    /// plantar — mesmo padrão de AdicionarFogRect/AdicionarParedeManual.</summary>
    public void AdicionarFonteDeLuz(LightSource fonte)
    {
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode editar fontes de luz.");
            return;
        }
        if (mapaAtivo == null || fonte == null) return;

        mapaAtivo.fontesDeLuz ??= new List<LightSource>();
        mapaAtivo.fontesDeLuz.Add(fonte);

        AtualizarVisaoDinamicaGeral();
        PersistirMapaAtivo();
    }

    /// <summary>Remove a fonte de luz mais próxima cujo marcador (ObterRaioMarcadorLuz) contém
    /// posicaoMundo. Retorna true se removeu (e já persistiu/propagou). Só o host pode apagar.</summary>
    public bool RemoverFonteDeLuzEm(Vector3 posicaoMundo)
    {
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode editar fontes de luz.");
            return false;
        }
        if (mapaAtivo?.fontesDeLuz == null || mapaAtivo.fontesDeLuz.Count == 0) return false;

        float raioMarcador = ObterRaioMarcadorLuz();
        LightSource alvo = null;
        float menorDistancia = float.MaxValue;
        foreach (LightSource fonte in mapaAtivo.fontesDeLuz)
        {
            if (fonte == null) continue;
            float distancia = Vector3.Distance(NormalizadoParaMundo(fonte.posicaoNormalizada), posicaoMundo);
            if (distancia <= raioMarcador && distancia < menorDistancia)
            {
                menorDistancia = distancia;
                alvo = fonte;
            }
        }
        if (alvo == null) return false;

        mapaAtivo.fontesDeLuz.Remove(alvo);
        AtualizarVisaoDinamicaGeral();
        PersistirMapaAtivo();
        return true;
    }

    /// <summary>Todas as fontes de luz do mapa ativo, já convertidas pra mundo — usado só por
    /// LightEditorTools pra desenhar marcadores de edição (ver IGerenciadorDeParedes).</summary>
    public IEnumerable<(string id, Vector3 posicaoMundo, float raioMundo, float raioMetros)> ObterFontesDeLuzEmMundo()
    {
        if (mapaAtivo?.fontesDeLuz == null) yield break;
        foreach (LightSource fonte in mapaAtivo.fontesDeLuz)
        {
            if (fonte == null) continue;
            yield return (fonte.id, NormalizadoParaMundo(fonte.posicaoNormalizada), ConverterMetrosParaMundo(fonte.raioMetros), fonte.raioMetros);
        }
    }

    /// <summary>Muda o raio de uma fonte de luz já plantada, ao vivo (recalcula a visão, NÃO
    /// persiste/propaga) — ver PersistirRaioDaFonteDeLuz. Só o host pode editar.</summary>
    public void DefinirRaioDaFonteDeLuz(string id, float raioMetros)
    {
        if (!SouHost()) return;
        LightSource fonte = mapaAtivo?.fontesDeLuz?.Find(f => f != null && f.id == id);
        if (fonte == null) return;

        fonte.raioMetros = Mathf.Max(0.1f, raioMetros);
        AtualizarVisaoDinamicaGeral();
    }

    /// <summary>Aplica + persiste + propaga pra rede o raio de uma fonte de luz — chamado ao soltar o
    /// slider do marcador (ver LightMarcadorUI/LightEditorTools), não a cada tick de arrasto. Só o
    /// host pode editar.</summary>
    public void PersistirRaioDaFonteDeLuz(string id, float raioMetros)
    {
        if (!SouHost()) return;
        DefinirRaioDaFonteDeLuz(id, raioMetros); // garante o valor final aplicado, mesmo sem onValueChanged intermediário
        PersistirMapaAtivo();
    }

    private void GarantirObjetosDeFog()
    {
        if (_containerFog != null) return;
        // offsetSortingOrder 11 (acima do padrão 10 da escuridão dinâmica) — fog manual sempre vence
        // visualmente sobre o gradiente.
        _containerFog = CriarContainerDeVisao("Visao_FogManual", out _meshFilterFog, out _meshRendererFog,
            out _materialFog, new Color(0f, 0f, 0f, 1f), offsetSortingOrder: 11);
    }

    /// <summary>Reconstrói o mesh dos retângulos de fog manual do mapa ativo — um quad por FogRect,
    /// chamado ao carregar o mapa (CarregarMapa), sempre que a lista muda (Adicionar/
    /// ApagarFogNoRetangulo/rede) e sempre que o alpha pode ter mudado (AtualizarVisaoDinamicaGeral, ver
    /// migração de host).</summary>
    private void AtualizarMeshDeFogRects()
    {
        if (mapaAtivo?.fogRects == null || mapaAtivo.fogRects.Count == 0)
        {
            if (_meshRendererFog != null) _meshRendererFog.enabled = false;
            return;
        }

        GarantirObjetosDeFog();
        if (_materialFog != null)
            _materialFog.color = new Color(0f, 0f, 0f, SouHost() ? alphaFogParaHost : 1f);

        var vertices = new List<Vector3>(mapaAtivo.fogRects.Count * 4);
        var triangulos = new List<int>(mapaAtivo.fogRects.Count * 6);
        float z = mapaRenderer != null ? mapaRenderer.transform.position.z - 0.03f : -0.03f;

        foreach (FogRect rect in mapaAtivo.fogRects)
        {
            if (rect == null) continue;
            AdicionarRetanguloDeFogAoBuffer(rect, z, vertices, triangulos);
        }

        Mesh mesh = _meshFilterFog.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh { name = "MeshFogManual" };
            _meshFilterFog.sharedMesh = mesh;
        }
        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangulos.ToArray();
        mesh.RecalculateBounds();

        _meshRendererFog.enabled = true;
    }

    private void AdicionarRetanguloDeFogAoBuffer(FogRect rect, float z, List<Vector3> vertices, List<int> triangulos)
    {
        Bounds boundsMin = CelulaParaBoundsMundo(rect.colMin, rect.linMin);
        Bounds boundsMax = CelulaParaBoundsMundo(rect.colMax, rect.linMax);

        float minX = Mathf.Min(boundsMin.min.x, boundsMax.min.x);
        float minY = Mathf.Min(boundsMin.min.y, boundsMax.min.y);
        float maxX = Mathf.Max(boundsMin.max.x, boundsMax.max.x);
        float maxY = Mathf.Max(boundsMin.max.y, boundsMax.max.y);

        int baseIndex = vertices.Count;
        vertices.Add(new Vector3(minX, minY, z));
        vertices.Add(new Vector3(maxX, minY, z));
        vertices.Add(new Vector3(maxX, maxY, z));
        vertices.Add(new Vector3(minX, maxY, z));

        triangulos.Add(baseIndex); triangulos.Add(baseIndex + 1); triangulos.Add(baseIndex + 2);
        triangulos.Add(baseIndex); triangulos.Add(baseIndex + 2); triangulos.Add(baseIndex + 3);
    }

    // ── Sincronização de paredes por rede (Fase 5 — ver NetworkSyncManager.MsgParedesPayload) ────

    private void EnviarParedesDoMapaParaTodos(MapConfig mapa)
    {
        if (mapa == null || NetworkSyncManager.Instancia == null) return;
        NetworkSyncManager.Instancia.EnviarParedesParaTodos(ComprimirParedes(mapa));
    }

    /// <summary>Chamado por NetworkSyncManager.AoNovoMembroEntrar — envia as paredes do mapa ATIVO
    /// pro jogador recém-chegado (os outros mapas da sala chegam sem paredes até virarem ativos numa
    /// próxima edição/vetorização; melhor esforço, evita reenviar todos os mapas de uma vez).</summary>
    public void EnviarParedesDoMapaAtivoParaJogador(SteamId alvo)
    {
        if (mapaAtivo == null || NetworkSyncManager.Instancia == null) return;
        NetworkSyncManager.Instancia.EnviarParedesParaJogador(ComprimirParedes(mapaAtivo), alvo);
    }

    private static byte[] ComprimirParedes(MapConfig mapa)
    {
        var payload = new MsgParedesPayload
        {
            mapaId     = mapa.mapaId,
            paredes    = mapa.paredes != null ? new List<WallLine>(mapa.paredes) : new List<WallLine>(),
            vetorizado = mapa.vetorizado,
            fogRects   = mapa.fogRects != null ? new List<FogRect>(mapa.fogRects) : new List<FogRect>(),
            alphaMaximoVisaoDinamica    = mapa.alphaMaximoVisaoDinamica,
            transicaoGradienteEmCelulas = mapa.transicaoGradienteEmCelulas,
            alphaVisaoGeralTokens       = mapa.alphaVisaoGeralTokens,
            alphaFogParaHost            = mapa.alphaFogParaHost,
            raiosBaseVisao              = mapa.raiosBaseVisao,
            corLuzVisao                 = mapa.corLuzVisao,
            fontesDeLuz                 = mapa.fontesDeLuz != null ? new List<LightSource>(mapa.fontesDeLuz) : new List<LightSource>(),
        };
        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
        using var saida = new MemoryStream();
        using (var gzip = new GZipStream(saida, CompressionMode.Compress, leaveOpen: true))
            gzip.Write(jsonBytes, 0, jsonBytes.Length);
        return saida.ToArray();
    }

    private static string DescomprimirParedes(byte[] dadosComprimidos)
    {
        using var entrada = new MemoryStream(dadosComprimidos);
        using var gzip = new GZipStream(entrada, CompressionMode.Decompress);
        using var saida = new MemoryStream();
        gzip.CopyTo(saida);
        return Encoding.UTF8.GetString(saida.ToArray());
    }

    /// <summary>Chamado por NetworkSyncManager quando o payload comprimido de paredes de um mapa
    /// termina de chegar — aplica ao MapConfig correspondente (por mapaId) e reconstrói a
    /// visualização se for o mapa ativo agora. Se o mapa ainda não chegou por outro canal (upload em
    /// andamento), ignora — melhor esforço, não há onde guardar paredes sem o MapConfig existir.</summary>
    public void ReceberParedesConcluidas(byte[] dadosComprimidos)
    {
        MsgParedesPayload payload;
        try
        {
            string json = DescomprimirParedes(dadosComprimidos);
            payload = JsonUtility.FromJson<MsgParedesPayload>(json);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MapManager] Falha ao decodificar paredes recebidas: {e.Message}");
            return;
        }

        if (payload == null || string.IsNullOrWhiteSpace(payload.mapaId)) return;

        MapConfig alvo = null;
        for (int i = 0; i < todosOsMapas.Count; i++)
        {
            if (todosOsMapas[i] != null && todosOsMapas[i].mapaId == payload.mapaId)
            { alvo = todosOsMapas[i]; break; }
        }
        if (alvo == null) return;

        alvo.paredes    = payload.paredes ?? new List<WallLine>();
        alvo.vetorizado = payload.vetorizado;
        alvo.fogRects   = payload.fogRects ?? new List<FogRect>();
        alvo.alphaMaximoVisaoDinamica    = payload.alphaMaximoVisaoDinamica;
        alvo.transicaoGradienteEmCelulas = payload.transicaoGradienteEmCelulas;
        alvo.alphaVisaoGeralTokens       = payload.alphaVisaoGeralTokens;
        alvo.alphaFogParaHost            = payload.alphaFogParaHost;
        alvo.raiosBaseVisao              = payload.raiosBaseVisao;
        alvo.corLuzVisao                 = payload.corLuzVisao;
        alvo.fontesDeLuz                 = payload.fontesDeLuz ?? new List<LightSource>();

        if (mapaAtivo == alvo)
        {
            // Config de iluminação chegou pro mapa que já está na tela (não é troca de mapa, é o
            // host mexendo nos sliders) — sincroniza os campos locais e redesenha com os novos valores,
            // mesmo tratamento que CarregarMapa faz ao trocar de mapa.
            SincronizarConfigIluminacaoDoMapa(alvo);
            ReconstruirParedes(alvo.paredes);
            AtualizarMeshDeFogRects();
        }
    }

    private int ObterLayerVisionBlocker()
    {
        int layer = LayerMask.NameToLayer(nomeLayerVisionBlocker);
        if (layer < 0)
        {
            Debug.LogWarning($"[MapManager] A layer '{nomeLayerVisionBlocker}' não existe no projeto — " +
                "crie-a em Edit > Project Settings > Tags and Layers. As paredes vão ficar na layer padrão do GameObject até isso ser feito.");
        }
        return layer;
    }

    private Material ObterMaterialLinhaParede()
    {
        if (_materialLinhaParede != null) return _materialLinhaParede;
        _materialLinhaParede = new Material(Shader.Find("Sprites/Default"));
        assetsRuntimeCriados.Add(_materialLinhaParede);
        return _materialLinhaParede;
    }

    private Transform ObterOuCriarContainerParedes()
    {
        if (_containerParedes != null) return _containerParedes;
        Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;
        GameObject go = new GameObject("Paredes_Detectadas");
        go.transform.SetParent(pai, false);
        _containerParedes = go.transform;
        return _containerParedes;
    }

    private void LimparParedes()
    {
        if (_containerParedes == null) return;
        for (int i = _containerParedes.childCount - 1; i >= 0; i--)
            Destroy(_containerParedes.GetChild(i).gameObject);
    }

    /// <summary>Liga/desliga a camada de depuração das paredes detectadas — ligado ao botão de
    /// WallEditorTools.AlternarVisibilidadeParedes.</summary>
    public void AlternarOverlayParedes()
    {
        _mostrarParedesDetectadas = !_mostrarParedesDetectadas;
        AplicarVisibilidadeOverlayParedes();
    }

    /// <summary>Chamado pelo clique de botaoFerramentasParede — mostra/esconde o painel de edição
    /// manual de paredes (WallEditorTools). Só o host pode abrir (mutação de parede é host-only).</summary>
    private void AlternarPainelFerramentasParede()
    {
        if (painelFerramentasParede == null) return;

        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode editar paredes.");
            return;
        }

        painelFerramentasParede.SetActive(!painelFerramentasParede.activeSelf);
    }

    private void AplicarVisibilidadeOverlayParedes()
    {
        if (_containerParedes == null) return;

        foreach (Transform filho in _containerParedes)
        {
            LineRenderer lr = filho.GetComponent<LineRenderer>();
            if (lr != null) lr.enabled = _mostrarParedesDetectadas;
        }
    }

    // ── Vetorização (detecção automática de paredes a partir da imagem do mapa) ──────────────────

    /// <summary>Roda a detecção automática de paredes sobre um mapa recém-criado — só o host pode
    /// vetorizar.</summary>
    public void VetorizarMapa(MapConfig mapa)
    {
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode vetorizar paredes.");
            return;
        }
        if (mapa == null || string.IsNullOrWhiteSpace(salaIdAtual)) return;

        if (_vetorizacaoEmAndamento)
        {
            Debug.LogWarning("[MapManager] Já existe uma vetorização em andamento — aguarde ela terminar antes de iniciar outra.");
            return;
        }

        StartCoroutine(VetorizarEExecutarCoroutine(mapa));
    }

    /// <summary>Chamado pelo clique de botaoRevetorizarParedes — pede confirmação e, se aceito, roda a
    /// vetorização de novo sobre o mapa ativo com a calibração atual dos sliders. Só o host pode
    /// revetorizar.</summary>
    public void RevetorizarMapaAtivo()
    {
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode vetorizar paredes.");
            return;
        }
        if (mapaAtivo == null || string.IsNullOrWhiteSpace(salaIdAtual))
        {
            Debug.LogWarning("[MapManager] Nenhum mapa ativo para revetorizar.");
            return;
        }

        if (_vetorizacaoEmAndamento)
        {
            Debug.LogWarning("[MapManager] Já existe uma vetorização em andamento — aguarde ela terminar antes de iniciar outra.");
            return;
        }

        MapConfig mapa = mapaAtivo;
        int paredesAtuais = mapa.paredes?.Count ?? 0;
        string mensagem = paredesAtuais > 0
            ? $"Detectar paredes automaticamente de novo em '{mapa.nomeDoMapa}'?\n\nIsso substitui as {paredesAtuais} parede(s) atual(is) desse mapa pelo resultado da nova detecção."
            : $"Detectar paredes automaticamente em '{mapa.nomeDoMapa}'?";

        if (PopupManager.Instance != null)
        {
            PopupManager.Instance.MostrarConfirmacao(
                "Revetorizar Paredes", mensagem,
                onSim: () => StartCoroutine(VetorizarEExecutarCoroutine(mapa)),
                labelSim: "Detectar", labelNao: "Cancelar");
        }
        else
        {
            StartCoroutine(VetorizarEExecutarCoroutine(mapa));
        }
    }

    private IEnumerator VetorizarEExecutarCoroutine(MapConfig mapa)
    {
        _vetorizacaoEmAndamento = true;

        Texture2D textura = CarregarTexturaBrutaDeArquivo(mapa.imagemPath);
        if (textura == null)
        {
            Debug.LogWarning($"[MapManager] Não foi possível reler a imagem '{mapa.imagemPath}' para vetorizar.");
            _vetorizacaoEmAndamento = false;
            yield break;
        }

        PopupManager.Instance?.MostrarCarregamento("Vetorizando paredes", mapa.nomeDoMapa);
        yield return null;

        List<WallLine> paredes = null;
        yield return StartCoroutine(CampanhaMapaVetorizador.Vetorizar(
            textura, _configVetorizacao,
            onProgresso: (p) => PopupManager.Instance?.AtualizarProgresso(p),
            onConcluido: (resultado) => paredes = resultado));

        Destroy(textura);

        // Filtra qualquer WallLine com coordenada NaN/Infinity antes de salvar — ver mesmo comentário
        // em CampanhaMapManager.VetorizarEExecutarCoroutine.
        List<WallLine> paredesValidas = SanitizarParedes(paredes);
        if (paredes != null && paredesValidas.Count < paredes.Count)
            Debug.LogWarning($"[MapManager] Descartados {paredes.Count - paredesValidas.Count} " +
                "segmento(s) de parede com coordenada inválida (NaN/Infinity) antes de salvar.");

        mapa.vetorizado = true;
        mapa.paredes = paredesValidas;
        SalvarEstadoVTT();
        EnviarParedesDoMapaParaTodos(mapa); // já é gated a SouHost() internamente pelas checagens acima (VetorizarMapa/RevetorizarMapaAtivo)

        // Só atualiza a visualização se esse ainda for o mapa ativo no grid — evita sobrescrever uma
        // cena diferente que o usuário tenha carregado enquanto a vetorização rodava em segundo plano.
        if (mapaAtivo != null && mapaAtivo.mapaId == mapa.mapaId)
        {
            ReconstruirParedes(mapa.paredes);
            _mostrarParedesDetectadas = true;
            AplicarVisibilidadeOverlayParedes();
        }

        PopupManager.Instance?.Fechar();
        _vetorizacaoEmAndamento = false;

        Debug.Log($"[MapManager] Mapa '{mapa.nomeDoMapa}' vetorizado (limiar {_configVetorizacao.limiarLuminancia:0.00}, " +
            $"dilatação {_configVetorizacao.raioDilatacao}px, contorno mín. {_configVetorizacao.comprimentoMinimoContorno}, " +
            $"épsilon {_configVetorizacao.epsilonRDP:0.00}): {mapa.paredes.Count} trecho(s) de parede detectado(s).");
    }

    private static List<WallLine> SanitizarParedes(List<WallLine> paredes)
    {
        var validas = new List<WallLine>();
        if (paredes == null) return validas;

        foreach (WallLine l in paredes)
            if (l != null && EhFinito(l.pontoA) && EhFinito(l.pontoB))
                validas.Add(l);

        return validas;
    }

    private static bool EhFinito(Vector2 v)
        => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsInfinity(v.x) && !float.IsInfinity(v.y);

    private static Texture2D CarregarTexturaBrutaDeArquivo(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return null;

        byte[] bytes = File.ReadAllBytes(caminho);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            Destroy(tex);
            return null;
        }
        return tex;
    }

    // -----------------------------------------------------------------------
    // Visão Dinâmica (Fog of War) — port de Campanhas/Grid/CampanhaMapManager.cs
    // -----------------------------------------------------------------------
    //
    // Mesma técnica de Campanhas: raios lançados do centro do token, cada um parando no primeiro
    // colisor da layer nomeLayerVisionBlocker (mesma layer das paredes — ver ObterLayerVisionBlocker,
    // já portado na região de Paredes acima), no raio de visão do token, ou na borda real da imagem do
    // mapa — o que vier primeiro. Raycasting orientado a vértice (cada ponta de parede/quina do mapa
    // ganha 3 raios exatos) garante cantos nítidos sem depender de uma grade densa de raios base.
    //
    // Duas ferramentas MANUAIS do host (spotlight de um token / visão combinada de todos) — mesmo
    // comportamento de Campanhas, só gated a SouHost(). NOVO em Salas: fog-of-war AUTOMÁTICO pra quem
    // NÃO é host — sempre ativo, combina a visão de todos os tokens cujo OwnerSteamId é o meu (ver
    // AtualizarVisaoAutomaticaDoJogador), opaco (é fog-of-war de verdade, não uma ferramenta de
    // referência do mestre). Host não recebe esse fog automático — vê o mapa inteiro por padrão.

    /// <summary>Chamado pela opção "Selecionar Visão" do menu de atalho de um token — alterna: clicar
    /// de novo no mesmo token esconde a visão; clicar em outro token troca pra a visão dele. Ferramenta
    /// do host (referência/preparação), não afeta o fog-of-war automático dos jogadores.</summary>
    public void MostrarVisaoDoToken(MapTokenDraggable token)
    {
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode usar o spotlight de visão.");
            return;
        }
        if (token == null) return;

        if (_tokenComVisaoAtiva == token)
        {
            EsconderVisaoDoToken();
            return;
        }

        DesligarVisaoDeTodosOsTokens(); // mutuamente exclusivo com a visão geral
        _tokenComVisaoAtiva = token;
        SincronizarSliderRaioVisao();
        AtualizarVisao(token);
    }

    public void EsconderVisaoDoToken()
    {
        _tokenComVisaoAtiva = null;
        if (_meshRendererVisao != null) _meshRendererVisao.enabled = false;
        if (_meshRendererLuz != null) _meshRendererLuz.enabled = false;
        SincronizarSliderRaioVisao();
    }

    /// <summary>Ponto único chamado por qualquer evento que possa invalidar a visão atual (token
    /// moveu — local ou rede —, spawnou, foi removido, ou as paredes mudaram): recalcula a ferramenta
    /// do host que estiver ativa (spotlight OU visão geral) e o fog-of-war automático do jogador
    /// (se este cliente não for host). Todos os cálculos são locais — nenhuma mensagem de rede nova.</summary>
    public void AtualizarVisaoDinamicaGeral()
    {
        if (_tokenComVisaoAtiva != null) AtualizarVisao(_tokenComVisaoAtiva);
        else if (_visaoTodosOsTokensAtiva) AtualizarVisaoDeTodosOsTokens();

        // _previaVisaoJogadorAtiva: botão "Visão de Jogador" na ferramenta de Fog — deixa o host
        // acionar o mesmo fog-of-war automático que um jogador comum veria, sem precisar de um
        // segundo cliente pra testar. Ver AlternarPreviaVisaoJogador.
        if (!SouHost() || _previaVisaoJogadorAtiva) AtualizarVisaoAutomaticaDoJogador();

        // Reavalia o alpha do fog manual (host vê semitransparente, jogador vê opaco) — cobre o caso
        // raro de mudança de host em runtime (migração de ownership do lobby).
        AtualizarMeshDeFogRects();
    }

    // ── Configuração de iluminação dinâmica (ferramenta do Mestre — ver BarraFerramentasManager, ──
    // content "Iluminação") — os 5 campos [SerializeField] acima (raiosBaseVisao,
    // alphaMaximoVisaoDinamica, transicaoGradienteEmCelulas, alphaVisaoGeralTokens,
    // alphaFogParaHost) deixam de ser só defaults de Inspector e passam a ser POR MAPA (ver
    // MapConfig, MapSaveData): CarregarMapa sincroniza os campos locais a partir do MapConfig ao
    // trocar de mapa, os setters abaixo aplicam mudança de slider LOCAL/AO VIVO (sem custo de
    // disco/rede), e PersistirConfigIluminacao grava de volta no MapConfig + propaga pra rede — só
    // chamado UMA VEZ quando o content da ferramenta fecha, não a cada frame de arrasto de slider
    // (mesmo cuidado já tomado pela borracha de paredes em WallEditorTools).

    private bool _previaVisaoJogadorAtiva;

    /// <summary>True enquanto o host estiver com a prévia de "Visão de Jogador" ligada — ver
    /// AlternarPreviaVisaoJogador. Sempre false pra um cliente que não é host (não faz sentido
    /// "prever" a própria visão).</summary>
    public bool PreviaVisaoJogadorAtiva => _previaVisaoJogadorAtiva;

    /// <summary>Chamado pelo botão "Visão de Jogador" dentro da ferramenta de Fog — liga/desliga,
    /// só pro host, o mesmo fog-of-war automático que um jogador comum veria (combinado dos tokens
    /// que o HOST possui neste mapa), sem precisar de um segundo cliente pra testar.</summary>
    public void AlternarPreviaVisaoJogador()
    {
        if (!SouHost()) return;

        _previaVisaoJogadorAtiva = !_previaVisaoJogadorAtiva;
        if (_previaVisaoJogadorAtiva)
            AtualizarVisaoAutomaticaDoJogador();
        else
        {
            if (_meshRendererVisaoJogador != null) _meshRendererVisaoJogador.enabled = false;
            if (_meshRendererLuzJogador != null) _meshRendererLuzJogador.enabled = false;
        }
    }

    /// <summary>Copia os 5 campos de config de iluminação do MapConfig pros campos locais — chamado
    /// por CarregarMapa (troca de mapa) e por ReceberParedesConcluidas (config de outro cliente
    /// chegou pro mapa já ativo). NÃO persiste nem propaga — é só sincronização de leitura.</summary>
    private void SincronizarConfigIluminacaoDoMapa(MapConfig mapa)
    {
        if (mapa == null) return;
        raiosBaseVisao              = mapa.raiosBaseVisao;
        alphaMaximoVisaoDinamica    = mapa.alphaMaximoVisaoDinamica;
        transicaoGradienteEmCelulas = mapa.transicaoGradienteEmCelulas;
        alphaVisaoGeralTokens       = mapa.alphaVisaoGeralTokens;
        alphaFogParaHost            = mapa.alphaFogParaHost;
        corLuzVisao                 = mapa.corLuzVisao;
        // Não chama AtualizarVisaoDinamicaGeral() aqui: os dois chamadores (CarregarMapa,
        // ReceberParedesConcluidas) já chamam ReconstruirParedes logo em seguida, que termina
        // chamando AtualizarVisaoDinamicaGeral() — chamar aqui também duplicaria o recálculo à toa.
        AtualizarMateriaisDeIluminacao();
    }

    /// <summary>Reaplica o alpha atual (campos locais) nos materiais de escuridão dinâmica JÁ
    /// criados — GarantirObjetosDeVisao* só define a cor na CRIAÇÃO do material (lazy, primeira vez
    /// que a visão é usada), então mudar o slider depois disso não tinha efeito sem isto. Chamado
    /// pelos setters ao vivo abaixo e por SincronizarConfigIluminacaoDoMapa.</summary>
    private void AtualizarMateriaisDeIluminacao()
    {
        if (_materialEscuridaoVisao != null)
            _materialEscuridaoVisao.color = new Color(0f, 0f, 0f, alphaMaximoVisaoDinamica);
        if (_materialEscuridaoVisaoJogador != null)
            _materialEscuridaoVisaoJogador.color = new Color(0f, 0f, 0f, alphaMaximoVisaoDinamica);
        if (_materialEscuridaoVisaoGeral != null)
            _materialEscuridaoVisaoGeral.color = new Color(0f, 0f, 0f, alphaVisaoGeralTokens);

        Color corLuz = ObterCorMaterialLuz();
        if (_materialLuz != null) _materialLuz.color = corLuz;
        if (_materialLuzGeral != null) _materialLuzGeral.color = corLuz;
        if (_materialLuzJogador != null) _materialLuzJogador.color = corLuz;
    }

    // Getters — usados por BarraFerramentasManager.SincronizarSlidersDeIluminacao pra popular os
    // sliders/roleta de cor com o valor atual (do mapa ativo) sempre que o content de Iluminação abre.
    public int   RaiosBaseVisao              => raiosBaseVisao;
    public float AlphaMaximoVisaoDinamica    => alphaMaximoVisaoDinamica;
    public float TransicaoGradienteEmCelulas => transicaoGradienteEmCelulas;
    public float AlphaVisaoGeralTokens       => alphaVisaoGeralTokens;
    public float AlphaFogParaHost            => alphaFogParaHost;
    public Color CorLuzVisao                 => corLuzVisao;

    // Setters ao vivo — chamados pelos sliders da ferramenta de Iluminação a cada onValueChanged.
    // Aplicam local e imediatamente (sem disco/rede); PersistirConfigIluminacao() é quem persiste.

    public void DefinirRaiosBaseVisao(int valor)
    {
        raiosBaseVisao = Mathf.Clamp(valor, 16, 360);
        AtualizarVisaoDinamicaGeral();
    }

    public void DefinirAlphaMaximoVisaoDinamica(float valor)
    {
        alphaMaximoVisaoDinamica = Mathf.Clamp01(valor);
        AtualizarMateriaisDeIluminacao();
    }

    public void DefinirTransicaoGradienteEmCelulas(float valor)
    {
        transicaoGradienteEmCelulas = Mathf.Max(0f, valor);
        AtualizarVisaoDinamicaGeral();
    }

    public void DefinirAlphaVisaoGeralTokens(float valor)
    {
        alphaVisaoGeralTokens = Mathf.Clamp01(valor);
        AtualizarMateriaisDeIluminacao();
    }

    public void DefinirAlphaFogParaHost(float valor)
    {
        alphaFogParaHost = Mathf.Clamp01(valor);
        AtualizarMeshDeFogRects();
    }

    /// <summary>Roleta de cor (FlexibleColorPicker.onColorChange) — ao vivo, a cada arrasto. Diferente
    /// dos outros 4 setters, também pode precisar CRIAR ou ESCONDER os 3 meshes de luz (ligar/desligar
    /// o filtro, ver LuzVisivel), não só reaplicar um material já existente — por isso chama
    /// AtualizarVisaoDinamicaGeral() (recalcula tudo) em vez de só AtualizarMateriaisDeIluminacao().</summary>
    public void DefinirCorLuzVisao(Color cor)
    {
        corLuzVisao = cor;
        AtualizarMateriaisDeIluminacao();
        AtualizarVisaoDinamicaGeral();
    }

    /// <summary>Grava os campos locais de volta no MapConfig ativo e persiste + propaga pra rede
    /// (mesmo canal de paredes/fogRects — ver MsgParedesPayload) — chamado UMA VEZ quando o content
    /// da ferramenta de Iluminação fecha, não a cada frame de slider/arrasto na roleta. Só o host
    /// pode configurar.</summary>
    public void PersistirConfigIluminacao()
    {
        if (!SouHost() || mapaAtivo == null) return;

        mapaAtivo.raiosBaseVisao              = raiosBaseVisao;
        mapaAtivo.alphaMaximoVisaoDinamica    = alphaMaximoVisaoDinamica;
        mapaAtivo.transicaoGradienteEmCelulas = transicaoGradienteEmCelulas;
        mapaAtivo.alphaVisaoGeralTokens       = alphaVisaoGeralTokens;
        mapaAtivo.alphaFogParaHost            = alphaFogParaHost;
        mapaAtivo.corLuzVisao                 = corLuzVisao;

        PersistirMapaAtivo();
    }

    private void AtualizarVisao(MapTokenDraggable token)
    {
        if (mapaRenderer == null || mapaRenderer.sprite == null || token == null) return;

        Vector3 origem = token.transform.position;
        origem.z = 0f;

        float raioMundo = ConverterMetrosParaMundo(token.RaioVisaoMetros);
        List<Vector3> pontos = CalcularPoligonoDeVisao(origem, raioMundo, out List<Vector3> pontosNaBorda);
        if (pontos.Count < 3) { EsconderVisaoDoToken(); return; }

        GarantirObjetosDeVisao();
        ConstruirMeshEscuridao(_meshFilterVisao, origem, pontos, pontosNaBorda, ObterDistanciaTransicaoGradiente());
        _meshRendererVisao.enabled = true;

        if (LuzVisivel)
        {
            GarantirObjetosDeLuz();
            var verticesLuz = new List<Vector3>(pontos.Count + 1);
            var triangulosLuz = new List<int>(pontos.Count * 3);
            var coresLuz = new List<Color>(pontos.Count + 1);
            AdicionarLequeDeLuzAoBuffer(origem, pontos, verticesLuz, triangulosLuz, coresLuz);
            AplicarBufferAoMesh(_meshFilterLuz, verticesLuz, triangulosLuz, coresLuz);
            _meshRendererLuz.enabled = true;
        }
        else if (_meshRendererLuz != null)
        {
            _meshRendererLuz.enabled = false;
        }
    }

    // Converte metros (unidade sempre usada na UI/slider/persistência) pra unidades de mundo do mapa
    // ATUAL, via a convenção fixa de mesa "1 quadrado = 1.5m" — não assume que unidadesPorCelula do
    // mapa seja 1.5 (embora normalmente seja), só que o slider sempre representa metros reais.
    private float ConverterMetrosParaMundo(float metros)
        => (metros / MetrosPorQuadrado) * ObterTamanhoAtualDaCelula();

    // Lança os raios (base + extras nos vértices de parede), ordena por ângulo — TODOS na mesma
    // convenção [0, 2π), ver NormalizarAngulo — e devolve os pontos de impacto (retorno) junto com os
    // mesmos pontos projetados até a borda real do mapa (pontosNaBordaExterna, out), usados por
    // ConstruirMeshEscuridao pra formar os quads do donut.
    private List<Vector3> CalcularPoligonoDeVisao(Vector3 origem, float raioMundo, out List<Vector3> pontosNaBordaExterna)
    {
        Bounds limiteMapa = ObterLimiteExternoDoMapa();

        var angulos = new List<float>(raiosBaseVisao + 32);
        float passoBase = 2f * Mathf.PI / raiosBaseVisao;
        for (int i = 0; i < raiosBaseVisao; i++)
            angulos.Add(i * passoBase);

        const float epsilonAngular = 0.0009f; // ~0.05°, evita o raio "raspar" exatamente na quina do colisor

        void AdicionarAnguloDeVertice(Vector3 ponto)
        {
            float ang = NormalizarAngulo(Mathf.Atan2(ponto.y - origem.y, ponto.x - origem.x));
            angulos.Add(NormalizarAngulo(ang - epsilonAngular));
            angulos.Add(ang);
            angulos.Add(NormalizarAngulo(ang + epsilonAngular));
        }

        foreach (Vector3 vertice in ColetarVerticesDeParedesNoRaio(origem, raioMundo))
            AdicionarAnguloDeVertice(vertice);

        AdicionarAnguloDeVertice(new Vector3(limiteMapa.min.x, limiteMapa.min.y, 0f));
        AdicionarAnguloDeVertice(new Vector3(limiteMapa.max.x, limiteMapa.min.y, 0f));
        AdicionarAnguloDeVertice(new Vector3(limiteMapa.max.x, limiteMapa.max.y, 0f));
        AdicionarAnguloDeVertice(new Vector3(limiteMapa.min.x, limiteMapa.max.y, 0f));

        angulos.Sort();

        int layer = ObterLayerVisionBlocker();
        int mascara = layer >= 0 ? (1 << layer) : 0;

        var pontos = new List<Vector3>(angulos.Count);
        pontosNaBordaExterna = new List<Vector3>(angulos.Count);

        float anguloAnterior = float.NegativeInfinity;
        foreach (float ang in angulos)
        {
            if (ang - anguloAnterior < 1e-5f) continue;
            anguloAnterior = ang;

            Vector3 direcao = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f);

            Vector3 pontoNaBorda = IntersetarRetangulo(origem, direcao, limiteMapa);
            float distanciaAteABorda = Vector3.Distance(origem, pontoNaBorda);

            float distanciaLimiteVisao = Mathf.Min(distanciaAteABorda, raioMundo);
            Vector3 pontoNoLimiteDeVisao = origem + direcao * distanciaLimiteVisao;

            Vector3 ponto = mascara != 0
                ? ObterPontoDeImpacto(origem, direcao, distanciaLimiteVisao, mascara, pontoNoLimiteDeVisao)
                : pontoNoLimiteDeVisao;

            pontos.Add(ponto);
            pontosNaBordaExterna.Add(pontoNaBorda);
        }

        return pontos;
    }

    // Normaliza qualquer ângulo (incluindo os de Mathf.Atan2, que vêm em (-π, π]) pra [0, 2π) — mesma
    // convenção dos raios base, condição necessária pra um Sort() numérico simples produzir a ordem
    // angular correta ao redor do círculo completo.
    private static float NormalizarAngulo(float ang)
    {
        const float duasVoltas = 2f * Mathf.PI;
        ang %= duasVoltas;
        if (ang < 0f) ang += duasVoltas;
        return ang;
    }

    private Vector3 ObterPontoDeImpacto(Vector3 origem, Vector3 direcao, float distanciaMaxima, int mascara, Vector3 fallback)
    {
        int qtd = Physics2D.RaycastNonAlloc(origem, direcao, _bufferRaycastHit, distanciaMaxima, mascara);
        return qtd > 0 && _bufferRaycastHit[0].collider != null ? (Vector3)_bufferRaycastHit[0].point : fallback;
    }

    // Culling espacial (Physics2D.OverlapCircleNonAlloc): só extrai vértices das paredes que realmente
    // caem dentro do raio de visão do token, em vez de percorrer TODAS as paredes da cena a cada
    // recálculo.
    private List<Vector3> ColetarVerticesDeParedesNoRaio(Vector3 origem, float raioMundo)
    {
        var vertices = new List<Vector3>();

        int layer = ObterLayerVisionBlocker();
        int mascara = layer >= 0 ? (1 << layer) : 0;
        if (mascara == 0) return vertices;

        int qtd = Physics2D.OverlapCircleNonAlloc(origem, raioMundo, _bufferColisoresNoRaio, mascara);
        for (int i = 0; i < qtd; i++)
        {
            if (_bufferColisoresNoRaio[i] is not EdgeCollider2D col) continue;

            foreach (Vector2 pontoLocal in col.points)
                vertices.Add(col.transform.TransformPoint(pontoLocal));
        }

        return vertices;
    }

    private Bounds ObterLimiteExternoDoMapa() => mapaRenderer.bounds;

    // Interseção raio×retângulo (método dos "slabs") assumindo origem DENTRO do retângulo.
    private static Vector3 IntersetarRetangulo(Vector3 origem, Vector3 direcao, Bounds rect)
    {
        float tMax = float.MaxValue;

        if (Mathf.Abs(direcao.x) > 1e-6f)
        {
            float t1 = (rect.min.x - origem.x) / direcao.x;
            float t2 = (rect.max.x - origem.x) / direcao.x;
            tMax = Mathf.Min(tMax, Mathf.Max(t1, t2));
        }

        if (Mathf.Abs(direcao.y) > 1e-6f)
        {
            float t1 = (rect.min.y - origem.y) / direcao.y;
            float t2 = (rect.max.y - origem.y) / direcao.y;
            tMax = Mathf.Min(tMax, Mathf.Max(t1, t2));
        }

        return origem + direcao * Mathf.Max(0f, tMax);
    }

    private void GarantirObjetosDeVisao()
    {
        if (_containerVisao != null) return;
        // Alpha do MATERIAL é o teto do gradiente (multiplicado pelo alpha por-vértice, ver
        // AdicionarDonutAoBuffer) — não é mais opaco fixo.
        _containerVisao = CriarContainerDeVisao("Visao_Escuridao", out _meshFilterVisao, out _meshRendererVisao,
            out _materialEscuridaoVisao, new Color(0f, 0f, 0f, alphaMaximoVisaoDinamica));
    }

    private void GarantirObjetosDeVisaoGeral()
    {
        if (_containerVisaoGeral != null) return;
        _containerVisaoGeral = CriarContainerDeVisao("Visao_Geral_Todos_Tokens", out _meshFilterVisaoGeral, out _meshRendererVisaoGeral,
            out _materialEscuridaoVisaoGeral, new Color(0f, 0f, 0f, alphaVisaoGeralTokens));
    }

    private void GarantirObjetosDeVisaoJogador()
    {
        if (_containerVisaoJogador != null) return;
        _containerVisaoJogador = CriarContainerDeVisao("Visao_Jogador_FogOfWar", out _meshFilterVisaoJogador, out _meshRendererVisaoJogador,
            out _materialEscuridaoVisaoJogador, new Color(0f, 0f, 0f, alphaMaximoVisaoDinamica));
    }

    private Color ObterCorMaterialLuz() => new Color(corLuzVisao.r, corLuzVisao.g, corLuzVisao.b, alphaLuzPersonagem);

    private void GarantirObjetosDeLuz()
    {
        if (_containerLuz != null) return;
        _containerLuz = CriarContainerDeVisao("Visao_CorDaLuz", out _meshFilterLuz, out _meshRendererLuz,
            out _materialLuz, ObterCorMaterialLuz());
    }

    private void GarantirObjetosDeLuzGeral()
    {
        if (_containerLuzGeral != null) return;
        _containerLuzGeral = CriarContainerDeVisao("Visao_CorDaLuz_Geral", out _meshFilterLuzGeral, out _meshRendererLuzGeral,
            out _materialLuzGeral, ObterCorMaterialLuz());
    }

    private void GarantirObjetosDeLuzJogador()
    {
        if (_containerLuzJogador != null) return;
        _containerLuzJogador = CriarContainerDeVisao("Visao_CorDaLuz_Jogador", out _meshFilterLuzJogador, out _meshRendererLuzJogador,
            out _materialLuzJogador, ObterCorMaterialLuz());
    }

    private Transform CriarContainerDeVisao(string nome, out MeshFilter meshFilter, out MeshRenderer meshRenderer,
        out Material material, Color cor, int offsetSortingOrder = 10)
    {
        Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;

        GameObject go = new GameObject(nome);
        go.transform.SetParent(pai, false);

        meshFilter = go.AddComponent<MeshFilter>();
        meshRenderer = go.AddComponent<MeshRenderer>();
        material = new Material(Shader.Find("Sprites/Default")) { color = cor };
        assetsRuntimeCriados.Add(material);
        meshRenderer.material = material;
        meshRenderer.sortingLayerID = mapaRenderer != null ? mapaRenderer.sortingLayerID : 0;
        // Acima de tudo (mapa/grid/paredes/tokens em +1..+4) — a escuridão precisa cobrir até os tokens.
        // offsetSortingOrder maior que o padrão (10) coloca por cima até da escuridão dinâmica — ver
        // GarantirObjetosDeFog (+11), pro fog manual sempre vencer visualmente sobre o gradiente.
        meshRenderer.sortingOrder = (mapaRenderer != null ? mapaRenderer.sortingOrder : 0) + offsetSortingOrder;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.enabled = false;

        return go.transform;
    }

    // Distância (em unidades de mundo) que o gradiente percorre a partir da borda da visão até
    // atingir o alpha máximo — ver AdicionarDonutAoBuffer.
    private float ObterDistanciaTransicaoGradiente() => transicaoGradienteEmCelulas * ObterTamanhoAtualDaCelula();

    // Constrói o mesh "donut" de UM token num MeshFilter específico — reaproveitado pelo spotlight
    // (_meshFilterVisao) e, futuramente, por qualquer outro mesh de token único.
    private static void ConstruirMeshEscuridao(MeshFilter meshFilter, Vector3 origem, List<Vector3> pontos, List<Vector3> pontosExternos, float distanciaTransicaoGradiente)
    {
        var vertices = new List<Vector3>(pontos.Count * 3);
        var triangulos = new List<int>(pontos.Count * 12);
        var cores = new List<Color>(pontos.Count * 3);
        AdicionarDonutAoBuffer(origem, pontos, pontosExternos, distanciaTransicaoGradiente, vertices, triangulos, cores);
        AplicarBufferAoMesh(meshFilter, vertices, triangulos, cores);
    }

    private static void AplicarBufferAoMesh(MeshFilter meshFilter, List<Vector3> vertices, List<int> triangulos, List<Color> cores)
    {
        Mesh mesh = meshFilter.sharedMesh;
        if (mesh == null)
        {
            mesh = new Mesh { name = "MeshEscuridaoVisao" };
            meshFilter.sharedMesh = mesh;
        }

        mesh.Clear();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangulos.ToArray();
        mesh.colors = cores.ToArray();
        mesh.RecalculateBounds();
    }

    // Adiciona ao buffer compartilhado (vertices/triangulos/cores) os 3×N vértices e 4 triângulos
    // por setor angular consecutivo de UM donut — reaproveitado tanto pelo spotlight de um único
    // token quanto pela combinação de vários tokens num mesh só (visão geral do host e fog-of-war
    // automático do jogador).
    //
    // Três anéis por setor (antes eram dois): INTERNO (borda da visão, alpha 0 — transparente ali
    // mesmo), MEIO (a distanciaTransicaoGradiente da borda da visão EM DIREÇÃO à borda do mapa —
    // Vector3.MoveTowards nunca ultrapassa o ponto externo, então clampa sozinho se o mapa for
    // pequeno) e EXTERNO (borda do mapa), os dois últimos com alpha 1. O alpha FINAL de cada pixel é
    // material.a × vertex.a (comportamento padrão do shader Sprites/Default) — como o material já
    // carrega o alpha máximo desejado (ver GarantirObjetosDeVisao/GarantirObjetosDeVisaoGeral/
    // GarantirObjetosDeVisaoJogador), o vertex alpha só precisa ir de 0 a 1 pra o efeito ficar
    // "escurece progressivamente até um teto, não pula direto pro opaco".
    private static void AdicionarDonutAoBuffer(Vector3 origem, List<Vector3> pontos, List<Vector3> pontosExternos,
        float distanciaTransicaoGradiente, List<Vector3> vertices, List<int> triangulos, List<Color> cores)
    {
        int n = pontos.Count;
        int baseIndex = vertices.Count;
        float z = origem.z - 0.02f;

        for (int i = 0; i < n; i++)
        {
            Vector3 pInterno = pontos[i]; pInterno.z = z;
            vertices.Add(pInterno);
            cores.Add(new Color(0f, 0f, 0f, 0f));
        }
        for (int i = 0; i < n; i++)
        {
            Vector3 pMeio = Vector3.MoveTowards(pontos[i], pontosExternos[i], distanciaTransicaoGradiente);
            pMeio.z = z;
            vertices.Add(pMeio);
            cores.Add(new Color(0f, 0f, 0f, 1f));
        }
        for (int i = 0; i < n; i++)
        {
            Vector3 pExterno = pontosExternos[i]; pExterno.z = z;
            vertices.Add(pExterno);
            cores.Add(new Color(0f, 0f, 0f, 1f));
        }

        for (int i = 0; i < n; i++)
        {
            int iProx = (i + 1) % n;
            int pi = baseIndex + i, piProx = baseIndex + iProx;
            int mi = baseIndex + n + i, miProx = baseIndex + n + iProx;
            int qi = baseIndex + 2 * n + i, qiProx = baseIndex + 2 * n + iProx;

            // Quad interno → meio (o gradiente em si)
            triangulos.Add(pi); triangulos.Add(piProx); triangulos.Add(miProx);
            triangulos.Add(pi); triangulos.Add(miProx); triangulos.Add(mi);

            // Quad meio → externo (achatado no alpha máximo)
            triangulos.Add(mi); triangulos.Add(miProx); triangulos.Add(qiProx);
            triangulos.Add(mi); triangulos.Add(qiProx); triangulos.Add(qi);
        }
    }

    // Leque simples (origem + polígono de visão, "pontos") pro filtro de cor de luz — o INVERSO do
    // donut acima: cobre o interior visível, não o exterior escurecido. Todos os vértices na mesma
    // cor (branco — o tingimento real vem do material, ver ObterCorMaterialLuz), cores só existe
    // porque AplicarBufferAoMesh espera um array do mesmo tamanho de vertices.
    private static void AdicionarLequeDeLuzAoBuffer(Vector3 origem, List<Vector3> pontos,
        List<Vector3> vertices, List<int> triangulos, List<Color> cores)
    {
        int n = pontos.Count;
        int baseIndex = vertices.Count;
        // Levemente mais perto da câmera que a escuridão (z = origem.z - 0.02, ver AdicionarDonutAoBuffer)
        // não importa pra correção visual (as regiões são disjuntas), só evita coplanaridade exata.
        float z = origem.z - 0.021f;

        Vector3 origemZ = origem; origemZ.z = z;
        vertices.Add(origemZ);
        cores.Add(Color.white);
        for (int i = 0; i < n; i++)
        {
            Vector3 p = pontos[i]; p.z = z;
            vertices.Add(p);
            cores.Add(Color.white);
        }

        for (int i = 0; i < n; i++)
        {
            int atual = baseIndex + 1 + i;
            int proximo = baseIndex + 1 + ((i + 1) % n);
            triangulos.Add(baseIndex); triangulos.Add(atual); triangulos.Add(proximo);
        }
    }

    // ── Visão Geral de Todos os Tokens (ferramenta do HOST) ──────────────────────────────────────

    /// <summary>Chamado pelo clique de botaoVisaoTodosOsTokens. Só o host pode ligar.</summary>
    public void AlternarVisaoDeTodosOsTokens()
    {
        if (!SouHost())
        {
            Debug.LogWarning("[MapManager] Apenas o host pode usar a visão geral de todos os tokens.");
            return;
        }

        if (_visaoTodosOsTokensAtiva)
        {
            DesligarVisaoDeTodosOsTokens();
            return;
        }

        EsconderVisaoDoToken(); // mutuamente exclusivo com o spotlight de um único token
        _visaoTodosOsTokensAtiva = true;
        AtualizarVisaoDeTodosOsTokens();
    }

    private void DesligarVisaoDeTodosOsTokens()
    {
        _visaoTodosOsTokensAtiva = false;
        if (_meshRendererVisaoGeral != null) _meshRendererVisaoGeral.enabled = false;
        if (_meshRendererLuzGeral != null) _meshRendererLuzGeral.enabled = false;
    }

    private void AtualizarVisaoDeTodosOsTokens()
    {
        if (!_visaoTodosOsTokensAtiva) return;
        if (mapaRenderer == null || mapaRenderer.sprite == null || containerTokensNoMapa == null)
        {
            DesligarVisaoDeTodosOsTokens();
            return;
        }

        var todosVertices = new List<Vector3>();
        var todosTriangulos = new List<int>();
        var todasCores = new List<Color>();
        var verticesLuz = new List<Vector3>();
        var triangulosLuz = new List<int>();
        var coresLuz = new List<Color>();
        float distanciaTransicao = ObterDistanciaTransicaoGradiente();

        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            MapTokenDraggable token = containerTokensNoMapa.GetChild(i)?.GetComponent<MapTokenDraggable>();
            if (token == null) continue;

            Vector3 origem = token.transform.position;
            origem.z = 0f;

            float raioMundo = ConverterMetrosParaMundo(token.RaioVisaoMetros);
            List<Vector3> pontos = CalcularPoligonoDeVisao(origem, raioMundo, out List<Vector3> pontosNaBorda);
            if (pontos.Count < 3) continue;

            AdicionarDonutAoBuffer(origem, pontos, pontosNaBorda, distanciaTransicao, todosVertices, todosTriangulos, todasCores);
            if (LuzVisivel) AdicionarLequeDeLuzAoBuffer(origem, pontos, verticesLuz, triangulosLuz, coresLuz);
        }

        // Fontes de luz estática (tocha — ver LightSource.cs) — matematicamente idênticas a um token
        // parado, mesmo buffer/mesmo bloqueio por parede (ver AdicionarFonteDeLuz).
        if (mapaAtivo?.fontesDeLuz != null)
        {
            foreach (LightSource fonte in mapaAtivo.fontesDeLuz)
            {
                if (fonte == null) continue;

                Vector3 origem = NormalizadoParaMundo(fonte.posicaoNormalizada);
                origem.z = 0f;

                float raioMundo = ConverterMetrosParaMundo(fonte.raioMetros);
                List<Vector3> pontos = CalcularPoligonoDeVisao(origem, raioMundo, out List<Vector3> pontosNaBorda);
                if (pontos.Count < 3) continue;

                AdicionarDonutAoBuffer(origem, pontos, pontosNaBorda, distanciaTransicao, todosVertices, todosTriangulos, todasCores);
                if (LuzVisivel) AdicionarLequeDeLuzAoBuffer(origem, pontos, verticesLuz, triangulosLuz, coresLuz);
            }
        }

        GarantirObjetosDeVisaoGeral();

        if (todosVertices.Count == 0)
        {
            _meshRendererVisaoGeral.enabled = false;
            if (_meshRendererLuzGeral != null) _meshRendererLuzGeral.enabled = false;
            return;
        }

        AplicarBufferAoMesh(_meshFilterVisaoGeral, todosVertices, todosTriangulos, todasCores);
        _meshRendererVisaoGeral.enabled = true;

        if (LuzVisivel && verticesLuz.Count > 0)
        {
            GarantirObjetosDeLuzGeral();
            AplicarBufferAoMesh(_meshFilterLuzGeral, verticesLuz, triangulosLuz, coresLuz);
            _meshRendererLuzGeral.enabled = true;
        }
        else if (_meshRendererLuzGeral != null)
        {
            _meshRendererLuzGeral.enabled = false;
        }
    }

    // ── Fog-of-war AUTOMÁTICO de jogador comum (NOVO em Salas — ver AtualizarVisaoDinamicaGeral) ──

    /// <summary>Combina a visão de todos os tokens cujo OwnerSteamId é o MEU steamId (ficha própria)
    /// num único mesh opaco — sempre chamado quando este cliente NÃO é host. Se eu não possuo nenhum
    /// token neste mapa, não há de onde "enxergar" — o mapa fica visível normalmente (melhor esforço,
    /// evita uma tela preta pra espectadores sem token).</summary>
    private void AtualizarVisaoAutomaticaDoJogador()
    {
        if (mapaRenderer == null || mapaRenderer.sprite == null || containerTokensNoMapa == null)
        {
            if (_meshRendererVisaoJogador != null) _meshRendererVisaoJogador.enabled = false;
            if (_meshRendererLuzJogador != null) _meshRendererLuzJogador.enabled = false;
            return;
        }

        string meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        var todosVertices = new List<Vector3>();
        var todosTriangulos = new List<int>();
        var todasCores = new List<Color>();
        var verticesLuz = new List<Vector3>();
        var triangulosLuz = new List<int>();
        var coresLuz = new List<Color>();
        float distanciaTransicao = ObterDistanciaTransicaoGradiente();

        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            MapTokenDraggable token = containerTokensNoMapa.GetChild(i)?.GetComponent<MapTokenDraggable>();
            if (token == null || string.IsNullOrWhiteSpace(token.OwnerSteamId) || token.OwnerSteamId != meuSteamId) continue;

            Vector3 origem = token.transform.position;
            origem.z = 0f;

            float raioMundo = ConverterMetrosParaMundo(token.RaioVisaoMetros);
            List<Vector3> pontos = CalcularPoligonoDeVisao(origem, raioMundo, out List<Vector3> pontosNaBorda);
            if (pontos.Count < 3) continue;

            AdicionarDonutAoBuffer(origem, pontos, pontosNaBorda, distanciaTransicao, todosVertices, todosTriangulos, todasCores);
            if (LuzVisivel) AdicionarLequeDeLuzAoBuffer(origem, pontos, verticesLuz, triangulosLuz, coresLuz);
        }

        // Fontes de luz estática (tocha — ver LightSource.cs) — valem pra TODO jogador, mesmo sem
        // token próprio no mapa: é o que faz o fallback "sem token = vê tudo" abaixo só continuar
        // valendo em mapas sem NENHUMA tocha (ver AdicionarFonteDeLuz).
        if (mapaAtivo?.fontesDeLuz != null)
        {
            foreach (LightSource fonte in mapaAtivo.fontesDeLuz)
            {
                if (fonte == null) continue;

                Vector3 origem = NormalizadoParaMundo(fonte.posicaoNormalizada);
                origem.z = 0f;

                float raioMundo = ConverterMetrosParaMundo(fonte.raioMetros);
                List<Vector3> pontos = CalcularPoligonoDeVisao(origem, raioMundo, out List<Vector3> pontosNaBorda);
                if (pontos.Count < 3) continue;

                AdicionarDonutAoBuffer(origem, pontos, pontosNaBorda, distanciaTransicao, todosVertices, todosTriangulos, todasCores);
                if (LuzVisivel) AdicionarLequeDeLuzAoBuffer(origem, pontos, verticesLuz, triangulosLuz, coresLuz);
            }
        }

        if (todosVertices.Count == 0)
        {
            // Nenhum token meu neste mapa NEM nenhuma tocha no mapa — melhor esforço, não escurece a
            // tela de quem não tem nada pra "ver por meio de". Basta existir 1 tocha (ver acima) pra
            // esse fallback parar de valer, mesmo sem token próprio.
            if (_meshRendererVisaoJogador != null) _meshRendererVisaoJogador.enabled = false;
            if (_meshRendererLuzJogador != null) _meshRendererLuzJogador.enabled = false;
            return;
        }

        GarantirObjetosDeVisaoJogador();
        AplicarBufferAoMesh(_meshFilterVisaoJogador, todosVertices, todosTriangulos, todasCores);
        _meshRendererVisaoJogador.enabled = true;

        if (LuzVisivel && verticesLuz.Count > 0)
        {
            GarantirObjetosDeLuzJogador();
            AplicarBufferAoMesh(_meshFilterLuzJogador, verticesLuz, triangulosLuz, coresLuz);
            _meshRendererLuzJogador.enabled = true;
        }
        else if (_meshRendererLuzJogador != null)
        {
            _meshRendererLuzJogador.enabled = false;
        }
    }

    // ── Slider de raio de visão (metros) do token com spotlight ativo — ferramenta do HOST ───────

    private void OnSliderRaioVisaoAlterado(float metros)
    {
        if (_tokenComVisaoAtiva == null) return;

        _tokenComVisaoAtiva.DefinirRaioVisao(metros);
        AtualizarTexto(textoRaioVisao, "Raio de Visão", metros, "0.0");
        AtualizarVisao(_tokenComVisaoAtiva);
        SalvarTokensNoMapaAtivo();
    }

    private void SincronizarSliderRaioVisao()
    {
        bool ativo = _tokenComVisaoAtiva != null;
        float metros = ativo ? _tokenComVisaoAtiva.RaioVisaoMetros : 0f;

        if (sliderRaioVisao != null)
        {
            sliderRaioVisao.interactable = ativo;
            sliderRaioVisao.SetValueWithoutNotify(ativo ? metros : sliderRaioVisao.minValue);
        }
        if (textoRaioVisao != null)
            textoRaioVisao.text = ativo ? $"Raio de Visão: {metros:0.0}m" : "Raio de Visão: —";
    }

    // -----------------------------------------------------------------------
    // Lore/Stat Block de NPC — somente leitura (Fase 7)
    // -----------------------------------------------------------------------
    //
    // Autoria de NPC (criar/editar stat block, lore, itens anexados) continua exclusiva de Campanhas —
    // aqui só EXIBE o EntryData completo já enviado por CampanhaParaSalaConverter (gravado em
    // SalaDataStorage.NpcsCampanhaFolderPath). Ver Salas/Grid/SalaNpcStatBlockLoreUI.cs.

    /// <summary>Chamado pela opção "Lore" do menu de atalho de um MapTokenDraggable com NpcIdCampanha
    /// preenchido (ver MapTokenDraggable.TemNpcDeCampanha).</summary>
    public void MostrarLoreDoNpc(MapTokenDraggable token)
    {
        if (token == null || !token.TemNpcDeCampanha || string.IsNullOrWhiteSpace(salaIdAtual)) return;

        string pasta = SalaDataStorage.NpcsCampanhaFolderPath(salaIdAtual);
        List<EntryData> npcs = DynamicXmlBuilder.LoadEntriesFromFolder(pasta, "npcs", Dnd5eNpcFieldCatalog.AllFields);

        EntryData npc = null;
        foreach (EntryData candidato in npcs)
        {
            if (DynamicXmlBuilder.ResolverEntryId(candidato, 0) == token.NpcIdCampanha)
            { npc = candidato; break; }
        }

        if (npc == null)
        {
            Debug.LogWarning($"[MapManager] Lore do NPC '{token.NpcIdCampanha}' não encontrada (sala '{salaIdAtual}').");
            return;
        }

        if (prefabLoreNpc == null)
        {
            Debug.LogWarning("[MapManager] Prefab de Lore de NPC não foi atribuído.");
            return;
        }

        if (loreNpcUI == null)
        {
            Transform pai = parentPopup != null ? parentPopup : transform;
            GameObject go = UIUtils.Spawn(prefabLoreNpc, pai);
            loreNpcUI = go.GetComponent<SalaNpcStatBlockLoreUI>();
            if (loreNpcUI == null)
                Debug.LogWarning("[MapManager] O prefab de Lore de NPC não possui SalaNpcStatBlockLoreUI.");
        }

        loreNpcUI?.Configurar(npc);
    }

    // -----------------------------------------------------------------------
    // Sincronização de Rede
    // -----------------------------------------------------------------------

    private bool SouHost() =>
        SteamClient.IsValid && steamLobbyManager?.SalaAtual?.IsOwnedBy(SteamClient.SteamId) == true;

    // ── Upload de token e broadcast ────────────────────────────────────────

    private async void UploadEBroadcastToken(string tokenId, string nomeToken, string imagemPath, Vector3 posicao,
        int fichasGeneralId = 0, int fichasDndId = 0, string ownerSteamId = "", int ownerPlayerId = 0, string monstroId = "")
    {
        ulong workshopId = 0;

        if (!string.IsNullOrWhiteSpace(imagemPath) && File.Exists(imagemPath))
        {
            const long LimiteTokenBytes = 25L * 1024 * 1024;
            var infoToken = new FileInfo(imagemPath);
            if (infoToken.Length > LimiteTokenBytes)
            {
                Debug.LogWarning($"[MapManager] Token '{nomeToken}' excede 25 MB ({infoToken.Length / (1024 * 1024f):0.##} MB). Upload ignorado.");
                workshopIdPorToken[tokenId] = 0;
                NetworkSyncManager.Instancia?.NotificarSpawnToken(tokenId, nomeToken, imagemPath, 0, posicao,
                    fichasGeneralId, fichasDndId, ownerSteamId, ownerPlayerId, monstroId);
                return;
            }

            string pastaTemp = Path.Combine(Application.temporaryCachePath, $"vtt_token_{tokenId}");
            Directory.CreateDirectory(pastaTemp);
            File.Copy(imagemPath, Path.Combine(pastaTemp, Path.GetFileName(imagemPath)), overwrite: true);

            try
            {
                workshopIdPorToken.TryGetValue(tokenId, out ulong idExistente);

                Steamworks.Ugc.Editor editor;
                if (idExistente > 0)
                {
                    editor = new Steamworks.Ugc.Editor(idExistente);
                    Debug.Log($"[MapManager] Atualizando token existente na Workshop: {idExistente}");
                }
                else
                {
                    editor = Steamworks.Ugc.Editor.NewCommunityFile
                        .WithTitle($"DiceVTT_Token_{tokenId}")
                        .WithFriendsOnlyVisibility();
                }

                var resultado = await editor
                    .WithContent(pastaTemp)
                    .WithPreviewFile(imagemPath)
                    .SubmitAsync(null);

                if (resultado.Success)
                    workshopId = resultado.FileId;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[MapManager] Upload token falhou: {e.Message}");
            }
        }

        workshopIdPorToken[tokenId] = workshopId;

        // 1. Notifica clientes para spawnarem o placeholder imediatamente
        NetworkSyncManager.Instancia?.NotificarSpawnToken(tokenId, nomeToken, imagemPath, workshopId, posicao,
            fichasGeneralId, fichasDndId, ownerSteamId, ownerPlayerId, monstroId);

        // 2. Envia os bytes da imagem via P2P (mais rápido e confiável que aguardar o Workshop)
        if (!string.IsNullOrWhiteSpace(imagemPath) && File.Exists(imagemPath))
        {
            byte[] bytes = File.ReadAllBytes(imagemPath);
            NetworkSyncManager.Instancia?.EnviarTokenImagemParaTodos(
                tokenId, bytes, Path.GetFileName(imagemPath));
        }
    }

    // ── Estado completo para novos jogadores ───────────────────────────────

    public EstadoTokenNet[] ObterEstadoTokensParaRede()
    {
        if (containerTokensNoMapa == null) return System.Array.Empty<EstadoTokenNet>();

        var lista = new List<EstadoTokenNet>();
        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            Transform filho = containerTokensNoMapa.GetChild(i);
            if (filho == null) continue;

            MapTokenDraggable drag = filho.GetComponent<MapTokenDraggable>();
            if (drag == null) continue;

            workshopIdPorToken.TryGetValue(drag.TokenId, out ulong wid);
            lista.Add(new EstadoTokenNet
            {
                tokenId        = drag.TokenId,
                nomeToken      = drag.NomeToken,
                imagemPath     = drag.ImagemPath,
                workshopIdToken = wid,
                x = filho.position.x,
                y = filho.position.y,
                z = filho.position.z,
                fichasGeneralId = drag.FichasGeneralId,
                fichasDndId     = drag.FichasDndId,
                ownerSteamId    = drag.OwnerSteamId,
                ownerPlayerId   = drag.OwnerPlayerId,
                monstroId       = drag.MonstroId
            });
        }
        return lista.ToArray();
    }

    // ── Recepção de spawn vindo da rede ────────────────────────────────────

    public void ReceberSpawnTokenRede(string tokenId, string nomeToken, string imagemPath, ulong workshopId, Vector3 pos,
        int fichasGeneralId = 0, int fichasDndId = 0, string ownerSteamId = "", int ownerPlayerId = 0, string monstroId = "")
    {
        // Evita duplicar token já existente
        if (containerTokensNoMapa != null)
        {
            for (int i = 0; i < containerTokensNoMapa.childCount; i++)
            {
                MapTokenDraggable d = containerTokensNoMapa.GetChild(i)?.GetComponent<MapTokenDraggable>();
                if (d != null && d.TokenId == tokenId) return;
            }
        }

        StartCoroutine(SpawnTokenDaRedeCoroutine(tokenId, nomeToken, imagemPath, workshopId, pos,
            fichasGeneralId, fichasDndId, ownerSteamId, ownerPlayerId, monstroId));
    }

    private IEnumerator SpawnTokenDaRedeCoroutine(string tokenId, string nomeToken, string imagemPath, ulong workshopId, Vector3 pos,
        int fichasGeneralId = 0, int fichasDndId = 0, string ownerSteamId = "", int ownerPlayerId = 0, string monstroId = "")
    {
        pos = AjustarPosicaoTokenAoGrid(pos);
        float tamanhoCelula = ObterTamanhoAtualDaCelula();

        // Caminho local disponível (LAN / host local) — spawn imediato com imagem real
        if (!string.IsNullOrWhiteSpace(imagemPath) && File.Exists(imagemPath))
        {
            Sprite spriteLocal = CarregarSpriteDeArquivo(imagemPath);
            if (spriteLocal != null)
            {
                SpawnTokenNoMapa(nomeToken, spriteLocal, pos, tamanhoCelula, imagemPath, tokenId,
                    fichasGeneralId, fichasDndId, ownerSteamId, ownerPlayerId, monstroId);
                AtualizarVisaoDinamicaGeral();
                yield break;
            }
        }

        // Imagem não disponível localmente — spawn imediato com placeholder cinza.
        // A imagem real chegará via P2P chunks e será aplicada por ReceberTokenImagemConcluida.
        SpawnTokenNoMapa(nomeToken, CriarSpritePlaceholder(), pos, tamanhoCelula, imagemPath, tokenId,
            fichasGeneralId, fichasDndId, ownerSteamId, ownerPlayerId, monstroId);
        tokensPendentesSprite.Add(tokenId);
        AtualizarVisaoDinamicaGeral();
        Debug.Log($"[MapManager] Token '{nomeToken}' spawnado com placeholder. Aguardando imagem P2P...");

        // Fallback paralelo: Workshop (usado se chunks P2P não chegarem a tempo)
        if (workshopId > 0)
        {
            var item = new Steamworks.Ugc.Item(workshopId);
            if (!item.IsInstalled) item.Download(highPriority: true);

            float elapsed = 0f;
            while (!item.IsInstalled && elapsed < 60f)
            {
                elapsed += 1f;
                yield return new WaitForSeconds(1f);

                // P2P já resolveu — Workshop não precisa mais esperar
                if (!tokensPendentesSprite.Contains(tokenId)) yield break;
            }

            if (item.IsInstalled && tokensPendentesSprite.Contains(tokenId))
            {
                string[] candidatos = Directory.GetFiles(item.Directory, "*.png");
                if (candidatos.Length == 0) candidatos = Directory.GetFiles(item.Directory, "*.jpg");
                if (candidatos.Length > 0)
                {
                    Sprite spriteWorkshop = CarregarSpriteDeArquivo(candidatos[0]);
                    if (spriteWorkshop != null)
                    {
                        AplicarSpriteAoToken(tokenId, spriteWorkshop);
                        Debug.Log($"[MapManager] Token '{nomeToken}' atualizado via Workshop.");
                    }
                }
            }
        }
    }

    // Cria um sprite quadrado cinza de 64×64 para usar como placeholder enquanto a imagem P2P não chega
    private Sprite CriarSpritePlaceholder()
    {
        Texture2D tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        Color cinza = new Color(0.45f, 0.45f, 0.45f, 0.85f);
        Color[] pixels = new Color[64 * 64];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = cinza;
        tex.SetPixels(pixels);
        tex.Apply();
        assetsRuntimeCriados.Add(tex);

        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 100f);
        assetsRuntimeCriados.Add(sprite);
        return sprite;
    }

    // Encontra o token pelo ID, troca o sprite e recalcula a escala para o novo tamanho de imagem
    private void AplicarSpriteAoToken(string tokenId, Sprite sprite)
    {
        tokensPendentesSprite.Remove(tokenId);

        if (containerTokensNoMapa == null) return;

        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            Transform filho = containerTokensNoMapa.GetChild(i);
            if (filho == null) continue;

            MapTokenDraggable drag = filho.GetComponent<MapTokenDraggable>();
            if (drag == null || drag.TokenId != tokenId) continue;

            // Encontra APENAS o filho que é a imagem
            SpriteRenderer sr = filho.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null)
            {
                sr.sprite = sprite;

                // Estica a imagem para caber no grid sem afetar a UI
                float tamanhoCelula = ObterTamanhoAtualDaCelula();
                float maiorDimensao = Mathf.Max(0.0001f, Mathf.Max(sprite.bounds.size.x, sprite.bounds.size.y));

                sr.transform.localScale = new Vector3(tamanhoCelula / maiorDimensao, tamanhoCelula / maiorDimensao, 1f);
            }
            return;
        }
    }

    /// <summary>
    /// Chamado pelo NetworkSyncManager quando todos os chunks P2P da imagem de um token chegaram.
    /// Decodifica os bytes, cria o Sprite e aplica ao token já instanciado no mapa.
    /// </summary>
    public void ReceberTokenImagemConcluida(string tokenId, byte[] bytes)
    {
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes))
        {
            Debug.LogError($"[MapManager] Falha ao decodificar imagem P2P do token '{tokenId}'.");
            Destroy(tex);
            return;
        }
        tex.Apply();
        assetsRuntimeCriados.Add(tex);

        Sprite sprite = Sprite.Create(tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f), 100f);
        assetsRuntimeCriados.Add(sprite);

        AplicarSpriteAoToken(tokenId, sprite);
        Debug.Log($"[MapManager] Sprite do token '{tokenId[..8]}' atualizado via P2P ({tex.width}×{tex.height}).");
    }

    /// <summary>
    /// Lê os bytes de cada token presente no mapa e os envia via P2P para um único jogador.
    /// Chamado pelo NetworkSyncManager quando um novo jogador entra na sala.
    /// </summary>
    public void EnviarImagensTokensParaJogador(Steamworks.SteamId alvo)
    {
        if (containerTokensNoMapa == null) return;

        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            MapTokenDraggable drag = containerTokensNoMapa.GetChild(i)?.GetComponent<MapTokenDraggable>();
            if (drag == null || string.IsNullOrWhiteSpace(drag.ImagemPath) || !File.Exists(drag.ImagemPath))
                continue;

            byte[] bytes = File.ReadAllBytes(drag.ImagemPath);
            NetworkSyncManager.Instancia?.EnviarTokenImagemParaJogador(
                drag.TokenId, bytes, Path.GetFileName(drag.ImagemPath), alvo);
        }
    }

    /// <summary>Busca a instância de token ativa no mapa pelo TokenId (loop simples sobre
    /// containerTokensNoMapa — mesmo padrão já usado em ReceberMovimentoTokenRede/RemoverTokenDoMapa/
    /// ReceberRemoverTokenRede, só extraído aqui pra reaproveitar em TokenPresencaOverlay e
    /// SalaMobileBridge sem duplicar o loop). Retorna null se não encontrado.</summary>
    public MapTokenDraggable EncontrarTokenPorId(string tokenId)
    {
        if (containerTokensNoMapa == null || string.IsNullOrWhiteSpace(tokenId)) return null;

        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            Transform filho = containerTokensNoMapa.GetChild(i);
            if (filho == null) continue;

            MapTokenDraggable drag = filho.GetComponent<MapTokenDraggable>();
            if (drag != null && drag.TokenId == tokenId) return drag;
        }
        return null;
    }

    /// <summary>Todos os tokens atualmente no grid — usado por SocialTurnosManager pra montar a
    /// lista de turnos (iniciativa). Snapshot: é uma cópia da lista de instâncias no momento da
    /// chamada, não uma referência viva a containerTokensNoMapa (adicionar/remover token depois não
    /// afeta a lista já retornada).</summary>
    public List<MapTokenDraggable> ObterTodosOsTokens()
    {
        var lista = new List<MapTokenDraggable>();
        if (containerTokensNoMapa == null) return lista;

        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            MapTokenDraggable drag = containerTokensNoMapa.GetChild(i)?.GetComponent<MapTokenDraggable>();
            if (drag != null) lista.Add(drag);
        }
        return lista;
    }

    /// <summary>
    /// Encontra o token pelo TokenId e aplica a posição recebida da rede (com interpolação).
    /// </summary>
    public void ReceberMovimentoTokenRede(string tokenId, Vector3 novaPosicao)
    {
        MapTokenDraggable drag = EncontrarTokenPorId(tokenId);
        if (drag != null) { drag.AplicarPosicaoRede(novaPosicao); return; }

        Debug.LogWarning($"[MapManager] Token '{tokenId}' não encontrado para mover.");
    }

    /// <summary>Chamado por NetworkSyncManager ao receber tipo 13 (DanoToken) — aplica a vida atual
    /// recebida neste cliente (MapTokenDraggable.DefinirVidaAtual já cuida de persistir na própria
    /// LocalFichaStorage SE este cliente for o dono do vínculo).</summary>
    public void ReceberDanoTokenRede(string tokenId, int novaVidaAtual)
    {
        MapTokenDraggable drag = EncontrarTokenPorId(tokenId);
        if (drag != null) { drag.DefinirVidaAtual(novaVidaAtual); return; }

        Debug.LogWarning($"[MapManager] Token '{tokenId}' não encontrado para aplicar dano.");
    }

    /// <summary>Chamado por NetworkSyncManager ao receber tipo 14 (FlagsToken) — aplica Lock/Esconder
    /// recebidos neste cliente.</summary>
    public void ReceberFlagsTokenRede(string tokenId, bool bloqueado, bool escondido)
    {
        MapTokenDraggable drag = EncontrarTokenPorId(tokenId);
        if (drag != null) { drag.DefinirFlags(bloqueado, escondido); return; }

        Debug.LogWarning($"[MapManager] Token '{tokenId}' não encontrado para aplicar flags.");
    }

    /// <summary>Clona um token já no mapa (chamado pelo content de ferramentas — ver
    /// TokenButtonUI.botaoDuplicar) — mesma identidade (ficha/monstro/dono), tokenId novo, uma
    /// célula ao lado, e vida CHEIA (não copia o dano atual do original — clone "nasce descansado").
    /// Mesmo padrão de TentarSoltarTokenNoMapa: spawna localmente e broadcasta via
    /// UploadEBroadcastToken (que já cuida do upload de imagem pra Workshop, se houver).</summary>
    public void DuplicarToken(string tokenId)
    {
        MapTokenDraggable original = EncontrarTokenPorId(tokenId);
        if (original == null)
        {
            Debug.LogWarning($"[MapManager] Token '{tokenId}' não encontrado para duplicar.");
            return;
        }

        float tamanhoCelula = ObterTamanhoAtualDaCelula();
        Vector3 posNova = AjustarPosicaoTokenAoGrid(original.transform.position + new Vector3(tamanhoCelula, 0f, 0f));
        string novoTokenId = System.Guid.NewGuid().ToString("N");

        Sprite spriteOriginal = original.GetComponentInChildren<SpriteRenderer>(true)?.sprite;
        if (spriteOriginal == null) spriteOriginal = CriarSpritePlaceholder();

        // vidaAtualSalva fica no padrão (-1) — o clone puxa vida cheia da ficha/monstro vinculado em
        // Setup(), não herda o dano atual do original.
        SpawnTokenNoMapa(original.NomeToken, spriteOriginal, posNova, tamanhoCelula, original.ImagemPath, novoTokenId,
            original.FichasGeneralId, original.FichasDndId, original.OwnerSteamId, original.OwnerPlayerId, original.MonstroId,
            original.NpcIdCampanha, original.RaioVisaoMetros);

        SalvarEstadoVTT();
        AtualizarVisaoDinamicaGeral();

        UploadEBroadcastToken(novoTokenId, original.NomeToken, original.ImagemPath, posNova,
            original.FichasGeneralId, original.FichasDndId, original.OwnerSteamId, original.OwnerPlayerId, original.MonstroId);
    }

    /// <summary>
    /// Remove SÓ a instância do token no mapa (o GameObject arrastável) — não mexe no catálogo de
    /// tokens (continua na lista pra arrastar de novo) nem na ficha/monstro linkado. Usado pela
    /// opção "Excluir" do menu de atalho. Persiste a remoção (SalvarEstadoVTT já reconstrói
    /// tokensNoMapa a partir dos filhos atuais de containerTokensNoMapa) e avisa os outros clientes.
    /// </summary>
    public void RemoverTokenDoMapa(string tokenId)
    {
        if (containerTokensNoMapa == null || string.IsNullOrWhiteSpace(tokenId)) return;

        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            Transform filho = containerTokensNoMapa.GetChild(i);
            if (filho == null) continue;

            MapTokenDraggable drag = filho.GetComponent<MapTokenDraggable>();
            if (drag != null && drag.TokenId == tokenId)
            {
                Destroy(filho.gameObject);
                SalvarEstadoVTT();
                NetworkSyncManager.Instancia?.NotificarRemoverToken(tokenId);
                AtualizarVisaoDinamicaGeral();
                return;
            }
        }

        Debug.LogWarning($"[MapManager] Token '{tokenId}' não encontrado para remover.");
    }

    /// <summary>Implementa IGerenciadorDeTokens — chamado por TokenButtonUI depois de
    /// ITokenNoMapa.DefinirRaioVisao(...) no slider de visão do próprio token. Mesmo par de chamadas
    /// que OnSliderRaioVisaoAlterado já faz pro spotlight do host (recalcula visão + persiste),
    /// reaproveitado aqui genericamente pra qualquer token, selecionado ou não.</summary>
    public void NotificarRaioVisaoAlterado(ITokenNoMapa token)
    {
        AtualizarVisaoDinamicaGeral();
        SalvarTokensNoMapaAtivo();

        // Sem isso, o novo painel "Content de Visão" (Mestre mexendo no raio de tokens de OUTROS
        // jogadores) não tinha efeito nenhum na visão real do jogador dono — cada cliente mantém sua
        // própria instância local de token, sincronizadas só por mensagem de rede pontual. Antes disto
        // só funcionava por acidente porque só o próprio dono mexia no próprio slider (fog roda local,
        // filtrado por OwnerSteamId==meuSteamId).
        NetworkSyncManager.Instancia?.EnviarRaioVisaoTokenParaTodos(token.TokenId, token.RaioVisaoMetros);
    }

    /// <summary>Aplicado ao receber MsgRaioVisaoToken (NetworkSyncManager, case 15) — sem
    /// rebroadcast, mesmo padrão de AplicarClimaRecebido/AplicarDonoDeToken.</summary>
    public void ReceberRaioVisaoTokenRede(string tokenId, float raioMetros)
    {
        if (containerTokensNoMapa == null || string.IsNullOrWhiteSpace(tokenId)) return;

        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            Transform filho = containerTokensNoMapa.GetChild(i);
            if (filho == null) continue;

            MapTokenDraggable drag = filho.GetComponent<MapTokenDraggable>();
            if (drag != null && drag.TokenId == tokenId)
            {
                drag.DefinirRaioVisao(raioMetros);
                AtualizarVisaoDinamicaGeral();
                return;
            }
        }
    }

    /// <summary>Todos os tokens de JOGADOR no mapa (OwnerSteamId não vazio) — monstro/NPC não têm
    /// "visão de jogador" própria, não entram no painel "Content de Visão". Usado por VisaoPainelUI
    /// pra popular um slot por token (ver VisaoTokenSliderSlot).</summary>
    public List<MapTokenDraggable> ObterTokensDeJogadorNoMapa()
    {
        var resultado = new List<MapTokenDraggable>();
        if (containerTokensNoMapa == null) return resultado;

        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            Transform filho = containerTokensNoMapa.GetChild(i);
            if (filho == null) continue;

            MapTokenDraggable drag = filho.GetComponent<MapTokenDraggable>();
            if (drag != null && !string.IsNullOrWhiteSpace(drag.OwnerSteamId))
                resultado.Add(drag);
        }
        return resultado;
    }

    /// <summary>Slider GLOBAL do painel "Content de Visão" — aplica o mesmo raio a TODOS os tokens de
    /// jogador de uma vez, mas notifica rede/persiste UMA VEZ SÓ no final (não por token, senão
    /// floodaria a rede a cada tick de arrasto do slider global).</summary>
    public void DefinirRaioVisaoGlobalParaTodosOsTokens(float metros)
    {
        List<MapTokenDraggable> tokens = ObterTokensDeJogadorNoMapa();
        foreach (MapTokenDraggable token in tokens)
            token.DefinirRaioVisao(metros);

        AtualizarVisaoDinamicaGeral();
        SalvarTokensNoMapaAtivo();

        foreach (MapTokenDraggable token in tokens)
            NetworkSyncManager.Instancia?.EnviarRaioVisaoTokenParaTodos(token.TokenId, token.RaioVisaoMetros);
    }

    /// <summary>Chamado pelo NetworkSyncManager quando outro cliente removeu um token do mapa.</summary>
    public void ReceberRemoverTokenRede(string tokenId)
    {
        if (containerTokensNoMapa == null || string.IsNullOrWhiteSpace(tokenId)) return;

        for (int i = 0; i < containerTokensNoMapa.childCount; i++)
        {
            Transform filho = containerTokensNoMapa.GetChild(i);
            if (filho == null) continue;

            MapTokenDraggable drag = filho.GetComponent<MapTokenDraggable>();
            if (drag != null && drag.TokenId == tokenId)
            {
                Destroy(filho.gameObject);
                AtualizarVisaoDinamicaGeral();
                return;
            }
        }
    }

    /// <summary>Reatribui o dono do token ligado a uma ficha — no mapa (gate de PodeMover, via
    /// MapTokenDraggable.DefinirDono) e no catálogo (TokenCatalogItem.ownerSteamId). Chamado
    /// localmente pelo Mestre (SalaAbasFichasManager.AtribuirDono) e pelos demais peers ao receber a
    /// mensagem de rede (NetworkSyncManager, tipo 10). Não mexe em ownerPlayerId — é um ID local
    /// por-máquina, não faz sentido propagar via rede.</summary>
    public void AplicarDonoDeToken(int fichasGeneralId, int fichasDndId, string novoDono)
    {
        if (fichasDndId <= 0) return;

        if (containerTokensNoMapa != null)
        {
            for (int i = 0; i < containerTokensNoMapa.childCount; i++)
            {
                Transform filho = containerTokensNoMapa.GetChild(i);
                if (filho == null) continue;

                MapTokenDraggable drag = filho.GetComponent<MapTokenDraggable>();
                if (drag != null && drag.FichasGeneralId == fichasGeneralId && drag.FichasDndId == fichasDndId)
                    drag.DefinirDono(novoDono);
            }
        }

        for (int i = 0; i < todosOsTokens.Count; i++)
        {
            TokenCatalogItem item = todosOsTokens[i];
            if (item != null && item.fichasGeneralId == fichasGeneralId && item.fichasDndId == fichasDndId)
                item.ownerSteamId = novoDono;
        }

        // Sem isso, a visão do jogador que ganhou/perdeu o token só ficava correta na próxima vez que
        // ALGO MAIS disparasse um recálculo incidental (mover token, trocar mapa etc). Cobre os dois
        // chamadores (Mestre local e peer via rede, NetworkSyncManager case 10) com um único ponto de
        // mudança, já que os dois passam por este método.
        AtualizarVisaoDinamicaGeral();
    }

    /// <summary>
    /// Lê o ficheiro de imagem e envia-o para todos os clientes via P2P chunking.
    /// Apenas o Host deve chamar este método.
    /// </summary>
    public void MestreEnviaMapaLocalParaRede(string caminhoImagem)
    {
        if (!File.Exists(caminhoImagem))
        {
            Debug.LogWarning($"[MapManager] Imagem não encontrada: {caminhoImagem}");
            return;
        }

        const long LimiteMapaBytes = 25L * 1024 * 1024;
        long tamanho = new FileInfo(caminhoImagem).Length;
        if (tamanho > LimiteMapaBytes)
        {
            Debug.LogError($"[MapManager] Mapa excede 25 MB ({tamanho / (1024 * 1024f):0.##} MB). Envio cancelado.");
            return;
        }

        caminhoMapaAtualParaRede = caminhoImagem;
        byte[] bytes = File.ReadAllBytes(caminhoImagem);
        Debug.Log($"[MapManager] Enviando mapa via P2P ({bytes.Length / 1024f:0.#} KB): {Path.GetFileName(caminhoImagem)}");
        NetworkSyncManager.Instancia?.EnviarFicheiroPesado(bytes, Path.GetFileName(caminhoImagem), geracaoMapaAtual);
    }

    /// <summary>Bytes da imagem do mapa ativo, prontos pra mandar por qualquer canal (usado por
    /// SalaMobileBridge — o celular não fala o protocolo de chunking P2P do NetworkSyncManager,
    /// manda tudo numa mensagem só via SalaLanServidor). Null se não houver mapa/arquivo disponível.</summary>
    public byte[] ObterImagemDoMapaAtivoParaRede()
    {
        if (string.IsNullOrWhiteSpace(caminhoMapaAtualParaRede) || !File.Exists(caminhoMapaAtualParaRede))
            return null;
        try { return File.ReadAllBytes(caminhoMapaAtualParaRede); }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MapManager] Falha ao ler imagem do mapa ativo pra rede: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Envia o mapa actual para um único jogador (recém-entrou na sala).
    /// </summary>
    public void EnviarMapaParaJogador(Steamworks.SteamId alvo)
    {
        if (string.IsNullOrWhiteSpace(caminhoMapaAtualParaRede) || !File.Exists(caminhoMapaAtualParaRede))
        {
            Debug.LogWarning("[MapManager] Sem mapa local disponível para reenviar ao novo jogador " +
                $"(SteamId {alvo}) — ele não vai receber a imagem do mapa atual.");
            return;
        }

        byte[] bytes = File.ReadAllBytes(caminhoMapaAtualParaRede);
        NetworkSyncManager.Instancia?.EnviarFicheiroPesadoParaJogador(bytes, Path.GetFileName(caminhoMapaAtualParaRede), alvo, geracaoMapaAtual);
    }

    /// <summary>
    /// Reconstrói a textura a partir dos bytes recebidos via P2P e aplica ao grid.
    /// Chamado pelo NetworkSyncManager após todos os chunks chegarem.
    /// </summary>
    public void ReceberMapaConcluido(byte[] imagemBytes)
    {
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
        if (!tex.LoadImage(imagemBytes))
        {
            Debug.LogError("[MapManager] Falha ao decodificar imagem recebida via P2P.");
            Destroy(tex);
            return;
        }
        tex.Apply();

        Sprite sprite = Sprite.Create(tex,
            new Rect(0, 0, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit: 100f);

        // Persiste os bytes recebidos num arquivo local determinístico — sem isso, imagemPath ficava
        // vazio e este cliente nunca conseguia reenviar o mapa se um dia assumisse o papel de host
        // (ex: dono original do lobby sai/migração de ownership). EnviarMapaParaJogador depende de
        // um imagemPath local válido pra funcionar.
        string caminhoCache = null;
        try
        {
            string pastaCache = Path.Combine(Application.temporaryCachePath, "vtt_map_cache");
            Directory.CreateDirectory(pastaCache);
            caminhoCache = Path.Combine(pastaCache, "mapa_recebido.png");
            File.WriteAllBytes(caminhoCache, imagemBytes);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MapManager] Não foi possível cachear o mapa recebido em disco (reenvio futuro ficará indisponível): {e.Message}");
        }

        MapConfig novoMapa = ScriptableObject.CreateInstance<MapConfig>();
        novoMapa.nomeDoMapa        = "P2P_Map";
        novoMapa.imagemMapa        = sprite;
        novoMapa.imagemPath        = caminhoCache ?? string.Empty;
        novoMapa.escalaVisual      = 1f;
        novoMapa.unidadesPorCelula = 1.5f;
        ConfigurarGridInicialPorAspecto(novoMapa, sprite);

        CarregarMapa(novoMapa);
        Debug.Log($"[MapManager] Mapa P2P aplicado ({tex.width}x{tex.height}).");
    }

    /// <summary>
    /// Baixa um mapa do Workshop e aplica-o ao grid. Chamado pelo cliente ao receber ID via P2P.
    /// </summary>
    public void BaixarEAplicarMapaWorkshop(ulong workshopId)
    {
        if (!SteamClient.IsValid)
        {
            Debug.LogWarning("[MapManager] Steam não disponível para download de Workshop.");
            return;
        }

        MapaAtualWorkshopId = workshopId;
        StartCoroutine(DownloadEAplicarWorkshopCoroutine(workshopId));
    }

    private IEnumerator DownloadEAplicarWorkshopCoroutine(ulong workshopId)
    {
        var item = new Steamworks.Ugc.Item(workshopId);

        if (!item.IsInstalled)
        {
            item.Download(highPriority: true);
            Debug.Log($"[MapManager] Baixando mapa Workshop {workshopId}...");

            float timeout = 120f;
            float elapsed = 0f;
            while (!item.IsInstalled && elapsed < timeout)
            {
                Debug.Log($"[MapManager] Download: {item.DownloadAmount * 100f:0}%");
                elapsed += 2f;
                yield return new WaitForSeconds(2f);
            }

            if (!item.IsInstalled)
            {
                Debug.LogError($"[MapManager] Timeout ao baixar Workshop {workshopId}.");
                yield break;
            }
        }

        // Procura a imagem dentro da pasta do item
        string pastaItem = item.Directory;
        if (string.IsNullOrWhiteSpace(pastaItem) || !Directory.Exists(pastaItem))
        {
            Debug.LogError("[MapManager] Pasta do item Workshop não encontrada.");
            yield break;
        }

        string[] candidatos = Directory.GetFiles(pastaItem, "*.png");
        if (candidatos.Length == 0)
            candidatos = Directory.GetFiles(pastaItem, "*.jpg");
        if (candidatos.Length == 0)
            candidatos = Directory.GetFiles(pastaItem, "*.jpeg");

        if (candidatos.Length == 0)
        {
            Debug.LogError("[MapManager] Nenhuma imagem encontrada no item Workshop.");
            yield break;
        }

        string caminhoImagem = candidatos[0];
        Sprite sprite = CarregarSpriteDeArquivo(caminhoImagem);
        if (sprite == null) yield break;

        // Cria e carrega um MapConfig temporário com o sprite baixado
        MapConfig novoMapa = ScriptableObject.CreateInstance<MapConfig>();
        novoMapa.nomeDoMapa      = $"Workshop_{workshopId}";
        novoMapa.imagemMapa      = sprite;
        novoMapa.imagemPath      = caminhoImagem;
        novoMapa.escalaVisual    = 1f;
        novoMapa.unidadesPorCelula = 1.5f;
        ConfigurarGridInicialPorAspecto(novoMapa, sprite);

        CarregarMapa(novoMapa);
        Debug.Log($"[MapManager] Mapa Workshop {workshopId} aplicado com sucesso.");
    }

    // -----------------------------------------------------------------------
    // Persistência VTT
    // -----------------------------------------------------------------------

    /// <summary>
    /// Limpa tokens instanciados, mapa ativo e grid visual. Chamado ao sair da sala
    /// para não deixar objetos residuais carregados nem o grid aparecendo no fundo.
    /// </summary>
    public void LimparElementosDaSala()
    {
        LimparTokensDoMapa();
        OcultarGridVisual();

        if (mapaRenderer != null)
            mapaRenderer.sprite = null;

        mapaAtivo = null;
        MapaAtualWorkshopId = 0;

        // Catálogos (mapas e tokens) são específicos da sessão da sala — se não forem limpos aqui,
        // ficam presos no componente entre sessões. Ao reabrir qualquer sala, CarregarEstadoVTT
        // deduplica mapas/tokens por nome contra essas listas: um mapa com nome repetido (ex: nome
        // padrão "Mapa 1") era considerado "já existente" e o carregamento era pulado silenciosamente,
        // deixando mapaAtivo nulo mesmo com os dados salvos corretos no disco.
        todosOsMapas.Clear();
        todosOsTokens.Clear();
        OnCatalogoDeMapasAlterado?.Invoke();
        OnCatalogoDeTokensAlterado?.Invoke();
    }

    private void LimparTokensDoMapa()
    {
        if (containerTokensNoMapa == null)
            return;

        for (int i = containerTokensNoMapa.childCount - 1; i >= 0; i--)
        {
            Transform filho = containerTokensNoMapa.GetChild(i);
            if (filho != null)
                Destroy(filho.gameObject);
        }
    }

    public void SalvarEstadoVTT()
    {
        if (string.IsNullOrWhiteSpace(salaIdAtual))
            return;

        SalaData dados = SalaDataStorage.HasSalaData(salaIdAtual)
            ? SalaDataStorage.LoadSalaData(salaIdAtual)
            : new SalaData { salaLocalId = salaIdAtual };

        if (dados == null)
            dados = new SalaData { salaLocalId = salaIdAtual };

        dados.multiplicadorGrid = multiplicadorTamanhoGrid;

        // Regista todos os paths persistidos para purgar órfãos no final
        var pathsPersistidos = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        // O mapa ATIVO tem seus tokens em memória sincronizados aqui — os outros mapas (não ativos
        // no momento) já carregam sua própria lista de uma troca anterior (ver CarregarMapa), então
        // não precisam ser re-coletados dos GameObjects (que só existem pro mapa ativo mesmo).
        SalvarTokensNoMapaAtivo();

        // Mapas criados em runtime (têm imagemPath — os do Inspector não têm)
        var mapasSalvos = new List<MapSaveData>();
        for (int i = 0; i < todosOsMapas.Count; i++)
        {
            MapConfig m = todosOsMapas[i];
            if (m == null || string.IsNullOrWhiteSpace(m.imagemPath))
                continue;

            if (string.IsNullOrWhiteSpace(m.mapaId))
                m.mapaId = System.Guid.NewGuid().ToString("N");

            string pathPersistida = SalaDataStorage.PersistirImagem(m.imagemPath, salaIdAtual, $"mapa_{i}");
            if (!string.IsNullOrWhiteSpace(pathPersistida))
            {
                pathsPersistidos.Add(pathPersistida);
                // Memoriza o caminho já persistido dentro da pasta da sala — evita depender pra
                // sempre do arquivo externo original (que pode ser movido/apagado depois do upload,
                // fazendo o próximo save gravar um caminho quebrado e o mapa sumir no próximo load).
                m.imagemPath = pathPersistida;
            }

            // Persiste a imagem de cada token deste mapa também (mesmo se não for o mapa ativo no
            // momento — a lista em memória já tem os dados, só falta garantir que os arquivos de
            // imagem sobrevivam a uma purga de órfãos).
            foreach (TokenInstanceSaveData t in m.tokensNoMapa)
            {
                if (t == null || string.IsNullOrWhiteSpace(t.imagemPath)) continue;
                pathsPersistidos.Add(t.imagemPath);
            }

            mapasSalvos.Add(new MapSaveData
            {
                mapaId            = m.mapaId,
                nome              = m.nomeDoMapa,
                imagemPath        = pathPersistida,
                gridColunas       = m.gridColunas,
                gridLinhas        = m.gridLinhas,
                unidadesPorCelula = m.unidadesPorCelula,
                escalaVisual      = m.escalaVisual,
                ehAtivo           = mapaAtivo == m,
                paredes           = m.paredes != null ? new List<WallLine>(m.paredes) : new List<WallLine>(),
                vetorizado        = m.vetorizado,
                fogRects          = m.fogRects != null ? new List<FogRect>(m.fogRects) : new List<FogRect>(),
                alphaMaximoVisaoDinamica    = m.alphaMaximoVisaoDinamica,
                transicaoGradienteEmCelulas = m.transicaoGradienteEmCelulas,
                alphaVisaoGeralTokens       = m.alphaVisaoGeralTokens,
                alphaFogParaHost            = m.alphaFogParaHost,
                raiosBaseVisao              = m.raiosBaseVisao,
                corLuzVisao                 = m.corLuzVisao,
                fontesDeLuz       = m.fontesDeLuz != null ? new List<LightSource>(m.fontesDeLuz) : new List<LightSource>(),
                tokensNoMapa      = m.tokensNoMapa != null ? new List<TokenInstanceSaveData>(m.tokensNoMapa) : new List<TokenInstanceSaveData>(),
            });
        }
        dados.mapas = mapasSalvos.ToArray();

        // Catálogo de tokens criados em runtime
        var tokensCat = new List<TokenCatalogSaveData>();
        for (int i = 0; i < todosOsTokens.Count; i++)
        {
            TokenCatalogItem t = todosOsTokens[i];
            if (t == null || string.IsNullOrWhiteSpace(t.imagemPath))
                continue;

            // Chave pelo nome (identidade do catálogo, já usada por AdicionarTokenAoCatalogo pra
            // detectar duplicados) em vez do índice — mesmo motivo do fix em tokensNoMapa logo
            // abaixo: o índice muda ao adicionar/remover itens do catálogo.
            string pathPersistida = SalaDataStorage.PersistirImagem(t.imagemPath, salaIdAtual, $"token_{t.nome}");
            if (!string.IsNullOrWhiteSpace(pathPersistida))
            {
                pathsPersistidos.Add(pathPersistida);
                t.imagemPath = pathPersistida;
            }
            tokensCat.Add(new TokenCatalogSaveData
            {
                nome = t.nome,
                imagemPath = pathPersistida,
                fichasGeneralId = t.fichasGeneralId,
                fichasDndId = t.fichasDndId,
                ownerSteamId = t.ownerSteamId,
                ownerPlayerId = t.ownerPlayerId,
                monstroId = t.monstroId
            });
        }
        dados.tokensCatalogo = tokensCat.ToArray();

        // dados.tokensNoMapa (lista antiga, plana) NUNCA mais é escrita a partir daqui — cada token
        // já foi incluído dentro do MapSaveData do seu próprio mapa, no loop de mapasSalvos acima
        // (ver SalvarTokensNoMapaAtivo). Zera o campo legado pra sinalizar que essa sala já migrou
        // (CarregarEstadoVTT só roda a migração se esse campo ainda tiver itens).
        dados.tokensNoMapa = System.Array.Empty<TokenInstanceSaveData>();

        SalaDataStorage.SaveSalaData(dados);
        // Remove ficheiros de imagens VTT que já não correspondem a nenhum mapa/token activo
        SalaDataStorage.PurgarImagensOrfas(salaIdAtual, pathsPersistidos);
    }

    public void CarregarEstadoVTT(SalaData dados)
    {
        if (dados == null)
            return;

        jaCarregouEstadoNaSessao = true;

        // Limpar tokens que possam estar na cena de uma sessão anterior
        LimparTokensDoMapa();

        // Restaurar escala do grid
        if (dados.multiplicadorGrid > 0f)
            multiplicadorTamanhoGrid = Mathf.Clamp(dados.multiplicadorGrid, multiplicadorMinimoGrid, multiplicadorMaximoGrid);

        // Restaurar mapas criados em runtime
        bool precisaResalvar = false;
        if (dados.mapas != null)
        {
            for (int i = 0; i < dados.mapas.Length; i++)
            {
                MapSaveData msd = dados.mapas[i];
                if (msd == null || string.IsNullOrWhiteSpace(msd.imagemPath) || !File.Exists(msd.imagemPath))
                    continue;

                bool jaExiste = false;
                for (int j = 0; j < todosOsMapas.Count; j++)
                {
                    if (todosOsMapas[j] != null && todosOsMapas[j].nomeDoMapa == msd.nome)
                    { jaExiste = true; break; }
                }
                if (jaExiste) continue;

                Sprite sprite = CarregarSpriteDeArquivo(msd.imagemPath);
                if (sprite == null) continue;

                MapConfig novoMapa = ScriptableObject.CreateInstance<MapConfig>();
                novoMapa.nomeDoMapa        = msd.nome;
                novoMapa.imagemMapa        = sprite;
                novoMapa.imagemPath        = msd.imagemPath;
                novoMapa.gridColunas       = msd.gridColunas;
                novoMapa.gridLinhas        = msd.gridLinhas;
                novoMapa.unidadesPorCelula = msd.unidadesPorCelula;
                novoMapa.escalaVisual      = msd.escalaVisual;
                novoMapa.paredes           = msd.paredes != null ? new List<WallLine>(msd.paredes) : new List<WallLine>();
                novoMapa.vetorizado        = msd.vetorizado;
                novoMapa.fogRects          = msd.fogRects != null ? new List<FogRect>(msd.fogRects) : new List<FogRect>();
                novoMapa.alphaMaximoVisaoDinamica    = msd.alphaMaximoVisaoDinamica;
                novoMapa.transicaoGradienteEmCelulas = msd.transicaoGradienteEmCelulas;
                novoMapa.alphaVisaoGeralTokens       = msd.alphaVisaoGeralTokens;
                novoMapa.alphaFogParaHost            = msd.alphaFogParaHost;
                novoMapa.raiosBaseVisao              = msd.raiosBaseVisao;
                novoMapa.corLuzVisao                 = msd.corLuzVisao;
                novoMapa.fontesDeLuz       = msd.fontesDeLuz != null ? new List<LightSource>(msd.fontesDeLuz) : new List<LightSource>();
                novoMapa.tokensNoMapa      = msd.tokensNoMapa != null ? new List<TokenInstanceSaveData>(msd.tokensNoMapa) : new List<TokenInstanceSaveData>();

                // Mapas salvos antes do campo mapaId existir vêm sem id — gera um agora e marca pra
                // regravar no disco no final deste método (self-healing, sem exigir ação do usuário).
                novoMapa.mapaId = string.IsNullOrWhiteSpace(msd.mapaId) ? System.Guid.NewGuid().ToString("N") : msd.mapaId;
                if (string.IsNullOrWhiteSpace(msd.mapaId)) precisaResalvar = true;

                todosOsMapas.Add(novoMapa);

                if (msd.ehAtivo)
                    mapaAtivo = novoMapa;
            }
        }

        // MIGRAÇÃO: dados.tokensNoMapa (lista antiga, plana, uma sala inteira) só existe em saves de
        // antes dos tokens ficarem escopados por mapa. Se ainda tiver itens E nenhum mapa já carregou
        // tokens próprios (ou seja, essa sala nunca passou pela migração), joga tudo pro mapa ativo
        // (ou o primeiro mapa, se nenhum estiver marcado ativo) — melhor esforço, mas garante que
        // nenhum token de sala já existente simplesmente desaparece.
        if (dados.tokensNoMapa != null && dados.tokensNoMapa.Length > 0)
        {
            bool algumMapaJaTemTokens = false;
            for (int j = 0; j < todosOsMapas.Count; j++)
                if (todosOsMapas[j] != null && todosOsMapas[j].tokensNoMapa != null && todosOsMapas[j].tokensNoMapa.Count > 0)
                { algumMapaJaTemTokens = true; break; }

            if (!algumMapaJaTemTokens)
            {
                MapConfig mapaAlvoMigracao = mapaAtivo != null ? mapaAtivo : (todosOsMapas.Count > 0 ? todosOsMapas[0] : null);
                if (mapaAlvoMigracao != null)
                {
                    mapaAlvoMigracao.tokensNoMapa = new List<TokenInstanceSaveData>(dados.tokensNoMapa);
                    precisaResalvar = true;
                    Debug.Log($"[MapManager] Migração: {dados.tokensNoMapa.Length} token(s) da lista antiga (sala inteira) " +
                        $"movido(s) pro mapa '{mapaAlvoMigracao.nomeDoMapa}'.");
                }
            }
        }

        // Restaurar catálogo de tokens
        if (dados.tokensCatalogo != null)
        {
            for (int i = 0; i < dados.tokensCatalogo.Length; i++)
            {
                TokenCatalogSaveData tsd = dados.tokensCatalogo[i];
                if (tsd == null || string.IsNullOrWhiteSpace(tsd.imagemPath) || !File.Exists(tsd.imagemPath))
                    continue;

                // Mesma identidade usada em AdicionarTokenAoCatalogo (ficha/monstro linkado, senão
                // nome) — comparar só por nome colapsava fichas/monstros diferentes com nome igual.
                string chaveRestaurada = ChaveCatalogo(tsd.fichasDndId, tsd.monstroId, tsd.nome);
                bool jaExiste = false;
                for (int j = 0; j < todosOsTokens.Count; j++)
                {
                    if (todosOsTokens[j] != null && ChaveCatalogo(todosOsTokens[j]) == chaveRestaurada)
                    { jaExiste = true; break; }
                }
                if (jaExiste) continue;

                Sprite sprite = CarregarSpriteDeArquivo(tsd.imagemPath);
                if (sprite == null) continue;

                todosOsTokens.Add(new TokenCatalogItem
                {
                    nome       = tsd.nome,
                    sprite     = sprite,
                    imagemPath = tsd.imagemPath,
                    fichasGeneralId = tsd.fichasGeneralId,
                    fichasDndId     = tsd.fichasDndId,
                    ownerSteamId    = tsd.ownerSteamId,
                    ownerPlayerId   = tsd.ownerPlayerId,
                    monstroId       = tsd.monstroId
                });
            }
        }

        // Notifica a UI (barra de ferramentas) pra repopular a galeria/catálogo
        OnCatalogoDeMapasAlterado?.Invoke();
        OnCatalogoDeTokensAlterado?.Invoke();

        // Carregar mapa ativo salvo — CarregarMapa já restaura os tokens DESSE mapa internamente
        // (novaConfig.tokensNoMapa, já populado acima direto do save ou pela migração logo acima).
        if (mapaAtivo != null)
            CarregarMapa(mapaAtivo);

        // Self-healing: se algum mapa ganhou um mapaId novo ou os tokens antigos foram migrados
        // pro formato por-mapa, regrava o estado imediatamente — assim essa sala nunca mais passa
        // por esse caminho de migração de novo.
        if (precisaResalvar)
            SalvarEstadoVTT();
    }

    private IEnumerator RestaurarTokensNoMapaAposFrame(TokenInstanceSaveData[] instancias)
    {
        yield return null;

        float tamanhoCelula = ObterTamanhoAtualDaCelula();

        for (int i = 0; i < instancias.Length; i++)
        {
            TokenInstanceSaveData inst = instancias[i];
            if (inst == null || string.IsNullOrWhiteSpace(inst.imagemPath) || !File.Exists(inst.imagemPath))
                continue;

            Sprite sprite = CarregarSpriteDeArquivo(inst.imagemPath);
            if (sprite == null) continue;

            Vector3 pos = new Vector3(inst.posX, inst.posY, inst.posZ);
            if (!PosicaoDentroDaAreaDeTokens(pos))
                pos = AjustarPosicaoTokenAoGrid(pos);

            SpawnTokenNoMapa(inst.nomeToken, sprite, pos, tamanhoCelula, inst.imagemPath, inst.tokenId,
                inst.fichasGeneralId, inst.fichasDndId, inst.ownerSteamId, inst.ownerPlayerId, inst.monstroId,
                inst.npcIdCampanha, inst.raioVisaoMetros,
                vidaAtualSalva: inst.vidaAtual, bloqueadoSalvo: inst.bloqueado, escondidoSalvo: inst.escondido);
        }

        AtualizarVisaoDinamicaGeral(); // tokens do mapa restaurados — recalcula visão dinâmica ativa
    }

    // -----------------------------------------------------------------------

    private void Update()
    {
        if (modoMoverMapa)
            AtualizarDragMapa();

        // Ctrl + Scroll = redimensionar mapa (sempre disponível)
        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        if (mouse != null && keyboard != null && keyboard.ctrlKey.isPressed)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (!Mathf.Approximately(scroll, 0f))
            {
                float norm = Mathf.Abs(scroll) >= 10f ? scroll / 120f : scroll;
                if (norm > 0f)
                    AumentarTamanhoGrid();
                else
                    DiminuirTamanhoGrid();
            }
        }
    }

    private void AtualizarDragMapa()
    {
        if (mapaRenderer == null || mapaRenderer.sprite == null)
            return;

        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        Camera cam = cameraVTT != null ? cameraVTT.GetComponent<Camera>() : Camera.main;
        if (cam == null)
            return;

        Vector2 posicaoTela = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            dragMapaBloqueadoPorUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!dragMapaBloqueadoPorUI)
            {
                Vector3 mundo = cam.ScreenToWorldPoint(
                    new Vector3(posicaoTela.x, posicaoTela.y, Mathf.Abs(cam.transform.position.z)));
                posicaoMapaInicioDrag = mapaRenderer.transform.position;
                mouseMundoInicioDrag = new Vector3(mundo.x, mundo.y, 0f);
                arrastandomApa = true;
                // Oculta linhas durante o arrasto (serão recriadas ao soltar)
                if (containerLinhasGrid != null)
                    containerLinhasGrid.gameObject.SetActive(false);
            }
        }

        if (arrastandomApa)
        {
            if (mouse.leftButton.isPressed)
            {
                Vector3 mundo = cam.ScreenToWorldPoint(
                    new Vector3(posicaoTela.x, posicaoTela.y, Mathf.Abs(cam.transform.position.z)));
                Vector3 delta = new Vector3(mundo.x - mouseMundoInicioDrag.x, mundo.y - mouseMundoInicioDrag.y, 0f);
                Vector3 novaPos = posicaoMapaInicioDrag + delta;
                mapaRenderer.transform.position = novaPos;

                // Move o fundo junto sem recriar
                if (fundoGridRenderer != null)
                {
                    Vector3 fp = fundoGridRenderer.transform.position;
                    fp.x = novaPos.x;
                    fp.y = novaPos.y;
                    fundoGridRenderer.transform.position = fp;
                }
            }
            else
            {
                arrastandomApa = false;
                AtualizarGridVisual();
                AjustarCamera();
            }
        }
    }

    private void LateUpdate()
    {
        AtualizarEspessuraLinhasPorZoom();

        if (textoEscalaGrid != null)
            AtualizarTextoEscalaGrid();
    }

    private void AtualizarTextoEscalaGrid()
    {
        if (textoEscalaGrid == null)
            return;

        textoEscalaGrid.text = $"Mapa: {multiplicadorTamanhoGrid * 100f:0}%";
    }

    private void LimparLinhasGrid()
    {
        for (int i = 0; i < linhasGridInstanciadas.Count; i++)
        {
            if (linhasGridInstanciadas[i] != null)
                Destroy(linhasGridInstanciadas[i].gameObject);
        }

        linhasGridInstanciadas.Clear();
        espessuraLinhasAplicada = -1f;
    }

    private Bounds ObterBoundsGridFixo()
    {
        Vector3 centro = transform.position;

        if (mapaRenderer != null)
            centro = mapaRenderer.transform.position;
        else if (gridPrincipal != null)
            centro = gridPrincipal.transform.position;

        float largura = Mathf.Max(ObterTamanhoAtualDaCelula() * 4f, tamanhoMundoGridFixo.x);
        float altura = Mathf.Max(ObterTamanhoAtualDaCelula() * 4f, tamanhoMundoGridFixo.y);
        return new Bounds(centro, new Vector3(largura, altura, 0.1f));
    }

    private void GarantirFundoGridVisual()
    {
        if (fundoGridRenderer == null)
        {
            Transform pai = gridPrincipal != null ? gridPrincipal.transform : transform;
            Transform existente = pai.Find("Grid_Background_Automatico");
            if (existente == null)
            {
                GameObject go = new GameObject("Grid_Background_Automatico");
                go.transform.SetParent(pai, false);
                existente = go.transform;
            }

            fundoGridRenderer = existente.GetComponent<SpriteRenderer>();
            if (fundoGridRenderer == null)
                fundoGridRenderer = existente.gameObject.AddComponent<SpriteRenderer>();
        }

        if (fundoGridRenderer.sprite == null)
            fundoGridRenderer.sprite = ObterSpriteFundoBranco();

        Bounds boundsGrid = ObterBoundsGridFixo();
        fundoGridRenderer.transform.position = new Vector3(
            boundsGrid.center.x,
            boundsGrid.center.y,
            mapaRenderer != null ? mapaRenderer.transform.position.z : 0f);
        fundoGridRenderer.transform.localScale = new Vector3(boundsGrid.size.x, boundsGrid.size.y, 1f);
        fundoGridRenderer.color = corFundoGrid;
        fundoGridRenderer.sortingLayerID = mapaRenderer != null ? mapaRenderer.sortingLayerID : SortingLayer.NameToID(sortingLayerMapa);
        fundoGridRenderer.sortingOrder = mapaRenderer != null ? mapaRenderer.sortingOrder - 1 : sortingOrderMapa - 1;
        fundoGridRenderer.gameObject.SetActive(true);

        // Sincroniza a cor de fundo da câmera para que o zoom além do sprite não mostre azul
        if (cameraVTT != null)
        {
            Camera camVTT = cameraVTT.GetComponent<Camera>();
            if (camVTT != null)
            {
                camVTT.clearFlags = UnityEngine.CameraClearFlags.SolidColor;
                camVTT.backgroundColor = corFundoGrid;
            }
        }
    }

    private Sprite ObterSpriteFundoBranco()
    {
        if (spriteFundoBranco != null)
            return spriteFundoBranco;

        Texture2D tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        assetsRuntimeCriados.Add(tex);

        spriteFundoBranco = Sprite.Create(
            tex,
            new Rect(0f, 0f, 1f, 1f),
            new Vector2(0.5f, 0.5f),
            1f);

        assetsRuntimeCriados.Add(spriteFundoBranco);
        return spriteFundoBranco;
    }

    private bool DeveExibirGridAgora()
    {
        return criarGridAutomatico && salaAtiva && abaMapaAtiva;
    }

    private void AplicarEstadoVisualGrid()
    {
        if (DeveExibirGridAgora())
        {
            AtualizarGridVisual();
            return;
        }

        OnDevoFecharGalerias?.Invoke();
        OcultarGridVisual();
    }

    private void OcultarGridVisual()
    {
        LimparLinhasGrid();

        if (containerLinhasGrid != null)
            containerLinhasGrid.gameObject.SetActive(false);

        if (fundoGridRenderer != null)
            fundoGridRenderer.gameObject.SetActive(false);
    }

    private void AtualizarEspessuraLinhasPorZoom()
    {
        if (!DeveExibirGridAgora() || cameraVTT == null || linhasGridInstanciadas.Count == 0)
            return;

        Camera cam = cameraVTT.GetComponent<Camera>();
        if (cam == null || !cam.orthographic || Screen.height <= 0)
            return;

        float mundoPorPixel = (cam.orthographicSize * 2f) / Screen.height;
        float larguraMinMundo = Mathf.Max(0.0001f, mundoPorPixel * larguraMinimaPixelsGrid);

        // Teto em unidades de MUNDO (não em pixels de tela!) — um teto em pixels usaria o mesmo
        // Screen.height pra se converter de volta, então sofreria do MESMO desvio que estamos
        // protegendo (ex: Screen.height menor que o real no build → mundoPorPixel inflado →
        // qualquer teto derivado dele infla junto, na mesma proporção, sem travar nada de verdade).
        float tetoMundo = Mathf.Max(espessuraBaseLinhasMundo, 0.0001f) * fatorMaximoEspessuraLinhasGrid;
        float larguraFinal = Mathf.Clamp(Mathf.Max(espessuraBaseLinhasMundo, larguraMinMundo), 0.0001f, tetoMundo);

        if (Mathf.Abs(larguraFinal - espessuraLinhasAplicada) < 0.0005f)
            return;

        espessuraLinhasAplicada = larguraFinal;

        for (int i = 0; i < linhasGridInstanciadas.Count; i++)
        {
            LineRenderer lr = linhasGridInstanciadas[i];
            if (lr == null)
                continue;

            lr.startWidth = larguraFinal;
            lr.endWidth = larguraFinal;
        }
    }
}
