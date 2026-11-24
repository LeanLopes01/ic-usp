using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Categoria de um FerramentaGaleriaSlot — Cena e Token já têm população automática (ver
/// PopularCena/PopularTokens); as demais só existem como espaço reservado na barra (botão + painel já
/// participam da exclusão mútua), sem lógica de conteúdo própria ainda.</summary>
public enum TipoFerramentaGaleria { Nenhuma, Cena, Token, Mapas, Adereco, Montaria, Anexo, Nota, Conteudo, FichasImportadas }

/// <summary>Lado preferido pra abrir o tooltip de nome (ver BarraFerramentasManager.MostrarNomeHover/
/// EscolherPosicaoTela) — cada chamador conhece o próprio layout (barra grudada na borda esquerda da
/// tela prefere Direita; abas perto do topo de um painel preferem Cima etc.), então só o PRIMEIRO
/// candidato muda; se a direção preferida não couber inteira na tela, cai pras demais na ordem fixa
/// de sempre (Cima, Baixo, Direita, Esquerda) — mesmo fallback genérico de antes.</summary>
public enum DirecaoTooltip { Cima, Baixo, Direita, Esquerda }

/// <summary>
/// Um espaço da barra de ferramentas com uma galeria própria (botão de abrir + painel + lista de
/// itens). Configurado inteiramente pelo Inspector — nenhuma dessas referências é resolvida por
/// busca em runtime, diferente dos painéis legados (chat/amigos/grid/monstros) acima.
/// </summary>
[Serializable]
public class FerramentaGaleriaSlot
{
    [Tooltip("Label desta entrada na lista do Inspector — TAMBÉM usado em runtime como texto do " +
        "tooltip de nome ao passar o mouse sobre o botão deste slot (ver ConfigurarTooltipHover).")]
    public string nomeExibicao;
    [Tooltip("Cena e Token já populam sozinhos (ver MapManager.todosOsMapas/TodosOsTokens). As " +
        "outras categorias só abrem/fecham por enquanto — sem conteúdo próprio ainda.")]
    public TipoFerramentaGaleria tipo;

    [Header("UI")]
    public Button botaoToggle;
    [Tooltip("GameObject do painel (moldura/fundo) — em botões que trocam de aba dentro do mesmo " +
        "espaço, pode apontar pro MESMO GameObject em vários slots (ver ToggleSlot: trocar de slot " +
        "não fecha o painel do slot anterior, só o content dele).")]
    public GameObject painel;
    [Tooltip("Content do ScrollView onde os itens (MapButtonUI) são instanciados — este SIM é " +
        "sempre desativado ao trocar de slot, diferente do painel.")]
    public Transform content;
    [Tooltip("Prefab de cada item da lista — precisa ter MapButtonUI pros tipos Cena/Token.")]
    public GameObject prefabItem;
    public bool scrollHorizontal;
    [Tooltip("Botão \"+\" — o OnClick já deve ser configurado DIRETO no Inspector pra chamar o " +
        "método de criação do script vinculado (ex: MapManager.AbrirPopupCriarMapa). " +
        "BarraFerramentasManager não mexe nele em código.")]
    public Button botaoAdicionar;
    [Tooltip("Só informativo — indica qual script é dono dos dados/ações deste slot (ex: MapManager).")]
    public UnityEngine.Object scriptVinculado;
}

/// <summary>Sub-aba DENTRO do painel "Conteudo" — segundo nível de exclusão mútua, aninhado no slot
/// externo Conteudo (ver ToggleSubAbaConteudo). Nome separado de TipoFerramentaGaleria porque "Cena"
/// já significa outra coisa lá (Cena = mapa da Sala; aqui CenaCampanha = cena do módulo Campanhas).</summary>
public enum TipoConteudoSubAba { Nenhuma, Mapa, Token, CenaCampanha, Ficha }

/// <summary>Uma sub-aba do painel Conteudo — botão + painel + lista. Sem botaoAdicionar genérico
/// (diferente de FerramentaGaleriaSlot): só Mapa/Token têm card de "importar", e esse card é wireado
/// DIRETO no Inspector pro método de criação (MapManager.AbrirPopupCriarMapa/AbrirPopupCriarToken) —
/// este código nunca mexe nele, mesma convenção do botaoAdicionar de FerramentaGaleriaSlot.</summary>
[Serializable]
public class ConteudoSubAbaSlot
{
    public string nomeExibicao;
    public TipoConteudoSubAba tipo;

    [Header("UI")]
    public Button botaoToggle;
    public GameObject painel;
    public Transform content;
    public GameObject prefabItem;
    public bool scrollHorizontal;
}

/// <summary>Uma entrada da barra fixa (Chat/Amigos/.../ferramentas de mapa/expandir/colapsar) a
/// receber tooltip por POLLING geométrico (ver BarraFerramentasManager.Update/
/// AtualizarTooltipBarraFixa), em vez do EventTrigger baseado em raycast que ConfigurarTooltipHover
/// usa pra galeria/Conteudo. Motivo: cada botão da barra fixa tem, como filho, uma Image com Raycast
/// Target ativo (necessária visualmente pro usuário ver o ícone) que fica na frente do Button em
/// z-order — o GraphicRaycaster acerta essa Image em vez do Button, o que (a) bloqueia o clique no
/// centro do botão e (b) faz o tooltip piscar (o EventTrigger fica no Button, que não recebe
/// PointerEnter/Exit quando o filho intercepta). Polling geométrico via
/// RectTransformUtility.RectangleContainsScreenPoint ignora o GraphicRaycaster por completo, então é
/// imune a qualquer filho por cima. Autoria 100% pelo Inspector, mesmo padrão de
/// TutorialManager.TutorialPassoData (alvo/texto/lado/distância) — nenhum item é populado em código,
/// arraste o GameObject de cada botão em "alvo" manualmente.</summary>
[Serializable]
public class TooltipBarraFixaData
{
    [Tooltip("GameObject de foco — o MESMO que tem o componente Button (com uma Image de Raycast " +
        "Target ativo como requisito mínimo pro clique funcionar). A área de hover é o " +
        "RectTransform deste objeto, testada por polling a cada frame — não depende de raycast.")]
    public GameObject alvo;
    [Tooltip("Texto mostrado no tooltip compartilhado (MostrarNomeHover) ao passar o mouse sobre alvo.")]
    public string texto;
    public DirecaoTooltip ladoPreferido = DirecaoTooltip.Direita;
    [Tooltip("Distância em pixels entre a borda de alvo e a borda do tooltip. Valor negativo (padrão) " +
        "usa distanciaTooltipBarraFixa.")]
    public float distancia = -1f;
}

/// <summary>
/// Coordena exclusão mútua entre os painéis de ferramentas da sala.
/// Listeners são registrados em OnEnable e removidos em OnDisable para
/// sobreviver a ciclos de ativação/desativação da tela de sala.
///
/// A galeria de cenas/tokens/etc. (FerramentaGaleriaSlot) vive aqui, não em MapManager — o
/// MapManager continua dono dos dados (todosOsMapas/TodosOsTokens) e das ações (trocar de mapa,
/// criar mapa/token, soltar no grid); esta classe só monta a lista de botões (MapButtonUI) e
/// coordena abrir/fechar, reagindo a MapManager.OnCatalogoDeMapasAlterado/OnCatalogoDeTokensAlterado/
/// OnDevoFecharGalerias.
/// </summary>
public class BarraFerramentasManager : MonoBehaviour
{
    [Header("GameObjects dos Painéis")]
    [SerializeField] private GameObject goChat;
    [SerializeField] private GameObject goAmigos;
    [SerializeField] private GameObject goGrid;

    [Header("Referências Diretas (arraste o componente, independente de hierarquia)")]
    [SerializeField] private SteamConviteUI         steamConviteUIRef;
    [SerializeField] private MonstroCatalogoPanelUI monstroCatalogoPanelUIRef;
    [SerializeField] private ClimaPainelUI          climaPainelUIRef;
    [SerializeField] private AudioTabletopPainelUI  audioTabletopPainelUIRef;
    [SerializeField] private VisaoPainelUI          visaoPainelUIRef;
    [SerializeField] private MapManager             mapManager;
    [SerializeField] private SalaAbasFichasManager  salaAbasFichasManager;
    [Tooltip("Mesma referência usada em ClimaPainelUI/AudioTabletopManager/etc. pra saber quem é o " +
        "dono da sala (ver SouMestre) — necessária aqui só pra decidir visibilidade mestre-vs-jogador " +
        "dos botões/painéis marcados abaixo.")]
    [SerializeField] private SteamLobbyManager      steamLobbyManager;

    [Header("Visibilidade Mestre x Jogador")]
    [Tooltip("GameObjects que só o mestre da sala deve ver — qualquer um (botão, painel, ícone, " +
        "grupo inteiro), não precisa ter Button. O jogador nunca vê estes objetos " +
        "(SetActive(false)), aplicado em AplicarVisibilidadePorPapel(). Os slots de galeria e as " +
        "sub-abas do painel Conteudo já são mestre-only por regra própria (com exceção fixa de " +
        "Conteudo/Ficha, ver AplicarVisibilidadePorPapel) — não precisam entrar nesta lista.")]
    [SerializeField] private List<GameObject> objetosSoMestre = new List<GameObject>();

    [Header("Botões das Ferramentas (Chat / Amigos / Grid / Monstros)")]
    [SerializeField] private Button botaoToggleChat;
    [SerializeField] private Button botaoToggleAmigos;
    [SerializeField] private Button botaoToggleGrid;
    [SerializeField] private Button botaoToggleMonstros;
    [SerializeField] private Button botaoToggleClima;
    [SerializeField] private Button botaoToggleAudio;
    [SerializeField] private Button botaoToggleVisao;
    [SerializeField] private Button botaoAjuda;

    [Header("Ferramentas de Mapa (Régua / AoE / Desenhar / Pointer)")]
    [Tooltip("Barra separada da barra fixa (Chat/Amigos/Grid/...) acima. Cada botão abre/fecha o " +
        "próprio content de opções (ver ToggleContentFerramentaMapa) — exclusão mútua só entre os " +
        "4, independente de Chat/Amigos/Grid/etc. Vivem dentro de containerFerramentasColapsavel " +
        "logo abaixo.")]
    [SerializeField] private Button botaoFerramentaRegua;
    [SerializeField] private Button botaoFerramentaAoE;
    [SerializeField] private Button botaoFerramentaDesenhar;
    [SerializeField] private Button botaoFerramentaPointer;
    [Tooltip("Fog manual (ver FogEditorTools.cs) — desenha/apaga retângulos presos ao grid que ficam " +
        "sempre 100% pretos pro jogador comum, independente da visão dinâmica dos tokens.")]
    [SerializeField] private Button botaoFerramentaFog;
    [Tooltip("Configuração de iluminação dinâmica (ver MapManager: raiosBaseVisao/" +
        "alphaMaximoVisaoDinamica/transicaoGradienteEmCelulas/alphaVisaoGeralTokens/" +
        "alphaFogParaHost) — ferramenta do Mestre, valores são POR MAPA e propagam pra todos os " +
        "jogadores conectados ao fechar o content (ver PersistirConfigIluminacao).")]
    [SerializeField] private Button botaoFerramentaIluminacao;
    [Tooltip("Fontes de luz estática (tocha — ver LightSource.cs/LightEditorTools.cs) — planta um " +
        "ponto que ilumina um raio configurável, respeitando paredes, exatamente como um token " +
        "parado. Fora do alcance de qualquer luz/token, o jogador vê escuro.")]
    [SerializeField] private Button botaoFerramentaLuz;
    [Tooltip("Pintura de terreno (ver Campanhas/Grid/TerrainManager.cs/TerrainEditorTools.cs) — " +
        "pincel que marca células como Água/Lava (custo de movimento, dano) — overlay tático só " +
        "aparece pro Mestre, mas VFX (ex: calor da lava) é visível pra todos.")]
    [SerializeField] private Button botaoFerramentaTerreno;

    [Header("Content de Opções — Régua / AoE / Desenhar / Pointer / Fog / Luz")]
    [Tooltip("Um content por ferramenta acima — abre ao clicar no botão correspondente, fecha os " +
        "outros (ver ToggleContentFerramentaMapa). Sem painel próprio (diferente da galeria), só o " +
        "content mesmo. AoE/Desenhar/Pointer ainda sem opções próprias definidas — deixe vazio ou " +
        "com um placeholder até terem conteúdo real.")]
    [SerializeField] private GameObject contentFerramentaRegua;
    [SerializeField] private GameObject contentFerramentaAoE;
    [SerializeField] private GameObject contentFerramentaDesenhar;
    [SerializeField] private GameObject contentFerramentaPointer;
    [SerializeField] private GameObject contentFerramentaFog;
    [SerializeField] private GameObject contentFerramentaIluminacao;
    [SerializeField] private GameObject contentFerramentaLuz;
    [SerializeField] private GameObject contentFerramentaTerreno;

    [Header("Opções da Régua (dentro de contentFerramentaRegua)")]
    [Tooltip("Régua normal — liga/desliga o modo de medição (ver ReguaMedicaoManager.Alternar): " +
        "clicar no grid define a origem, linha reta + distância em pés seguem o mouse a partir daí. " +
        "Precisa de um ReguaMedicaoManager na cena (mesmo requisito de SelecaoAlvoManager).")]
    [SerializeField] private Button botaoReguaNormal;
    [Tooltip("Esquadro — reservado pro futuro (medição só nos eixos do grid, sem diagonal), sem " +
        "lógica própria ainda. Nenhum onClick religado até ter comportamento definido.")]
    [SerializeField] private Button botaoReguaEsquadro;

    [Header("Opções do Fog (dentro de contentFerramentaFog)")]
    [Tooltip("Referência DIRETA (não singleton — diferente de ReguaMedicaoManager, um FogEditorTools " +
        "também existe em Campanhas pro CampanhaMapManager de lá; um singleton estático destruiria um " +
        "dos dois se ambos existissem na mesma cena). Arraste aqui o FogEditorTools desta cena, " +
        "configurado com mapManagerBehaviour apontando pro MapManager.")]
    [SerializeField] private FogEditorTools fogEditorTools;
    [Tooltip("Arrastar no grid desenha um retângulo de fog manual.")]
    [SerializeField] private Button botaoFogDesenhar;
    [Tooltip("Clicar num retângulo de fog manual já desenhado apaga ele inteiro.")]
    [SerializeField] private Button botaoFogApagar;
    [Tooltip("Liga/desliga a prévia de \"Visão de Jogador\" (ver MapManager.AlternarPreviaVisaoJogador) " +
        "— deixa o Mestre ver o mapa com o mesmo fog-of-war automático que um jogador comum veria, " +
        "sem precisar de um segundo cliente pra testar. Só aparece/faz efeito pro Mestre.")]
    [SerializeField] private Button botaoVisaoDeJogador;
    [Tooltip("GameObject arbitrário ligado/desligado junto com a prévia de Visão de Jogador (ex: um " +
        "overlay/moldura indicando que a prévia está ativa) — configure na cena o que fizer sentido; " +
        "deixe vazio se não precisar de nada extra além do próprio fog-of-war.")]
    [SerializeField] private GameObject objetoVisaoDeJogador;

    [Header("Opções de Iluminação (dentro de contentFerramentaIluminacao)")]
    [Tooltip("Espelham os 5 campos de configuração de iluminação dinâmica de MapManager.cs — " +
        ".interactable é ligado/desligado por SouMestre() (só o Mestre configura), e os valores são " +
        "sincronizados a partir do MapManager sempre que o content abre (ver " +
        "ToggleContentFerramentaIluminacao/SincronizarSlidersDeIluminacao). Mudar o slider aplica ao " +
        "vivo e local; a persistência + propagação pra rede só acontece ao FECHAR o content (ver " +
        "MapManager.PersistirConfigIluminacao), pelo mesmo motivo que a borracha de paredes só " +
        "persiste ao soltar o mouse — evita gravar/enviar a cada frame de arrasto.")]
    [SerializeField] private Slider sliderRaiosBaseVisao;
    [SerializeField] private TMP_Text textoRaiosBaseVisao;
    [SerializeField] private Slider sliderAlphaMaximoVisaoDinamica;
    [SerializeField] private TMP_Text textoAlphaMaximoVisaoDinamica;
    [SerializeField] private Slider sliderTransicaoGradienteEmCelulas;
    [SerializeField] private TMP_Text textoTransicaoGradienteEmCelulas;
    [SerializeField] private Slider sliderAlphaVisaoGeralTokens;
    [SerializeField] private TMP_Text textoAlphaVisaoGeralTokens;
    [SerializeField] private Slider sliderAlphaFogParaHost;
    [SerializeField] private TMP_Text textoAlphaFogParaHost;
    [Tooltip("Campo de texto hexadecimal (ex: FFD27F) — escolhe o MATIZ do filtro de luz sobre a " +
        "área visível dos tokens (ver MapManager.corLuzVisao/DefinirCorLuzVisao). A intensidade do " +
        "filtro é fixa e baixa (MapManager.alphaLuzPersonagem, não exposta aqui) — só a cor é " +
        "escolhível, de propósito, pra manter o efeito 'bem claro' (amarelado, azulado, etc.), nunca " +
        "uma cor sólida forte. Aceita com ou sem '#' na frente; texto inválido ao confirmar (Enter/" +
        "perder foco) é ignorado e o campo volta a mostrar a cor atual.")]
    [SerializeField] private TMP_InputField campoCorLuzHex;
    [Tooltip("Opcional — amostra visual da cor atual (um Image simples), atualizada junto com o " +
        "campo de texto. Deixe vazio se não quiser esse retorno visual.")]
    [SerializeField] private Image previaCorLuz;

    [Header("Opções de Fontes de Luz (dentro de contentFerramentaLuz)")]
    [Tooltip("Referência DIRETA (não singleton — diferente de ReguaMedicaoManager, um LightEditorTools " +
        "também existe em Campanhas pro CampanhaMapManager de lá; um singleton estático destruiria um " +
        "dos dois se ambos existissem na mesma cena, mesmo motivo de fogEditorTools). Arraste aqui o " +
        "LightEditorTools desta cena, configurado com mapManagerBehaviour apontando pro MapManager — " +
        "o slider de raio da luz mora NELE (não aqui), ver LightEditorTools.sliderRaioLuz.")]
    [SerializeField] private LightEditorTools lightEditorTools;
    [Tooltip("Planta uma fonte de luz no ponto clicado, com o raio atual do slider (dentro de LightEditorTools).")]
    [SerializeField] private Button botaoLuzAdicionar;
    [Tooltip("Clicar perto de uma fonte de luz já plantada remove ela inteira.")]
    [SerializeField] private Button botaoLuzApagar;
    [Tooltip("Liga/desliga a VISIBILIDADE dos marcadores das luzes já plantadas (ver " +
        "LightEditorTools.AlternarVisibilidadeMarcadores) — independente de Adicionar/Apagar estarem " +
        "ativos, só pra conferir onde as tochas estão. Ferramenta do Mestre — os marcadores são só " +
        "uma referência de edição local, nunca aparecem pro jogador comum.")]
    [SerializeField] private Button botaoLuzAlternarVisibilidade;

    [Header("Opções de Terreno (dentro de contentFerramentaTerreno)")]
    [Tooltip("Referência DIRETA (não singleton — mesmo raciocínio de fogEditorTools/lightEditorTools). " +
        "Arraste aqui o TerrainEditorTools desta cena, configurado com terrainManager apontando pro " +
        "TerrainManager de Salas.")]
    [SerializeField] private TerrainEditorTools terrainEditorTools;
    [Tooltip("Referência DIRETA ao TerrainManager desta cena — usada só pra propagar o papel " +
        "Mestre/Jogador (ver AplicarVisibilidadePorPapel/TerrainManager.SetRole). Pode ser a MESMA " +
        "instância arrastada em terrainEditorTools.")]
    [SerializeField] private TerrainManager terrainManager;
    [Tooltip("Pincel de terreno — clicar seleciona o tipo, arrastar no grid pinta cada célula sob o " +
        "cursor. Clicar de novo no tipo já selecionado desliga o pincel.")]
    [SerializeField] private Button botaoTerrenoNenhum;
    [SerializeField] private Button botaoTerrenoLava;
    [SerializeField] private Button botaoTerrenoAgua;

    [Header("Expandir/Colapsar Barra de Ferramentas de Mapa")]
    [Tooltip("Container com os 4 botões de ferramenta acima (régua/AoE/desenhar/pointer) — o botão " +
        "de Ajuda (da barra fixa, acima) fica DE FORA deste container de propósito, sempre visível " +
        "independente do estado colapsado/expandido.")]
    [SerializeField] private GameObject containerFerramentasColapsavel;
    [Tooltip("Dois botões separados (não um só toggle) porque cada um tem seu próprio sprite — " +
        "mesmo padrão de EquipmentSelector.expandButton/collapseButton. Só um fica visível por vez " +
        "(ver ExpandirFerramentasDeMapa/ColapsarFerramentasDeMapa).")]
    [SerializeField] private Button botaoExpandirFerramentas;
    [SerializeField] private Button botaoColapsarFerramentas;

    [Header("Galeria (Cena / Token / Mapas / Adereços / Montarias / Anexos / Notas)")]
    [Tooltip("Adicione um elemento por categoria — só tipo=Cena e tipo=Token populam sozinhos hoje.")]
    [SerializeField] private List<FerramentaGaleriaSlot> slots = new List<FerramentaGaleriaSlot>();

    [Header("Sub-abas do painel Conteudo (Mapa / Token / Cena de Campanha / Ficha)")]
    [Tooltip("4 entradas, uma por TipoConteudoSubAba — cada uma DEVE viver como filha do content/" +
        "painel do slot externo tipo=Conteudo, senão fechar o Conteudo não esconde a sub-aba junto.")]
    [SerializeField] private List<ConteudoSubAbaSlot> slotsConteudo = new List<ConteudoSubAbaSlot>();

    [Header("Tooltip de Nome (hover em item de galeria)")]
    [Tooltip("Prefab simples com um TMP_Text — instanciado UMA vez e reaproveitado pra mostrar o " +
        "nome de qualquer item de galeria (cena, token, e futuras categorias) ao passar o mouse por " +
        "cima — centralizado no próprio item, não no mouse (ver MapButtonUI.OnPointerEnter/Exit e " +
        "MostrarNomeHover/EsconderNomeHover abaixo). " +
        "Central aqui em vez de um Text fixo em cada botão, pra reaproveitar em qualquer lugar futuro.")]
    [SerializeField] private GameObject prefabTooltipNome;
    [Tooltip("Content ONDE o tooltip é instanciado e posicionado — se vazio, usa este próprio GameObject.")]
    [SerializeField] private Transform parentTooltipNome;
    [Tooltip("Distância em pixels entre a borda do game object sob o mouse e a borda do tooltip, na " +
        "direção escolhida (cima/baixo/direita/esquerda — ver EscolherPosicaoTela). É o \"quão para " +
        "cima\" pedido: aumente pra afastar mais o tooltip do objeto. Usada pelos itens de galeria/" +
        "sub-abas do Conteudo — a barra fixa (Chat/Amigos/.../ferramentas de mapa) usa " +
        "distanciaTooltipBarraFixa abaixo, separada porque os botões da barra são maiores/mais " +
        "espaçados que os ícones de galeria (mesmo valor pros dois ficava visualmente errado — " +
        "\"muito longe\" pra um lado, apertado demais pro outro).")]
    [SerializeField] private float distanciaTooltipNome = 4f;
    [Tooltip("Mesma ideia de distanciaTooltipNome, só que pra barra fixa de botões (Chat/Amigos/" +
        "Grid/.../ferramentas de mapa/expandir-colapsar) — ajuste este valor pra afastar/aproximar " +
        "SÓ os tooltips dessa barra, sem afetar os de galeria/Conteudo.")]
    [SerializeField] private float distanciaTooltipBarraFixa = 8f;
    [Tooltip("Ajuste fino opcional em pixels (X, Y) somado por cima da posição já escolhida " +
        "(distância + direção) — deixe (0,0) pra não mexer.")]
    [SerializeField] private Vector2 ajusteFinoTooltipNome = Vector2.zero;

    [Header("Tooltips da Barra Fixa (detecção por polling — ver TooltipBarraFixaData)")]
    [Tooltip("Substitui ConfigurarTooltipHover pros botões fixos (Chat/Amigos/Grid/Monstros/Clima/" +
        "Audio/Ajuda/Sincronização/Régua/AoE/Desenhar/Pointer/Expandir/Colapsar) — corrige o texto " +
        "piscando e o clique não registrando no centro do botão (ver comentário de " +
        "TooltipBarraFixaData). Um elemento por botão, configurado inteiramente aqui: arraste o " +
        "GameObject do próprio botão em \"alvo\". A galeria (slots) e as sub-abas do Conteudo " +
        "(slotsConteudo) NÃO usam esta lista — continuam com ConfigurarTooltipHover normalmente.")]
    [SerializeField] private List<TooltipBarraFixaData> tooltipsBarraFixa = new List<TooltipBarraFixaData>();

    // Entrada atualmente em hover (ou null) — evita chamar MostrarNomeHover/EsconderNomeHover todo
    // frame em AtualizarTooltipBarraFixa, só nas trocas de estado.
    private TooltipBarraFixaData _tooltipBarraFixaAtual;

    /// <summary>Acesso estático — MapButtonUI (e futuros itens de galeria) chamam
    /// MostrarNomeHover/EsconderNomeHover direto daqui, sem precisar de uma referência arrastada no
    /// Inspector, mesmo padrão de NetworkSyncManager.Instancia/PopupManager.Instance.</summary>
    public static BarraFerramentasManager Instancia { get; private set; }

    private GameObject   tooltipNomeInstancia;
    private RectTransform tooltipNomeRect;
    private TMP_Text     tooltipNomeTexto;

    [Header("Sincronização Mobile")]
    [Tooltip("Liga/desliga o broadcast UDP + servidor TCP para o(s) app(s) mobile encontrarem e entrarem na mesa.")]
    [SerializeField] private Button botaoToggleSincronizacaoMobile;
    [SerializeField] private AnunciadorDeSalaLocal anunciadorDeSalaLocalRef;
    [SerializeField] private SalaLanServidor salaLanServidorRef;
    [SerializeField] private SalaMobileBridge salaMobileBridgeRef;

    private ChatManager            chatManager;
    private SteamConviteUI         steamConviteUI;
    private GridAjustePanelUI      gridAjusteUI;
    private MonstroCatalogoPanelUI monstroCatalogoUI;
    private ClimaPainelUI          climaPainelUI;
    private AudioTabletopPainelUI  audioTabletopPainelUI;
    private VisaoPainelUI          visaoPainelUI;
    private AnunciadorDeSalaLocal  anunciadorDeSalaLocal;
    private SalaLanServidor        salaLanServidor;
    private SalaMobileBridge       salaMobileBridge;

    // Último slot de galeria com o CONTENT aberto — usado por ToggleSlot/FecharTodos pra saber qual
    // content fechar ao trocar de aba (o painel dele, por design, não é mexido nessa troca).
    private FerramentaGaleriaSlot slotGaleriaAberto;

    // Sub-aba atualmente aberta DENTRO do painel Conteudo — segundo nível de exclusão mútua,
    // independente de slotGaleriaAberto (ver ToggleSubAbaConteudo/FecharTodasSubAbasConteudo).
    private ConteudoSubAbaSlot subAbaConteudoAberta;

    // Content de opções atualmente aberto entre Régua/AoE/Desenhar/Pointer — exclusão mútua própria,
    // independente de slotGaleriaAberto/subAbaConteudoAberta (ver ToggleContentFerramentaMapa).
    private GameObject ferramentaMapaContentAberto;

    // Recalculado a cada OnEnable (ver AplicarVisibilidadePorPapel) — cacheado em vez de chamar
    // SouMestre() a cada clique porque IsOwnedBy faz uma consulta ao SteamLobby a cada chamada, e a
    // sala não muda de dono no meio de uma sessão.
    private bool _souMestre;

    // Estado do container de ferramentas de mapa (régua/AoE/desenhar/pointer) — resetado pra
    // expandido a cada OnEnable, mesmo padrão de "estado inicial limpo" já usado pros slots de galeria.
    private bool _ferramentasColapsadas;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instancia = this;
        ResolverReferencias();
    }

    private void OnDestroy()
    {
        if (Instancia == this) Instancia = null;
        if (tooltipNomeInstancia != null) Destroy(tooltipNomeInstancia);
    }

    // Só a barra fixa usa polling (ver AtualizarTooltipBarraFixa/TooltipBarraFixaData) — galeria/
    // Conteudo continuam no EventTrigger de ConfigurarTooltipHover, sem custo extra por frame aqui.
    private void Update()
    {
        AtualizarTooltipBarraFixa();
    }

    private void OnEnable()
    {
        // Re-resolve caso alguma referência tenha ficado nula (ex: objeto recriado)
        ResolverReferencias();
        // Melhor esforço já aqui — cobre reabrir esta tela numa sala que já estava pronta. Sozinho
        // não basta pra sala RECÉM-criada: SteamLobbyManager.SalaAtual só é preenchido de forma
        // assíncrona (ver OnSalaCriadaComSucesso/OnEntrouNaSalaComSucesso abaixo), então se este
        // OnEnable rodar antes disso, SouMestre() ainda vê SalaAtual vazio e devolve false mesmo
        // pro próprio criador da sala — os botões mestre-only ficavam escondidos até a próxima
        // reativação. Os eventos abaixo recalculam assim que o lobby fica disponível de verdade.
        _souMestre = SouMestre();
        AplicarVisibilidadePorPapel();
        RegistrarBotoes();

        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso  += OnLobbyDisponivelParaVisibilidade;
            steamLobbyManager.OnEntrouNaSalaComSucesso += OnLobbyDisponivelParaVisibilidade;
        }
        Debug.Log($"[BarraFerr] OnEnable — chat={chatManager != null}, amigos={steamConviteUI != null}, " +
                  $"grid={gridAjusteUI != null}, monstros={monstroCatalogoUI != null} | " +
                  $"btnChat={botaoToggleChat != null}, btnAmigos={botaoToggleAmigos != null}, " +
                  $"btnGrid={botaoToggleGrid != null}, btnMonstros={botaoToggleMonstros != null}");

        // Estado inicial limpo — todo slot começa fechado, independente do que ficou ativo no Editor.
        // Checagem explícita (!= null), NÃO ?. — em UnityEngine.Object, o operador ?. faz a checagem
        // de null "crua" do C# e não pega o "fake null" de um objeto destruído, ao contrário de !=
        // null (que usa o operator== sobrecarregado da Unity). Ver mesmo padrão no resto do método.
        ExpandirFerramentasDeMapa();

        foreach (FerramentaGaleriaSlot slot in slots)
        {
            if (slot.content != null) slot.content.gameObject.SetActive(false);
            if (slot.painel != null) slot.painel.SetActive(false);
        }
        slotGaleriaAberto = null;
        FecharTodasSubAbasConteudo();
        FecharTodosOsContentsDeFerramentaDeMapa();

        if (mapManager != null)
        {
            mapManager.OnCatalogoDeMapasAlterado  += PopularCena;
            mapManager.OnCatalogoDeTokensAlterado += PopularTokens;
            mapManager.OnDevoFecharGalerias       += FecharTodosOsSlotsCompletamente;
        }

        if (salaAbasFichasManager != null)
            salaAbasFichasManager.OnFichasImportadasAlterado += PopularFichasImportadas;

        // Popula uma vez de imediato — cobre o caso de todosOsMapas/TodosOsTokens já terem itens
        // antes de qualquer evento futuro disparar (ex: sala restaurada antes deste OnEnable rodar).
        PopularCena();
        PopularTokens();
        PopularFichasImportadas();
    }

    private void OnDisable()
    {
        RemoverBotoes();

        if (mapManager != null)
        {
            mapManager.OnCatalogoDeMapasAlterado  -= PopularCena;
            mapManager.OnCatalogoDeTokensAlterado -= PopularTokens;
            mapManager.OnDevoFecharGalerias       -= FecharTodosOsSlotsCompletamente;
        }

        if (salaAbasFichasManager != null)
            salaAbasFichasManager.OnFichasImportadasAlterado -= PopularFichasImportadas;

        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso  -= OnLobbyDisponivelParaVisibilidade;
            steamLobbyManager.OnEntrouNaSalaComSucesso -= OnLobbyDisponivelParaVisibilidade;
        }

        // Evita o tooltip ficar preso visível apontando pra um botão desta barra depois dela ser
        // desativada (ex: trocar de tela) — Update (e portanto AtualizarTooltipBarraFixa) para de
        // rodar em OnDisable, então sem isso o SetActive(true) do tooltip sobreviveria escondido.
        EsconderNomeHover();
        _tooltipBarraFixaAtual = null;
    }

    // Disparado quando SteamLobbyManager.SalaAtual FINALMENTE fica disponível (criação ou entrada
    // concluída) — o valor computado em OnEnable pode ter rodado cedo demais (ver comentário lá),
    // então refaz aqui com a informação de dono já correta.
    private void OnLobbyDisponivelParaVisibilidade(Steamworks.Data.Lobby lobby)
    {
        _souMestre = SouMestre();
        AplicarVisibilidadePorPapel();
    }

    // ── Resolução de referências ──────────────────────────────────────────────

    private void ResolverReferencias()
    {
        if (chatManager == null)
            chatManager = goChat != null
                ? goChat.GetComponentInChildren<ChatManager>(true)
                : FindFirstObjectByType<ChatManager>(FindObjectsInactive.Include);

        if (steamConviteUI == null)
            steamConviteUI = steamConviteUIRef != null
                ? steamConviteUIRef
                : goAmigos != null
                    ? goAmigos.GetComponentInChildren<SteamConviteUI>(true)
                    : FindFirstObjectByType<SteamConviteUI>(FindObjectsInactive.Include);

        if (gridAjusteUI == null)
            gridAjusteUI = goGrid != null
                ? goGrid.GetComponentInChildren<GridAjustePanelUI>(true)
                : FindFirstObjectByType<GridAjustePanelUI>(FindObjectsInactive.Include);

        // Referência direta tem prioridade; fallback para busca na cena
        if (monstroCatalogoUI == null)
            monstroCatalogoUI = monstroCatalogoPanelUIRef != null
                ? monstroCatalogoPanelUIRef
                : FindFirstObjectByType<MonstroCatalogoPanelUI>(FindObjectsInactive.Include);

        if (climaPainelUI == null)
            climaPainelUI = climaPainelUIRef != null
                ? climaPainelUIRef
                : FindFirstObjectByType<ClimaPainelUI>(FindObjectsInactive.Include);

        if (audioTabletopPainelUI == null)
            audioTabletopPainelUI = audioTabletopPainelUIRef != null
                ? audioTabletopPainelUIRef
                : FindFirstObjectByType<AudioTabletopPainelUI>(FindObjectsInactive.Include);

        if (visaoPainelUI == null)
            visaoPainelUI = visaoPainelUIRef != null
                ? visaoPainelUIRef
                : FindFirstObjectByType<VisaoPainelUI>(FindObjectsInactive.Include);

        if (mapManager == null)
            mapManager = FindFirstObjectByType<MapManager>(FindObjectsInactive.Include);

        if (salaAbasFichasManager == null)
            salaAbasFichasManager = FindFirstObjectByType<SalaAbasFichasManager>(FindObjectsInactive.Include);

        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>(FindObjectsInactive.Include);

        // Sem GameObject dedicado ainda na cena (feature de teste): cria um sob demanda
        // para o botão funcionar sem exigir configuração manual no Editor.
        if (anunciadorDeSalaLocal == null)
        {
            anunciadorDeSalaLocal = anunciadorDeSalaLocalRef != null
                ? anunciadorDeSalaLocalRef
                : FindFirstObjectByType<AnunciadorDeSalaLocal>(FindObjectsInactive.Include);
            if (anunciadorDeSalaLocal == null)
                anunciadorDeSalaLocal = new GameObject("AnunciadorDeSalaLocal (auto)").AddComponent<AnunciadorDeSalaLocal>();
        }

        if (salaLanServidor == null)
        {
            salaLanServidor = salaLanServidorRef != null
                ? salaLanServidorRef
                : FindFirstObjectByType<SalaLanServidor>(FindObjectsInactive.Include);
            if (salaLanServidor == null)
                salaLanServidor = new GameObject("SalaLanServidor (auto)").AddComponent<SalaLanServidor>();
        }

        if (salaMobileBridge == null)
        {
            salaMobileBridge = salaMobileBridgeRef != null
                ? salaMobileBridgeRef
                : FindFirstObjectByType<SalaMobileBridge>(FindObjectsInactive.Include);
            if (salaMobileBridge == null)
                salaMobileBridge = salaLanServidor.gameObject.AddComponent<SalaMobileBridge>();
        }
    }

    // ── Mestre x Jogador ──────────────────────────────────────────────────────

    // Mesmo padrão já duplicado em AudioTabletopManager/ClimaPainelUI/EfeitoClimaManager/
    // SalaAbasFichasManager/MapManager/NetworkSyncManager — dono do lobby Steam == mestre da sala.
    private bool SouMestre() =>
        Steamworks.SteamClient.IsValid && steamLobbyManager?.SalaAtual?.IsOwnedBy(Steamworks.SteamClient.SteamId) == true;

    // Aplica (nos dois sentidos, nunca só "esconder") a visibilidade mestre-vs-jogador dos
    // GameObjects marcados em objetosSoMestre, e de TODA a galeria (slots) + TODAS as sub-abas do painel
    // Conteudo (slotsConteudo) — MESTRE-ONLY POR INTEIRO, com duas exceções fixas: o slot externo
    // tipo=Conteudo (o jogador precisa poder abrir o painel) e a sub-aba tipo=Ficha dentro dele (é
    // o único jeito do jogador importar a própria ficha — ver PopularConteudoFicha/
    // ImportarFichaDoConteudo). As outras sub-abas (Mapa/Token/CenaCampanha) continuam mestre-only,
    // então dentro do Conteudo o jogador só consegue abrir a aba Ficha.
    //
    // SEMPRE usa SetActive(visivel) no botão, nos dois sentidos — nunca só SetActive(false) quando
    // escondendo — porque este método roda mais de uma vez por sessão com _souMestre podendo mudar
    // de valor entre as chamadas: SteamLobbyManager.SalaAtual só fica disponível de forma
    // ASSÍNCRONA após criar/entrar numa sala (ver OnLobbyDisponivelParaVisibilidade), então a
    // primeira chamada (OnEnable) pode rodar com _souMestre ainda errado (false pro próprio
    // criador da sala) e precisa ser corrigida pela chamada seguinte assim que o lobby responder —
    // um "só esconde, nunca re-mostra" deixaria os botões do mestre escondidos pra sempre nesse caso.
    private void AplicarVisibilidadePorPapel()
    {
        // TerrainManager tem seu PRÓPRIO gate interno de permissão (_souMestre, default true — ver
        // Campanhas/Grid/TerrainManager.cs), diferente do resto das ferramentas de mapa (que dependem
        // só de visibilidade externa). Sem propagar o papel aqui, um jogador comum em Salas poderia
        // pintar/apagar terreno normalmente, já que ninguém mais chama SetRole nesta cena.
        terrainManager?.SetRole(_souMestre);

        foreach (GameObject go in objetosSoMestre)
        {
            if (go == null) continue;
            go.SetActive(_souMestre);
        }

        foreach (FerramentaGaleriaSlot slot in slots)
        {
            bool visivel = _souMestre || slot.tipo == TipoFerramentaGaleria.Conteudo;
            if (slot.botaoToggle != null) slot.botaoToggle.gameObject.SetActive(visivel);
            if (visivel) continue;

            if (slot.painel != null) slot.painel.SetActive(false);
            if (slot.content != null) slot.content.gameObject.SetActive(false);
        }

        foreach (ConteudoSubAbaSlot slot in slotsConteudo)
        {
            bool visivel = _souMestre || slot.tipo == TipoConteudoSubAba.Ficha;
            if (slot.botaoToggle != null) slot.botaoToggle.gameObject.SetActive(visivel);
            if (visivel) continue;

            if (slot.painel != null) slot.painel.SetActive(false);
            if (slot.content != null) slot.content.gameObject.SetActive(false);
        }
    }

    // ── Registro de botões ────────────────────────────────────────────────────

    private void RegistrarBotoes()
    {
        Religar(botaoToggleChat,     ToggleChat);
        Religar(botaoToggleAmigos,   ToggleAmigos);
        Religar(botaoToggleGrid,     ToggleGrid);
        Religar(botaoToggleMonstros, ToggleMonstros);
        Religar(botaoToggleClima,    ToggleClima);
        Religar(botaoToggleAudio,    ToggleAudio);
        Religar(botaoToggleVisao,    ToggleVisao);
        Religar(botaoAjuda,          AbrirTutorial);
        Religar(botaoToggleSincronizacaoMobile, ToggleSincronizacaoMobile);
        Religar(botaoExpandirFerramentas, ExpandirFerramentasDeMapa);
        Religar(botaoColapsarFerramentas, ColapsarFerramentasDeMapa);
        Religar(botaoFerramentaRegua,    ToggleContentFerramentaRegua);
        Religar(botaoFerramentaAoE,      ToggleContentFerramentaAoE);
        Religar(botaoFerramentaDesenhar, ToggleContentFerramentaDesenhar);
        Religar(botaoFerramentaPointer,  ToggleContentFerramentaPointer);
        Religar(botaoFerramentaFog,      ToggleContentFerramentaFog);
        Religar(botaoFerramentaIluminacao, ToggleContentFerramentaIluminacao);
        Religar(botaoFerramentaLuz,      ToggleContentFerramentaLuz);
        Religar(botaoFerramentaTerreno,  ToggleContentFerramentaTerreno);
        Religar(botaoReguaNormal, AlternarReguaNormal);
        Religar(botaoFogDesenhar, SelecionarFogDesenhar);
        Religar(botaoFogApagar,   SelecionarFogApagar);
        Religar(botaoVisaoDeJogador, AlternarVisaoDeJogador);
        Religar(botaoLuzAdicionar, SelecionarLuzAdicionar);
        Religar(botaoLuzApagar,    SelecionarLuzApagar);
        Religar(botaoLuzAlternarVisibilidade, AlternarVisibilidadeLuzes);
        Religar(botaoTerrenoNenhum, SelecionarTerrenoNenhum);
        Religar(botaoTerrenoLava,   SelecionarTerrenoLava);
        Religar(botaoTerrenoAgua,   SelecionarTerrenoAgua);
        RegistrarSlidersDeIluminacao();
        AtualizarLabelBotaoSincronizacaoMobile();

        // Tooltip da barra fixa (Chat/Amigos/.../ferramentas de mapa/expandir/colapsar) NÃO usa mais
        // ConfigurarTooltipHover — ver AtualizarTooltipBarraFixa (polling geométrico via Update) e
        // TooltipBarraFixaData. Configure os 13 botões na lista tooltipsBarraFixa pelo Inspector.

        foreach (FerramentaGaleriaSlot slot in slots)
        {
            FerramentaGaleriaSlot capturado = slot; // evita captura da variável de loop compartilhada
            Religar(capturado.botaoToggle, () => ToggleSlot(capturado));
            ConfigurarTooltipHover(capturado.botaoToggle, capturado.nomeExibicao, DirecaoTooltip.Direita);
        }

        // Sub-abas do Conteudo ficam numa fileira perto do TOPO do painel aberto — Cima é o lado
        // natural aqui, diferente da barra fixa acima.
        foreach (ConteudoSubAbaSlot slot in slotsConteudo)
        {
            ConteudoSubAbaSlot capturado = slot;
            Religar(capturado.botaoToggle, () => ToggleSubAbaConteudo(capturado));
            ConfigurarTooltipHover(capturado.botaoToggle, capturado.nomeExibicao, DirecaoTooltip.Cima);
        }
    }

    // Liga hover PointerEnter/Exit num botão pra mostrar seu nome/funcionalidade no tooltip
    // compartilhado (MostrarNomeHover/EsconderNomeHover) — mesmo mecanismo já usado pelos itens de
    // galeria (MapButtonUI), reaproveitado aqui pros botões fixos da barra. Guardado por
    // JaTemTooltipConfigurado: RegistrarBotoes roda de novo a cada OnEnable, então sem essa checagem
    // as entries do EventTrigger se acumulariam (EventTrigger não tem um RemoveListener direto).
    // distancia < 0 (padrão) = usa distanciaTooltipNome (galeria/Conteudo); passe
    // distanciaTooltipBarraFixa explicitamente pros botões da barra fixa (ver chamadas em RegistrarBotoes).
    private static void ConfigurarTooltipHover(Button botao, string nome, DirecaoTooltip preferida = DirecaoTooltip.Cima, float distancia = -1f)
    {
        if (botao == null || string.IsNullOrWhiteSpace(nome)) return;

        RectTransform rect = botao.transform as RectTransform;
        if (rect == null) return;

        EventTrigger trigger = botao.GetComponent<EventTrigger>();
        if (trigger != null && trigger.triggers.Exists(e => e.eventID == EventTriggerType.PointerEnter))
            return; // já configurado numa passada anterior de RegistrarBotoes

        if (trigger == null) trigger = botao.gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry entrar = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entrar.callback.AddListener(_ => Instancia?.MostrarNomeHover(nome, rect, preferida, distancia));
        trigger.triggers.Add(entrar);

        EventTrigger.Entry sair = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        sair.callback.AddListener(_ => Instancia?.EsconderNomeHover());
        trigger.triggers.Add(sair);
    }

    private void RemoverBotoes()
    {
        Desligar(botaoToggleChat,     ToggleChat);
        Desligar(botaoToggleAmigos,   ToggleAmigos);
        Desligar(botaoToggleGrid,     ToggleGrid);
        Desligar(botaoToggleMonstros, ToggleMonstros);
        Desligar(botaoToggleClima,    ToggleClima);
        Desligar(botaoToggleAudio,    ToggleAudio);
        Desligar(botaoToggleVisao,    ToggleVisao);
        Desligar(botaoAjuda,          AbrirTutorial);
        Desligar(botaoToggleSincronizacaoMobile, ToggleSincronizacaoMobile);
        Desligar(botaoExpandirFerramentas, ExpandirFerramentasDeMapa);
        Desligar(botaoColapsarFerramentas, ColapsarFerramentasDeMapa);
        Desligar(botaoFerramentaRegua,    ToggleContentFerramentaRegua);
        Desligar(botaoFerramentaAoE,      ToggleContentFerramentaAoE);
        Desligar(botaoFerramentaDesenhar, ToggleContentFerramentaDesenhar);
        Desligar(botaoFerramentaPointer,  ToggleContentFerramentaPointer);
        Desligar(botaoFerramentaFog,      ToggleContentFerramentaFog);
        Desligar(botaoFerramentaIluminacao, ToggleContentFerramentaIluminacao);
        Desligar(botaoFerramentaLuz,      ToggleContentFerramentaLuz);
        Desligar(botaoFerramentaTerreno,  ToggleContentFerramentaTerreno);
        Desligar(botaoReguaNormal, AlternarReguaNormal);
        Desligar(botaoFogDesenhar, SelecionarFogDesenhar);
        Desligar(botaoFogApagar,   SelecionarFogApagar);
        Desligar(botaoVisaoDeJogador, AlternarVisaoDeJogador);
        Desligar(botaoLuzAdicionar, SelecionarLuzAdicionar);
        Desligar(botaoLuzApagar,    SelecionarLuzApagar);
        Desligar(botaoLuzAlternarVisibilidade, AlternarVisibilidadeLuzes);
        Desligar(botaoTerrenoNenhum, SelecionarTerrenoNenhum);
        Desligar(botaoTerrenoLava,   SelecionarTerrenoLava);
        Desligar(botaoTerrenoAgua,   SelecionarTerrenoAgua);
        DesligarSlidersDeIluminacao();

        foreach (FerramentaGaleriaSlot slot in slots)
            if (slot.botaoToggle != null) slot.botaoToggle.onClick.RemoveAllListeners();

        foreach (ConteudoSubAbaSlot slot in slotsConteudo)
            if (slot.botaoToggle != null) slot.botaoToggle.onClick.RemoveAllListeners();
    }

    // ── Toggles (painéis legados) ────────────────────────────────────────────

    public void ToggleChat()
    {
        bool aberto = chatManager != null && chatManager.EstaAbertoPainel();
        Debug.Log($"[BarraFerr] ToggleChat — aberto={aberto}, chatManager={chatManager != null}");
        FecharTodos();
        if (!aberto && chatManager != null)
        {
            chatManager.AbrirPainel();
            // Chat e a faixa de abas de fichas dividem o mesmo espaço de tela (ver
            // SalaAbasFichasManager.painelAbasFichas) — abrir um esconde o outro; fechar não
            // restaura nada sozinho (ver mesma regra em ToggleSlot pro slot Nota).
            salaAbasFichasManager?.FecharPainelAbas();
            PostHogTracker.DefinirContextoAtual("Sala > Chat");
        }
        else
        {
            PostHogTracker.DefinirContextoAtual("Sala > Mapa");
        }
    }

    public void ToggleAmigos()
    {
        bool aberto = steamConviteUI != null && steamConviteUI.EstaAbertoPainel();
        Debug.Log($"[BarraFerr] ToggleAmigos — aberto={aberto}, steamConviteUI={steamConviteUI != null}");
        FecharTodos();
        if (!aberto && steamConviteUI != null)
        {
            steamConviteUI.AbrirPainel();
            PostHogTracker.DefinirContextoAtual("Sala > Amigos");
        }
        else
        {
            PostHogTracker.DefinirContextoAtual("Sala > Mapa");
        }
    }

    public void ToggleGrid()
    {
        bool aberto = gridAjusteUI != null && gridAjusteUI.EstaAbertoPainel();
        Debug.Log($"[BarraFerr] ToggleGrid — aberto={aberto}, gridAjusteUI={gridAjusteUI != null}");
        FecharTodos();
        if (!aberto && gridAjusteUI != null)
        {
            gridAjusteUI.AbrirPainel();
            PostHogTracker.DefinirContextoAtual("Sala > Grid");
        }
        else
        {
            PostHogTracker.DefinirContextoAtual("Sala > Mapa");
        }
    }

    public void ToggleClima()
    {
        bool aberto = climaPainelUI != null && climaPainelUI.EstaAbertoPainel();
        Debug.Log($"[BarraFerr] ToggleClima — aberto={aberto}, climaPainelUI={climaPainelUI != null}");
        FecharTodos();
        if (!aberto && climaPainelUI != null)
        {
            climaPainelUI.AbrirPainel();
            PostHogTracker.DefinirContextoAtual("Sala > Clima");
        }
        else
        {
            PostHogTracker.DefinirContextoAtual("Sala > Mapa");
        }
    }

    public void ToggleAudio()
    {
        bool aberto = audioTabletopPainelUI != null && audioTabletopPainelUI.EstaAbertoPainel();
        FecharTodos();
        if (!aberto && audioTabletopPainelUI != null)
        {
            audioTabletopPainelUI.AbrirPainel();
            PostHogTracker.DefinirContextoAtual("Sala > Audio");
        }
        else
        {
            PostHogTracker.DefinirContextoAtual("Sala > Mapa");
        }
    }

    /// <summary>Corpo idêntico a ToggleClima/ToggleAudio. O gate "só Mestre" do botão em si não
    /// precisa de código — basta adicionar o GameObject do botão à lista objetosSoMestre no
    /// Inspector.</summary>
    public void ToggleVisao()
    {
        bool aberto = visaoPainelUI != null && visaoPainelUI.EstaAbertoPainel();
        FecharTodos();
        if (!aberto && visaoPainelUI != null)
        {
            visaoPainelUI.AbrirPainel();
            PostHogTracker.DefinirContextoAtual("Sala > Visão");
        }
        else
        {
            PostHogTracker.DefinirContextoAtual("Sala > Mapa");
        }
    }

    // Não é um toggle de painel exclusivo (não chama FecharTodos) — só esconde/mostra o container
    // dos botões de régua/AoE/desenhar/pointer, independente de qualquer outro painel estar aberto.
    // Dois métodos (não um toggle só) porque expandir/colapsar são dois botões com sprites
    // diferentes, um visível de cada vez — mesmo padrão de EquipmentSelector.ExpandirPainelPesquisa/
    // RecolherPainelPesquisa.
    private void ExpandirFerramentasDeMapa()
    {
        _ferramentasColapsadas = false;
        if (containerFerramentasColapsavel != null) containerFerramentasColapsavel.SetActive(true);
        if (botaoExpandirFerramentas != null) botaoExpandirFerramentas.gameObject.SetActive(false);
        if (botaoColapsarFerramentas != null) botaoColapsarFerramentas.gameObject.SetActive(true);
    }

    private void ColapsarFerramentasDeMapa()
    {
        _ferramentasColapsadas = true;
        if (containerFerramentasColapsavel != null) containerFerramentasColapsavel.SetActive(false);
        if (botaoExpandirFerramentas != null) botaoExpandirFerramentas.gameObject.SetActive(true);
        if (botaoColapsarFerramentas != null) botaoColapsarFerramentas.gameObject.SetActive(false);
    }

    // ── Ferramentas de Mapa: content de opções (Régua / AoE / Desenhar / Pointer) ──────────────
    //
    // Mesmo comportamento pros 4: clicar no botão abre o content de opções dele, fecha o dos outros
    // 3; clicar de novo no que já está aberto só fecha. Independente de FecharTodos (Chat/Amigos/
    // Grid/etc.) de propósito — mesma independência já usada por Expandir/ColapsarFerramentasDeMapa.

    private void ToggleContentFerramentaRegua()    => ToggleContentFerramentaMapa(contentFerramentaRegua);
    private void ToggleContentFerramentaAoE()      => ToggleContentFerramentaMapa(contentFerramentaAoE);
    private void ToggleContentFerramentaDesenhar() => ToggleContentFerramentaMapa(contentFerramentaDesenhar);
    private void ToggleContentFerramentaPointer()  => ToggleContentFerramentaMapa(contentFerramentaPointer);
    private void ToggleContentFerramentaFog()      => ToggleContentFerramentaMapa(contentFerramentaFog);
    private void ToggleContentFerramentaIluminacao() => ToggleContentFerramentaMapa(contentFerramentaIluminacao);
    private void ToggleContentFerramentaLuz()      => ToggleContentFerramentaMapa(contentFerramentaLuz);
    private void ToggleContentFerramentaTerreno()  => ToggleContentFerramentaMapa(contentFerramentaTerreno);

    private void ToggleContentFerramentaMapa(GameObject content)
    {
        if (content == null) return;

        bool jaAberto = ferramentaMapaContentAberto == content && content.activeSelf;
        // Fechar o content da régua (seja pra trocar de ferramenta, seja fechando de vez) sempre
        // desativa o modo de medição ativo — sem isso a linha ficaria seguindo o mouse sem nenhum
        // botão visível pra desligar (ver ReguaMedicaoManager.Desativar). Mesmo raciocínio pro fog
        // manual (FogEditorTools.Desativar) e pra luz (LightEditorTools.Desativar) — sem isso o modo
        // Desenhar/Apagar/Adicionar/Remover ficaria ativo sem nenhum botão visível pra desligar. E
        // pra Iluminação: fechar o content é o gatilho de persistência (ver PersistirConfigIluminacao)
        // — sliders só aplicam ao vivo/local enquanto arrastados, salvar/propagar a cada frame seria
        // caro à toa.
        bool reguaEstavaAberta = contentFerramentaRegua != null && contentFerramentaRegua.activeSelf;
        bool fogEstavaAberto = contentFerramentaFog != null && contentFerramentaFog.activeSelf;
        bool iluminacaoEstavaAberta = contentFerramentaIluminacao != null && contentFerramentaIluminacao.activeSelf;
        bool luzEstavaAberta = contentFerramentaLuz != null && contentFerramentaLuz.activeSelf;
        bool terrenoEstavaAberto = contentFerramentaTerreno != null && contentFerramentaTerreno.activeSelf;

        FecharTodosOsContentsDeFerramentaDeMapa();
        if (reguaEstavaAberta) ReguaMedicaoManager.Instancia?.Desativar();
        if (fogEstavaAberto) fogEditorTools?.Desativar();
        if (iluminacaoEstavaAberta) mapManager?.PersistirConfigIluminacao();
        if (luzEstavaAberta) lightEditorTools?.Desativar();
        // Sem isso, o pincel de terreno ficaria pintando sem nenhum botão visível pra desligar —
        // mesmo raciocínio de fogEditorTools/lightEditorTools acima.
        if (terrenoEstavaAberto) terrainEditorTools?.Desativar();

        if (jaAberto) return;

        // Abrindo (não fechando) o content de Iluminação: puxa os valores atuais do mapa ativo pros
        // sliders antes de mostrar — senão os sliders mostrariam o valor da última vez que este
        // painel existiu na cena, não o do mapa realmente carregado agora.
        if (content == contentFerramentaIluminacao) SincronizarSlidersDeIluminacao();

        content.SetActive(true);
        ferramentaMapaContentAberto = content;
    }

    private void FecharTodosOsContentsDeFerramentaDeMapa()
    {
        if (contentFerramentaRegua != null) contentFerramentaRegua.SetActive(false);
        if (contentFerramentaAoE != null) contentFerramentaAoE.SetActive(false);
        if (contentFerramentaDesenhar != null) contentFerramentaDesenhar.SetActive(false);
        if (contentFerramentaPointer != null) contentFerramentaPointer.SetActive(false);
        if (contentFerramentaFog != null) contentFerramentaFog.SetActive(false);
        if (contentFerramentaIluminacao != null) contentFerramentaIluminacao.SetActive(false);
        if (contentFerramentaLuz != null) contentFerramentaLuz.SetActive(false);
        if (contentFerramentaTerreno != null) contentFerramentaTerreno.SetActive(false);
        ferramentaMapaContentAberto = null;
    }

    /// <summary>Botão "Régua normal" dentro de contentFerramentaRegua — ver
    /// ReguaMedicaoManager.Alternar(). Precisa de um ReguaMedicaoManager na cena (mesmo requisito de
    /// SelecaoAlvoManager/TutorialManager) — sem ele, o clique só abre/fecha o content, sem medir nada.</summary>
    private void AlternarReguaNormal() => ReguaMedicaoManager.Instancia?.Alternar();

    /// <summary>Botões "Desenhar"/"Apagar" dentro de contentFerramentaFog — ver
    /// FogEditorTools.SelecionarFerramenta (referência direta em fogEditorTools, ver campo) — sem
    /// ele, o clique só abre/fecha o content, sem nenhuma ferramenta ativa.</summary>
    private void SelecionarFogDesenhar() => fogEditorTools?.SelecionarFerramenta(FogToolType.Desenhar);
    private void SelecionarFogApagar()   => fogEditorTools?.SelecionarFerramenta(FogToolType.Apagar);

    /// <summary>Botões "Adicionar"/"Remover" dentro de contentFerramentaLuz — ver
    /// LightEditorTools.SelecionarFerramenta (referência direta em lightEditorTools, ver campo).</summary>
    private void SelecionarLuzAdicionar() => lightEditorTools?.SelecionarFerramenta(LightToolType.Adicionar);
    private void SelecionarLuzApagar()    => lightEditorTools?.SelecionarFerramenta(LightToolType.Remover);

    /// <summary>Botões "Nenhum"/"Lava"/"Água" dentro de contentFerramentaTerreno — ver
    /// TerrainEditorTools.SelecionarFerramenta (referência direta em terrainEditorTools, ver campo).
    /// "Nenhum" funciona como borracha — TerrainManager.PintarTerreno já redireciona esse caso pra
    /// ApagarTerreno internamente.</summary>
    private void SelecionarTerrenoNenhum() => terrainEditorTools?.SelecionarFerramenta(TerrainManager.TerrainType.Nenhum);
    private void SelecionarTerrenoLava()   => terrainEditorTools?.SelecionarFerramenta(TerrainManager.TerrainType.Lava);
    private void SelecionarTerrenoAgua()   => terrainEditorTools?.SelecionarFerramenta(TerrainManager.TerrainType.Agua);

    /// <summary>Botão "Mostrar/Esconder" dentro de contentFerramentaLuz — ver
    /// LightEditorTools.AlternarVisibilidadeMarcadores. Independente de Adicionar/Apagar estarem
    /// ativos; ferramenta do Mestre, os marcadores nunca aparecem pro jogador comum (são só uma
    /// referência de edição local, nunca chegam a existir no client de quem não é host).</summary>
    private void AlternarVisibilidadeLuzes() => lightEditorTools?.AlternarVisibilidadeMarcadores();

    /// <summary>Botão "Visão de Jogador" dentro de contentFerramentaFog — ver
    /// MapManager.AlternarPreviaVisaoJogador. Liga junto o GameObject arbitrário configurado em
    /// objetoVisaoDeJogador (se houver); mantém o estado do botão sincronizado com o do MapManager
    /// (PreviaVisaoJogadorAtiva) já que ele também pode ser desligado indiretamente (troca de mapa).</summary>
    private void AlternarVisaoDeJogador()
    {
        mapManager?.AlternarPreviaVisaoJogador();
        if (objetoVisaoDeJogador != null)
            objetoVisaoDeJogador.SetActive(mapManager != null && mapManager.PreviaVisaoJogadorAtiva);
    }

    // ── Ferramenta de Iluminação: sliders (dentro de contentFerramentaIluminacao) ──────────────
    //
    // Valores aplicam ao vivo/local a cada onValueChanged (ver métodos Definir* de MapManager);
    // persistência + propagação pra rede só acontece quando o content fecha (ver
    // MapManager.PersistirConfigIluminacao, chamado por ToggleContentFerramentaMapa acima).

    private void RegistrarSlidersDeIluminacao()
    {
        // RemoveAllListeners antes de religar — RegistrarBotoes roda a cada OnEnable, sem isso os
        // listeners se acumulariam (Slider não tem um Religar/Desligar próprio como Button).
        ConfigurarSliderIluminacao(sliderRaiosBaseVisao, valor =>
        {
            int v = Mathf.RoundToInt(valor);
            mapManager?.DefinirRaiosBaseVisao(v);
            AtualizarTextoIluminacao(textoRaiosBaseVisao, "Raios Base", v);
        });
        ConfigurarSliderIluminacao(sliderAlphaMaximoVisaoDinamica, valor =>
        {
            mapManager?.DefinirAlphaMaximoVisaoDinamica(valor);
            AtualizarTextoIluminacao(textoAlphaMaximoVisaoDinamica, "Alpha Máximo (Visão Dinâmica)", valor);
        });
        ConfigurarSliderIluminacao(sliderTransicaoGradienteEmCelulas, valor =>
        {
            mapManager?.DefinirTransicaoGradienteEmCelulas(valor);
            AtualizarTextoIluminacao(textoTransicaoGradienteEmCelulas, "Transição do Gradiente (células)", valor);
        });
        ConfigurarSliderIluminacao(sliderAlphaVisaoGeralTokens, valor =>
        {
            mapManager?.DefinirAlphaVisaoGeralTokens(valor);
            AtualizarTextoIluminacao(textoAlphaVisaoGeralTokens, "Alpha (Visão Geral)", valor);
        });
        ConfigurarSliderIluminacao(sliderAlphaFogParaHost, valor =>
        {
            mapManager?.DefinirAlphaFogParaHost(valor);
            AtualizarTextoIluminacao(textoAlphaFogParaHost, "Alpha do Fog (Mestre)", valor);
        });

        // Cor do filtro de luz desativada por enquanto a pedido do usuário — campo/preview ficam
        // escondidos (nunca chegam a ser religados) em vez de removidos, pra religar depois é só
        // descomentar este bloco + o resto marcado "cor da luz desativada" neste arquivo.
        // if (campoCorLuzHex != null)
        // {
        //     campoCorLuzHex.onEndEdit.RemoveListener(OnCorLuzHexConfirmado);
        //     campoCorLuzHex.onEndEdit.AddListener(OnCorLuzHexConfirmado);
        // }
        if (campoCorLuzHex != null) campoCorLuzHex.gameObject.SetActive(false);
        if (previaCorLuz != null) previaCorLuz.gameObject.SetActive(false);
    }

    // Cor da luz desativada por enquanto (ver RegistrarSlidersDeIluminacao) — método sem chamadores
    // enquanto isso durar, deixado aqui comentado pra religar fácil depois.
    // /// <summary>onEndEdit (Enter ou perder foco) do campo de hex — não onValueChanged, pra não tentar
    // /// parsear a cada tecla digitada (um hex incompleto no meio da digitação é inválido o tempo
    // /// todo). Texto inválido não aplica nada e o campo volta a mostrar a cor atual (ver
    // /// AtualizarCampoCorLuz), dando feedback de que não colou sem travar a digitação.</summary>
    // private void OnCorLuzHexConfirmado(string texto)
    // {
    //     if (mapManager == null) return;
    //
    //     string sanitizado = texto != null ? texto.Trim() : "";
    //     if (sanitizado.Length > 0 && sanitizado[0] != '#') sanitizado = "#" + sanitizado;
    //
    //     if (ColorUtility.TryParseHtmlString(sanitizado, out Color cor))
    //         mapManager.DefinirCorLuzVisao(cor);
    //
    //     AtualizarCampoCorLuz(mapManager.CorLuzVisao); // reflete a cor final (nova, ou a antiga se o texto era inválido)
    // }
    //
    // private void AtualizarCampoCorLuz(Color cor)
    // {
    //     if (campoCorLuzHex != null) campoCorLuzHex.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(cor));
    //     if (previaCorLuz != null) previaCorLuz.color = cor;
    // }

    private static void ConfigurarSliderIluminacao(Slider slider, UnityEngine.Events.UnityAction<float> aoAlterar)
    {
        if (slider == null) return;
        slider.onValueChanged.RemoveAllListeners();
        slider.onValueChanged.AddListener(aoAlterar);
    }

    private void DesligarSlidersDeIluminacao()
    {
        sliderRaiosBaseVisao?.onValueChanged.RemoveAllListeners();
        sliderAlphaMaximoVisaoDinamica?.onValueChanged.RemoveAllListeners();
        sliderTransicaoGradienteEmCelulas?.onValueChanged.RemoveAllListeners();
        sliderAlphaVisaoGeralTokens?.onValueChanged.RemoveAllListeners();
        sliderAlphaFogParaHost?.onValueChanged.RemoveAllListeners();
        // campoCorLuzHex?.onEndEdit.RemoveListener(OnCorLuzHexConfirmado); — cor da luz desativada por enquanto
    }

    private static void AtualizarTextoIluminacao(TMP_Text texto, string rotulo, float valor)
    {
        if (texto != null) texto.text = $"{rotulo}: {valor:0.##}";
    }

    /// <summary>Puxa os valores atuais do MapManager (mapa ativo) pros 5 sliders + o campo de hex —
    /// chamado ao ABRIR o content de Iluminação (ver ToggleContentFerramentaMapa). slider.value = ...
    /// dispara o próprio evento de mudança, que já reaplica o mesmo valor no MapManager (no-op) e
    /// atualiza o texto — não precisa de nenhuma lógica extra pra manter os dois sincronizados. O
    /// campo de hex usa SetTextWithoutNotify (ver AtualizarCampoCorLuz) porque texto não deve
    /// disparar onEndEdit só por ser setado via código.</summary>
    private void SincronizarSlidersDeIluminacao()
    {
        if (mapManager == null) return;

        if (sliderRaiosBaseVisao != null) sliderRaiosBaseVisao.value = mapManager.RaiosBaseVisao;
        if (sliderAlphaMaximoVisaoDinamica != null) sliderAlphaMaximoVisaoDinamica.value = mapManager.AlphaMaximoVisaoDinamica;
        if (sliderTransicaoGradienteEmCelulas != null) sliderTransicaoGradienteEmCelulas.value = mapManager.TransicaoGradienteEmCelulas;
        if (sliderAlphaVisaoGeralTokens != null) sliderAlphaVisaoGeralTokens.value = mapManager.AlphaVisaoGeralTokens;
        if (sliderAlphaFogParaHost != null) sliderAlphaFogParaHost.value = mapManager.AlphaFogParaHost;
        // AtualizarCampoCorLuz(mapManager.CorLuzVisao); — cor da luz desativada por enquanto

        bool souMestreAgora = _souMestre;
        if (sliderRaiosBaseVisao != null) sliderRaiosBaseVisao.interactable = souMestreAgora;
        if (sliderAlphaMaximoVisaoDinamica != null) sliderAlphaMaximoVisaoDinamica.interactable = souMestreAgora;
        if (sliderTransicaoGradienteEmCelulas != null) sliderTransicaoGradienteEmCelulas.interactable = souMestreAgora;
        if (sliderAlphaVisaoGeralTokens != null) sliderAlphaVisaoGeralTokens.interactable = souMestreAgora;
        if (sliderAlphaFogParaHost != null) sliderAlphaFogParaHost.interactable = souMestreAgora;
        // if (campoCorLuzHex != null) campoCorLuzHex.interactable = souMestreAgora; — cor da luz desativada por enquanto
    }

    /// <summary>Ligado ao botão "?" — fecha qualquer painel aberto e abre o tutorial guiado
    /// (TutorialManager.Instancia). Não é um toggle (o overlay do tutorial não é um painel desta
    /// classe, é um UIDocument à parte); clicar de novo durante o tutorial não fecha nada aqui, quem
    /// fecha é o próprio overlay (Pular/Entendi/fim dos passos).</summary>
    public void AbrirTutorial()
    {
        FecharTodos();
        TutorialManager.Instancia?.IniciarTutorial();
    }

    public void ToggleMonstros()
    {
        Debug.Log($"[BarraFerr] ToggleMonstros — monstroCatalogoUI={monstroCatalogoUI != null}, " +
                  $"btnAtivo={botaoToggleMonstros?.gameObject.activeSelf}, " +
                  $"btnInteractable={botaoToggleMonstros?.interactable}");

        if (monstroCatalogoUI != null)
        {
            bool aberto = monstroCatalogoUI.EstaAbertoPainel();
            Debug.Log($"[BarraFerr] ToggleMonstros — EstaAbertoPainel={aberto}");
            FecharTodos();
            if (aberto)
            {
                monstroCatalogoUI.FecharPainel();
                PostHogTracker.DefinirContextoAtual("Sala > Mapa");
            }
            else
            {
                monstroCatalogoUI.AbrirPainel();
                PostHogTracker.DefinirContextoAtual("Sala > Monstros");
            }
        }
        else
        {
            Debug.LogWarning("[BarraFerr] ToggleMonstros — monstroCatalogoUI não encontrado. Arraste a referência no Inspector.");
        }
    }

    // Liga/desliga o anúncio UDP + servidor TCP + ponte pro(s) celular(es) acharem e entrarem na
    // mesa sem precisar digitar IP (ver SalaMobileBridge/SalaLanServidor).
    public void ToggleSincronizacaoMobile()
    {
        if (anunciadorDeSalaLocal == null || salaLanServidor == null || salaMobileBridge == null)
        {
            Debug.LogWarning("[BarraFerr] ToggleSincronizacaoMobile — referências de rede não encontradas.");
            return;
        }

        bool ligado = anunciadorDeSalaLocal.Anunciando || salaLanServidor.Rodando;
        if (ligado)
        {
            salaMobileBridge.DesligarSincronizacao();
            Debug.Log("[BarraFerr] Sincronização mobile: desligada.");
        }
        else
        {
            salaMobileBridge.LigarSincronizacao(null);
            Debug.Log($"[BarraFerr] Sincronização mobile: ligada. IP local: {ServidorFichaTCP.ObterIPLocal()}");
        }

        AtualizarLabelBotaoSincronizacaoMobile();
    }

    private void AtualizarLabelBotaoSincronizacaoMobile()
    {
        if (botaoToggleSincronizacaoMobile == null) return;

        var label = botaoToggleSincronizacaoMobile.GetComponentInChildren<Text>();
        var labelTMP = botaoToggleSincronizacaoMobile.GetComponentInChildren<TMPro.TMP_Text>();

        bool ligado = anunciadorDeSalaLocal != null && (anunciadorDeSalaLocal.Anunciando || (salaLanServidor != null && salaLanServidor.Rodando));
        string texto = ligado ? "Sincronização Mobile: ON" : "Sincronização Mobile: OFF";

        if (label != null) label.text = texto;
        if (labelTMP != null) labelTMP.text = texto;
    }

    // ── Galeria (Cena / Token / demais slots) ───────────────────────────────────
    //
    // Regra pedida: clicar no botão de um slot ativa painel + content dele. Trocar pra outro botão
    // de ferramenta (outro slot OU chat/amigos/grid/monstros) só desativa o CONTENT do slot que
    // estava aberto — o painel dele fica como está (permite reaproveitar o mesmo painel entre vários
    // slots sem reabrir/animar de novo). Só clicar de novo no MESMO botão que já está aberto fecha os
    // dois (painel + content).

    private void ToggleSlot(FerramentaGaleriaSlot slot)
    {
        if (slot == null) return;
        // Defesa extra além do botão escondido (ver AplicarVisibilidadePorPapel): cobre chamadores
        // que abrem um slot sem passar pelo próprio botão, como AbrirConteudoESelecionarFicha.
        // Galeria é mestre-only, exceto o slot Conteudo — ver AplicarVisibilidadePorPapel.
        if (!_souMestre && slot.tipo != TipoFerramentaGaleria.Conteudo) return;

        bool clicouNoQueJaEstavaAberto = slotGaleriaAberto == slot
            && slot.painel != null && slot.painel.activeSelf;

        FecharTodos(); // fecha chat/amigos/grid + o CONTENT do slot de galeria previamente aberto

        if (clicouNoQueJaEstavaAberto)
        {
            if (slot.painel != null) slot.painel.SetActive(false);
            slotGaleriaAberto = null;
            PostHogTracker.DefinirContextoAtual("Sala > Mapa");
            return;
        }

        if (slot.painel != null) slot.painel.SetActive(true);
        if (slot.content != null) slot.content.gameObject.SetActive(true);
        slotGaleriaAberto = slot;

        // Notas divide o mesmo espaço de tela de Chat/faixa de abas de fichas (ver ToggleChat) —
        // só este tipo entre todos os slots de galeria precisa fechar a faixa ao abrir.
        if (slot.tipo == TipoFerramentaGaleria.Nota)
            salaAbasFichasManager?.FecharPainelAbas();

        PostHogTracker.DefinirContextoAtual($"Sala > Galeria: {slot.nomeExibicao}");
    }

    // Fecha TUDO de todos os slots (painel + content) — usado quando a aba de mapa fica inativa
    // (MapManager.OnDevoFecharGalerias): não faz sentido nenhuma galeria continuar aberta sobre um
    // grid escondido, diferente da troca normal entre slots.
    private void FecharTodosOsSlotsCompletamente()
    {
        foreach (FerramentaGaleriaSlot slot in slots)
        {
            if (slot.content != null) slot.content.gameObject.SetActive(false);
            if (slot.painel != null) slot.painel.SetActive(false);
        }
        slotGaleriaAberto = null;
        FecharTodasSubAbasConteudo();
        FecharTodosOsContentsDeFerramentaDeMapa();
    }

    private void PopularCena()
    {
        if (mapManager == null) return;
        Action<int, MapButtonUI> configurar = (i, botaoUI) => botaoUI.Setup(mapManager.todosOsMapas[i], mapManager);

        FerramentaGaleriaSlot slotExterno = EncontrarSlot(TipoFerramentaGaleria.Cena);
        if (slotExterno != null) PopularGenerico(slotExterno, mapManager.todosOsMapas.Count, configurar);

        ConteudoSubAbaSlot subAba = EncontrarSubAbaConteudo(TipoConteudoSubAba.Mapa);
        if (subAba != null) PopularGenerico(subAba.content, subAba.prefabItem, subAba.scrollHorizontal, mapManager.todosOsMapas.Count, configurar);
    }

    private void PopularTokens()
    {
        if (mapManager == null) return;
        IReadOnlyList<MapManager.TokenCatalogItem> tokens = mapManager.TodosOsTokens;
        Action<int, MapButtonUI> configurar = (i, botaoUI) =>
        {
            MapManager.TokenCatalogItem token = tokens[i];
            if (token == null || token.sprite == null)
            {
                Debug.LogWarning($"[BarraFerr] PopularTokens — item {i} ficou sem Setup() (token={(token != null)}, " +
                    $"sprite={(token?.sprite != null)}, nome='{token?.nome}') — o botão fica inerte (drag/click não funcionam nele).");
                return;
            }
            string nomeToken = string.IsNullOrWhiteSpace(token.nome) ? $"Token {i + 1}" : token.nome;
            botaoUI.Setup(mapManager, token, nomeToken);
        };

        FerramentaGaleriaSlot slotExterno = EncontrarSlot(TipoFerramentaGaleria.Token);
        if (slotExterno != null) PopularGenerico(slotExterno, tokens.Count, configurar);

        ConteudoSubAbaSlot subAba = EncontrarSubAbaConteudo(TipoConteudoSubAba.Token);
        if (subAba != null) PopularGenerico(subAba.content, subAba.prefabItem, subAba.scrollHorizontal, tokens.Count, configurar);
    }

    // Galeria de fichas importadas — dados/regra de visibilidade/menu de dono vivem inteiramente em
    // SalaAbasFichasManager (já dono de tudo isso pra faixa de abas); esta classe só entrega
    // content/prefabItem e deixa SalaAbasFichasManager montar os cards (ver PopularGaleriaFichasImportadas).
    private void PopularFichasImportadas()
    {
        FerramentaGaleriaSlot slot = EncontrarSlot(TipoFerramentaGaleria.FichasImportadas);
        if (slot == null || salaAbasFichasManager == null) return;

        salaAbasFichasManager.PopularGaleriaFichasImportadas(slot.content, slot.prefabItem);
    }

    // Cenas do módulo Campanhas — agregadas de TODAS as campanhas salvas na máquina (hoje não existe
    // uma listagem assim nem dentro da própria tela de Campanhas, que só lista uma por vez). Sem
    // evento de mudança dedicado (dados só mudam na tela de Campanhas, separada) — repopulado sob
    // demanda toda vez que a sub-aba é aberta (ver ToggleSubAbaConteudo).
    private void PopularConteudoCenaCampanha()
    {
        ConteudoSubAbaSlot slot = EncontrarSubAbaConteudo(TipoConteudoSubAba.CenaCampanha);
        if (slot?.content == null || slot.prefabItem == null) return;

        for (int i = slot.content.childCount - 1; i >= 0; i--)
            Destroy(slot.content.GetChild(i).gameObject);

        foreach (CampanhaLocalSnapshot campanha in CampanhasLocalStorage.LoadAll())
        {
            if (campanha == null || string.IsNullOrWhiteSpace(campanha.campanhaId)) continue;

            foreach (CampanhaMapaData cena in CampanhaMapasStorage.LoadAll(campanha.campanhaId))
            {
                if (cena == null) continue;
                GameObject go = UIUtils.Spawn(slot.prefabItem, slot.content, false);
                ConteudoCenaCampanhaItemUI item = go.GetComponent<ConteudoCenaCampanhaItemUI>();
                if (item == null)
                {
                    Debug.LogWarning("[BarraFerr] Prefab da sub-aba Cena de Campanha não possui ConteudoCenaCampanhaItemUI.");
                    continue;
                }
                item.Setup(cena, campanha.campanhaId, campanha.nome, ImportarCenaDeCampanha);
            }
        }

        if (slot.content is RectTransform rt)
        {
            ConfigurarScrollRect(slot.content, slot.scrollHorizontal);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    private void ImportarCenaDeCampanha(CampanhaMapaData cena, string campanhaId)
    {
        // Dispara OnCatalogoDeMapasAlterado -> PopularCena() recarrega a galeria externa "Cena" E a
        // sub-aba interna "Mapa" na hora, mesmo com a sala já aberta (ver MapManager.ImportarCenaDeCampanha).
        mapManager?.ImportarCenaDeCampanha(cena, campanhaId);
    }

    // Fichas locais do jogador atual, filtradas pelo sistema configurado na sala — substitui o antigo
    // SalaFichaLoaderUI/DataManager (que só retornava fichas de D&D, nunca de outro sistema).
    private void PopularConteudoFicha()
    {
        ConteudoSubAbaSlot slot = EncontrarSubAbaConteudo(TipoConteudoSubAba.Ficha);
        if (slot?.content == null || slot.prefabItem == null || salaAbasFichasManager == null || DataManager.Instance == null) return;

        for (int i = slot.content.childCount - 1; i >= 0; i--)
            Destroy(slot.content.GetChild(i).gameObject);

        string steamId = DataManager.Instance.GetCurrentSteamId();
        int playerId   = DataManager.Instance.GetCurrentId();
        int sistemaId  = salaAbasFichasManager.ObterSistemaIdDaSalaAtual();

        foreach (LocalFichaData snap in LocalFichaStorage.LoadFichasForSistemaESteamId(steamId, playerId, sistemaId))
        {
            // LoadFichasForSistemaESteamId lista TODO arquivo .json da pasta, sem deduplicar por
            // identidade — inclui rascunhos abandonados que o fluxo de criação chegou a criar em
            // disco (ex: recuperação automática de RaceSelector/ClassSelector.GarantirFichaLocalDisponivel)
            // mas nunca foram terminados, e podem carregar campos antigos/incoerentes (ex:
            // level_pc_detalhe de uma combinação de classe que não corresponde mais à ficha real).
            // Mesmo critério de "incompleta" já usado em FichaLoaderUI/FichaCreator (raça+classe+
            // antecedente+atributos) — também faz sentido aqui: esta aba serve pra IMPORTAR uma
            // ficha pronta pra sala, não pra mostrar rascunhos inacabados.
            bool racaSelecionada = snap.raca != null && !string.IsNullOrWhiteSpace(snap.raca.raca_id);
            bool classeSelecionada = snap.classes != null && snap.classes.Count > 0;
            bool antecedenteSelecionado = !string.IsNullOrWhiteSpace(snap.background_id);
            bool atributosSelecionados = snap.atributos != null && (
                snap.atributos.forca > 0 || snap.atributos.destreza > 0 || snap.atributos.constituicao > 0 ||
                snap.atributos.inteligencia > 0 || snap.atributos.sabedoria > 0 || snap.atributos.carisma > 0);
            if (!racaSelecionada || !classeSelecionada || !antecedenteSelecionado || !atributosSelecionados)
                continue;

            GameObject go = UIUtils.Spawn(slot.prefabItem, slot.content, false);
            ConteudoFichaItemUI item = go.GetComponent<ConteudoFichaItemUI>();
            if (item == null)
            {
                Debug.LogWarning("[BarraFerr] Prefab da sub-aba Ficha não possui ConteudoFichaItemUI.");
                continue;
            }
            item.Setup(snap, ImportarFichaDoConteudo);
        }

        if (slot.content is RectTransform rt)
        {
            ConfigurarScrollRect(slot.content, slot.scrollHorizontal);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    private void ImportarFichaDoConteudo(LocalFichaData snap)
    {
        if (salaAbasFichasManager == null || snap == null) return;

        // Mesma conversão LocalFichaData -> FichaData já usada em LocalFichaStorage.LoadFichasForSystem.
        string raceNome = snap.raca?.raca_nome ?? "-";
        string classDisplay = !string.IsNullOrWhiteSpace(snap.level_pc_detalhe)
            ? snap.level_pc_detalhe
            : (snap.classes != null && snap.classes.Count > 0 ? snap.classes[0].class_name : "-");

        salaAbasFichasManager.CriarOuSelecionarAbaDaFicha(new FichaData
        {
            name_pc           = snap.name_pc,
            level_pc          = snap.level_pc,
            level_pc_detalhe  = snap.level_pc_detalhe ?? "",
            race_pc           = raceNome,
            class_pc          = classDisplay,
            fichas_general_id = snap.fichas_general_id,
            fichas_dnd_id     = snap.fichas_dnd_id,
            raca              = snap.raca ?? new RacaData(),
            classes           = snap.classes ?? new List<ClasseData>(),
        }); // ownerSteamId=null -> assume meu próprio steamId, igual comportamento antigo do loader
    }

    private FerramentaGaleriaSlot EncontrarSlot(TipoFerramentaGaleria tipo)
    {
        foreach (FerramentaGaleriaSlot slot in slots)
            if (slot.tipo == tipo) return slot;
        return null;
    }

    private ConteudoSubAbaSlot EncontrarSubAbaConteudo(TipoConteudoSubAba tipo)
    {
        foreach (ConteudoSubAbaSlot slot in slotsConteudo)
            if (slot.tipo == tipo) return slot;
        return null;
    }

    // Segundo nível de exclusão mútua, aninhado dentro do painel externo Conteudo — independente de
    // ToggleSlot/slotGaleriaAberto (abrir/fechar Conteudo não é afetado por isto, e vice-versa; ver
    // FecharTodosOsSlotsCompletamente pro reset conjunto ao trocar de sala).
    private void ToggleSubAbaConteudo(ConteudoSubAbaSlot slot)
    {
        if (slot == null) return;
        // Sub-abas do Conteudo são mestre-only, exceto Ficha — ver AplicarVisibilidadePorPapel.
        if (!_souMestre && slot.tipo != TipoConteudoSubAba.Ficha) return;

        bool jaAberta = subAbaConteudoAberta == slot && slot.painel != null && slot.painel.activeSelf;

        FecharTodasSubAbasConteudo();
        if (jaAberta)
            return;

        if (slot.painel != null) slot.painel.SetActive(true);
        if (slot.content != null) slot.content.gameObject.SetActive(true);
        subAbaConteudoAberta = slot;

        // Sempre repopula ao abrir — dado fresco sem precisar de evento dedicado pras fontes que não
        // têm um (Cena de Campanha, Ficha só mudam em telas separadas de Salas).
        switch (slot.tipo)
        {
            case TipoConteudoSubAba.Mapa: PopularCena(); break;
            case TipoConteudoSubAba.Token: PopularTokens(); break;
            case TipoConteudoSubAba.CenaCampanha: PopularConteudoCenaCampanha(); break;
            case TipoConteudoSubAba.Ficha: PopularConteudoFicha(); break;
        }
    }

    private void FecharTodasSubAbasConteudo()
    {
        foreach (ConteudoSubAbaSlot slot in slotsConteudo)
        {
            if (slot.content != null) slot.content.gameObject.SetActive(false);
            if (slot.painel != null) slot.painel.SetActive(false);
        }
        subAbaConteudoAberta = null;
    }

    /// <summary>Abre o painel Conteudo e já seleciona a sub-aba Ficha — usado pelo botão "+" da faixa
    /// de abas de fichas (antes abria o popup SalaFichaLoaderUI, removido).</summary>
    public void AbrirConteudoESelecionarFicha()
    {
        FerramentaGaleriaSlot outer = EncontrarSlot(TipoFerramentaGaleria.Conteudo);
        // Só chama ToggleSlot se ainda não estiver aberto — ToggleSlot FECHA quando chamado de novo
        // no slot já aberto (ver comentário em ToggleSlot), então chamar sempre aqui fecharia o
        // Conteudo em vez de só trocar de sub-aba quando ele já estivesse aberto noutra sub-aba.
        bool jaAberto = outer != null && slotGaleriaAberto == outer && outer.painel != null && outer.painel.activeSelf;
        if (outer != null && !jaAberto) ToggleSlot(outer);

        ConteudoSubAbaSlot ficha = EncontrarSubAbaConteudo(TipoConteudoSubAba.Ficha);
        if (ficha != null) ToggleSubAbaConteudo(ficha);
    }

    private void PopularGenerico(FerramentaGaleriaSlot slot, int quantidade, Action<int, MapButtonUI> configurarItem)
        => PopularGenerico(slot.content, slot.prefabItem, slot.scrollHorizontal, quantidade, configurarItem);

    private void PopularGenerico<T>(Transform content, GameObject prefabItem, bool scrollHorizontal,
        int quantidade, Action<int, T> configurarItem) where T : Component
    {
        if (content == null || prefabItem == null) return;

        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        for (int i = 0; i < quantidade; i++)
        {
            GameObject novoBotao = UIUtils.Spawn(prefabItem, content, false);
            T componente = novoBotao.GetComponent<T>();
            if (componente == null)
            {
                Debug.LogWarning($"[BarraFerr] Prefab não possui {typeof(T).Name}.");
                continue;
            }
            configurarItem(i, componente);
        }

        if (content is RectTransform rt)
        {
            ConfigurarScrollRect(content, scrollHorizontal);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    // ── Tooltip de Nome (hover em qualquer item de galeria) ─────────────────────
    //
    // Um único GameObject instanciado sob demanda e reaproveitado por QUALQUER item que chame
    // MostrarNomeHover — em vez de cada MapButtonUI carregar seu próprio Text interno (o pedido
    // explícito foi tirar isso do botão e centralizar aqui, pra caber em mais lugares no futuro sem
    // duplicar a lógica de posicionamento/texto).

    private void GarantirTooltipNome()
    {
        if (tooltipNomeInstancia != null) return;
        if (prefabTooltipNome == null) return;

        Transform pai = parentTooltipNome != null ? parentTooltipNome : transform;
        tooltipNomeInstancia = UIUtils.Spawn(prefabTooltipNome, pai);
        tooltipNomeRect = tooltipNomeInstancia.transform as RectTransform;
        tooltipNomeTexto = tooltipNomeInstancia.GetComponentInChildren<TMP_Text>(true);
        tooltipNomeInstancia.SetActive(false);
    }

    /// <summary>Mostra o nome de um item de galeria (cena, token, etc.) — chamar em OnPointerEnter.
    /// Centralizado no GAME OBJECT sob o mouse (origemHover), não na posição do cursor. "preferida" é
    /// o lado onde o tooltip SEMPRE aparece (nunca troca de lado — ver EscolherPosicaoTela) — cada
    /// chamador escolhe de acordo com o próprio layout (ver ConfigurarTooltipHover/DirecaoTooltip).
    /// distancia &lt; 0 (padrão) usa distanciaTooltipNome; passe um valor explícito (ex:
    /// distanciaTooltipBarraFixa) pra contextos com espaçamento diferente. Reaproveita a mesma
    /// instância pra qualquer chamador.</summary>
    public void MostrarNomeHover(string nome, RectTransform origemHover, DirecaoTooltip preferida = DirecaoTooltip.Cima, float distancia = -1f)
    {
        if (string.IsNullOrWhiteSpace(nome) || origemHover == null) return;

        GarantirTooltipNome();
        if (tooltipNomeInstancia == null) return;

        if (tooltipNomeTexto != null)
        {
            tooltipNomeTexto.text = nome;
            // Alinhamento do texto acompanha o lado preferido — Direita/Esquerda focam o texto na
            // borda mais próxima do alvo (tooltip abre "colado" visualmente); Cima/Baixo continuam
            // centralizados, já que a distância é vertical, não horizontal.
            tooltipNomeTexto.alignment = preferida switch
            {
                DirecaoTooltip.Direita  => TextAlignmentOptions.Left,
                DirecaoTooltip.Esquerda => TextAlignmentOptions.Right,
                _                       => TextAlignmentOptions.Center,
            };
        }
        tooltipNomeInstancia.SetActive(true);
        PosicionarTooltipNome(origemHover, preferida, distancia >= 0f ? distancia : distanciaTooltipNome);
    }

    // Polling geométrico pra barra fixa (ver TooltipBarraFixaData) — roda a cada frame em Update(),
    // testando a posição do mouse contra o RectTransform de cada "alvo" via
    // RectTransformUtility.RectangleContainsScreenPoint. Não depende do GraphicRaycaster: mesmo com
    // uma Image filho de Raycast Target ativo por cima do Button (necessária visualmente pro ícone),
    // o polling continua acertando o alvo certo, então nem o clique nem o hover são bloqueados/
    // piscam. activeInHierarchy é checado por entrada — cobre o caso de um alvo ficar escondido
    // (ex: botaoExpandirFerramentas/botaoColapsarFerramentas alternando) sem precisar de nenhum
    // evento dedicado: a entrada simplesmente para de bater na próxima passada.
    private void AtualizarTooltipBarraFixa()
    {
        if (tooltipsBarraFixa.Count == 0 || Mouse.current == null) return;

        Vector2 posMouse = Mouse.current.position.ReadValue();
        TooltipBarraFixaData encontrado = null;

        foreach (TooltipBarraFixaData entrada in tooltipsBarraFixa)
        {
            if (entrada?.alvo == null || !entrada.alvo.activeInHierarchy) continue;

            RectTransform rect = entrada.alvo.transform as RectTransform;
            if (rect == null) continue;

            Canvas canvasDoAlvo = rect.GetComponentInParent<Canvas>();
            Camera cam = (canvasDoAlvo != null && canvasDoAlvo.renderMode != RenderMode.ScreenSpaceOverlay)
                ? canvasDoAlvo.worldCamera : null;

            if (RectTransformUtility.RectangleContainsScreenPoint(rect, posMouse, cam))
            {
                encontrado = entrada;
                break;
            }
        }

        if (encontrado == _tooltipBarraFixaAtual) return; // sem mudança — não repete a cada frame

        _tooltipBarraFixaAtual = encontrado;
        if (encontrado != null)
        {
            float distancia = encontrado.distancia >= 0f ? encontrado.distancia : distanciaTooltipBarraFixa;
            MostrarNomeHover(encontrado.texto, encontrado.alvo.transform as RectTransform, encontrado.ladoPreferido, distancia);
        }
        else
        {
            EsconderNomeHover();
        }
    }

    // Pivot centralizado (0.5, 0.5) — o CENTRO do texto (não a base) é o ponto ancorado. Rebuild de
    // layout ANTES de medir o retângulo do tooltip — o texto acabou de ser trocado (MostrarNomeHover),
    // então o tamanho anterior ainda estaria em cache sem isso.
    private void PosicionarTooltipNome(RectTransform origemHover, DirecaoTooltip preferida, float distancia)
    {
        if (tooltipNomeRect == null) return;

        tooltipNomeRect.pivot = new Vector2(0.5f, 0.5f);
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(tooltipNomeRect);

        Rect retanguloOrigem = ObterRetanguloTela(origemHover);
        Rect retanguloTooltip = ObterRetanguloTela(tooltipNomeRect); // só o tamanho (size) importa aqui

        Vector2 pontoTela = EscolherPosicaoTela(retanguloOrigem, retanguloTooltip.size, preferida, distancia) + ajusteFinoTooltipNome;

        // Converte o ponto de tela pra posição LOCAL dentro do content configurado (parentTooltipNome)
        // — nunca atribui .position bruto (só funciona por acidente em Canvas Screen Space Overlay
        // puro; quebra se o content real usa Screen Space Camera/está escalado, mesmo bug já
        // corrigido em DadosRolagemTooltipManager).
        Transform pai = parentTooltipNome != null ? parentTooltipNome : transform;
        RectTransform paiRect = pai as RectTransform;
        Canvas canvasTooltip = tooltipNomeInstancia.GetComponentInParent<Canvas>();
        Camera camTooltip = (canvasTooltip != null && canvasTooltip.renderMode != RenderMode.ScreenSpaceOverlay) ? canvasTooltip.worldCamera : null;

        if (paiRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(paiRect, pontoTela, camTooltip, out Vector2 pontoLocal))
            tooltipNomeRect.localPosition = pontoLocal;
        else
            tooltipNomeRect.position = pontoTela; // fallback bruto — só se parentTooltipNome não for um RectTransform
    }

    // Calcula o ponto no lado "preferida" do objeto (cada chamador conhece o próprio layout — ver
    // DirecaoTooltip/ConfigurarTooltipHover) e NUNCA troca de lado — antes, se esse lado não coubesse
    // 100% inteiro na tela, o código pulava pra outro lado inteiramente diferente (ex: pedia Cima e
    // acabava saindo Direita), o que parecia "errado" mesmo com a configuração certa. Agora só
    // EMPURRA (clamp) o tooltip pra dentro da tela nos dois eixos se ele vazar por alguma borda,
    // preservando sempre o lado pedido — ver ClampNaTela.
    private Vector2 EscolherPosicaoTela(Rect origem, Vector2 tamanhoTooltip, DirecaoTooltip preferida, float distancia)
    {
        Vector2 centro = origem.center;
        float metadeLargura = tamanhoTooltip.x / 2f;
        float metadeAltura = tamanhoTooltip.y / 2f;

        Vector2 ponto = preferida switch
        {
            DirecaoTooltip.Cima    => new Vector2(centro.x, origem.yMax + distancia + metadeAltura),
            DirecaoTooltip.Baixo   => new Vector2(centro.x, origem.yMin - distancia - metadeAltura),
            DirecaoTooltip.Direita => new Vector2(origem.xMax + distancia + metadeLargura, centro.y),
            _                      => new Vector2(origem.xMin - distancia - metadeLargura, centro.y),
        };

        return ClampNaTela(ponto, tamanhoTooltip);
    }

    // Empurra o CENTRO do tooltip pra dentro dos limites da tela nos dois eixos, o mínimo necessário
    // pra caber inteiro — se o tooltip for maior que a tela num eixo, Mathf.Clamp devolve o mínimo
    // (sem travar/lançar exceção), só fica cortado visualmente nesse caso extremo.
    private static Vector2 ClampNaTela(Vector2 centroTela, Vector2 tamanho)
    {
        float metadeLargura = tamanho.x / 2f;
        float metadeAltura = tamanho.y / 2f;
        float x = Mathf.Clamp(centroTela.x, metadeLargura, Screen.width - metadeLargura);
        float y = Mathf.Clamp(centroTela.y, metadeAltura, Screen.height - metadeAltura);
        return new Vector2(x, y);
    }

    // Bounding box em coordenada de TELA de um RectTransform qualquer — converte os 4 cantos mundiais
    // usando a câmera do CANVAS DELE (não a do tooltip; podem estar em Canvas/render modes
    // diferentes). Reaproveitado tanto pro objeto sob o mouse quanto pro próprio tooltip (nesse caso
    // só o .size do retângulo importa, a posição atual dele é irrelevante pra decidir a nova).
    /// <summary>Converte qualquer RectTransform de uGUI pro Rect em coordenada de TELA (mesma
    /// convenção de RectTransformUtility.WorldToScreenPoint) — público pra que TutorialOverlayUI
    /// (UI Toolkit) consiga destacar um botão desta barra a partir de um overlay em outro sistema de
    /// UI, convertendo esse Rect de tela pro espaço de painel dele via RuntimePanelUtils.ScreenToPanel.</summary>
    public static Rect ObterRetanguloTela(RectTransform rt)
    {
        if (rt == null) return new Rect();

        Vector3[] cantosMundo = new Vector3[4];
        rt.GetWorldCorners(cantosMundo);

        Canvas canvasDoRect = rt.GetComponentInParent<Canvas>();
        Camera cam = (canvasDoRect != null && canvasDoRect.renderMode != RenderMode.ScreenSpaceOverlay) ? canvasDoRect.worldCamera : null;

        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, cantosMundo[0]);
        Vector2 max = min;
        for (int i = 1; i < 4; i++)
        {
            Vector2 ponto = RectTransformUtility.WorldToScreenPoint(cam, cantosMundo[i]);
            min = Vector2.Min(min, ponto);
            max = Vector2.Max(max, ponto);
        }
        return new Rect(min, max - min);
    }

    /// <summary>Como ObterRetanguloTela(RectTransform), mas aceita qualquer GameObject e calcula o
    /// retângulo a partir de TODOS os RectTransforms visíveis dentro dele
    /// (RectTransformUtility.CalculateRelativeRectTransformBounds) em vez do rect declarado de uma
    /// única RectTransform — mais robusto quando o anchor/pivot do objeto-alvo não reflete
    /// exatamente a área renderizada na tela (ex: um filho maior que o pai, padding, ícone
    /// descentralizado), evitando que o destaque do tutorial fique por cima do próprio alvo. Usado
    /// por TutorialOverlayUI.PosicionarTudo.</summary>
    public static Rect ObterRetanguloTela(GameObject alvo)
    {
        if (alvo == null) return new Rect();

        Bounds boundsLocal = RectTransformUtility.CalculateRelativeRectTransformBounds(alvo.transform);
        if (boundsLocal.size == Vector3.zero) return new Rect();

        Canvas canvasDoAlvo = alvo.GetComponentInParent<Canvas>();
        Camera cam = (canvasDoAlvo != null && canvasDoAlvo.renderMode != RenderMode.ScreenSpaceOverlay) ? canvasDoAlvo.worldCamera : null;

        Vector3 bMin = boundsLocal.min;
        Vector3 bMax = boundsLocal.max;
        Vector3[] cantosLocais =
        {
            new Vector3(bMin.x, bMin.y, 0f), new Vector3(bMax.x, bMin.y, 0f),
            new Vector3(bMin.x, bMax.y, 0f), new Vector3(bMax.x, bMax.y, 0f),
        };

        Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, alvo.transform.TransformPoint(cantosLocais[0]));
        Vector2 max = min;
        for (int i = 1; i < cantosLocais.Length; i++)
        {
            Vector2 ponto = RectTransformUtility.WorldToScreenPoint(cam, alvo.transform.TransformPoint(cantosLocais[i]));
            min = Vector2.Min(min, ponto);
            max = Vector2.Max(max, ponto);
        }
        return new Rect(min, max - min);
    }

    /// <summary>Esconde o tooltip de nome — chamar em OnPointerExit.</summary>
    public void EsconderNomeHover()
    {
        if (tooltipNomeInstancia != null) tooltipNomeInstancia.SetActive(false);
    }

    private static void ConfigurarScrollRect(Transform content, bool scrollHorizontal)
    {
        if (content == null)
            return;

        ScrollRect sr = content.GetComponentInParent<ScrollRect>(true);
        if (sr == null)
            return;

        sr.horizontal = scrollHorizontal;
        sr.vertical   = !scrollHorizontal;
        sr.horizontalScrollbarVisibility = scrollHorizontal
            ? ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport
            : ScrollRect.ScrollbarVisibility.AutoHide;
        sr.verticalScrollbarVisibility = scrollHorizontal
            ? ScrollRect.ScrollbarVisibility.AutoHide
            : ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        // Ativa apenas a scrollbar relevante para a direção e faz o link com o ScrollRect
        Scrollbar[] scrollbars = sr.GetComponentsInChildren<Scrollbar>(true);
        foreach (Scrollbar sb in scrollbars)
        {
            bool ehHorizontal = sb.direction == Scrollbar.Direction.LeftToRight
                             || sb.direction == Scrollbar.Direction.RightToLeft;
            bool ehVertical   = sb.direction == Scrollbar.Direction.BottomToTop
                             || sb.direction == Scrollbar.Direction.TopToBottom;

            // Desativa scrollbars da direção oposta para não aparecerem sobrepostas
            bool deveMostrar = scrollHorizontal ? ehHorizontal : ehVertical;
            sb.gameObject.SetActive(deveMostrar);

            if (ehHorizontal && sr.horizontalScrollbar == null)
                sr.horizontalScrollbar = sb;
            else if (ehVertical && sr.verticalScrollbar == null)
                sr.verticalScrollbar = sb;
        }
    }

    // ── API pública ───────────────────────────────────────────────────────────

    public void DefinirSistemaSala(string sistemaSala)
    {
        Debug.Log($"[BarraFerr] DefinirSistemaSala='{sistemaSala}' — monstroCatalogoUI={monstroCatalogoUI != null}");
        monstroCatalogoUI?.DefinirSistemaSala(sistemaSala);

        if (botaoToggleMonstros != null)
        {
            bool deveExibir = sistemaSala == "quinta-edicao-srd";
            botaoToggleMonstros.gameObject.SetActive(deveExibir);
            Debug.Log($"[BarraFerr] botaoToggleMonstros.SetActive({deveExibir})");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void FecharTodos()
    {
        chatManager?.FecharPainel();
        steamConviteUI?.FecharPainel();
        gridAjusteUI?.FecharPainel();
        climaPainelUI?.FecharPainel();
        audioTabletopPainelUI?.FecharPainel();
        visaoPainelUI?.FecharPainel();
        // monstros permanece aberto ao alternar outros painéis

        // Só o CONTENT do slot de galeria aberto — o painel fica como está (ver ToggleSlot).
        if (slotGaleriaAberto != null && slotGaleriaAberto.content != null)
            slotGaleriaAberto.content.gameObject.SetActive(false);
    }

    /// <summary>Fachada pública de FecharTodos — usada por TutorialManager.IniciarTutorial() pra não
    /// deixar nenhum painel desta barra disputando atenção visual com o overlay do tutorial.</summary>
    public void FecharTodosOsPaineis() => FecharTodos();

    private static void Religar(Button botao, UnityEngine.Events.UnityAction acao)
    {
        if (botao == null) return;
        botao.onClick.RemoveListener(acao);
        botao.onClick.AddListener(acao);
    }

    private static void Desligar(Button botao, UnityEngine.Events.UnityAction acao)
    {
        if (botao == null) return;
        botao.onClick.RemoveListener(acao);
    }
}
