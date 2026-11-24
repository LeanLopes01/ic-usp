using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public class FichaGeralManager : MonoBehaviour
{
    [Header("Catálogos (itens/magias) — locais")]
    public string pastaItensLocalRelativa = "sistemas/quinta-edicao-srd/itens";
    public string pastaMagiasLocalRelativa = "sistemas/quinta-edicao-srd/magias";

    [Header("UI - Cabeçalho & Extras")]
    public TMP_InputField nomePersonagemInput;
    public TMP_Text nivelText;
    public TMP_Text classeTagText;
    public TMP_Text racaTagText;
    public TMP_Text proficienciaTxt;
    public TMP_InputField caTxt;
    public Image perfilImage;

    [Header("UI - Inspiração")]
    public TMP_Text inspiracaoTxt;
    public Button btnInspiracaoMais;
    public Button btnInspiracaoMenos;
    private int inspiracaoAtual = 0;

    [Header("UI - Atributos e Modificadores")]
    public TMP_Text forcaTxt; public TMP_Text forcaModTxt;
    public TMP_Text destrezaTxt; public TMP_Text destrezaModTxt;
    public TMP_Text constituicaoTxt; public TMP_Text constituicaoModTxt;
    public TMP_Text inteligenciaTxt; public TMP_Text inteligenciaModTxt;
    public TMP_Text sabedoriaTxt; public TMP_Text sabedoriaModTxt;
    public TMP_Text carismaTxt; public TMP_Text carismaModTxt;

    [Header("UI - Combate")]
    public TMP_InputField hpAtualTxt;
    public TMP_Text hpMaxTxt;
    public TMP_Text iniciativaTxt;
    public TMP_InputField deslocamentoTxt;

    [Header("UI - Combate (Entradas)")]
    public TMP_InputField danoRecebidoInput;
    public TMP_InputField hpTemporarioInput;
    public TMP_InputField dadoVidaInput;
    public Button btnCuraTotal;
    public Button btnAplicarDano;

    [Header("UI - Teste Contra Morte")]
    [Tooltip("3 toggles de sucessos")]
    public Toggle[] morteSucessoToggles = new Toggle[3];
    [Tooltip("3 toggles de falhas")]
    public Toggle[] morteFalhaToggles = new Toggle[3];

    [Header("UI - Defesas e Condições")]
    // Os botões de adicionar (Defesa/Condição) abrem a BarraLateralFichaDnd,
    // que fica responsável por exibir a prefab de seleção correspondente.
    public Transform defesasContainer;
    public GameObject defesaItemPrefab;
    public Transform condicoesContainer;

    [Header("UI - História e Notas")]
    public TMP_InputField historiaInput;
    public TMP_InputField notasInput;

    [Header("UI - Sentidos")]
    public Transform sentidosContent;
    public GameObject sentidoPrefab;

    [Header("UI - Perícias")]
    public Transform periciasContent;
    public GameObject periciaPrefab;

    [Header("UI - Filtros Expansíveis (Prefab Compartilhado)")]
    // Prefab único (toggle + texto) instanciado por código nos 4 painéis de filtro abaixo.
    // Os nomes/tipos de cada filtro são definidos em código (TagsFiltroX), não no Inspector.
    public GameObject filtroChipPrefab;

    [Header("UI - Habilidades")]
    public TMP_InputField buscaHabilidadesInput;
    public Transform habilidadesContent;
    public GameObject habilidadePrefab;
    public Button btnBuscarHabilidades;
    public Button btnExpandirFiltrosHabilidades;
    public Button btnColapsarFiltrosHabilidades;
    public Transform conteudoFiltrosHabilidades;

    [Header("UI - Resumo Proficiências")]
    public TMP_Text linguasTxt;
    public TMP_Text armadurasTxt;
    public TMP_Text armasTxt;
    public TMP_Text ferramentasTxt;

    [Header("UI - Salvaguardas")]
    public Transform salvaguardasContent;
    public GameObject salvaguardaPrefab;

    [Header("UI - Inventário")]
    public TMP_InputField buscaInventarioInput;
    public Transform inventarioContent;
    public GameObject inventarioPrefab;
    public Button btnExpandirFiltrosInventario;
    public Button btnColapsarFiltrosInventario;
    public Transform conteudoFiltrosInventario;
    public Button btnAdicionarItem;
    // Painel de listagem/adição de itens - conteúdo interno (busca, catálogo, lista de itens
    // adicionados na sessão) fica a cargo de quem construir o painel; este script só garante que
    // ele começa desativado e liga abrir/fechar aos botões correspondentes.
    public GameObject painelAdicionarItem;
    public Button btnFecharAdicionarItem;

    [Header("UI - Magias")]
    public TMP_InputField buscaMagiaInput;
    public Transform magiasContent;
    // Separador entre grupos de magias (Truque, por nível, ou fora do catálogo local): espaços atuais/totais,
    // CD, bônus de ataque, botão expandir/colapsar — e também quem instancia os itens de magia dentro do seu content.
    public GameObject magiaNivelSeparadorPrefab;
    public TMP_InputField cdMagiaInput;             // CD para resistir às magias do personagem (8 + prof. + mod. de conjuração)
    public TMP_InputField bonusAtaqueMagicoInput;    // Bônus de ataque com magias do personagem (prof. + mod. de conjuração)
    public Button btnBuscarMagias;
    public Button btnExpandirFiltrosMagias;
    public Button btnColapsarFiltrosMagias;
    public Transform conteudoFiltrosMagias;

    [Header("UI - Utilitários")]
    [Tooltip("Desativado = colapsado. Ativar o toggle expande conteudoUtilitariosExpandido.")]
    public Toggle toggleExpandirUtilitarios;
    public GameObject conteudoUtilitariosExpandido;
    public TMP_InputField atributoConjuracaoUtilInput;   // editável — atributo de conjuração da classe dominante (ex: "Carisma")
    public TMP_InputField cdSalvaguardaUtilInput;        // espelha cdMagiaInput
    public TMP_InputField bonusAtaqueUtilInput;          // espelha bonusAtaqueMagicoInput
    public TMP_Text classeSubclasse1UtilTxt;
    public TMP_Text classeSubclasse2UtilTxt;
    public TMP_Text nivelClasse1UtilTxt;
    public TMP_Text nivelClasse2UtilTxt;
    public TMP_Text bonusConjuracao1UtilTxt;
    public TMP_Text bonusConjuracao2UtilTxt;

    [Header("UI - Ações")]
    public TMP_InputField buscaAcoesInput;
    public TMP_Dropdown tipoAcaoDropdown;
    public Button btnAdicionarAcao;
    public Transform acoesContentArea;
    public GameObject acaoItemPrefab;
    public GameObject tooltipSugestaoPrefab;
    public ActionRecommender actionRecommender;
    public string saveAcoesUrl; // mantido para compatibilidade de cena (não mais utilizado)
    public Button btnExpandirFiltrosAcoes;
    public Button btnColapsarFiltrosAcoes;
    public Transform conteudoFiltrosAcoes; // Content dos chips de filtro (toggle+texto) — não confundir com acoesContentArea, que lista as ações

    [Header("UI - Slots de Magia (Nível 1 ao 9)")]
    public TMP_Text[] slotsMagiaTxt;

    [Header("UI - Editar / Nível")]
    public Button btnEditarFicha;
    public Button btnSubirNivel;
    public GameObject telaCriacaoFichaDnd;
    public GameObject telaFichaDnd;

    [Header("UI - Tooltip (Prefab)")]
    public GameObject tooltipPrefab; 
    private TooltipUI tooltipInstancia;
    private TooltipSugestaoAcaoUI tooltipSugestaoInstancia;

    // --- CLASSES DE DADOS DO CATÁLOGO LOCAL ---
    [Serializable] private class ItemData { public int id; public string nome; public string categoria; public string xml; }
    [Serializable] private class MagiaData { public int id; public string nome; public int level; public string xml; }
    
    // InventoryWrapper/InventorySaveItem e GrimorioWrapper/GrimorioSaveItem agora são as classes
    // canônicas e públicas de LocalFichaStorage.cs — não são mais declaradas aqui (evita a
    // duplicata privada que só "funcionava" por coincidência de nomes de campo via JsonUtility).
    // classeOrigemId (em GrimorioSaveItem) não é usado por esta tela, mas precisa ser preservado no
    // round-trip: SpellSelector.cs (criação de ficha) usa este campo pra separar o orçamento de
    // magias conhecidas por classe em personagens multiclasse.
    
    [Serializable] private class DetalhesPrincipaisWrapper {
        public string subrace_id;
        public string subrace_name;
        public ClasseInfoData classe_info;
        public BackgroundInfoData background_info;
        public ProficienciesInfoWrapper proficiencies_info;
        public AcoesInfoData acoes_info;
    }
    [Serializable] private class ClasseInfoData {
        public string subclass_id;
        public string subclass_name;
        public List<string> saves = new List<string>();
        public List<string> skills = new List<string>();
        public List<string> armors = new List<string>();
        public List<string> weapons = new List<string>();
        public List<string> tools = new List<string>();
        public List<string> languages = new List<string>();
        public List<string> equipment = new List<string>();
        public List<string> asi_feats = new List<string>();
        public List<ClassPowerData> class_powers = new List<ClassPowerData>();
        public List<ClassPowerData> subclass_powers = new List<ClassPowerData>();
        public List<ClassPowerChoiceData> class_power_choices = new List<ClassPowerChoiceData>();
    }
    [Serializable] private class ClassPowerData { public string id; public string name; public int level; public string description; }
    [Serializable] private class ClassPowerChoiceData { public string feature_id; public string feature_name; public int level; public string selected_option; }
    [Serializable] private class BackgroundInfoData {
        public string trait;
        public string ideal;
        public string bond;
        public string flaw;
        public List<string> skills = new List<string>();
        public List<string> armors = new List<string>();
        public List<string> weapons = new List<string>();
        public List<string> tools = new List<string>();
        public List<string> languages = new List<string>();
        public List<string> equipment = new List<string>();
    }
    [Serializable] private class AcoesInfoData { public List<AcaoData> actions = new List<AcaoData>(); }
    [Serializable] private class ProficienciesInfoWrapper {
        public List<SkillSaveItem> skills = new List<SkillSaveItem>();
        public List<SkillSaveItem> saves = new List<SkillSaveItem>();
        public List<string> armors = new List<string>();
        public List<string> weapons = new List<string>();
        public List<string> tools = new List<string>();
        public List<string> languages = new List<string>();
    }
    [Serializable] private class SkillSaveItem { public string nome; public bool treinado; public int attrIndex; public int miscBonus; public int valorTotal; }
    [Serializable] private class SentidoPassivoData { public string nome; public int valor; }
    [Serializable] private class CombateExtraData
    {
        public string defesas;
        public string condicoes;
        public string historia;
        public string notas;
        public string dado_vida;
        public int vida_temporaria;
        public bool[] morte_sucessos = new bool[3];
        public bool[] morte_falhas = new bool[3];
        public List<SentidoPassivoData> sentidos = new List<SentidoPassivoData>();
        public int ca_manual = -1; // -1 = usar cálculo automático (base 10 + DEX + escudo/armadura)
        public int cd_magia_manual = -1; // -1 = usar cálculo automático (8 + prof. + mod. de conjuração)
        public int bonus_ataque_magico_manual = -1; // -1 = usar cálculo automático (prof. + mod. de conjuração)
    }

    // --- Wrappers para leitura de dados locais ---
    [Serializable] private class ClasseInfoJsonWrapper { public ClasseInfoData classe_info; }
    [Serializable] private class ProficienciesInfoContainer { public ProficienciesInfoWrapper proficiencies_info; }
    [Serializable] private class BackgroundInfoContainer { public BackgroundInfoData background_info; }
    [Serializable] private class RacaInfoSnapshot {
        public string subrace_id;
        public string subrace_name;
        public List<string> skills = new List<string>();
        public List<string> armors = new List<string>();
        public List<string> weapons = new List<string>();
        public List<string> tools = new List<string>();
        public List<string> languages = new List<string>();
    }

    private class HabilidadeEntry {
        public string nome;
        public string categoria;
        public int nivel;
        public string origem;
        public string descricao;
        public string tipoAcao;
        public string recarga;
        public string acaoResumo;
        public string grupo;
        public bool causaDano;
        public int ordem;
        public string fonteOverride;
    }

    [Serializable] private class HabilidadesOverridesWrapper { public List<HabilidadeOverrideData> overrides = new List<HabilidadeOverrideData>(); }

    [Serializable] private class SlotsWrapper { public int[] totais; public int[] gastos; }

    private Dictionary<int, ItemData> catalogoItens = new Dictionary<int, ItemData>();
    private Dictionary<int, MagiaData> catalogoMagias = new Dictionary<int, MagiaData>();
    
    private List<InventorySaveItem> meuInventario = new List<InventorySaveItem>();
    private float _inventarioGp = 0f;
    private List<GrimorioSaveItem> meuGrimorio = new List<GrimorioSaveItem>();
    private List<SkillSaveItem> minhasPericias = new List<SkillSaveItem>();
    private List<ClasseData> minhasClasses = new List<ClasseData>();
    private Dictionary<int, int> meusSlotsPorNivel = new Dictionary<int, int>();
    private Dictionary<int, int> meusSlotsGastosPorNivel = new Dictionary<int, int>();
    private readonly HashSet<int> niveisMagiaColapsados = new HashSet<int>();

    private SlotsWrapper meusSlots = new SlotsWrapper();

    private List<GameObject> itensInstanciados = new List<GameObject>();
    private List<GameObject> magiasInstanciadas = new List<GameObject>();
    private List<GameObject> periciasInstanciadas = new List<GameObject>();
    private List<GameObject> habilidadesInstanciadas = new List<GameObject>();
    private List<GameObject> acoesInstanciadas = new List<GameObject>();
    private List<GameObject> sentidosInstanciados = new List<GameObject>();
    private List<GameObject> salvaguardasInstanciadas = new List<GameObject>();
    private List<HabilidadeEntry> habilidades = new List<HabilidadeEntry>();
    private Dictionary<GameObject, string> tipoAcaoPorItem = new Dictionary<GameObject, string>();
    private HashSet<string> acoesSugeridasIgnoradas = new HashSet<string>();
    private readonly HashSet<string> tagsAtivasMagias = new HashSet<string>();
    private readonly HashSet<string> tagsAtivasInventario = new HashSet<string>();
    private readonly HashSet<string> tagsAtivasHabilidades = new HashSet<string>();
    private readonly HashSet<string> tagsAtivasAcoes = new HashSet<string>();
    private readonly List<GameObject> chipsFiltroMagiasInstanciados = new List<GameObject>();
    private readonly List<GameObject> chipsFiltroInventarioInstanciados = new List<GameObject>();
    private readonly List<GameObject> chipsFiltroHabilidadesInstanciados = new List<GameObject>();
    private readonly List<GameObject> chipsFiltroAcoesInstanciados = new List<GameObject>();
    private DetalhesPrincipaisWrapper detalhesPrincipaisCache = new DetalhesPrincipaisWrapper();
    private Dictionary<string, HabilidadeOverrideData> habilidadesOverridesAtuais = new Dictionary<string, HabilidadeOverrideData>();
    private bool ultimaPosicaoTooltipValida = false;
    private Vector2 ultimaPosicaoTooltip = Vector2.zero;
    private int habilidadeOrdemAtual = 0;

    private int bonusProficiencia = 2;
    private int modDestrezaAtual = 0;
    private bool suppressCombatEvents;
    private int caManualOverride = -1; // -1 = usar cálculo automático
    private int cdMagiaManualOverride = -1; // -1 = usar cálculo automático
    private int bonusAtaqueMagicoManualOverride = -1; // -1 = usar cálculo automático

    // Habilidade de conjuração de cada classe (índice compatível com ObterModAtributoPorIndice: 3=INT, 4=SAB, 5=CAR).
    private static readonly Dictionary<string, int> AtributoConjuracaoPorClasse = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "Mago", 3 },
        { "Clérigo", 4 },
        { "Druida", 4 },
        { "Patrulheiro", 4 },
        { "Bardo", 5 },
        { "Feiticeiro", 5 },
        { "Bruxo", 5 },
        { "Paladino", 5 },
    };

    private static readonly (string nome, int attrIndex)[] TodasPericiasBase =
    {
        ("Acrobacia",          1),  // DEX
        ("Arcanismo",          3),  // INT
        ("Atletismo",          0),  // FOR
        ("Atuação",            5),  // CAR
        ("Enganação",          5),  // CAR
        ("Furtividade",        1),  // DEX
        ("História",           3),  // INT
        ("Intimidação",        5),  // CAR
        ("Intuição",           4),  // SAB
        ("Investigação",       3),  // INT
        ("Lidar com Animais",  4),  // SAB
        ("Medicina",           4),  // SAB
        ("Natureza",           3),  // INT
        ("Percepção",          4),  // SAB
        ("Persuasão",          5),  // CAR
        ("Prestidigitação",    1),  // DEX
        ("Religião",           3),  // INT
        ("Sobrevivência",      4),  // SAB
    };

    // Nomes e tipos dos filtros expansíveis definidos em código (provisórios — sujeitos a ajuste).
    private static readonly string[] TagsFiltroHabilidades = { "Classe", "Subclasse", "Raça", "Sub-raça", "Talento", "Antecedente", "Dano", "Não Dano" };
    private static readonly HashSet<string> TagsDanoHabilidades = new HashSet<string> { "Dano", "Não Dano" };
    // Valores válidos de cada campo de override de habilidade — usados para rejeitar dados salvos
    // com a semântica antiga/trocada (ex: override "acao" com valor de "grupo" ou "tipo").
    private static readonly HashSet<string> TiposAcaoValidos = new HashSet<string> { "Ação", "Ação Bônus", "Reação", "Passiva" };
    private static readonly HashSet<string> ResumosAcaoValidos = new HashSet<string> { "Dano", "Cura", "Buff", "Debuff", "Controle", "Movimento", "Utilidade" };
    private static readonly string[] TagsFiltroInventario = { "Arma", "Armadura", "Escudo", "Poção", "Ferramenta", "Munição", "Diversos", "Equipado" };
    private static readonly string[] TagsFiltroMagias = {
        "Truque", "Nível 1", "Nível 2", "Nível 3", "Nível 4", "Nível 5", "Nível 6", "Nível 7", "Nível 8", "Nível 9",
        "Preparada", "Ritual", "Concentração", "Ataque", "Salvamento"
    };
    private static readonly HashSet<string> TagsNivelMagia = new HashSet<string> {
        "Truque", "Nível 1", "Nível 2", "Nível 3", "Nível 4", "Nível 5", "Nível 6", "Nível 7", "Nível 8", "Nível 9"
    };
    private static readonly string[] TagsFiltroAcoes = { "Corpo a Corpo", "À Distância", "Arremesso", "Feitiço" };

    void Awake()
    {
        if (actionRecommender == null) actionRecommender = GetComponent<ActionRecommender>();
        if (actionRecommender == null) actionRecommender = gameObject.AddComponent<ActionRecommender>();

        if (tooltipPrefab != null && tooltipInstancia == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas == null) canvas = FindObjectOfType<Canvas>();
            
            GameObject go = Instantiate(tooltipPrefab, canvas.transform);
            tooltipInstancia = go.GetComponent<TooltipUI>();
            tooltipInstancia.Esconder();
            go.transform.SetAsLastSibling();
        }
    }

    void OnEnable()
    {
        EsconderTooltip();
        nomePersonagemInput.text = "Carregando...";
        
        // Listeners Magias
        if (buscaMagiaInput) buscaMagiaInput.onValueChanged.AddListener((val) => FiltrarMagias());
        if (btnBuscarMagias) btnBuscarMagias.onClick.AddListener(FiltrarMagias);
        ConfigurarExpansaoFiltro(btnExpandirFiltrosMagias, btnColapsarFiltrosMagias, conteudoFiltrosMagias, TagsFiltroMagias, chipsFiltroMagiasInstanciados, tagsAtivasMagias, FiltrarMagias);
        ConfigurarFiltrosMagiaPadrao();

        // Listeners Utilitários
        if (toggleExpandirUtilitarios != null)
        {
            toggleExpandirUtilitarios.SetIsOnWithoutNotify(false);
            if (conteudoUtilitariosExpandido) conteudoUtilitariosExpandido.SetActive(false);
            toggleExpandirUtilitarios.onValueChanged.AddListener(OnToggleUtilitariosChanged);
        }
        if (atributoConjuracaoUtilInput) atributoConjuracaoUtilInput.onEndEdit.AddListener(OnAtributoConjuracaoUtilEdit);
        if (cdSalvaguardaUtilInput) cdSalvaguardaUtilInput.onEndEdit.AddListener(OnCdSalvaguardaUtilEdit);
        if (bonusAtaqueUtilInput) bonusAtaqueUtilInput.onEndEdit.AddListener(OnBonusAtaqueUtilEdit);

        // Listeners Inventário
        if (buscaInventarioInput) buscaInventarioInput.onValueChanged.AddListener((val) => FiltrarInventario());
        ConfigurarExpansaoFiltro(btnExpandirFiltrosInventario, btnColapsarFiltrosInventario, conteudoFiltrosInventario, TagsFiltroInventario, chipsFiltroInventarioInstanciados, tagsAtivasInventario, FiltrarInventario);

        // O painel só existe enquanto o jogador está adicionando itens — garante que ele começa
        // fechado mesmo se alguém esquecer de desativá-lo no prefab/Editor.
        if (painelAdicionarItem) painelAdicionarItem.SetActive(false);
        if (btnAdicionarItem) btnAdicionarItem.onClick.AddListener(() => { if (painelAdicionarItem) painelAdicionarItem.SetActive(true); });
        if (btnFecharAdicionarItem) btnFecharAdicionarItem.onClick.AddListener(FecharPainelAdicionarItem);

        // Listeners Habilidades
        if (buscaHabilidadesInput) buscaHabilidadesInput.onValueChanged.AddListener((val) => FiltrarHabilidades());
        if (btnBuscarHabilidades) btnBuscarHabilidades.onClick.AddListener(FiltrarHabilidades);
        ConfigurarExpansaoFiltro(btnExpandirFiltrosHabilidades, btnColapsarFiltrosHabilidades, conteudoFiltrosHabilidades, TagsFiltroHabilidades, chipsFiltroHabilidadesInstanciados, tagsAtivasHabilidades, FiltrarHabilidades);

        // Listeners Ações
        if (tipoAcaoDropdown) tipoAcaoDropdown.onValueChanged.AddListener((val) => FiltrarAcoes());
        if (buscaAcoesInput) buscaAcoesInput.onValueChanged.AddListener((val) => FiltrarAcoes());
        ConfigurarExpansaoFiltro(btnExpandirFiltrosAcoes, btnColapsarFiltrosAcoes, conteudoFiltrosAcoes, TagsFiltroAcoes, chipsFiltroAcoesInstanciados, tagsAtivasAcoes, FiltrarAcoes);
        if (btnAdicionarAcao) btnAdicionarAcao.onClick.AddListener(CriarAcaoVazia);

        if (btnInspiracaoMais) btnInspiracaoMais.onClick.AddListener(() => AlterarInspiracao(1));
        if (btnInspiracaoMenos) btnInspiracaoMenos.onClick.AddListener(() => AlterarInspiracao(-1));

        if (hpAtualTxt) hpAtualTxt.onEndEdit.AddListener(OnHpAtualEdit);
        if (hpTemporarioInput) hpTemporarioInput.onEndEdit.AddListener((_) => SalvarEstadoCombateLocal());
        if (dadoVidaInput) dadoVidaInput.onEndEdit.AddListener((_) => SalvarEstadoCombateLocal());
        if (caTxt) caTxt.onEndEdit.AddListener(OnCaEdit);
        if (deslocamentoTxt) deslocamentoTxt.onEndEdit.AddListener(OnDeslocamentoEdit);
        if (cdMagiaInput) cdMagiaInput.onEndEdit.AddListener(OnCdMagiaEdit);
        if (bonusAtaqueMagicoInput) bonusAtaqueMagicoInput.onEndEdit.AddListener(OnBonusAtaqueMagicoEdit);

        if (historiaInput) historiaInput.onEndEdit.AddListener((_) => SalvarEstadoCombateLocal());
        if (notasInput) notasInput.onEndEdit.AddListener((_) => SalvarEstadoCombateLocal());

        if (danoRecebidoInput && string.IsNullOrWhiteSpace(danoRecebidoInput.text))
            danoRecebidoInput.text = "0";

        if (btnCuraTotal) btnCuraTotal.onClick.AddListener(OnClickCurar);
        if (btnAplicarDano) btnAplicarDano.onClick.AddListener(AplicarDanoDoInput);

        if (btnEditarFicha) btnEditarFicha.onClick.AddListener(OnClickEditarFicha);
        if (btnSubirNivel) btnSubirNivel.onClick.AddListener(OnClickSubirNivel);

        if (morteSucessoToggles != null)
            for (int i = 0; i < morteSucessoToggles.Length; i++)
                if (morteSucessoToggles[i] != null)
                    morteSucessoToggles[i].onValueChanged.AddListener((_) => OnDeathSavesChanged());

        if (morteFalhaToggles != null)
            for (int i = 0; i < morteFalhaToggles.Length; i++)
                if (morteFalhaToggles[i] != null)
                    morteFalhaToggles[i].onValueChanged.AddListener((_) => OnDeathSavesChanged());

        // Gatilho independente do próprio ciclo OnEnable/OnDisable — mesma correção já aplicada em
        // SpellSelector/ClassSelector/RaceSelector/AttributeManager pro bug de GameObject "sempre
        // ativo": se esta tela não for desativada/reativada ao trocar de ficha (ex: virou um painel
        // sempre ativo em vez de ser alternado via SetActive), OnEnable nunca dispara de novo e o
        // inventário/magias/habilidades continuam com os dados da PRIMEIRA ficha vista (ou vazios,
        // se OnEnable rodou antes de fichasDndId ser definido) — GlobalFichaData.OnSessaoDeFichaIniciada
        // dispara em TODA troca de ficha, independente do ciclo de vida do GameObject.
        GlobalFichaData.OnSessaoDeFichaIniciada += OnSessaoDeFichaIniciada;

        if (GlobalFichaData.fichasDndId > 0) StartCoroutine(RotinaCarregarFichaCompleta());
    }

    private void OnSessaoDeFichaIniciada()
    {
        if (gameObject.activeInHierarchy && GlobalFichaData.fichasDndId > 0)
            StartCoroutine(RotinaCarregarFichaCompleta());
    }

    void OnDisable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada -= OnSessaoDeFichaIniciada;

        // historiaInput/notasInput (e hp temporário/dado de vida) só salvam no onEndEdit — se a
        // tela for fechada/trocada enquanto o campo ainda está focado (ex: navegação disparada
        // por script, sem passar por um clique que desseleciona o campo antes), o onEndEdit pode
        // nunca disparar e a última edição se perde. Flush síncrono aqui garante que nada some.
        if (GlobalFichaData.fichasDndId > 0) SalvarEstadoCombateLocal();

        if (buscaMagiaInput) buscaMagiaInput.onValueChanged.RemoveAllListeners();
        if (btnBuscarMagias) btnBuscarMagias.onClick.RemoveAllListeners();
        if (btnExpandirFiltrosMagias) btnExpandirFiltrosMagias.onClick.RemoveAllListeners();
        if (btnColapsarFiltrosMagias) btnColapsarFiltrosMagias.onClick.RemoveAllListeners();

        if (toggleExpandirUtilitarios) toggleExpandirUtilitarios.onValueChanged.RemoveAllListeners();
        if (atributoConjuracaoUtilInput) atributoConjuracaoUtilInput.onEndEdit.RemoveAllListeners();
        if (cdSalvaguardaUtilInput) cdSalvaguardaUtilInput.onEndEdit.RemoveAllListeners();
        if (bonusAtaqueUtilInput) bonusAtaqueUtilInput.onEndEdit.RemoveAllListeners();

        if (buscaInventarioInput) buscaInventarioInput.onValueChanged.RemoveAllListeners();
        if (btnExpandirFiltrosInventario) btnExpandirFiltrosInventario.onClick.RemoveAllListeners();
        if (btnColapsarFiltrosInventario) btnColapsarFiltrosInventario.onClick.RemoveAllListeners();
        if (btnAdicionarItem) btnAdicionarItem.onClick.RemoveAllListeners();
        if (btnFecharAdicionarItem) btnFecharAdicionarItem.onClick.RemoveAllListeners();

        if (buscaHabilidadesInput) buscaHabilidadesInput.onValueChanged.RemoveAllListeners();
        if (btnBuscarHabilidades) btnBuscarHabilidades.onClick.RemoveAllListeners();
        if (btnExpandirFiltrosHabilidades) btnExpandirFiltrosHabilidades.onClick.RemoveAllListeners();
        if (btnColapsarFiltrosHabilidades) btnColapsarFiltrosHabilidades.onClick.RemoveAllListeners();

        if (tipoAcaoDropdown) tipoAcaoDropdown.onValueChanged.RemoveAllListeners();
        if (buscaAcoesInput) buscaAcoesInput.onValueChanged.RemoveAllListeners();
        if (btnAdicionarAcao) btnAdicionarAcao.onClick.RemoveAllListeners();
        if (btnExpandirFiltrosAcoes) btnExpandirFiltrosAcoes.onClick.RemoveAllListeners();
        if (btnColapsarFiltrosAcoes) btnColapsarFiltrosAcoes.onClick.RemoveAllListeners();

        if (btnInspiracaoMais) btnInspiracaoMais.onClick.RemoveAllListeners();
        if (btnInspiracaoMenos) btnInspiracaoMenos.onClick.RemoveAllListeners();

        if (hpAtualTxt) hpAtualTxt.onEndEdit.RemoveAllListeners();
        if (hpTemporarioInput) hpTemporarioInput.onEndEdit.RemoveAllListeners();
        if (dadoVidaInput) dadoVidaInput.onEndEdit.RemoveAllListeners();
        if (caTxt) caTxt.onEndEdit.RemoveAllListeners();
        if (deslocamentoTxt) deslocamentoTxt.onEndEdit.RemoveAllListeners();
        if (cdMagiaInput) cdMagiaInput.onEndEdit.RemoveAllListeners();
        if (bonusAtaqueMagicoInput) bonusAtaqueMagicoInput.onEndEdit.RemoveAllListeners();

        if (historiaInput) historiaInput.onEndEdit.RemoveAllListeners();
        if (notasInput) notasInput.onEndEdit.RemoveAllListeners();

        if (btnCuraTotal) btnCuraTotal.onClick.RemoveAllListeners();
        if (btnAplicarDano) btnAplicarDano.onClick.RemoveAllListeners();

        if (btnEditarFicha) btnEditarFicha.onClick.RemoveAllListeners();
        if (btnSubirNivel) btnSubirNivel.onClick.RemoveAllListeners();

        if (morteSucessoToggles != null)
            for (int i = 0; i < morteSucessoToggles.Length; i++)
                if (morteSucessoToggles[i] != null)
                    morteSucessoToggles[i].onValueChanged.RemoveAllListeners();

        if (morteFalhaToggles != null)
            for (int i = 0; i < morteFalhaToggles.Length; i++)
                if (morteFalhaToggles[i] != null)
                    morteFalhaToggles[i].onValueChanged.RemoveAllListeners();
    }

    private void IrParaCriacaoFicha(int? nivelOverride = null)
    {
        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        int generalIdAlvo = GlobalFichaData.fichasGeneralId;
        int dndIdAlvo      = GlobalFichaData.fichasDndId;

        LocalFichaStorage.TryGetFichaSnapshot(
            steamId, playerId,
            generalIdAlvo,
            dndIdAlvo,
            out LocalFichaData snap);

        if (snap != null)
        {
            // Portão central: reseta tudo primeiro, incondicionalmente (era o inverso antes —
            // este método não chamava nenhum reset, deixando resíduo de qualquer ficha visitada
            // anteriormente sobrepor os dados desta). A restauração abaixo sobrescreve por cima
            // do estado limpo com os dados reais desta ficha.
            GlobalFichaData.IniciarSessaoParaFicha(generalIdAlvo, dndIdAlvo);

            if (nivelOverride.HasValue)
            {
                snap.level_pc = nivelOverride.Value;
                GlobalFichaData.levelPersonagem = nivelOverride.Value;

                // Persiste o novo nível no disco antes de abrir a tela de criação
                LocalFichaStorage.UpdateDndFichaIdentity(
                    steamId, playerId,
                    generalIdAlvo,
                    dndIdAlvo,
                    snap.name_pc,
                    nivelOverride.Value);

                GlobalFichaData.modoSubirNivel = true;
            }
            FichaLoaderUI.CarregarRestauracaoDoSnapshot(snap);
        }

        telaFichaDnd?.SetActive(false);
        telaCriacaoFichaDnd?.SetActive(true);
    }

    private void OnClickEditarFicha() => IrParaCriacaoFicha();

    private void OnClickSubirNivel()
    {
        int nivelAtual = GlobalFichaData.levelPersonagem;
        if (nivelAtual >= 20)
        {
            PopupManager.Instance?.MostrarInfo("Nível Máximo", "Este personagem já está no nível 20.");
            return;
        }

        PopupManager.Instance?.MostrarSubirNivel(nivelAtual, onConfirmar: (novoNivel) =>
        {
            GlobalFichaData.levelPersonagem = novoNivel;
            IrParaCriacaoFicha(nivelOverride: novoNivel);
        });
    }

    private void AlterarInspiracao(int valor)
    {
        inspiracaoAtual += valor;
        if (inspiracaoAtual < 0) inspiracaoAtual = 0;
        if (inspiracaoTxt) inspiracaoTxt.text = inspiracaoAtual.ToString();
    }

    private IEnumerator RotinaCarregarFichaCompleta()
    {
        CarregarDadosFichaLocal();
        AtualizarUIBasica();
        yield return StartCoroutine(CarregarCatalogos());
        RepararItensCustomizadosContraCatalogo();
        ConfigurarFiltrosMagiaPadrao();
        FiltrarInventario();
        FiltrarMagias();
        MontarPericiasUI();
        CalcularClasseArmadura();
        CalcularCdEBonusMagico();
        AtualizarUtilitariosClasses();
    }

    // Instancia filtroChipPrefab (toggle + texto) uma vez por tag dentro de 'conteudo'.
    // Nomes/tipos vêm do array de tags definido em código — nada é montado no Inspector.
    private void PopularChipsFiltro(Transform conteudo, string[] tags, List<GameObject> chipsInstanciados, HashSet<string> tagsAtivas, Action onFiltroAlterado)
    {
        foreach (var go in chipsInstanciados) if (go != null) Destroy(go);
        chipsInstanciados.Clear();
        tagsAtivas.Clear();

        if (conteudo == null || filtroChipPrefab == null || tags == null) return;

        foreach (string tag in tags)
        {
            GameObject go = Instantiate(filtroChipPrefab, conteudo, false);
            go.SetActive(true);
            chipsInstanciados.Add(go);

            FiltroChipToggleUI chip = go.GetComponent<FiltroChipToggleUI>();
            if (chip == null) continue;

            chip.Configurar(tag, (tagAlterada, isOn) =>
            {
                if (isOn) tagsAtivas.Add(tagAlterada);
                else tagsAtivas.Remove(tagAlterada);
                onFiltroAlterado?.Invoke();
            });
        }
    }

    // Liga o par de botões expandir/colapsar ao content de filtros da aba e popula seus
    // chips a partir das tags definidas em código. Usado igualmente por Magias, Ações,
    // Inventário e Habilidades.
    private void ConfigurarExpansaoFiltro(Button btnExpandir, Button btnColapsar, Transform conteudo, string[] tags, List<GameObject> chipsInstanciados, HashSet<string> tagsAtivas, Action onFiltroAlterado)
    {
        PopularChipsFiltro(conteudo, tags, chipsInstanciados, tagsAtivas, onFiltroAlterado);

        if (conteudo != null) conteudo.gameObject.SetActive(false);
        if (btnColapsar != null) btnColapsar.gameObject.SetActive(false);
        if (btnExpandir != null) btnExpandir.gameObject.SetActive(true);

        if (btnExpandir != null)
        {
            btnExpandir.onClick.AddListener(() =>
            {
                if (conteudo != null) conteudo.gameObject.SetActive(true);
                btnExpandir.gameObject.SetActive(false);
                if (btnColapsar != null) btnColapsar.gameObject.SetActive(true);
            });
        }

        if (btnColapsar != null)
        {
            btnColapsar.onClick.AddListener(() =>
            {
                if (conteudo != null) conteudo.gameObject.SetActive(false);
                btnColapsar.gameObject.SetActive(false);
                if (btnExpandir != null) btnExpandir.gameObject.SetActive(true);
            });
        }
    }

    private void ConfigurarFiltrosMagiaPadrao()
    {
        if (buscaMagiaInput != null)
            buscaMagiaInput.SetTextWithoutNotify(string.Empty);
    }

    private void CarregarDadosFichaLocal()
    {
        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        LocalFichaData snapshot;
        bool found = LocalFichaStorage.TryGetFichaSnapshot(
            steamId, playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            out snapshot);

        if (!found || snapshot == null)
        {
            Debug.LogWarning("[FichaGeralManager] Snapshot local não encontrado para a ficha.");
            if (nomePersonagemInput) nomePersonagemInput.text = GlobalFichaData.nomePersonagem;
            return;
        }

        // --- GlobalFichaData ---
        GlobalFichaData.nomePersonagem = snapshot.name_pc;
        GlobalFichaData.levelPersonagem = snapshot.level_pc > 0 ? snapshot.level_pc : 1;
        GlobalFichaData.racaPersonagem   = snapshot.raca?.raca_nome ?? "";
        GlobalFichaData.classePersonagem = snapshot.classes?.PrimaryClass()?.class_name ?? snapshot.level_pc_detalhe ?? "";
        GlobalFichaData.forca           = snapshot.atributos.forca;
        GlobalFichaData.destreza        = snapshot.atributos.destreza;
        GlobalFichaData.constituicao    = snapshot.atributos.constituicao;
        GlobalFichaData.inteligencia    = snapshot.atributos.inteligencia;
        GlobalFichaData.sabedoria       = snapshot.atributos.sabedoria;
        GlobalFichaData.carisma         = snapshot.atributos.carisma;
        GlobalFichaData.vidaMaxima      = snapshot.vida_maxima;
        GlobalFichaData.vidaAtual       = snapshot.vida_atual > 0 ? snapshot.vida_atual : snapshot.vida_maxima;
        GlobalFichaData.deslocamento    = snapshot.deslocamento;

        // --- Cabeçalho UI ---
        if (nomePersonagemInput) nomePersonagemInput.text = snapshot.name_pc;
        if (nivelText) nivelText.text = $"Nível: {GlobalFichaData.levelPersonagem}";

        // Carrega info da classe primária a partir de snapshot.classes (novo formato)
        ClasseInfoData classeInfo = null;
        string subclasseName = "";

        bool temClassesArray = snapshot.classes != null && snapshot.classes.Count > 0;
        minhasClasses = temClassesArray ? snapshot.classes : new List<ClasseData>();
        if (temClassesArray)
        {
            var primary = snapshot.classes.PrimaryClass();
            if (primary != null)
            {
                subclasseName = primary.subclass_name ?? "";
                // Reconstrói ClasseInfoData a partir dos campos do ClasseData
                classeInfo = new ClasseInfoData
                {
                    subclass_id   = primary.subclass_id   ?? "",
                    subclass_name = primary.subclass_name ?? "",
                    saves     = primary.saves     ?? new List<string>(),
                    skills    = primary.skills    ?? new List<string>(),
                    armors    = primary.armors    ?? new List<string>(),
                    weapons   = primary.weapons   ?? new List<string>(),
                    tools     = primary.tools     ?? new List<string>(),
                    languages = primary.languages ?? new List<string>(),
                    equipment = primary.equipment ?? new List<string>(),
                    asi_feats = primary.asi_feats ?? new List<string>(),
                };
                foreach (var p in primary.class_powers)    classeInfo.class_powers.Add(new ClassPowerData { id = p.id, name = p.name, level = p.level, description = p.description });
                foreach (var p in primary.subclass_powers) classeInfo.subclass_powers.Add(new ClassPowerData { id = p.id, name = p.name, level = p.level, description = p.description });
                foreach (var c in primary.class_power_choices) classeInfo.class_power_choices.Add(new ClassPowerChoiceData { feature_id = c.feature_id, feature_name = c.feature_name, level = c.level, selected_option = c.selected_option });
            }
        }

        string classeDisplay = !string.IsNullOrWhiteSpace(snapshot.level_pc_detalhe)
            ? snapshot.level_pc_detalhe
            : snapshot.classes?.PrimaryClass()?.class_name ?? "";
        if (classeTagText) classeTagText.text = classeDisplay;
        if (racaTagText) racaTagText.text = snapshot.raca?.raca_nome ?? "";
        CarregarImagemPerfil(snapshot.token_image_path);

        // --- Grimório e Slots ---
        GlobalFichaData.magiasJson = snapshot.magias_json;
        GlobalFichaData.slotsMagiasJson = !string.IsNullOrWhiteSpace(snapshot.slots_magias_json)
            ? snapshot.slots_magias_json : "{}";
        meuGrimorio = GlobalFichaData.magiasJson?.magias ?? new List<GrimorioSaveItem>();

        ParseSlotsMagiasJson(ObterJsonSlotsMagia(snapshot));
        AtualizarTextosSlotsMagia();

        // --- Inventário ---
        GlobalFichaData.inventarioJson = snapshot.inventario_json;
        meuInventario = GlobalFichaData.inventarioJson?.itens ?? new List<InventorySaveItem>();
        _inventarioGp = GlobalFichaData.inventarioJson?.gp ?? 0f;

        // --- Subrace (lido de snapshot.raca) ---
        string subraceId = snapshot.raca?.subraca_id ?? "";
        string subraceName = snapshot.raca?.subraca_nome ?? "";
        RacaInfoSnapshot raceInfo = snapshot.raca != null ? new RacaInfoSnapshot
        {
            subrace_id   = snapshot.raca.subraca_id   ?? "",
            subrace_name = snapshot.raca.subraca_nome ?? "",
            skills    = snapshot.raca.skills    ?? new List<string>(),
            armors    = snapshot.raca.armors    ?? new List<string>(),
            weapons   = snapshot.raca.weapons   ?? new List<string>(),
            tools     = snapshot.raca.tools     ?? new List<string>(),
            languages = snapshot.raca.languages ?? new List<string>(),
        } : null;

        // --- Antecedente ---
        BackgroundInfoData bgInfo = null;
        if (!string.IsNullOrWhiteSpace(snapshot.background_info_json))
        {
            try
            {
                var bw = JsonUtility.FromJson<BackgroundInfoContainer>(snapshot.background_info_json);
                bgInfo = bw?.background_info;
            }
            catch { }
        }

        // --- Proficiências ---
        ProficienciesInfoWrapper profInfo = null;
        if (!string.IsNullOrWhiteSpace(snapshot.proficiencies_info_json))
        {
            try
            {
                var pw = JsonUtility.FromJson<ProficienciesInfoContainer>(snapshot.proficiencies_info_json);
                profInfo = pw?.proficiencies_info;
            }
            catch { }
        }
        var equipFallback = ConstruirFallbackProficiencias(classeInfo, raceInfo, bgInfo);
        if (profInfo == null || ((profInfo.skills == null || profInfo.skills.Count == 0)
            && (profInfo.saves == null || profInfo.saves.Count == 0)))
        {
            profInfo = equipFallback;
        }
        else
        {
            profInfo.armors    = equipFallback.armors;
            profInfo.weapons   = equipFallback.weapons;
            profInfo.tools     = equipFallback.tools;
            profInfo.languages = equipFallback.languages;
        }

        minhasPericias = ConstruirListaPericiasCompleta(profInfo != null ? profInfo.skills : null);

        AtualizarSalvaguardasUI(profInfo?.saves);
        AtualizarResumoLinguasEProficiencias(profInfo);

        CombateExtraData combateExtra = null;
        if (!string.IsNullOrWhiteSpace(snapshot.combate_extra_json))
        {
            try { combateExtra = JsonUtility.FromJson<CombateExtraData>(snapshot.combate_extra_json); }
            catch { }
        }
        caManualOverride = combateExtra != null ? combateExtra.ca_manual : -1;
        cdMagiaManualOverride = combateExtra != null ? combateExtra.cd_magia_manual : -1;
        bonusAtaqueMagicoManualOverride = combateExtra != null ? combateExtra.bonus_ataque_magico_manual : -1;

        suppressCombatEvents = true;
        if (hpTemporarioInput) hpTemporarioInput.text = Mathf.Max(0, snapshot.vida_temporaria).ToString();
        if (dadoVidaInput) dadoVidaInput.text = !string.IsNullOrWhiteSpace(snapshot.dado_vida) ? snapshot.dado_vida : "";

        string defesasSalvas = combateExtra != null ? (combateExtra.defesas ?? "") : "";
        if (string.IsNullOrWhiteSpace(defesasSalvas))
            defesasSalvas = ExtrairResistenciasRaciais(snapshot.raca?.raca_nome ?? "", subraceId);
        PopularDefesas(defesasSalvas);
        PopularCondicoes(combateExtra != null ? (combateExtra.condicoes ?? "") : "");
        if (historiaInput) historiaInput.text = combateExtra != null ? (combateExtra.historia ?? "") : "";
        if (notasInput) notasInput.text = combateExtra != null ? (combateExtra.notas ?? "") : "";

        AplicarTogglesMorte(morteSucessoToggles, combateExtra != null ? combateExtra.morte_sucessos : null);
        AplicarTogglesMorte(morteFalhaToggles, combateExtra != null ? combateExtra.morte_falhas : null);
        suppressCombatEvents = false;

        List<SentidoPassivoData> sentidosBase = (combateExtra != null && combateExtra.sentidos != null && combateExtra.sentidos.Count > 0)
            ? combateExtra.sentidos
            : new List<SentidoPassivoData>();
        List<SentidoPassivoData> sentidos = MergeSentidosComPassivas(sentidosBase);
        RenderSentidos(sentidos);

        // --- Ações salvas ---
        AcoesInfoData acoesInfo = null;
        if (!string.IsNullOrWhiteSpace(snapshot.acoes_info_json))
        {
            try { acoesInfo = JsonUtility.FromJson<AcoesInfoData>(snapshot.acoes_info_json); }
            catch { }
        }

        if (acoesInfo == null && !string.IsNullOrWhiteSpace(GlobalFichaData.detalhesPrincipaisJson))
        {
            try
            {
                var detalhesFallback = JsonUtility.FromJson<DetalhesPrincipaisWrapper>(GlobalFichaData.detalhesPrincipaisJson);
                if (detalhesFallback != null && detalhesFallback.acoes_info != null)
                    acoesInfo = detalhesFallback.acoes_info;
            }
            catch { }
        }

        // --- Cache de detalhes ---
        detalhesPrincipaisCache = new DetalhesPrincipaisWrapper
        {
            subrace_id        = subraceId,
            subrace_name      = subraceName,
            classe_info       = classeInfo,
            proficiencies_info = profInfo,
            background_info   = bgInfo,
            acoes_info        = acoesInfo
        };
        GlobalFichaData.detalhesPrincipaisJson = JsonUtility.ToJson(detalhesPrincipaisCache);

        // --- Habilidades de todas as classes (single e multiclasse) ---
        CarregarHabilidadesOverrides(snapshot.habilidades_overrides_json);
        habilidades.Clear();
        habilidadeOrdemAtual = 0;

        if (temClassesArray)
        {
            // Novo formato: lê poderes diretamente de cada ClasseData
            foreach (var cd in snapshot.classes)
            {
                if (cd == null) continue;
                string subLabel = string.IsNullOrWhiteSpace(cd.subclass_name) ? "Subclasse" : cd.subclass_name;

                foreach (var p in cd.class_powers ?? new List<ClassPowerEntry>())
                    AdicionarOuMesclarHabilidade(p.name, "Classe", p.level, "Classe", p.description, "Passiva", "Ilimitado");

                foreach (var p in cd.subclass_powers ?? new List<ClassPowerEntry>())
                    AdicionarOuMesclarHabilidade(p.name, "Classe", p.level, subLabel, p.description, "Passiva", "Ilimitado");

                foreach (var c in cd.class_power_choices ?? new List<ClassPowerChoiceEntry>())
                    AdicionarOuMesclarHabilidade($"{c.feature_name}: {c.selected_option}", "Classe", c.level,
                        "Escolha de Classe", $"Escolha registrada para {c.feature_name}: {c.selected_option}.", "Passiva", "Ilimitado");

                foreach (var t in cd.asi_feats ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    int nivel = ExtrairNivelDoTexto(t);
                    string categoria = t.ToUpperInvariant().Contains("TALENTO") ? "Talentos" : "Classe";
                    string nomeLimpo = LimparRotuloNivel(t);
                    if (categoria == "Classe" && TentarMesclarAsiNaHabilidadeExistente(nivel, nomeLimpo)) continue;
                    AdicionarOuMesclarHabilidade(nomeLimpo, categoria, nivel, categoria, nomeLimpo, "Passiva", "Ilimitado");
                }
            }
        }
        else if (classeInfo != null)
        {
            // Fallback legado: class_info_json (fichas antigas sem classes array)
            foreach (var p in classeInfo.class_powers ?? new List<ClassPowerData>())
                AdicionarOuMesclarHabilidade(p.name, "Classe", p.level, "Classe", p.description, "Passiva", "Ilimitado");

            foreach (var p in classeInfo.subclass_powers ?? new List<ClassPowerData>())
                AdicionarOuMesclarHabilidade(p.name, "Classe", p.level,
                    string.IsNullOrWhiteSpace(classeInfo.subclass_name) ? "Subclasse" : classeInfo.subclass_name,
                    p.description, "Passiva", "Ilimitado");

            foreach (var c in classeInfo.class_power_choices ?? new List<ClassPowerChoiceData>())
                AdicionarOuMesclarHabilidade($"{c.feature_name}: {c.selected_option}", "Classe", c.level,
                    "Escolha de Classe", $"Escolha registrada para {c.feature_name}: {c.selected_option}.", "Passiva", "Ilimitado");

            foreach (var t in classeInfo.asi_feats ?? new List<string>())
            {
                if (string.IsNullOrWhiteSpace(t)) continue;
                int nivel = ExtrairNivelDoTexto(t);
                string categoria = t.ToUpperInvariant().Contains("TALENTO") ? "Talentos" : "Classe";
                string nomeLimpo = LimparRotuloNivel(t);
                if (categoria == "Classe" && TentarMesclarAsiNaHabilidadeExistente(nivel, nomeLimpo)) continue;
                AdicionarOuMesclarHabilidade(nomeLimpo, categoria, nivel, categoria, nomeLimpo, "Passiva", "Ilimitado");
            }
        }

        CarregarHabilidadesRacaLocal(snapshot.raca?.raca_nome ?? "", subraceName);
        CarregarAcoesSalvas(acoesInfo);
        AplicarHabilidadesOverrides();
        FiltrarHabilidades();
    }

    private void AtualizarUIBasica()
    {
        bonusProficiencia = Mathf.FloorToInt((GlobalFichaData.levelPersonagem - 1) / 4f) + 2;
        if (proficienciaTxt) proficienciaTxt.text = $"+{bonusProficiencia}";

        if(forcaTxt) forcaTxt.text = GlobalFichaData.forca.ToString();
        if(destrezaTxt) destrezaTxt.text = GlobalFichaData.destreza.ToString();
        if(constituicaoTxt) constituicaoTxt.text = GlobalFichaData.constituicao.ToString();
        if(inteligenciaTxt) inteligenciaTxt.text = GlobalFichaData.inteligencia.ToString();
        if(sabedoriaTxt) sabedoriaTxt.text = GlobalFichaData.sabedoria.ToString();
        if(carismaTxt) carismaTxt.text = GlobalFichaData.carisma.ToString();

        modDestrezaAtual = Mathf.FloorToInt((GlobalFichaData.destreza - 10) / 2f);
        SetModText(forcaModTxt, Mathf.FloorToInt((GlobalFichaData.forca - 10) / 2f));
        SetModText(destrezaModTxt, modDestrezaAtual);
        SetModText(constituicaoModTxt, Mathf.FloorToInt((GlobalFichaData.constituicao - 10) / 2f));
        SetModText(inteligenciaModTxt, Mathf.FloorToInt((GlobalFichaData.inteligencia - 10) / 2f));
        SetModText(sabedoriaModTxt, Mathf.FloorToInt((GlobalFichaData.sabedoria - 10) / 2f));
        SetModText(carismaModTxt, Mathf.FloorToInt((GlobalFichaData.carisma - 10) / 2f));

        if(hpAtualTxt) hpAtualTxt.text = GlobalFichaData.vidaAtual.ToString();
        if(hpMaxTxt) hpMaxTxt.text = GlobalFichaData.vidaMaxima.ToString();
        if(deslocamentoTxt) deslocamentoTxt.SetTextWithoutNotify(GlobalFichaData.deslocamento.ToString());
        SetModText(iniciativaTxt, modDestrezaAtual); 

        AlterarInspiracao(0); 
    }

    private void SetModText(TMP_Text txt, int mod)
    {
        if (txt != null) txt.text = mod >= 0 ? $"+{mod}" : mod.ToString();
    }

    private void AtualizarTextosSlotsMagia()
    {
        if (slotsMagiaTxt == null) return;

        for (int i = 0; i < slotsMagiaTxt.Length; i++)
        {
            if (slotsMagiaTxt[i] != null)
            {
                int totalNoNivel = 0;
                if (meusSlotsPorNivel != null && meusSlotsPorNivel.TryGetValue(i + 1, out int mapValue))
                    totalNoNivel = mapValue;
                else if (meusSlots != null && meusSlots.totais != null)
                    totalNoNivel = (i < meusSlots.totais.Length) ? meusSlots.totais[i] : 0;

                slotsMagiaTxt[i].text = totalNoNivel.ToString();
            }
        }
    }

    private IEnumerator CarregarCatalogos()
    {
        CarregarCatalogoItensLocal();
        CarregarCatalogoMagiasLocal();
        yield break;
    }

    private void CarregarCatalogoItensLocal()
    {
        try
        {
            string pasta = DndSrdLocalPath.Resolver(pastaItensLocalRelativa);
            if (!Directory.Exists(pasta)) return;

            catalogoItens.Clear();

            // Existem arquivos duplicados com o mesmo id lógico (atributo id="..."), porém com o
            // <name> em capitalização diferente (ex: armas/Martelo.xml = "Martelo de Guerra" com
            // <weapon> completo, e armas/Martelo_De_Guerra.xml = "Martelo de guerra" placeholder
            // "Item importado do SRD" sem stats). Como o id numérico salvo no inventário é um hash
            // de (id lógico + nome), essa diferença de capitalização já basta para gerar hashes
            // DIFERENTES — os dois arquivos nunca colidem na mesma chave do dicionário, então a
            // checagem antiga (que só comparava contra o item já presente na MESMA chave) nunca
            // detectava esse caso: dependendo de qual variante foi usada ao montar a ficha, o
            // inventário podia acabar apontando para o placeholder sem stats, e leituras de dano
            // (ActionRecommender) ou CA simplesmente não encontravam nada para aquele item.
            //
            // Por isso o carregamento é feito em duas passadas: a primeira descobre, por id lógico,
            // qual arquivo tem os stats completos (<armor>/<weapon>); a segunda usa esses stats para
            // TODAS as variantes daquele id lógico, mesmo as com nome diferente — assim qualquer id
            // numérico antigo salvo em uma ficha continua resolvendo para dados utilizáveis.
            var arquivos = Directory.GetFiles(pasta, "*.xml", SearchOption.AllDirectories);
            var melhorXmlPorIdLogico = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (string arquivo in arquivos)
            {
                try
                {
                    string xmlProbe = File.ReadAllText(arquivo).Trim();
                    XDocument docProbe = XDocument.Parse(xmlProbe.Replace(((char)0xFEFF).ToString(), ""));
                    XElement rootProbe = docProbe.Element("item") ?? docProbe.Root;
                    string idLogicoProbe = rootProbe?.Attribute("id")?.Value;
                    if (string.IsNullOrWhiteSpace(idLogicoProbe)) continue;

                    bool temStats = xmlProbe.Contains("<armor>") || xmlProbe.Contains("<weapon>");
                    if (!temStats) continue;
                    if (melhorXmlPorIdLogico.ContainsKey(idLogicoProbe)) continue; // já achou uma versão com stats

                    melhorXmlPorIdLogico[idLogicoProbe] = xmlProbe;
                }
                catch { }
            }

            foreach (string arquivo in arquivos)
            {
                try
                {
                    string xml = File.ReadAllText(arquivo).Trim().Replace("\uFEFF", "");
                    XDocument doc = XDocument.Parse(xml);
                    XElement root = doc.Element("item") ?? doc.Root;
                    if (root == null) continue;

                    string nome = root.Element("name")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(nome)) continue;

                    string idLogico = root.Attribute("id")?.Value;
                    string xmlFinal = xml;
                    // Usa a versão com stats descoberta na 1ª passada mesmo se ESTE arquivo específico
                    // for o placeholder sem <armor>/<weapon> — garante que o id gerado a partir do nome
                    // deste arquivo sempre resolva para dados utilizáveis.
                    if (!string.IsNullOrWhiteSpace(idLogico) && melhorXmlPorIdLogico.TryGetValue(idLogico, out string xmlComStats))
                        xmlFinal = xmlComStats;

                    XElement rootFinal = xmlFinal == xml ? root : (XDocument.Parse(xmlFinal).Element("item") ?? XDocument.Parse(xmlFinal).Root);
                    string categoria = rootFinal?.Element("metadata")?.Element("category")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(categoria)) categoria = "Outros";

                    int id = GerarIdDeterministico(idLogico, nome);
                    catalogoItens[id] = new ItemData { id = id, nome = nome, categoria = categoria, xml = xmlFinal };
                }
                catch { }
            }
        }
        catch { }
    }

    private void CarregarCatalogoMagiasLocal()
    {
        try
        {
            string pasta = DndSrdLocalPath.Resolver(pastaMagiasLocalRelativa);
            if (!Directory.Exists(pasta)) return;

            catalogoMagias.Clear();

            foreach (string arquivo in Directory.GetFiles(pasta, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    string xml = File.ReadAllText(arquivo).Trim().Replace("\uFEFF", "");
                    XDocument doc = XDocument.Parse(xml);
                    XElement root = doc.Element("spell") ?? doc.Root;
                    if (root == null) continue;

                    string nome = root.Element("name")?.Value?.Trim();
                    if (string.IsNullOrWhiteSpace(nome)) continue;

                    int level = 0;
                    int.TryParse(root.Element("metadata")?.Element("level")?.Value ?? root.Element("level")?.Value, out level);

                    int id = GerarIdDeterministico(root.Attribute("id")?.Value, nome);
                    catalogoMagias[id] = new MagiaData { id = id, nome = nome, level = level, xml = xml };
                }
                catch { }
            }
        }
        catch { }
    }

    private static int GerarIdDeterministico(string idLogico, string nome)
    {
        string chave = (idLogico ?? string.Empty) + "|" + (nome ?? string.Empty);
        unchecked
        {
            int hash = 23;
            for (int i = 0; i < chave.Length; i++) hash = (hash * 31) + chave[i];
            if (hash == int.MinValue) hash = int.MaxValue;
            hash = Mathf.Abs(hash);
            return hash == 0 ? 1 : hash;
        }
    }

    // ── Reparo retroativo de itens customizados (id=-2) ────────────────────
    // Fichas salvas antes de melhorias no matching do EquipmentSelector podem ter
    // itens de equipamento de classe/antecedente que caíram no fallback "customName"
    // por não terem casado com o catálogo (ex: id da classe no singular vs plural no
    // catálogo). Esta rotina tenta re-casar esses itens comparando o texto salvo
    // (customName) com os nomes do catálogo por sobreposição de palavras, ignorando
    // artigos/preposições — e persiste o inventário corrigido de volta em disco.
    private static readonly HashSet<string> StopWordsPtBr = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "um", "uma", "uns", "umas", "de", "do", "da", "dos", "das", "e", "o", "a", "os", "as", "para", "com", "no", "na", "em"
    };

    private static HashSet<string> NormalizarEDividirPalavras(string texto)
    {
        var resultado = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(texto)) return resultado;

        string semAcento = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(semAcento.Length);
        foreach (char c in semAcento)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }

        foreach (string palavra in sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!StopWordsPtBr.Contains(palavra)) resultado.Add(palavra);
        }
        return resultado;
    }

    // Retorna o id do item do catálogo cujo nome "cabe inteiro" dentro do texto customizado
    // (todas as palavras significativas do nome do item aparecem no customName), ou -1.
    private int TentarRecasarItemCustomizado(string customName)
    {
        var palavrasCustom = NormalizarEDividirPalavras(customName);
        if (palavrasCustom.Count == 0) return -1;

        foreach (var kv in catalogoItens)
        {
            var palavrasItem = NormalizarEDividirPalavras(kv.Value.nome);
            if (palavrasItem.Count == 0) continue;
            if (palavrasItem.All(p => palavrasCustom.Contains(p)))
                return kv.Key;
        }
        return -1;
    }

    // Percorre meuInventario procurando itens id=-2 (customizados) e tenta re-casá-los
    // com o catálogo agora carregado. Persiste em disco se algo foi corrigido.
    private void RepararItensCustomizadosContraCatalogo()
    {
        if (meuInventario == null || meuInventario.Count == 0 || catalogoItens.Count == 0) return;

        bool houveReparo = false;
        foreach (var invItem in meuInventario)
        {
            if (invItem.id != -2 || string.IsNullOrWhiteSpace(invItem.customName)) continue;

            int idEncontrado = TentarRecasarItemCustomizado(invItem.customName);
            if (idEncontrado <= 0) continue;

            Debug.Log($"[FichaGeralManager] Reparo retroativo: '{invItem.customName}' -> id={idEncontrado} nome='{catalogoItens[idEncontrado].nome}'");
            invItem.id = idEncontrado;
            invItem.customName = "";
            houveReparo = true;
        }

        if (!houveReparo) return;

        InventoryWrapper invCorrigido = new InventoryWrapper { itens = meuInventario, gp = _inventarioGp };
        GlobalFichaData.inventarioJson = invCorrigido;

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        LocalFichaStorage.SaveInventory(steamId, playerId, GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, invCorrigido);
    }

    private void MontarPericiasUI()
    {
        foreach (var go in periciasInstanciadas) Destroy(go);
        periciasInstanciadas.Clear();

        if (periciaPrefab == null || periciasContent == null)
        {
            Debug.LogError(">>>[ERRO UI] O 'Pericia Prefab' ou 'Pericias Content' não foram arrastados no Inspector do script FichaGeralManager!");
            return;
        }

        string[] siglasAtributos = { "FOR", "DEX", "CON", "INT", "SAB", "CAR" };

        foreach (var skill in minhasPericias)
        {
            GameObject go = UIUtils.Spawn(periciaPrefab, periciasContent);
            go.SetActive(true);
            periciasInstanciadas.Add(go);

            int modBase = ObterModAtributoPorIndice(skill.attrIndex);
            string siglaAttr = (skill.attrIndex >= 0 && skill.attrIndex < 6)
                ? siglasAtributos[skill.attrIndex]
                : "---";

            // Não treinada: apenas o modificador do atributo base.
            // Treinada: modificador + bÃ´nus de proficiência.
            int total = skill.treinado
                ? modBase + bonusProficiencia
                : modBase;

            PericiaUIItem uiItem = go.GetComponent<PericiaUIItem>();
            if (uiItem != null) uiItem.Setup(skill.nome, $"({siglaAttr})", total, skill.treinado);
        }

        if (periciasContent.gameObject.activeInHierarchy)
            LayoutRebuilder.ForceRebuildLayoutImmediate(periciasContent.GetComponent<RectTransform>());
    }

    private void CalcularClasseArmadura()
    {
        if (caManualOverride >= 0)
        {
            if (caTxt) caTxt.SetTextWithoutNotify(caManualOverride.ToString());
            return;
        }

        int baseAc = 10;
        int bonusEscudo = 0;
        bool isHeavy = false;
        bool isMedium = false;

        foreach (var invItem in meuInventario)
        {
            if (invItem.equipSlot == "Equipado" || invItem.equipSlot == "Sintonizado")
            {
                if (catalogoItens.TryGetValue(invItem.id, out ItemData item))
                {
                    string xml = item.xml.ToLower();
                    
                    string acStr = ExtrairPropriedadeUnica(xml, "ac") ?? ExtrairPropriedadeUnica(xml, "armor-class");
                    int itemAc = 0;
                    int.TryParse(acStr, out itemAc);

                    if (xml.Contains("heavy") || xml.Contains("pesada")) {
                        if (itemAc > baseAc) { baseAc = itemAc; isHeavy = true; isMedium = false; }
                    }
                    else if (xml.Contains("medium") || xml.Contains("média")) {
                        if (itemAc > baseAc) { baseAc = itemAc; isMedium = true; isHeavy = false; }
                    }
                    else if (xml.Contains("light") || xml.Contains("leve")) {
                        if (itemAc > baseAc) { baseAc = itemAc; isHeavy = false; isMedium = false; }
                    }
                    else if (xml.Contains("shield") || xml.Contains("escudo")) {
                        // Regra de stacking: só um escudo contribui com o bônus por vez,
                        // mesmo que a validação de equipar já impeça 2 escudos equipados.
                        bonusEscudo = 2;
                    }
                }
            }
        }

        int dexAplicavel = modDestrezaAtual;
        if (isHeavy) dexAplicavel = 0;
        else if (isMedium) dexAplicavel = Mathf.Min(modDestrezaAtual, 2);

        int totalCA = baseAc + dexAplicavel + bonusEscudo;
        if (caTxt) caTxt.SetTextWithoutNotify(totalCA.ToString());
    }

    private void ObterCdEBonusMagicoAtuais(out int cd, out int bonusAtaque)
    {
        int modConjuracao = 0;
        if (!string.IsNullOrWhiteSpace(GlobalFichaData.classePersonagem)
            && AtributoConjuracaoPorClasse.TryGetValue(GlobalFichaData.classePersonagem.Trim(), out int attrIndex))
        {
            modConjuracao = ObterModAtributoPorIndice(attrIndex);
        }

        cd = cdMagiaManualOverride >= 0 ? cdMagiaManualOverride : 8 + bonusProficiencia + modConjuracao;
        bonusAtaque = bonusAtaqueMagicoManualOverride >= 0 ? bonusAtaqueMagicoManualOverride : bonusProficiencia + modConjuracao;
    }

    private void CalcularCdEBonusMagico()
    {
        ObterCdEBonusMagicoAtuais(out int cd, out int bonusAtaque);
        if (cdMagiaInput) cdMagiaInput.SetTextWithoutNotify(cd.ToString());
        if (bonusAtaqueMagicoInput) bonusAtaqueMagicoInput.SetTextWithoutNotify((bonusAtaque >= 0 ? "+" : "") + bonusAtaque);
        if (cdSalvaguardaUtilInput) cdSalvaguardaUtilInput.SetTextWithoutNotify(cd.ToString());
        if (bonusAtaqueUtilInput) bonusAtaqueUtilInput.SetTextWithoutNotify((bonusAtaque >= 0 ? "+" : "") + bonusAtaque);
    }

    // Atributo de conjuração de cada classe pelo nome (0=FOR,1=DEX,2=CON,3=INT,4=SAB,5=CAR), ou -1 se não conjuradora.
    private int ObterAttrIndexConjuracaoDaClasse(ClasseData classe)
    {
        if (classe == null || string.IsNullOrWhiteSpace(classe.class_name)) return -1;
        return AtributoConjuracaoPorClasse.TryGetValue(classe.class_name.Trim(), out int idx) ? idx : -1;
    }

    private string ObterNomeAtributoPorIndice(int idx)
    {
        switch (idx)
        {
            case 0: return "Força";
            case 1: return "Destreza";
            case 2: return "Constituição";
            case 3: return "Inteligência";
            case 4: return "Sabedoria";
            case 5: return "Carisma";
            default: return "-";
        }
    }

    private int ObterIndiceAtributoPorNome(string nome)
    {
        string n = NormalizarTexto(nome);
        if (n.Contains("for") || n.Contains("str")) return 0;
        if (n.Contains("des") || n.Contains("dex")) return 1;
        if (n.Contains("con")) return 2;
        if (n.Contains("int")) return 3;
        if (n.Contains("sab") || n.Contains("wis")) return 4;
        if (n.Contains("car") || n.Contains("cha")) return 5;
        return -1;
    }

    // Classe 1 = classe principal (is_primary, ou a primeira da lista); Classe 2 = a outra, se houver multiclasse.
    private void ObterClasses1E2(out ClasseData classe1, out ClasseData classe2)
    {
        classe1 = minhasClasses != null ? minhasClasses.PrimaryClass() : null;
        ClasseData primaria = classe1;
        classe2 = minhasClasses != null ? minhasClasses.FirstOrDefault(c => c != primaria) : null;
    }

    // Classe dominante para fins de conjuração: a classe 1, ou a classe 2 se a 1 não for conjuradora.
    private ClasseData ObterClasseConjuradoraDominante()
    {
        ObterClasses1E2(out ClasseData classe1, out ClasseData classe2);
        if (ObterAttrIndexConjuracaoDaClasse(classe1) >= 0) return classe1;
        if (ObterAttrIndexConjuracaoDaClasse(classe2) >= 0) return classe2;
        return null;
    }

    private void AtualizarUtilitariosClasses()
    {
        ObterClasses1E2(out ClasseData classe1, out ClasseData classe2);

        AplicarTextoClasseSubclasse(classeSubclasse1UtilTxt, FormatarClasseSubclasse(classe1));
        AplicarTextoClasseSubclasse(classeSubclasse2UtilTxt, FormatarClasseSubclasse(classe2));
        if (nivelClasse1UtilTxt) nivelClasse1UtilTxt.text = classe1 != null ? classe1.level.ToString() : "-";
        if (nivelClasse2UtilTxt) nivelClasse2UtilTxt.text = classe2 != null ? classe2.level.ToString() : "-";
        if (bonusConjuracao1UtilTxt) bonusConjuracao1UtilTxt.text = FormatarBonusConjuracaoClasse(classe1);
        if (bonusConjuracao2UtilTxt) bonusConjuracao2UtilTxt.text = FormatarBonusConjuracaoClasse(classe2);

        int attrIndex = ObterAttrIndexConjuracaoDaClasse(ObterClasseConjuradoraDominante());
        if (atributoConjuracaoUtilInput)
            atributoConjuracaoUtilInput.SetTextWithoutNotify(attrIndex >= 0 ? ObterNomeAtributoPorIndice(attrIndex) : "-");
    }

    private static string FormatarClasseSubclasse(ClasseData classe)
    {
        if (classe == null || string.IsNullOrWhiteSpace(classe.class_name)) return "Sem classe...";
        return string.IsNullOrWhiteSpace(classe.subclass_name) ? classe.class_name : $"{classe.class_name}/{classe.subclass_name}";
    }

    private string FormatarBonusConjuracaoClasse(ClasseData classe)
    {
        int attrIndex = ObterAttrIndexConjuracaoDaClasse(classe);
        if (attrIndex < 0) return "-";
        int bonus = bonusProficiencia + ObterModAtributoPorIndice(attrIndex);
        return (bonus >= 0 ? "+" : "") + bonus;
    }

    private void OnToggleUtilitariosChanged(bool isOn)
    {
        if (conteudoUtilitariosExpandido) conteudoUtilitariosExpandido.SetActive(isOn);
        // Ao expandir, a largura do RectTransform só fica disponível agora — recalcula o corte do texto.
        if (isOn) AtualizarUtilitariosClasses();
    }

    // Editar o atributo recalcula CD e bônus de ataque usando o modificador do atributo escolhido,
    // e persiste o resultado nos mesmos overrides manuais usados pela aba Magias (fonte única).
    private void OnAtributoConjuracaoUtilEdit(string value)
    {
        if (suppressCombatEvents) return;

        int attrIndex = ObterIndiceAtributoPorNome(value);
        if (attrIndex < 0)
        {
            AtualizarUtilitariosClasses();
            return;
        }

        int modConjuracao = ObterModAtributoPorIndice(attrIndex);
        cdMagiaManualOverride = 8 + bonusProficiencia + modConjuracao;
        bonusAtaqueMagicoManualOverride = bonusProficiencia + modConjuracao;

        if (atributoConjuracaoUtilInput) atributoConjuracaoUtilInput.SetTextWithoutNotify(ObterNomeAtributoPorIndice(attrIndex));
        CalcularCdEBonusMagico();
        SalvarEstadoCombateLocal();
        FiltrarMagias();
    }

    private void OnCdSalvaguardaUtilEdit(string value)
    {
        if (suppressCombatEvents) return;
        OnCdMagiaEdit(value);
    }

    private void OnBonusAtaqueUtilEdit(string value)
    {
        if (suppressCombatEvents) return;
        OnBonusAtaqueMagicoEdit(value);
    }

    // Faz o texto cortar com "..." em vez de estourar o layout quando não cabe na largura do campo
    // (ex: "Paladino/Juramento da Vingança" -> "Paladino/Juramento da..."). O corte é calculado manualmente
    // (em vez de usar TextOverflowModes.Ellipsis) porque o texto fica sob conteudoUtilitariosExpandido, que
    // começa desativado — nesse estado o TMP não recalcula a largura do RectTransform, e o modo Ellipsis do
    // TMP pode acabar ocultando a linha inteira (texto "invisível") em vez de só cortar.
    private void AplicarTextoClasseSubclasse(TMP_Text texto, string textoCompleto)
    {
        if (texto == null) return;
        texto.textWrappingMode = TextWrappingModes.NoWrap;
        texto.overflowMode = TextOverflowModes.Overflow;
        texto.text = ObterTextoTruncadoParaLargura(texto, textoCompleto);
    }

    private string ObterTextoTruncadoParaLargura(TMP_Text texto, string textoCompleto)
    {
        if (string.IsNullOrEmpty(textoCompleto)) return textoCompleto;

        float largura = texto.rectTransform.rect.width;
        if (largura <= 0f) return textoCompleto; // largura ainda não resolvida (objeto inativo) — mostra completo por ora

        if (texto.GetPreferredValues(textoCompleto).x <= largura) return textoCompleto;

        string cortado = textoCompleto;
        while (cortado.Length > 1)
        {
            cortado = cortado.Substring(0, cortado.Length - 1);
            string candidato = cortado.TrimEnd() + "...";
            if (texto.GetPreferredValues(candidato).x <= largura) return candidato;
        }
        return cortado + "...";
    }

    private string ExtrairPropriedadeUnica(string xml, string tag)
    {
        try {
            XDocument doc = XDocument.Parse(xml);
            return doc.Descendants(tag).FirstOrDefault()?.Value;
        } catch { return null; }
    }

    // Lê os blocos <armor> e <weapon> do xml do item para exibir CA/dano no inventário.
    private void ExtrairEstatisticasItem(string xml, out string ca, out string dano)
    {
        ca = null;
        dano = null;
        if (string.IsNullOrEmpty(xml)) return;

        try
        {
            XDocument doc = XDocument.Parse(xml);
            XElement root = doc.Root;
            if (root == null) return;

            XElement armorEl = root.Element("armor");
            string acValor = armorEl?.Element("armor-class")?.Value?.Trim();
            if (!string.IsNullOrEmpty(acValor))
            {
                ca = acValor;
            }
            else if (armorEl != null)
            {
                // Escudos não têm CA fixa: usam armor-class-bonus (ex: +2) em vez de armor-class.
                string acTexto = armorEl.Element("ac-text")?.Value?.Trim();
                string bonusCa = armorEl.Element("armor-class-bonus")?.Value?.Trim();
                if (!string.IsNullOrEmpty(acTexto)) ca = acTexto;
                else if (!string.IsNullOrEmpty(bonusCa)) ca = $"+{bonusCa}";
            }

            XElement weaponEl = root.Element("weapon");
            string danoValor = FormatarDanoArma(weaponEl?.Element("damage"));
            string danoTipo = weaponEl?.Element("damage-type")?.Value?.Trim();
            if (!string.IsNullOrEmpty(danoValor))
                dano = string.IsNullOrEmpty(danoTipo) ? danoValor : $"{danoValor} ({danoTipo})";
        }
        catch { }
    }

    // Heurística por palavras-chave: itens de cura (poções, etc.) não têm campo estruturado de
    // cura no XML, só descrição em texto livre. Pega a primeira notação de dados perto de uma
    // palavra ligada a cura — é uma aproximação, pode falhar em descrições fora do padrão.
    private static string ExtrairCuraItem(string descricaoXml)
    {
        if (string.IsNullOrEmpty(descricaoXml)) return null;

        string texto = descricaoXml.ToLowerInvariant();
        if (!texto.Contains("cura") && !texto.Contains("recupera") && !texto.Contains("restaura")) return null;

        Match dado = Regex.Match(descricaoXml, @"\d+d\d+(\s*\+\s*\d+)?");
        return dado.Success ? dado.Value : null;
    }

    private static string FormatarPesoItem(float peso) => peso.ToString("0.##", CultureInfo.InvariantCulture);

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

    // ===================================================================
    // REGRAS DE EQUIPAR: limite físico de slots (1 armadura, 1 escudo, 2 mãos,
    // 1 elmo/bota/luva/capa/amuleto, 2 anéis) e proficiência de arma/armadura
    // pela classe. Chamado antes de marcar um item do inventário como equipado.
    // ===================================================================

    private static string ObterIdLogicoItem(ItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.xml)) return "";
        try { return XDocument.Parse(item.xml).Root?.Attribute("id")?.Value ?? ""; }
        catch { return ""; }
    }

    private static string ObterSubcategoriaItem(ItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.xml)) return "";
        try { return XDocument.Parse(item.xml).Root?.Element("metadata")?.Element("subcategory")?.Value?.Trim() ?? ""; }
        catch { return ""; }
    }

    private static List<string> ObterPropriedadesArma(ItemData item)
    {
        var props = new List<string>();
        if (item == null || string.IsNullOrEmpty(item.xml)) return props;
        try
        {
            var propsEl = XDocument.Parse(item.xml).Root?.Element("weapon")?.Element("properties");
            if (propsEl != null)
                foreach (var p in propsEl.Elements("property"))
                    if (!string.IsNullOrWhiteSpace(p.Value)) props.Add(p.Value.Trim().ToLowerInvariant());
        }
        catch { }
        return props;
    }

    private static bool ItemEhArma(ItemData item)
    {
        if (item == null) return false;
        if (!string.IsNullOrEmpty(item.xml) && item.xml.Contains("<weapon>")) return true;
        return string.Equals(item.categoria?.Trim(), "arma", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ItemEhEscudo(ItemData item)
    {
        if (item == null) return false;
        string cat = item.categoria?.Trim() ?? "";
        if (cat.IndexOf("escudo", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return ObterSubcategoriaItem(item).IndexOf("escudo", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ItemEhArmaduraCorporal(ItemData item)
    {
        if (item == null || ItemEhEscudo(item)) return false;
        return !string.IsNullOrEmpty(item.xml) && item.xml.Contains("<armor>");
    }

    private static string ObterSlotDeclarado(ItemData item)
    {
        if (item == null || string.IsNullOrEmpty(item.xml)) return "";
        try { return XDocument.Parse(item.xml).Root?.Element("metadata")?.Element("slot")?.Value?.Trim()?.ToLowerInvariant() ?? ""; }
        catch { return ""; }
    }

    // Prioriza a tag <slot> do metadata quando presente no catálogo (head/neck/shoulders/
    // hands/feet/ring); só cai no chute por palavra-chave no nome quando ela não existir,
    // o que falha silenciosamente em itens cujo nome não contém a palavra óbvia.
    private static string ClassificarSlotAcessorio(ItemData item)
    {
        if (item == null) return "";

        string slotDeclarado = ObterSlotDeclarado(item);
        switch (slotDeclarado)
        {
            case "ring": return "anel";
            case "neck": return "amuleto";
            case "shoulders": return "capa";
            case "hands": return "luva";
            case "feet": return "bota";
            case "head": return "elmo";
        }

        if (string.IsNullOrWhiteSpace(item.nome)) return "";
        var palavras = NormalizarEDividirPalavras(item.nome);
        if (palavras.Contains("anel")) return "anel";
        if (palavras.Contains("amuleto") || palavras.Contains("colar") || palavras.Contains("pingente")) return "amuleto";
        if (palavras.Contains("capa") || palavras.Contains("manto")) return "capa";
        if (palavras.Contains("luva") || palavras.Contains("luvas") || palavras.Contains("manopla") || palavras.Contains("manoplas")) return "luva";
        if (palavras.Contains("bota") || palavras.Contains("botas") || palavras.Contains("sapato") || palavras.Contains("sapatos")) return "bota";
        if (palavras.Contains("elmo") || palavras.Contains("capacete") || palavras.Contains("chapeu") || palavras.Contains("coroa") || palavras.Contains("diadema")) return "elmo";
        return "";
    }

    // Custo em "mãos" para empunhar: armas de duas mãos ocupam as 2, as demais (inclusive
    // versáteis, aqui tratadas como empunhadas com 1 mão) e escudos ocupam 1 cada.
    private static int CustoMaosArma(ItemData item)
    {
        bool duasMaos = ObterPropriedadesArma(item).Any(p => p.Contains("two-handed") || p.Contains("duas maos") || p.Contains("duas-maos"));
        return duasMaos ? 2 : 1;
    }

    private static bool ArmaEhSimples(ItemData item)
    {
        string sub = ObterSubcategoriaItem(item).ToLowerInvariant();
        if (sub.Contains("simples")) return true;
        if (sub.Contains("marcial")) return false;
        return ObterPropriedadesArma(item).Any(p => p.Contains("simple"));
    }

    private static bool ArmaEhMarcial(ItemData item) => ObterSubcategoriaItem(item).ToLowerInvariant().Contains("marcial");

    private static HashSet<string> SingularizarTokens(HashSet<string> tokens)
    {
        var resultado = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string t in tokens) resultado.Add(t.Length > 1 && t.EndsWith("s") ? t[..^1] : t);
        return resultado;
    }

    // Compara a lista de proficiências de arma da classe (ex: "Simple weapons",
    // "Longswords") contra a arma sendo equipada. Sem dados suficientes para classificar
    // a arma (placeholder "importado do SRD" sem bloco <weapon>), libera por padrão —
    // bloquear sem ter certeza puniria uma lacuna de dado, não uma escolha do jogador.
    private bool TemProficienciaArma(ItemData item)
    {
        if (string.IsNullOrEmpty(item.xml) || !item.xml.Contains("<weapon>")) return true;

        var profs = detalhesPrincipaisCache?.proficiencies_info?.weapons;
        if (profs == null || profs.Count == 0) return true;

        bool ehSimples = ArmaEhSimples(item);
        bool ehMarcial = ArmaEhMarcial(item);
        var tokensArma = SingularizarTokens(NormalizarEDividirPalavras(ObterIdLogicoItem(item)));

        foreach (string prof in profs)
        {
            string profLower = prof.ToLowerInvariant();
            if (profLower.Contains("simple") && ehSimples) return true;
            if (profLower.Contains("martial") && ehMarcial) return true;

            var tokensProf = SingularizarTokens(NormalizarEDividirPalavras(prof));
            if (tokensArma.Count > 0 && tokensProf.IsSupersetOf(tokensArma)) return true;
        }
        return false;
    }

    // Mesma lógica de fallback para armadura: sem dado de tipo (leve/média/pesada) ou
    // sem lista de proficiência carregada, libera em vez de bloquear às cegas.
    private bool TemProficienciaArmadura(ItemData item)
    {
        var profs = detalhesPrincipaisCache?.proficiencies_info?.armors;
        if (profs == null || profs.Count == 0) return true;

        if (ItemEhEscudo(item))
            return profs.Any(p => p.ToLowerInvariant().Contains("shield") || p.ToLowerInvariant().Contains("escudo"));

        string xmlLower = (item.xml ?? "").ToLowerInvariant();
        bool pesada = xmlLower.Contains("heavy") || xmlLower.Contains("pesada");
        bool media = xmlLower.Contains("medium") || xmlLower.Contains("média") || xmlLower.Contains("media");
        bool leve = xmlLower.Contains("light") || xmlLower.Contains("leve");
        if (!pesada && !media && !leve) return true;

        foreach (string prof in profs)
        {
            string p = prof.ToLowerInvariant();
            if (pesada && (p.Contains("heavy") || p.Contains("pesada"))) return true;
            if (media && (p.Contains("medium") || p.Contains("media"))) return true;
            if (leve && (p.Contains("light") || p.Contains("leve"))) return true;
        }
        return false;
    }

    private int ContarEquipados(Func<ItemData, bool> filtro, string excluirInstanceId)
    {
        int total = 0;
        foreach (var inv in meuInventario)
        {
            if (inv.instanceId == excluirInstanceId) continue;
            if (inv.equipSlot != "Equipado" && inv.equipSlot != "Sintonizado") continue;
            if (inv.id <= 0 || !catalogoItens.TryGetValue(inv.id, out ItemData db)) continue;
            if (filtro(db)) total++;
        }
        return total;
    }

    private int MaosOcupadasAtuais(string excluirInstanceId)
    {
        int maos = 0;
        foreach (var inv in meuInventario)
        {
            if (inv.instanceId == excluirInstanceId) continue;
            if (inv.equipSlot != "Equipado" && inv.equipSlot != "Sintonizado") continue;
            if (inv.id <= 0 || !catalogoItens.TryGetValue(inv.id, out ItemData db)) continue;
            if (ItemEhEscudo(db)) maos += 1;
            else if (ItemEhArma(db)) maos += CustoMaosArma(db);
        }
        return maos;
    }

    // Valida senso comum corporal (1 armadura, 1 escudo, mãos ocupadas, slots de
    // anel/amuleto/capa/luva/bota/elmo) e proficiência da classe antes de permitir
    // marcar um item do inventário como equipado.
    private bool PodeEquipar(InventorySaveItem invItem, ItemData dbItem, out string motivoBloqueio)
    {
        motivoBloqueio = "";
        if (dbItem == null) return true; // item customizado/desconhecido: sem regra física pra validar

        if (ItemEhArmaduraCorporal(dbItem))
        {
            if (ContarEquipados(ItemEhArmaduraCorporal, invItem.instanceId) >= 1)
            {
                motivoBloqueio = "Você só pode vestir uma armadura no corpo por vez. Desequipe a atual antes de vestir outra.";
                return false;
            }
            if (!TemProficienciaArmadura(dbItem))
            {
                motivoBloqueio = "Sua classe não tem proficiência com este tipo de armadura.";
                return false;
            }
            return true;
        }

        if (ItemEhEscudo(dbItem))
        {
            if (ContarEquipados(ItemEhEscudo, invItem.instanceId) >= 1)
            {
                motivoBloqueio = "Você só recebe o bônus de um escudo por vez. Desequipe o escudo atual antes de usar outro.";
                return false;
            }
            if (MaosOcupadasAtuais(invItem.instanceId) + 1 > 2)
            {
                motivoBloqueio = "Suas mãos já estão ocupadas. Desequipe uma arma antes de usar um escudo.";
                return false;
            }
            if (!TemProficienciaArmadura(dbItem))
            {
                motivoBloqueio = "Sua classe não tem proficiência com escudos.";
                return false;
            }
            return true;
        }

        if (ItemEhArma(dbItem))
        {
            int custo = CustoMaosArma(dbItem);
            if (MaosOcupadasAtuais(invItem.instanceId) + custo > 2)
            {
                motivoBloqueio = custo == 2
                    ? "Esta arma exige as duas mãos. Desequipe outras armas/escudo antes de usá-la."
                    : "Suas mãos já estão ocupadas com outras armas/escudo. Desequipe algo antes de equipar esta.";
                return false;
            }
            if (!TemProficienciaArma(dbItem))
            {
                motivoBloqueio = "Sua classe não tem proficiência com esta arma.";
                return false;
            }
            return true;
        }

        string slot = ClassificarSlotAcessorio(dbItem);
        if (!string.IsNullOrEmpty(slot))
        {
            int limite = slot == "anel" ? 2 : 1;
            if (ContarEquipados(i => ClassificarSlotAcessorio(i) == slot, invItem.instanceId) >= limite)
            {
                string rotulo = slot switch
                {
                    "anel" => "anéis (máximo 2, um em cada mão)",
                    "amuleto" => "amuleto/colar",
                    "capa" => "capa/manto",
                    "luva" => "luvas/manoplas",
                    "bota" => "botas",
                    "elmo" => "elmo/capacete",
                    _ => "este tipo de item"
                };
                motivoBloqueio = $"Você já está usando o limite de {rotulo}. Desequipe um antes de usar outro.";
                return false;
            }
            return true;
        }

        return true;
    }

    private void SalvarInventarioAtual()
    {
        // DataManager.Instance é usado logo abaixo (GetCurrentId) — evita a NRE se não existir.
        if (DataManager.Instance == null) return;

        InventoryWrapper inv = new InventoryWrapper { itens = meuInventario, gp = _inventarioGp };
        GlobalFichaData.inventarioJson = inv;

        int playerId = DataManager.Instance.GetCurrentId();
        string steamId = DataManager.Instance.GetCurrentSteamId();
        LocalFichaStorage.SaveInventory(steamId, playerId, GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, inv);
    }

    /// <summary>
    /// Ponto de entrada público para o painel "Adicionar Item" (EquipmentSelector em modo
    /// delegado) inserir um item comprado/escolhido diretamente no inventário desta ficha.
    /// O painel não tem acesso direto a meuInventario, então ele só manda o id do catálogo e a
    /// quantidade, e este método monta, salva (síncrono, rápido, mesmo padrão de
    /// SalvarInventarioAtual) e já atualiza a lista renderizada. Retorna o instanceId gerado para
    /// o painel poder pedir a remoção do MESMO item depois, se o jogador desfizer.
    /// </summary>
    public string AdicionarItemAoInventario(int itemId, int quantidade = 1)
    {
        string instanceId = Guid.NewGuid().ToString();
        meuInventario.Add(new InventorySaveItem
        {
            instanceId = instanceId,
            id = itemId,
            equipSlot = "Desequipado",
            quantidade = Mathf.Max(1, quantidade)
        });

        SalvarInventarioAtual();
        FiltrarInventario();
        return instanceId;
    }

    /// <summary>
    /// Contraparte de AdicionarItemAoInventario — usada pelo painel "Adicionar Item" quando o
    /// jogador desfaz uma adição (duplo clique no item recém-adicionado dentro do próprio painel).
    /// </summary>
    public void RemoverItemDoInventario(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return;
        meuInventario.RemoveAll(i => i.instanceId == instanceId);
        SalvarInventarioAtual();
        FiltrarInventario();
    }

    // Cada AdicionarItemAoInventario já salva na hora, então "confirmar" aqui é só garantir que
    // a lista principal reflete tudo que foi adicionado antes de fechar o painel.
    private void FecharPainelAdicionarItem()
    {
        if (painelAdicionarItem) painelAdicionarItem.SetActive(false);
        FiltrarInventario();
    }

    private void FiltrarInventario()
    {
        try
        {
            if (inventarioPrefab == null || inventarioContent == null) {
                Debug.LogError(">>>[ERRO UI] O 'Inventario Prefab' ou 'Inventario Content' não foram arrastados no Inspector do script FichaGeralManager!");
                return;
            }

            foreach (var go in itensInstanciados) Destroy(go);
            itensInstanciados.Clear();

            string filtroTexto = buscaInventarioInput ? buscaInventarioInput.text.Trim().ToLower() : "";
            var tagsCategoriaAtivas = tagsAtivasInventario.Where(t => t != "Equipado").ToList();
            bool apenasEquipados = tagsAtivasInventario.Contains("Equipado");

            foreach (var invItem in meuInventario)
            {
                if (invItem.id == -999) continue;

                string itemName = "";
                string itemCat = "Outros";
                float pesoIndividual = 0f;
                bool temPeso = false;
                string itemCa = null;
                string itemDano = null;
                string xmlDesc = "";
                string itemCura = null;

                if (catalogoItens.TryGetValue(invItem.id, out ItemData dbItem))
                {
                    itemName = string.IsNullOrEmpty(invItem.customName) ? dbItem.nome : invItem.customName;
                    itemCat = string.IsNullOrEmpty(dbItem.categoria) ? "Outros" : dbItem.categoria.Trim();
                    string pesoNum = ExtrairPropriedadeUnica(dbItem.xml, "weight");
                    temPeso = float.TryParse(pesoNum, NumberStyles.Float, CultureInfo.InvariantCulture, out pesoIndividual);
                    xmlDesc = ExtrairDescricaoXML(dbItem.xml);
                    ExtrairEstatisticasItem(dbItem.xml, out itemCa, out itemDano);
                    itemCura = ExtrairCuraItem(xmlDesc);
                }
                else
                {
                    itemName = string.IsNullOrEmpty(invItem.customName) ? "Item Desconhecido" : invItem.customName;
                    xmlDesc = "Item personalizado da ficha. Não possui atributos extras.";
                }

                bool equipado = invItem.equipSlot == "Equipado" || invItem.equipSlot == "Sintonizado";
                int quantidade = Mathf.Max(1, invItem.quantidade);

                if (!string.IsNullOrEmpty(filtroTexto) && !itemName.ToLower().Contains(filtroTexto)) continue;
                if (apenasEquipados && !equipado) continue;

                string itemTipo = char.ToUpperInvariant(itemCat[0]) + itemCat.Substring(1);
                if (tagsCategoriaAtivas.Count > 0 && !tagsCategoriaAtivas.Contains(itemTipo)) continue;

                GameObject go = Instantiate(inventarioPrefab, inventarioContent, false);
                go.SetActive(true);
                itensInstanciados.Add(go);

                string caFinal = !string.IsNullOrEmpty(invItem.caOverride) ? invItem.caOverride : (!string.IsNullOrEmpty(itemCa) ? $"CA {itemCa}" : "");
                string danoFinal = string.IsNullOrEmpty(invItem.danoOverride) ? (itemDano ?? "") : invItem.danoOverride;
                string curaFinal = string.IsNullOrEmpty(invItem.curaOverride) ? (itemCura ?? "") : invItem.curaOverride;
                string descFinal = string.IsNullOrEmpty(invItem.descricaoOverride) ? xmlDesc : invItem.descricaoOverride;
                string pesoFinal = temPeso
                    ? $"{FormatarPesoItem(pesoIndividual)}w/{FormatarPesoItem(pesoIndividual * quantidade)}w"
                    : "-";

                InventarioUIItem uiItem = go.GetComponent<InventarioUIItem>();
                if (uiItem != null)
                {
                    uiItem.Setup(itemName, itemTipo, pesoFinal, quantidade, equipado, (novoEquipado) =>
                    {
                        if (novoEquipado && !PodeEquipar(invItem, dbItem, out string motivoBloqueio))
                        {
                            PopupManager.Instance?.MostrarInfo("Não é possível equipar", motivoBloqueio);
                            FiltrarInventario();
                            return;
                        }

                        invItem.equipSlot = novoEquipado ? "Equipado" : "Desequipado";
                        CalcularClasseArmadura();
                        SalvarInventarioAtual();
                        if (apenasEquipados) FiltrarInventario();
                    }, (novaQuantidade) =>
                    {
                        invItem.quantidade = Mathf.Max(1, novaQuantidade);
                        SalvarInventarioAtual();
                        FiltrarInventario();
                    }, caFinal, danoFinal, curaFinal, descFinal);

                    uiItem.RegistrarAutoSave(() =>
                    {
                        var dados = uiItem.ObterDadosAtuais();
                        invItem.caOverride = dados.ca;
                        invItem.danoOverride = dados.dano;
                        invItem.curaOverride = dados.cura;
                        invItem.descricaoOverride = dados.descricao;
                        SalvarInventarioAtual();
                    });
                }

                ConfigurarHoverTooltip(go, itemName, itemCat, descFinal);
            }

            if (inventarioContent.gameObject.activeInHierarchy) {
                LayoutRebuilder.ForceRebuildLayoutImmediate(inventarioContent.GetComponent<RectTransform>());
            }
        }
        catch (Exception e)
        {
            Debug.LogError(">>>[ERRO UI] Falha ao renderizar o inventário: " + e.Message);
        }
    }

    private void FiltrarMagias()
    {
        if (magiaNivelSeparadorPrefab == null || magiasContent == null)
        {
            Debug.LogError(">>>[ERRO UI] O 'Magia Nivel Separador Prefab' ou 'Magias Content' não foram arrastados no Inspector do script FichaGeralManager!");
            return;
        }

        foreach (var go in magiasInstanciadas) Destroy(go);
        magiasInstanciadas.Clear();
        bool houveAjustePreparacao = AplicarLimitePreparacaoPorSlots();

        string filtroTexto = buscaMagiaInput ? buscaMagiaInput.text.Trim().ToLower() : "";
        var tagsNivelAtivas = tagsAtivasMagias.Where(t => TagsNivelMagia.Contains(t)).ToList();
        bool soPreparadas = tagsAtivasMagias.Contains("Preparada");

        // Agrupa por nível (0 = truque) para intercalar o separador de nível entre os grupos.
        // Magias fora do catálogo local não têm nível conhecido e ficam à parte, sem separador.
        var visiveisPorNivel = new SortedDictionary<int, List<GrimorioSaveItem>>();
        var semCatalogo = new List<GrimorioSaveItem>();

        foreach (var splItem in meuGrimorio)
        {
            if (soPreparadas && !splItem.preparada) continue;

            if (catalogoMagias.TryGetValue(splItem.id, out MagiaData dbMagia))
            {
                string nv = dbMagia.level == 0 ? "Truque" : $"Nível {dbMagia.level}";

                if (tagsNivelAtivas.Count > 0 && !tagsNivelAtivas.Contains(nv)) continue;
                if (!string.IsNullOrEmpty(filtroTexto) && !dbMagia.nome.ToLower().Contains(filtroTexto)) continue;

                if (!visiveisPorNivel.TryGetValue(dbMagia.level, out var lista))
                {
                    lista = new List<GrimorioSaveItem>();
                    visiveisPorNivel[dbMagia.level] = lista;
                }
                lista.Add(splItem);
            }
            else
            {
                if (tagsNivelAtivas.Count > 0) continue;

                string nomeFallback = $"Magia #{splItem.id}";
                if (!string.IsNullOrEmpty(filtroTexto) && !nomeFallback.ToLower().Contains(filtroTexto)) continue;

                semCatalogo.Add(splItem);
            }
        }

        foreach (var kv in visiveisPorNivel)
        {
            int nivel = kv.Key;
            string label = nivel.ToString();
            MagiaNivelSeparadorUI separadorUI = InstanciarSeparadorGrupoMagia(nivel, label, comEspacos: nivel > 0);

            foreach (var splItem in kv.Value)
                InstanciarItemMagia(splItem, catalogoMagias[splItem.id], separadorUI);
        }

        if (semCatalogo.Count > 0)
        {
            MagiaNivelSeparadorUI separadorFallback = InstanciarSeparadorGrupoMagia(ChaveGrupoMagiaSemCatalogo, "?", comEspacos: false);
            foreach (var splItem in semCatalogo)
                InstanciarItemMagiaFallback(splItem, separadorFallback);
        }

        if (houveAjustePreparacao)
            SalvarEstadoMagiasLocal();

        if (magiasContent.gameObject.activeInHierarchy) {
            LayoutRebuilder.ForceRebuildLayoutImmediate(magiasContent.GetComponent<RectTransform>());
        }
    }

    // Chave usada em niveisMagiaColapsados para o grupo de magias fora do catálogo local — fora da
    // faixa de níveis reais (0-9), então não colide com nenhum nível de verdade.
    private const int ChaveGrupoMagiaSemCatalogo = -1;

    private void InstanciarItemMagia(GrimorioSaveItem splItem, MagiaData dbMagia, MagiaNivelSeparadorUI separadorUI)
    {
        if (separadorUI == null) return;
        GameObject go = separadorUI.InstanciarItemMagia();
        if (go == null) return;
        magiasInstanciadas.Add(go);

        bool isTruque = dbMagia.level == 0;
        string nv = isTruque ? "Truque" : $"Nível {dbMagia.level}";

        MagiaUIItem uiItem = go.GetComponent<MagiaUIItem>();
        string nomeExibido = string.IsNullOrEmpty(splItem.nomeOverride) ? dbMagia.nome : splItem.nomeOverride;
        string nivelExibido = string.IsNullOrEmpty(splItem.nivelOverride) ? nv : splItem.nivelOverride;
        string tempoExibido = string.IsNullOrEmpty(splItem.tempoOverride) ? ExtrairTempoConjuracaoMagiaXML(dbMagia.xml) : splItem.tempoOverride;
        string alcanceExibido = string.IsNullOrEmpty(splItem.alcanceOverride) ? ExtrairAlcanceMagiaXML(dbMagia.xml) : splItem.alcanceOverride;
        string duracaoExibida = string.IsNullOrEmpty(splItem.duracaoOverride) ? ExtrairDuracaoMagiaXML(dbMagia.xml) : splItem.duracaoOverride;
        string componentesExibidos = string.IsNullOrEmpty(splItem.componentesOverride) ? ExtrairComponentesMagiaXML(dbMagia.xml) : splItem.componentesOverride;
        string descricaoMagia = string.IsNullOrEmpty(splItem.descricaoOverride) ? ExtrairDescricaoXML(dbMagia.xml) : splItem.descricaoOverride;
        if (uiItem != null)
        {
            bool estadoInicial = isTruque ? true : splItem.preparada;
            uiItem.Setup(
                nomeExibido,
                nivelExibido,
                tempoExibido,
                alcanceExibido,
                duracaoExibida,
                componentesExibidos,
                estadoInicial,
                (novoValor) => OnMagiaPreparacaoAlterada(splItem, novoValor),
                isTruque,
                descricaoMagia);
            uiItem.RegistrarAutoSave(() =>
            {
                var dados = uiItem.ObterDadosAtuais();
                splItem.nomeOverride = dados.nome;
                splItem.nivelOverride = dados.nivel;
                splItem.tempoOverride = dados.tempo;
                splItem.alcanceOverride = dados.alcance;
                splItem.duracaoOverride = dados.duracao;
                splItem.componentesOverride = dados.componentes;
                splItem.descricaoOverride = dados.descricao;
                SalvarEstadoMagiasLocal();
            });
        }

        string subtituloTooltip = $"{nivelExibido} | {tempoExibido} | {alcanceExibido} | {duracaoExibida} | {componentesExibidos}";
        ConfigurarHoverTooltip(go, nomeExibido, subtituloTooltip, descricaoMagia);
    }

    private void InstanciarItemMagiaFallback(GrimorioSaveItem splItem, MagiaNivelSeparadorUI separadorUI)
    {
        if (separadorUI == null) return;
        GameObject go = separadorUI.InstanciarItemMagia();
        if (go == null) return;
        magiasInstanciadas.Add(go);

        MagiaUIItem uiItem = go.GetComponent<MagiaUIItem>();
        string nomeFallbackExibido = string.IsNullOrEmpty(splItem.nomeOverride) ? $"Magia #{splItem.id}" : splItem.nomeOverride;
        string nivelFallbackExibido = string.IsNullOrEmpty(splItem.nivelOverride) ? "Nível ?" : splItem.nivelOverride;
        string tempoFallbackExibido = string.IsNullOrEmpty(splItem.tempoOverride) ? "-" : splItem.tempoOverride;
        string alcanceFallbackExibido = string.IsNullOrEmpty(splItem.alcanceOverride) ? "-" : splItem.alcanceOverride;
        string duracaoFallbackExibida = string.IsNullOrEmpty(splItem.duracaoOverride) ? "-" : splItem.duracaoOverride;
        string componentesFallbackExibidos = string.IsNullOrEmpty(splItem.componentesOverride) ? "-" : splItem.componentesOverride;
        string descricaoFallback = string.IsNullOrEmpty(splItem.descricaoOverride) ? "Catálogo indisponível para esta magia." : splItem.descricaoOverride;
        if (uiItem != null)
        {
            uiItem.Setup(
                nomeFallbackExibido,
                nivelFallbackExibido,
                tempoFallbackExibido,
                alcanceFallbackExibido,
                duracaoFallbackExibida,
                componentesFallbackExibidos,
                splItem.preparada,
                (novoValor) => OnMagiaPreparacaoAlterada(splItem, novoValor),
                false,
                descricaoFallback);
            uiItem.RegistrarAutoSave(() =>
            {
                var dados = uiItem.ObterDadosAtuais();
                splItem.nomeOverride = dados.nome;
                splItem.nivelOverride = dados.nivel;
                splItem.tempoOverride = dados.tempo;
                splItem.alcanceOverride = dados.alcance;
                splItem.duracaoOverride = dados.duracao;
                splItem.componentesOverride = dados.componentes;
                splItem.descricaoOverride = dados.descricao;
                SalvarEstadoMagiasLocal();
            });
        }

        string subtituloFallbackTooltip = $"{nivelFallbackExibido} | {tempoFallbackExibido} | {alcanceFallbackExibido} | {duracaoFallbackExibida} | {componentesFallbackExibidos}";
        ConfigurarHoverTooltip(go, nomeFallbackExibido, subtituloFallbackTooltip, descricaoFallback);
    }

    // Separador entre os grupos de magias (por nível, Truque ou fora do catálogo local): além de
    // dividir visualmente, agora também é quem instancia os itens de magia dentro do próprio content
    // (o Manager não guarda mais um prefab de magia solto) e quem anima a expansão/colapso do grupo.
    // "comEspacos" desliga o cálculo de espaços de magia para Truque e para o grupo sem catálogo,
    // que não consomem espaços de magia.
    private MagiaNivelSeparadorUI InstanciarSeparadorGrupoMagia(int nivel, string label, bool comEspacos)
    {
        if (magiaNivelSeparadorPrefab == null) return null;

        GameObject go = Instantiate(magiaNivelSeparadorPrefab, magiasContent, false);
        go.SetActive(true);
        magiasInstanciadas.Add(go);

        MagiaNivelSeparadorUI separadorUI = go.GetComponent<MagiaNivelSeparadorUI>();
        if (separadorUI == null) return null;

        int espacosTotais = comEspacos ? ObterLimiteSlotsPorNivel(nivel) : 0;
        int espacosAtuais = comEspacos && meusSlotsGastosPorNivel.TryGetValue(nivel, out int gastos)
            ? Mathf.Clamp(gastos, 0, espacosTotais)
            : 0;
        ObterCdEBonusMagicoAtuais(out int cd, out int bonusAtaque);
        string bonusTexto = (bonusAtaque >= 0 ? "+" : "") + bonusAtaque;
        bool expandido = !niveisMagiaColapsados.Contains(nivel);

        separadorUI.Setup(
            label,
            espacosAtuais,
            espacosTotais,
            cd.ToString(),
            bonusTexto,
            expandido,
            (novoValor) =>
            {
                if (!comEspacos) return;
                meusSlotsGastosPorNivel[nivel] = Mathf.Clamp(novoValor, 0, espacosTotais);
                SalvarEstadoMagiasLocal();
            },
            OnCdMagiaEdit,
            OnBonusAtaqueMagicoEdit,
            (novoExpandido) =>
            {
                // Só guarda o estado para a próxima reconstrução da lista — o próprio separador já
                // anima a transição, então não reconstruímos a lista aqui (isso cortaria a animação).
                if (novoExpandido) niveisMagiaColapsados.Remove(nivel);
                else niveisMagiaColapsados.Add(nivel);
            });

        return separadorUI;
    }

    private void OnMagiaPreparacaoAlterada(GrimorioSaveItem splItem, bool preparada)
    {
        if (splItem == null) return;

        bool isTruque = false;
        int spellLevel = -1;
        if (catalogoMagias.TryGetValue(splItem.id, out MagiaData dbMagia))
        {
            spellLevel = dbMagia.level;
            isTruque = spellLevel == 0;
        }

        if (!isTruque && preparada && spellLevel > 0)
        {
            int limiteNivel = ObterLimiteSlotsPorNivel(spellLevel);
            int jaPreparadasNoNivel = ContarMagiasPreparadasNoNivel(spellLevel, splItem);
            if (limiteNivel <= 0 || jaPreparadasNoNivel >= limiteNivel)
            {
                splItem.preparada = false;
                FiltrarMagias();
                Debug.LogWarning($"[Magias] Limite de magias preparadas atingido para o nível {spellLevel} ({limiteNivel}).");
                return;
            }
        }

        splItem.preparada = isTruque ? true : preparada;
        SalvarEstadoMagiasLocal();
    }

    private bool AplicarLimitePreparacaoPorSlots()
    {
        if (meuGrimorio == null || meuGrimorio.Count == 0) return false;

        bool alterou = false;
        Dictionary<int, int> preparadosPorNivel = new Dictionary<int, int>();

        foreach (var splItem in meuGrimorio)
        {
            if (splItem == null) continue;
            if (!catalogoMagias.TryGetValue(splItem.id, out MagiaData dbMagia)) continue;

            int nivel = dbMagia.level;
            if (nivel == 0)
            {
                if (!splItem.preparada)
                {
                    splItem.preparada = true;
                    alterou = true;
                }
                continue;
            }

            if (!splItem.preparada) continue;

            int limite = ObterLimiteSlotsPorNivel(nivel);
            if (limite <= 0)
            {
                splItem.preparada = false;
                alterou = true;
                continue;
            }

            if (!preparadosPorNivel.ContainsKey(nivel)) preparadosPorNivel[nivel] = 0;

            if (preparadosPorNivel[nivel] >= limite)
            {
                splItem.preparada = false;
                alterou = true;
                continue;
            }

            preparadosPorNivel[nivel]++;
        }

        return alterou;
    }

    private int ObterLimiteSlotsPorNivel(int nivel)
    {
        if (nivel <= 0) return int.MaxValue;

        if (meusSlotsPorNivel != null && meusSlotsPorNivel.TryGetValue(nivel, out int mapValue))
            return Mathf.Max(0, mapValue);

        if (meusSlots != null && meusSlots.totais != null)
        {
            int idx = nivel - 1;
            if (idx >= 0 && idx < meusSlots.totais.Length)
                return Mathf.Max(0, meusSlots.totais[idx]);
        }

        return 0;
    }

    private int ContarMagiasPreparadasNoNivel(int nivel, GrimorioSaveItem ignorar)
    {
        if (meuGrimorio == null || meuGrimorio.Count == 0) return 0;

        int total = 0;
        foreach (var item in meuGrimorio)
        {
            if (item == null || ReferenceEquals(item, ignorar)) continue;
            if (!item.preparada) continue;
            if (!catalogoMagias.TryGetValue(item.id, out MagiaData dbMagia)) continue;
            if (dbMagia.level == nivel) total++;
        }

        return total;
    }

    private void SalvarEstadoMagiasLocal()
    {
        if (GlobalFichaData.fichasDndId <= 0) return;

        var wrapper = new GrimorioWrapper
        {
            magias = meuGrimorio ?? new List<GrimorioSaveItem>()
        };

        string slotsJson = ConstruirSlotsMagiasJson();

        GlobalFichaData.magiasJson = wrapper;
        GlobalFichaData.slotsMagiasJson = slotsJson;

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        LocalFichaStorage.SaveSpells(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            slotsJson,
            wrapper);
    }

    // Sempre grava no formato totais+gastos (índices 0..8 = níveis 1..9), preservando os totais
    // herdados da classe e incluindo os gastos que o jogador ajusta manualmente no separador de nível.
    private string ConstruirSlotsMagiasJson()
    {
        int[] totais = new int[9];
        int[] gastos = new int[9];

        for (int nivel = 1; nivel <= 9; nivel++)
        {
            totais[nivel - 1] = ObterLimiteSlotsPorNivel(nivel);
            gastos[nivel - 1] = meusSlotsGastosPorNivel.TryGetValue(nivel, out int g) ? Mathf.Max(0, g) : 0;
        }

        return JsonUtility.ToJson(new SlotsWrapper { totais = totais, gastos = gastos });
    }

    private void ConfigurarHoverTooltip(GameObject go, string titulo, string subtitulo, string descricao)
    {
        EventTrigger trigger = go.GetComponent<EventTrigger>();
        if (trigger == null) trigger = go.AddComponent<EventTrigger>();

        EventTrigger.Entry entryEnter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entryEnter.callback.AddListener((data) => {
            MostrarTooltip(titulo, subtitulo, descricao);
        });
        trigger.triggers.Add(entryEnter);

        EventTrigger.Entry entryExit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        entryExit.callback.AddListener((data) => {
            EsconderTooltip();
        });
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

    private string ExtrairDescricaoXML(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "Sem detalhes.";
        try {
            XDocument doc = XDocument.Parse(xml);
            return doc.Descendants("description").FirstOrDefault()?.Value ?? doc.Descendants("summary").FirstOrDefault()?.Value ?? "Sem detalhes no XML.";
        } catch { return "Erro ao ler detalhes."; }
    }

    private string ExtrairTempoConjuracaoMagiaXML(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "-";
        try {
            XDocument doc = XDocument.Parse(xml);
            string valor = doc.Descendants("casting-time").FirstOrDefault()?.Value;
            return string.IsNullOrWhiteSpace(valor) ? "-" : valor;
        } catch { return "-"; }
    }

    private string ExtrairAlcanceMagiaXML(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "-";
        try {
            XDocument doc = XDocument.Parse(xml);
            string valor = doc.Descendants("range").FirstOrDefault()?.Value;
            return string.IsNullOrWhiteSpace(valor) ? "-" : valor;
        } catch { return "-"; }
    }

    private string ExtrairDuracaoMagiaXML(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "-";
        try {
            XDocument doc = XDocument.Parse(xml);
            string valor = doc.Descendants("duration").FirstOrDefault()?.Value;
            return string.IsNullOrWhiteSpace(valor) ? "-" : valor;
        } catch { return "-"; }
    }

    // Componentes V/S/M vêm do XML da magia; C (Concentração) e R (Ritual) são inferidos a partir do
    // texto de duração/tempo de conjuração, já que o XML local não marca esses dois explicitamente.
    private string ExtrairComponentesMagiaXML(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "-";
        try
        {
            XDocument doc = XDocument.Parse(xml);
            var letras = new List<string>();

            foreach (var comp in doc.Descendants("component"))
            {
                string tipo = comp.Attribute("type")?.Value?.ToLowerInvariant() ?? "";
                if (tipo == "verbal") letras.Add("V");
                else if (tipo == "somatic") letras.Add("S");
                else if (tipo == "material") letras.Add("M");
            }

            string duracao = doc.Descendants("duration").FirstOrDefault()?.Value ?? "";
            if (duracao.IndexOf("concentration", StringComparison.OrdinalIgnoreCase) >= 0) letras.Add("C");

            string tempo = doc.Descendants("casting-time").FirstOrDefault()?.Value ?? "";
            if (tempo.IndexOf("ritual", StringComparison.OrdinalIgnoreCase) >= 0) letras.Add("R");

            return letras.Count == 0 ? "-" : string.Join(", ", letras);
        }
        catch { return "-"; }
    }

    private string ExtrairTipoAcaoXML(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "Passiva";
        try {
            XDocument doc = XDocument.Parse(xml);
            string action = doc.Descendants("action").FirstOrDefault()?.Value;
            return string.IsNullOrEmpty(action) ? "Passiva" : action;
        } catch { return "Passiva"; }
    }

    private string ExtrairRecargaXML(string xml)
    {
        if (string.IsNullOrEmpty(xml)) return "Ilimitado";
        try {
            XDocument doc = XDocument.Parse(xml);
            string reset = doc.Descendants("reset").FirstOrDefault()?.Value;
            return string.IsNullOrEmpty(reset) ? "Ilimitado" : reset;
        } catch { return "Ilimitado"; }
    }

    // Heurística por palavras-chave: classifica a habilidade em uma palavra que resume seu efeito (Dano, Cura, Buff, etc.).
    private string ClassificarAcaoResumo(string nome, string descricao)
    {
        string texto = NormalizarTexto((nome ?? "") + " " + (descricao ?? ""));
        if (string.IsNullOrEmpty(texto)) return "Utilidade";

        if (Regex.IsMatch(texto, @"\d+d\d+") || texto.Contains("dano") || texto.Contains("ataque"))
            return "Dano";
        if (texto.Contains("cura") || texto.Contains("recupera") && texto.Contains("pontos de vida"))
            return "Cura";
        if (texto.Contains("desvantagem") || texto.Contains("reduz") || texto.Contains("enfraquece"))
            return "Debuff";
        if (texto.Contains("vantagem") || texto.Contains("bonus") || texto.Contains("resistencia") || texto.Contains("imunidade"))
            return "Buff";
        if (texto.Contains("paralisa") || texto.Contains("atordoa") || texto.Contains("enraiza") || texto.Contains("imobiliza") || texto.Contains("amedronta"))
            return "Controle";
        if (texto.Contains("desloca") || texto.Contains("voar") || texto.Contains("escalar") || texto.Contains("nadar") || texto.Contains("teleporte"))
            return "Movimento";

        return "Utilidade";
    }

    // Heurística por palavras-chave: classifica a habilidade em uma categoria temática ampla (para o campo "grupo").
    private string ClassificarGrupoEfeito(string nome, string descricao)
    {
        string texto = NormalizarTexto((nome ?? "") + " " + (descricao ?? ""));
        if (string.IsNullOrEmpty(texto)) return "Utilidade";

        if (texto.Contains("persuasao") || texto.Contains("enganacao") || texto.Contains("intimidacao") || texto.Contains("atuacao"))
            return "Social";
        if (texto.Contains("percepcao") || texto.Contains("sentido") || texto.Contains("explora") || texto.Contains("viagem") || texto.Contains("sobrevivencia"))
            return "Exploração";
        if (Regex.IsMatch(texto, @"\d+d\d+") || texto.Contains("dano") || texto.Contains("ataque") || texto.Contains("ca ") || texto.Contains("iniciativa"))
            return "Combate";

        return "Utilidade";
    }

    // Heurística por palavras-chave: usada no filtro Dano/Não Dano.
    private bool ClassificarCausaDano(string descricao)
    {
        string texto = NormalizarTexto(descricao ?? "");
        if (string.IsNullOrEmpty(texto)) return false;

        return Regex.IsMatch(texto, @"\d+d\d+")
            || texto.Contains("dano")
            || texto.Contains("ataque corpo a corpo")
            || texto.Contains("ataque a distancia");
    }

    private void ProcessarDetalhesPrincipais(string detalhesJson, string nomeRacaAtual, string nomeSubracaAtual)
    {
        habilidades.Clear();
        habilidadeOrdemAtual = 0;

        if (string.IsNullOrWhiteSpace(detalhesJson))
        {
            FiltrarHabilidades();
            return;
        }

        try
        {
            var detalhes = JsonUtility.FromJson<DetalhesPrincipaisWrapper>(detalhesJson.Trim());
            if (detalhes == null)
            {
                FiltrarHabilidades();
                return;
            }

            detalhesPrincipaisCache = detalhes;
            GlobalFichaData.detalhesPrincipaisJson = detalhesJson.Trim();

            minhasPericias = ConstruirListaPericiasCompleta(
                detalhes.proficiencies_info != null ? detalhes.proficiencies_info.skills : null);

            AtualizarResumoLinguasEProficiencias(detalhes.proficiencies_info);

            if (detalhes.classe_info != null)
            {
                foreach (var p in detalhes.classe_info.class_powers ?? new List<ClassPowerData>())
                {
                    AdicionarOuMesclarHabilidade(p.name, "Classe", p.level, "Classe", p.description, "Passiva", "Ilimitado");
                }

                foreach (var p in detalhes.classe_info.subclass_powers ?? new List<ClassPowerData>())
                {
                    AdicionarOuMesclarHabilidade(
                        p.name,
                        "Classe",
                        p.level,
                        string.IsNullOrWhiteSpace(detalhes.classe_info.subclass_name) ? "Subclasse" : detalhes.classe_info.subclass_name,
                        p.description,
                        "Passiva",
                        "Ilimitado");
                }

                foreach (var c in detalhes.classe_info.class_power_choices ?? new List<ClassPowerChoiceData>())
                {
                    AdicionarOuMesclarHabilidade(
                        $"{c.feature_name}: {c.selected_option}",
                        "Classe",
                        c.level,
                        "Escolha de Classe",
                        $"Escolha registrada para {c.feature_name}: {c.selected_option}.",
                        "Passiva",
                        "Ilimitado");
                }

                foreach (var t in detalhes.classe_info.asi_feats ?? new List<string>())
                {
                    if (string.IsNullOrWhiteSpace(t)) continue;
                    int nivel = ExtrairNivelDoTexto(t);
                    string categoria = t.ToUpperInvariant().Contains("TALENTO") ? "Talentos" : "Classe";
                    string nomeLimpo = LimparRotuloNivel(t);
                    string descricaoLimpa = nomeLimpo;

                    if (categoria == "Classe" && TentarMesclarAsiNaHabilidadeExistente(nivel, nomeLimpo))
                        continue;

                    AdicionarOuMesclarHabilidade(nomeLimpo, categoria, nivel, categoria, descricaoLimpa, "Passiva", "Ilimitado");
                }
            }

            CarregarHabilidadesRacaLocal(nomeRacaAtual, nomeSubracaAtual);
            CarregarAcoesSalvas(detalhes.acoes_info);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[FichaGeralManager] Falha ao processar detalhes_principais: " + e.Message);
        }

        AplicarHabilidadesOverrides();
        FiltrarHabilidades();
    }

    private void AtualizarResumoLinguasEProficiencias(ProficienciesInfoWrapper prof)
    {
        if (prof == null)
        {
            if (linguasTxt) linguasTxt.text = "";
            if (armadurasTxt) armadurasTxt.text = "";
            if (armasTxt) armasTxt.text = "";
            if (ferramentasTxt) ferramentasTxt.text = "";
            return;
        }

        if (linguasTxt)
            linguasTxt.text = (prof.languages != null && prof.languages.Count > 0)
                ? string.Join(", ", prof.languages.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct())
                : "-";

        if (armadurasTxt)
            armadurasTxt.text = (prof.armors != null && prof.armors.Count > 0)
                ? string.Join("\n", prof.armors.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Select(x => "• " + x))
                : "-";

        if (armasTxt)
            armasTxt.text = (prof.weapons != null && prof.weapons.Count > 0)
                ? string.Join("\n", prof.weapons.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Select(x => "• " + x))
                : "-";

        if (ferramentasTxt)
            ferramentasTxt.text = (prof.tools != null && prof.tools.Count > 0)
                ? string.Join("\n", prof.tools.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().Select(x => "• " + x))
                : "-";
    }

    private void AtualizarSalvaguardasUI(List<SkillSaveItem> saves)
    {
        foreach (var go in salvaguardasInstanciadas)
            if (go != null) Destroy(go);
        salvaguardasInstanciadas.Clear();

        if (salvaguardasContent == null || salvaguardaPrefab == null) return;

        string[] ordem = { "FOR", "DEX", "CON", "INT", "SAB", "CAR" };

        for (int i = 0; i < 6; i++)
        {
            SkillSaveItem save = null;
            if (saves != null)
            {
                save = saves.FirstOrDefault(s =>
                    string.Equals((s?.nome ?? "").Trim(), ordem[i], StringComparison.OrdinalIgnoreCase));

                if (save == null)
                    save = saves.FirstOrDefault(s => s != null && s.attrIndex == i);
            }

            bool treinado  = save != null && save.treinado;
            int  modBase   = ObterModAtributoPorIndice(i);
            int  bonusProf = treinado ? bonusProficiencia : 0;
            int  bonusMisc = save != null ? save.miscBonus : 0;
            int  total     = (save != null && save.valorTotal != 0)
                ? save.valorTotal
                : (modBase + bonusProf + bonusMisc);

            GameObject go = UIUtils.Spawn(salvaguardaPrefab, salvaguardasContent);
            go.SetActive(true);
            salvaguardasInstanciadas.Add(go);

            SalvaguardaUIItem ui = go.GetComponent<SalvaguardaUIItem>();
            if (ui != null)
                ui.Setup(ordem[i], treinado, total);
        }

        if (salvaguardasContent.gameObject.activeInHierarchy)
            LayoutRebuilder.ForceRebuildLayoutImmediate(salvaguardasContent.GetComponent<RectTransform>());
    }

    private int ObterModAtributoPorIndice(int idx)
    {
        switch (idx)
        {
            case 0: return Mathf.FloorToInt((GlobalFichaData.forca - 10) / 2f);
            case 1: return Mathf.FloorToInt((GlobalFichaData.destreza - 10) / 2f);
            case 2: return Mathf.FloorToInt((GlobalFichaData.constituicao - 10) / 2f);
            case 3: return Mathf.FloorToInt((GlobalFichaData.inteligencia - 10) / 2f);
            case 4: return Mathf.FloorToInt((GlobalFichaData.sabedoria - 10) / 2f);
            case 5: return Mathf.FloorToInt((GlobalFichaData.carisma - 10) / 2f);
            default: return 0;
        }
    }

    private ProficienciesInfoWrapper ConstruirFallbackProficiencias(ClasseInfoData classeInfo, RacaInfoSnapshot raceInfo, BackgroundInfoData bgInfo)
    {
        ProficienciesInfoWrapper fallback = new ProficienciesInfoWrapper();

        HashSet<string> skills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> saves = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> armors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> weapons = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> tools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (classeInfo != null)
        {
            foreach (string s in classeInfo.skills ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) skills.Add(s.Trim());
            foreach (string s in classeInfo.saves ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) saves.Add(s.Trim());
            foreach (string s in classeInfo.armors ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) armors.Add(s.Trim());
            foreach (string s in classeInfo.weapons ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) weapons.Add(s.Trim());
            foreach (string s in classeInfo.tools ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) tools.Add(s.Trim());
            foreach (string s in classeInfo.languages ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) languages.Add(s.Trim());
        }

        if (raceInfo != null)
        {
            foreach (string s in raceInfo.skills ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) skills.Add(s.Trim());
            foreach (string s in raceInfo.armors ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) armors.Add(s.Trim());
            foreach (string s in raceInfo.weapons ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) weapons.Add(s.Trim());
            foreach (string s in raceInfo.tools ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) tools.Add(s.Trim());
            foreach (string s in raceInfo.languages ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) languages.Add(s.Trim());
        }

        if (bgInfo != null)
        {
            foreach (string s in bgInfo.skills ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) skills.Add(s.Trim());
            foreach (string s in bgInfo.armors ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) armors.Add(s.Trim());
            foreach (string s in bgInfo.weapons ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) weapons.Add(s.Trim());
            foreach (string s in bgInfo.tools ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) tools.Add(s.Trim());
            foreach (string s in bgInfo.languages ?? new List<string>()) if (!string.IsNullOrWhiteSpace(s)) languages.Add(s.Trim());
        }

        foreach (string s in skills)
        {
            string nomePt = TraduzirNomePericia(s);
            fallback.skills.Add(new SkillSaveItem
            {
                nome = nomePt,
                treinado = true,
                attrIndex = InferirAttrIndexPericia(nomePt),
                miscBonus = 0,
                valorTotal = 0
            });
        }

        foreach (string s in saves)
        {
            int idx = InferirAttrIndexSave(s);
            fallback.saves.Add(new SkillSaveItem
            {
                nome = AttrSiglaPorIndice(idx),
                treinado = true,
                attrIndex = idx,
                miscBonus = 0,
                valorTotal = 0
            });
        }

        fallback.armors = armors.OrderBy(x => x).ToList();
        fallback.weapons = weapons.OrderBy(x => x).ToList();
        fallback.tools = tools.OrderBy(x => x).ToList();
        fallback.languages = languages.OrderBy(x => x).ToList();

        return fallback;
    }

    private int InferirAttrIndexSave(string save)
    {
        string n = (save ?? string.Empty).Trim().ToUpperInvariant();
        if (n == "STR" || n == "FOR") return 0;
        if (n == "DEX" || n == "DES") return 1;
        if (n == "CON") return 2;
        if (n == "INT") return 3;
        if (n == "WIS" || n == "SAB") return 4;
        if (n == "CHA" || n == "CAR") return 5;
        return 0;
    }

    private string AttrSiglaPorIndice(int idx)
    {
        switch (idx)
        {
            case 0: return "FOR";
            case 1: return "DEX";
            case 2: return "CON";
            case 3: return "INT";
            case 4: return "SAB";
            case 5: return "CAR";
            default: return "FOR";
        }
    }

    private int InferirAttrIndexPericia(string skill)
    {
        string n = NormalizarTexto(skill);

        if (n.Contains("atlet") || n.Contains("athlet")) return 0;
        if (n.Contains("acrob") || n.Contains("furtiv") || n.Contains("stealth") || n.Contains("prestid") || n.Contains("sleight")) return 1;
        if (n.Contains("arcan") || n.Contains("hist") || n.Contains("invest") || n.Contains("nature") || n.Contains("relig")) return 3;
        if (n.Contains("animal") || n.Contains("lidar") || n.Contains("intu") || n.Contains("insight") || n.Contains("medic") || n.Contains("percep") || n.Contains("sobreviv") || n.Contains("surviv")) return 4;
        if (n.Contains("engan") || n.Contains("decep") || n.Contains("intimid") || n.Contains("atuac") || n.Contains("perform") || n.Contains("persua")) return 5;

        return 0;
    }

    private List<SkillSaveItem> ConstruirListaPericiasCompleta(List<SkillSaveItem> salvas)
    {
        List<SkillSaveItem> lista = TodasPericiasBase
            .Select(p => new SkillSaveItem
            {
                nome = p.nome,
                attrIndex = p.attrIndex,
                treinado = false,
                miscBonus = 0,
                valorTotal = 0
            })
            .ToList();

        if (salvas == null || salvas.Count == 0)
        {
            return lista;
        }

        foreach (SkillSaveItem skillSalva in salvas)
        {
            if (skillSalva == null || string.IsNullOrWhiteSpace(skillSalva.nome))
            {
                continue;
            }

            string nomeNormalizado = NormalizarTexto(skillSalva.nome);
            SkillSaveItem existente = lista.FirstOrDefault(p => NormalizarTexto(p.nome) == nomeNormalizado);

            if (existente == null)
            {
                lista.Add(new SkillSaveItem
                {
                    nome = skillSalva.nome.Trim(),
                    attrIndex = skillSalva.attrIndex >= 0 && skillSalva.attrIndex <= 5
                        ? skillSalva.attrIndex
                        : InferirAttrIndexPericia(skillSalva.nome),
                    treinado = skillSalva.treinado,
                    miscBonus = skillSalva.miscBonus,
                    valorTotal = skillSalva.valorTotal
                });
                continue;
            }

            existente.treinado = skillSalva.treinado;
            existente.miscBonus = skillSalva.miscBonus;
            existente.valorTotal = skillSalva.valorTotal;
            // attrIndex não é sobrescrito: TodasPericiasBase tem o valor canÃ´nico correto.
        }

        return lista;
    }

    private void OnHpAtualEdit(string value)
    {
        if (suppressCombatEvents) return;

        int hp = ParseIntOrDefault(value, GlobalFichaData.vidaAtual);
        hp = Mathf.Clamp(hp, 0, Mathf.Max(0, GlobalFichaData.vidaMaxima));
        GlobalFichaData.vidaAtual = hp;
        if (hpAtualTxt) hpAtualTxt.text = hp.ToString();
        SalvarEstadoCombateLocal();
    }

    private void OnCaEdit(string value)
    {
        if (suppressCombatEvents) return;

        if (string.IsNullOrWhiteSpace(value))
        {
            caManualOverride = -1;
            CalcularClasseArmadura(); // volta a exibir o valor calculado automaticamente
            SalvarEstadoCombateLocal();
            return;
        }

        int ca = Mathf.Max(0, ParseIntOrDefault(value, 10));
        caManualOverride = ca;
        if (caTxt) caTxt.SetTextWithoutNotify(ca.ToString());
        SalvarEstadoCombateLocal();
    }

    private void OnDeslocamentoEdit(string value)
    {
        if (suppressCombatEvents) return;

        int deslocamento = Mathf.Max(0, ParseIntOrDefault(value, GlobalFichaData.deslocamento));
        GlobalFichaData.deslocamento = deslocamento;
        if (deslocamentoTxt) deslocamentoTxt.SetTextWithoutNotify(deslocamento.ToString());
    }

    // Chamado tanto pelo campo do topo da aba quanto pelo campo repetido em cada separador de nível
    // (os dois editam o mesmo valor de personagem) — por isso recarrega a lista de magias ao final,
    // para que todo separador visível mostre o valor atualizado.
    private void OnCdMagiaEdit(string value)
    {
        if (suppressCombatEvents) return;

        if (string.IsNullOrWhiteSpace(value))
        {
            cdMagiaManualOverride = -1;
            CalcularCdEBonusMagico(); // volta a exibir o valor calculado automaticamente
            SalvarEstadoCombateLocal();
            FiltrarMagias();
            return;
        }

        int cd = Mathf.Max(0, ParseIntOrDefault(value, 10));
        cdMagiaManualOverride = cd;
        if (cdMagiaInput) cdMagiaInput.SetTextWithoutNotify(cd.ToString());
        SalvarEstadoCombateLocal();
        FiltrarMagias();
    }

    private void OnBonusAtaqueMagicoEdit(string value)
    {
        if (suppressCombatEvents) return;

        if (string.IsNullOrWhiteSpace(value))
        {
            bonusAtaqueMagicoManualOverride = -1;
            CalcularCdEBonusMagico(); // volta a exibir o valor calculado automaticamente
            SalvarEstadoCombateLocal();
            FiltrarMagias();
            return;
        }

        int bonus = Mathf.Max(0, ParseIntOrDefault(value, 0));
        bonusAtaqueMagicoManualOverride = bonus;
        if (bonusAtaqueMagicoInput) bonusAtaqueMagicoInput.SetTextWithoutNotify("+" + bonus);
        SalvarEstadoCombateLocal();
        FiltrarMagias();
    }

    private const float DuploCliqueCuraLimiteSegundos = 0.35f;
    private float ultimoCliqueCuraTempo = -10f;

    private void OnClickCurar()
    {
        float agora = Time.unscaledTime;
        bool duploClique = (agora - ultimoCliqueCuraTempo) <= DuploCliqueCuraLimiteSegundos;

        if (duploClique)
        {
            ultimoCliqueCuraTempo = -10f;
            CurarAteVidaTotal();
        }
        else
        {
            ultimoCliqueCuraTempo = agora;
            AplicarCuraDoInput();
        }
    }

    private void CurarAteVidaTotal()
    {
        GlobalFichaData.vidaAtual = Mathf.Max(0, GlobalFichaData.vidaMaxima);
        if (hpAtualTxt) hpAtualTxt.text = GlobalFichaData.vidaAtual.ToString();
        if (danoRecebidoInput) danoRecebidoInput.text = "0";
        SalvarEstadoCombateLocal();
    }

    private void AplicarCuraDoInput()
    {
        int cura = ParseIntOrDefault(danoRecebidoInput != null ? danoRecebidoInput.text : "0", 0);
        if (cura < 0) cura = 0;

        int hpAtual = ParseIntOrDefault(hpAtualTxt != null ? hpAtualTxt.text : GlobalFichaData.vidaAtual.ToString(), GlobalFichaData.vidaAtual);
        hpAtual = Mathf.Clamp(hpAtual + cura, 0, Mathf.Max(0, GlobalFichaData.vidaMaxima));

        GlobalFichaData.vidaAtual = hpAtual;
        if (hpAtualTxt) hpAtualTxt.text = hpAtual.ToString();
        if (danoRecebidoInput) danoRecebidoInput.text = "0";

        SalvarEstadoCombateLocal();
    }

    private void AplicarDanoDoInput()
    {
        int dano = ParseIntOrDefault(danoRecebidoInput != null ? danoRecebidoInput.text : "0", 0);
        if (dano < 0) dano = 0;

        int hpAtual = ParseIntOrDefault(hpAtualTxt != null ? hpAtualTxt.text : GlobalFichaData.vidaAtual.ToString(), GlobalFichaData.vidaAtual);
        int hpTemp = ParseIntOrDefault(hpTemporarioInput != null ? hpTemporarioInput.text : "0", 0);

        int absorvidoTemp = Mathf.Min(hpTemp, dano);
        hpTemp -= absorvidoTemp;
        int danoRestante = dano - absorvidoTemp;

        hpAtual = Mathf.Max(0, hpAtual - danoRestante);

        GlobalFichaData.vidaAtual = hpAtual;
        if (hpAtualTxt) hpAtualTxt.text = hpAtual.ToString();
        if (hpTemporarioInput) hpTemporarioInput.text = Mathf.Max(0, hpTemp).ToString();
        if (danoRecebidoInput) danoRecebidoInput.text = "0";

        SalvarEstadoCombateLocal();
    }

    private void OnDeathSavesChanged()
    {
        if (suppressCombatEvents) return;

        int sucessos = ContarTogglesAtivos(morteSucessoToggles);
        int falhas = ContarTogglesAtivos(morteFalhaToggles);

        if (sucessos >= 3)
        {
            suppressCombatEvents = true;
            LimparTogglesMorte();
            suppressCombatEvents = false;

            GlobalFichaData.vidaAtual = 1;
            if (hpAtualTxt) hpAtualTxt.text = "1";
        }
        else if (falhas >= 3)
        {
            GlobalFichaData.vidaAtual = 0;
            if (hpAtualTxt) hpAtualTxt.text = "0";
        }

        SalvarEstadoCombateLocal();
    }

    private void LimparTogglesMorte()
    {
        AplicarTogglesMorte(morteSucessoToggles, null);
        AplicarTogglesMorte(morteFalhaToggles, null);
    }

    private void AplicarTogglesMorte(Toggle[] toggles, bool[] source)
    {
        if (toggles == null) return;
        for (int i = 0; i < toggles.Length; i++)
        {
            if (toggles[i] == null) continue;
            bool v = source != null && i < source.Length && source[i];
            toggles[i].isOn = v;
        }
    }

    private int ContarTogglesAtivos(Toggle[] toggles)
    {
        if (toggles == null) return 0;
        int total = 0;
        for (int i = 0; i < toggles.Length; i++)
            if (toggles[i] != null && toggles[i].isOn) total++;
        return total;
    }

    private void SalvarEstadoCombateLocal()
    {
        if (GlobalFichaData.fichasDndId <= 0) return;

        // DataManager (singleton DontDestroyOnLoad) pode já estar destruído quando isto roda a
        // partir de OnDisable durante parada do Play Mode/saída do app — Unity não garante ordem
        // entre OnDisable de objetos de cena e de singletons persistentes nesse momento. GetCurrentId
        // logo abaixo dependeria dele — evita a NRE.
        if (DataManager.Instance == null) return;

        int hpAtual = ParseIntOrDefault(hpAtualTxt != null ? hpAtualTxt.text : GlobalFichaData.vidaAtual.ToString(), GlobalFichaData.vidaAtual);
        hpAtual = Mathf.Clamp(hpAtual, 0, Mathf.Max(0, GlobalFichaData.vidaMaxima));
        GlobalFichaData.vidaAtual = hpAtual;

        int hpTemp = ParseIntOrDefault(hpTemporarioInput != null ? hpTemporarioInput.text : "0", 0);
        if (hpTemporarioInput) hpTemporarioInput.text = Mathf.Max(0, hpTemp).ToString();

        var extra = new CombateExtraData
        {
            defesas = SerializarDefesas(),
            condicoes = SerializarCondicoes(),
            historia = historiaInput != null ? (historiaInput.text ?? "") : "",
            notas = notasInput != null ? (notasInput.text ?? "") : "",
            dado_vida = dadoVidaInput != null ? (dadoVidaInput.text ?? "") : "",
            vida_temporaria = Mathf.Max(0, hpTemp),
            morte_sucessos = ColetarEstadoToggles(morteSucessoToggles, 3),
            morte_falhas = ColetarEstadoToggles(morteFalhaToggles, 3),
            sentidos = ColetarSentidosDaUI(),
            ca_manual = caManualOverride,
            cd_magia_manual = cdMagiaManualOverride,
            bonus_ataque_magico_manual = bonusAtaqueMagicoManualOverride
        };

        int playerId = DataManager.Instance.GetCurrentId();
        string steamId = DataManager.Instance.GetCurrentSteamId();

        LocalFichaStorage.SaveCombatState(
            steamId,
            playerId,
            GlobalFichaData.fichasGeneralId,
            GlobalFichaData.fichasDndId,
            hpAtual,
            Mathf.Max(0, hpTemp),
            extra.dado_vida,
            JsonUtility.ToJson(extra));
    }

    private bool[] ColetarEstadoToggles(Toggle[] toggles, int expected)
    {
        bool[] data = new bool[Mathf.Max(1, expected)];
        if (toggles == null) return data;

        int max = Mathf.Min(toggles.Length, data.Length);
        for (int i = 0; i < max; i++)
            data[i] = toggles[i] != null && toggles[i].isOn;

        return data;
    }

    private int ParseIntOrDefault(string value, int fallback)
    {
        return int.TryParse((value ?? string.Empty).Trim(), out int parsed) ? parsed : fallback;
    }

    private List<SentidoPassivoData> BuildSentidosPadrao()
    {
        List<SentidoPassivoData> lista = BuildSentidosPassivosDasPericias();
        if (lista.Count == 0)
            lista.Add(new SentidoPassivoData { nome = "Percepção Passiva", valor = 10 + Mathf.FloorToInt((GlobalFichaData.sabedoria - 10) / 2f) });
        return lista;
    }

    private List<SentidoPassivoData> BuildSentidosPassivosDasPericias()
    {
        List<SentidoPassivoData> lista = new List<SentidoPassivoData>();
        if (minhasPericias == null || minhasPericias.Count == 0) return lista;

        int bonusProfAtual = Mathf.FloorToInt((GlobalFichaData.levelPersonagem - 1) / 4f) + 2;

        foreach (var pericia in minhasPericias)
        {
            if (pericia == null || string.IsNullOrWhiteSpace(pericia.nome)) continue;

            int ativa;
            if (pericia.valorTotal != 0)
            {
                ativa = pericia.valorTotal;
            }
            else
            {
                int modBase = ObterModAtributoPorIndice(pericia.attrIndex);
                int bonusProf = pericia.treinado ? bonusProfAtual : 0;
                int bonusMisc = pericia.miscBonus;
                ativa = modBase + bonusProf + bonusMisc;
            }

            lista.Add(new SentidoPassivoData
            {
                nome = pericia.nome.Trim() + " Passiva",
                valor = 10 + ativa
            });
        }

        return lista;
    }

    private List<SentidoPassivoData> MergeSentidosComPassivas(List<SentidoPassivoData> existentes)
    {
        List<SentidoPassivoData> resultado = new List<SentidoPassivoData>();

        if (existentes != null)
        {
            foreach (var s in existentes)
            {
                if (s == null || string.IsNullOrWhiteSpace(s.nome)) continue;
                if (resultado.Any(x => NormalizarTexto(x.nome) == NormalizarTexto(s.nome))) continue;
                resultado.Add(new SentidoPassivoData { nome = s.nome.Trim(), valor = s.valor });
            }
        }

        var passivas = BuildSentidosPassivosDasPericias();
        foreach (var p in passivas)
        {
            if (resultado.Any(x => NormalizarTexto(x.nome) == NormalizarTexto(p.nome))) continue;
            resultado.Add(p);
        }

        if (resultado.Count == 0)
            resultado = BuildSentidosPadrao();

        return resultado;
    }

    private void RenderSentidos(List<SentidoPassivoData> sentidos)
    {
        foreach (var go in sentidosInstanciados)
            if (go != null) Destroy(go);
        sentidosInstanciados.Clear();

        if (sentidosContent == null || sentidoPrefab == null) return;

        var lista = (sentidos != null && sentidos.Count > 0) ? sentidos : BuildSentidosPadrao();
        foreach (var s in lista)
        {
            GameObject go = Instantiate(sentidoPrefab, sentidosContent, false);
            go.SetActive(true);
            sentidosInstanciados.Add(go);

            SentidosUIItem sentidoUI = go.GetComponent<SentidosUIItem>();
            if (sentidoUI != null)
            {
                sentidoUI.Setup(string.IsNullOrWhiteSpace(s?.nome) ? "Sentido" : s.nome, s != null ? s.valor : 0);
                continue;
            }

            TMP_Text[] texts = go.GetComponentsInChildren<TMP_Text>(true);
            TMP_Text nomeTxt = texts.FirstOrDefault(t => t != null && t.name.ToLowerInvariant().Contains("nome"));
            TMP_Text valorTxt = texts.FirstOrDefault(t => t != null && t.name.ToLowerInvariant().Contains("valor"));

            if (nomeTxt == null && texts.Length > 0) nomeTxt = texts[0];
            if (valorTxt == null && texts.Length > 1) valorTxt = texts[1];

            if (nomeTxt) nomeTxt.text = string.IsNullOrWhiteSpace(s?.nome) ? "Sentido" : s.nome;
            if (valorTxt) valorTxt.text = (s != null ? s.valor : 0).ToString();
        }

        if (sentidosContent.gameObject.activeInHierarchy)
            LayoutRebuilder.ForceRebuildLayoutImmediate(sentidosContent.GetComponent<RectTransform>());
    }

    private List<SentidoPassivoData> ColetarSentidosDaUI()
    {
        List<SentidoPassivoData> sentidos = new List<SentidoPassivoData>();
        if (sentidosInstanciados == null || sentidosInstanciados.Count == 0)
            return BuildSentidosPadrao();

        foreach (var go in sentidosInstanciados)
        {
            if (go == null) continue;

            SentidosUIItem sentidoUI = go.GetComponent<SentidosUIItem>();
            if (sentidoUI != null)
            {
                string nomeComp = sentidoUI.GetNome();
                int valorComp = sentidoUI.GetValor();
                sentidos.Add(new SentidoPassivoData
                {
                    nome = string.IsNullOrWhiteSpace(nomeComp) ? "Sentido" : nomeComp,
                    valor = valorComp
                });
                continue;
            }

            TMP_Text[] texts = go.GetComponentsInChildren<TMP_Text>(true);
            if (texts == null || texts.Length == 0) continue;

            TMP_Text nomeTxt = texts.FirstOrDefault(t => t != null && t.name.ToLowerInvariant().Contains("nome"));
            TMP_Text valorTxt = texts.FirstOrDefault(t => t != null && t.name.ToLowerInvariant().Contains("valor"));

            if (nomeTxt == null && texts.Length > 0) nomeTxt = texts[0];
            if (valorTxt == null && texts.Length > 1) valorTxt = texts[1];

            string nome = nomeTxt != null ? nomeTxt.text : "Sentido";
            int valor = ParseIntOrDefault(valorTxt != null ? valorTxt.text : "0", 0);
            sentidos.Add(new SentidoPassivoData { nome = nome, valor = valor });
        }

        if (sentidos.Count == 0)
            sentidos = BuildSentidosPadrao();

        return sentidos;
    }

    private void ParseSlotsMagiasJson(string slotsJson)
    {
        meusSlots = new SlotsWrapper();
        meusSlotsPorNivel.Clear();
        meusSlotsGastosPorNivel.Clear();

        if (string.IsNullOrWhiteSpace(slotsJson)) return;

        string json = slotsJson.Trim();

        try
        {
            SlotsWrapper wrapper = JsonUtility.FromJson<SlotsWrapper>(json);
            if (wrapper != null && wrapper.totais != null && wrapper.totais.Length > 0)
            {
                meusSlots = wrapper;
                for (int i = 0; i < wrapper.totais.Length; i++)
                    if (wrapper.totais[i] > 0) meusSlotsPorNivel[i + 1] = wrapper.totais[i];

                if (wrapper.gastos != null)
                    for (int i = 0; i < wrapper.gastos.Length; i++)
                        if (wrapper.gastos[i] > 0) meusSlotsGastosPorNivel[i + 1] = wrapper.gastos[i];

                if (meusSlotsPorNivel.Count > 0) return;
            }
        }
        catch { }

        try
        {
            MatchCollection matches = Regex.Matches(json, "\\\"(?<lvl>\\d+)\\\"\\s*:\\s*(?<val>-?\\d+)");
            foreach (Match m in matches)
            {
                if (!m.Success) continue;
                if (!int.TryParse(m.Groups["lvl"].Value, out int lvl)) continue;
                if (!int.TryParse(m.Groups["val"].Value, out int val)) continue;
                if (lvl <= 0) continue;
                meusSlotsPorNivel[lvl] = Mathf.Max(0, val);
            }
        }
        catch { }
    }

    private string ObterJsonSlotsMagia(LocalFichaData snapshot)
    {
        if (snapshot != null && JsonSlotsTemDados(snapshot.slots_magias_json))
        {
            return snapshot.slots_magias_json;
        }

        if (JsonSlotsTemDados(GlobalFichaData.slotsMagiasJson))
        {
            return GlobalFichaData.slotsMagiasJson;
        }

        if (snapshot != null && !string.IsNullOrWhiteSpace(snapshot.slots_magias_json))
        {
            return snapshot.slots_magias_json;
        }

        return GlobalFichaData.slotsMagiasJson ?? string.Empty;
    }

    private static bool JsonSlotsTemDados(string slotsJson)
    {
        if (string.IsNullOrWhiteSpace(slotsJson)) return false;
        return Regex.IsMatch(slotsJson, "\\\"(?<lvl>\\d+)\\\"\\s*:\\s*(?<val>-?\\d+)");
    }

    private void CarregarHabilidadesRacaLocal(string nomeRacaAtual, string nomeSubracaAtual)
    {
        if (string.IsNullOrWhiteSpace(nomeRacaAtual)) return;

        try
        {
            string pastaRacas = DndSrdLocalPath.Resolver("sistemas/quinta-edicao-srd/racas");
            if (!Directory.Exists(pastaRacas)) return;

            string alvoRaca = NormalizarTexto(nomeRacaAtual);
            XElement raceRoot = null;

            foreach (var arquivo in ImportadorConteudoUniversal.EnumerarArquivosXmlEJson(pastaRacas, SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(arquivo).ToLowerInvariant().Contains("template")) continue;

                string xml;
                if (ImportadorConteudoUniversal.EhArquivoJson(arquivo))
                {
                    string json = ImportadorConteudoUniversal.LerTextoComFallbackDeEncoding(arquivo);
                    xml = json != null ? ImportadorConteudoUniversal.ConverterJsonParaXml(json, "race") : null;
                    if (xml == null) continue;
                }
                else
                {
                    xml = File.ReadAllText(arquivo).Trim().Replace("\uFEFF", "");
                }
                XDocument doc = XDocument.Parse(xml);
                XElement root = doc.Element("race") ?? doc.Root;
                if (root == null) continue;

                string nome = root.Element("name")?.Value ?? "";
                if (NormalizarTexto(nome) == alvoRaca)
                {
                    raceRoot = root;
                    break;
                }
            }

            if (raceRoot == null) return;

            foreach (var t in raceRoot.Descendants("traits").Elements("trait"))
            {
                string nome = t.Element("name")?.Value ?? "Traço Racial";
                string desc = t.Element("description")?.Value ?? t.Element("text")?.Value ?? "";
                AdicionarOuMesclarHabilidade(nome, "Raça", 1, "Raça", desc, ExtrairTipoAcaoXML(t.ToString()), ExtrairRecargaXML(t.ToString()));
            }

            if (!string.IsNullOrWhiteSpace(nomeSubracaAtual))
            {
                string alvoSub = NormalizarTexto(nomeSubracaAtual);
                var sub = raceRoot.Descendants("subrace").FirstOrDefault(s => NormalizarTexto(s.Element("name")?.Value ?? "") == alvoSub);
                if (sub != null)
                {
                    foreach (var f in sub.Descendants("feature"))
                    {
                        string nome = f.Element("name")?.Value ?? "Habilidade de Sub-raça";
                        string desc = f.Element("description")?.Value ?? f.Element("text")?.Value ?? "";
                        AdicionarOuMesclarHabilidade(nome, "Raça", 1, nomeSubracaAtual, desc, ExtrairTipoAcaoXML(f.ToString()), ExtrairRecargaXML(f.ToString()));
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[FichaGeralManager] Falha ao carregar habilidades de raça locais: " + e.Message);
        }
    }

    private void PopularDefesas(string defesasStr)
    {
        if (defesasContainer == null || defesaItemPrefab == null) return;

        for (int i = defesasContainer.childCount - 1; i >= 0; i--)
            Destroy(defesasContainer.GetChild(i).gameObject);

        if (string.IsNullOrWhiteSpace(defesasStr)) return;

        foreach (string linha in defesasStr.Split('\n'))
        {
            string l = linha.Trim();
            if (string.IsNullOrEmpty(l)) continue;

            string titulo, descricao;
            int sep = l.IndexOf(": ", StringComparison.Ordinal);
            if (sep > 0) { titulo = l[..sep]; descricao = l[(sep + 2)..]; }
            else          { titulo = l; descricao = ""; }

            CriarDefesaItem(titulo, descricao);
        }
    }

    private void CriarDefesaItem(string titulo, string descricao)
    {
        if (defesasContainer == null || defesaItemPrefab == null) return;
        GameObject go = Instantiate(defesaItemPrefab, defesasContainer);
        go.SetActive(true);
        go.GetComponent<DefesaItemUI>()?.Setup(titulo, descricao, SalvarEstadoCombateLocal);
    }

    private string SerializarDefesas()
    {
        if (defesasContainer == null) return "";
        var linhas = new System.Text.StringBuilder();
        foreach (Transform child in defesasContainer)
        {
            if (child == null) continue;
            var item = child.GetComponent<DefesaItemUI>();
            if (item == null) continue;
            string t = item.GetTitulo().Trim();
            string d = item.GetDescricao().Trim();
            if (string.IsNullOrEmpty(t) && string.IsNullOrEmpty(d)) continue;
            if (linhas.Length > 0) linhas.Append('\n');
            linhas.Append(string.IsNullOrEmpty(d) ? t : $"{t}: {d}");
        }
        return linhas.ToString();
    }

    private void PopularCondicoes(string condicoesStr)
    {
        if (condicoesContainer == null || defesaItemPrefab == null) return;

        for (int i = condicoesContainer.childCount - 1; i >= 0; i--)
            Destroy(condicoesContainer.GetChild(i).gameObject);

        if (string.IsNullOrWhiteSpace(condicoesStr)) return;

        foreach (string linha in condicoesStr.Split('\n'))
        {
            string l = linha.Trim();
            if (string.IsNullOrEmpty(l)) continue;

            string titulo, descricao;
            int sep = l.IndexOf(": ", StringComparison.Ordinal);
            if (sep > 0) { titulo = l[..sep]; descricao = l[(sep + 2)..]; }
            else          { titulo = l; descricao = ""; }

            CriarCondicaoItem(titulo, descricao);
        }
    }

    private void CriarCondicaoItem(string titulo, string descricao)
    {
        if (condicoesContainer == null || defesaItemPrefab == null) return;
        GameObject go = Instantiate(defesaItemPrefab, condicoesContainer);
        go.SetActive(true);
        go.GetComponent<DefesaItemUI>()?.Setup(titulo, descricao, SalvarEstadoCombateLocal);
    }

    private string SerializarCondicoes()
    {
        if (condicoesContainer == null) return "";
        var linhas = new System.Text.StringBuilder();
        foreach (Transform child in condicoesContainer)
        {
            if (child == null) continue;
            var item = child.GetComponent<DefesaItemUI>();
            if (item == null) continue;
            string t = item.GetTitulo().Trim();
            string d = item.GetDescricao().Trim();
            if (string.IsNullOrEmpty(t) && string.IsNullOrEmpty(d)) continue;
            if (linhas.Length > 0) linhas.Append('\n');
            linhas.Append(string.IsNullOrEmpty(d) ? t : $"{t}: {d}");
        }
        return linhas.ToString();
    }


    private void CarregarImagemPerfil(string caminhoImagem)
    {
        if (perfilImage == null || string.IsNullOrWhiteSpace(caminhoImagem)) return;
        try
        {
            string caminho = Path.IsPathRooted(caminhoImagem)
                ? caminhoImagem
                : Path.Combine(Application.dataPath, caminhoImagem);
            if (!File.Exists(caminho)) return;
            byte[] dados = File.ReadAllBytes(caminho);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(dados);
            perfilImage.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
            perfilImage.enabled = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[FichaGeralManager] Erro ao carregar imagem de perfil: " + ex.Message);
        }
    }

    private string ExtrairResistenciasRaciais(string nomeRaca, string subraceId)
    {
        if (string.IsNullOrWhiteSpace(nomeRaca)) return "";
        try
        {
            string pastaRacas = DndSrdLocalPath.Resolver("sistemas/quinta-edicao-srd/racas");
            if (!Directory.Exists(pastaRacas)) return "";

            string alvoRaca = NormalizarTexto(nomeRaca);
            XElement raceRoot = null;
            foreach (var arquivo in ImportadorConteudoUniversal.EnumerarArquivosXmlEJson(pastaRacas, SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(arquivo).ToLowerInvariant().Contains("template")) continue;
                string xmlText;
                if (ImportadorConteudoUniversal.EhArquivoJson(arquivo))
                {
                    string json = ImportadorConteudoUniversal.LerTextoComFallbackDeEncoding(arquivo);
                    xmlText = json != null ? ImportadorConteudoUniversal.ConverterJsonParaXml(json, "race") : null;
                    if (xmlText == null) continue;
                }
                else
                {
                    xmlText = File.ReadAllText(arquivo).Trim().Replace("ï»¿", "");
                }
                XDocument doc = XDocument.Parse(xmlText);
                XElement root = doc.Element("race") ?? doc.Root;
                if (root == null) continue;
                if (NormalizarTexto(root.Element("name")?.Value ?? "") == alvoRaca) { raceRoot = root; break; }
            }
            if (raceRoot == null) return "";

            var entries = new List<string>();
            foreach (var t in raceRoot.Descendants("traits").Elements("trait"))
            {
                string id   = (t.Attribute("id")?.Value ?? "").ToLowerInvariant();
                string nome = t.Element("name")?.Value ?? "";
                string desc = t.Element("description")?.Value ?? "";
                bool isResistance = id.Contains("resist") || id.Contains("immunit")
                    || nome.IndexOf("Resistência", StringComparison.OrdinalIgnoreCase) >= 0
                    || nome.IndexOf("Imunidade",   StringComparison.OrdinalIgnoreCase) >= 0
                    || desc.IndexOf("resistência", StringComparison.OrdinalIgnoreCase) >= 0
                    || desc.IndexOf("imunidade",   StringComparison.OrdinalIgnoreCase) >= 0;
                if (isResistance) entries.Add(nome + ": " + desc);
            }

            if (!string.IsNullOrWhiteSpace(subraceId))
            {
                string alvoSub = subraceId.ToLowerInvariant();
                var sub = raceRoot.Descendants("subrace")
                    .FirstOrDefault(s => (s.Attribute("id")?.Value ?? "").ToLowerInvariant() == alvoSub);
                if (sub != null)
                    foreach (var f in sub.Descendants("feature"))
                    {
                        string id   = (f.Attribute("id")?.Value ?? "").ToLowerInvariant();
                        string nome = f.Element("name")?.Value ?? "";
                        string desc = f.Element("description")?.Value ?? "";
                        bool isResistance = id.Contains("resist") || id.Contains("immunit")
                            || nome.IndexOf("Resistência", StringComparison.OrdinalIgnoreCase) >= 0
                            || nome.IndexOf("Imunidade",   StringComparison.OrdinalIgnoreCase) >= 0
                            || desc.IndexOf("resistência", StringComparison.OrdinalIgnoreCase) >= 0
                            || desc.IndexOf("imunidade",   StringComparison.OrdinalIgnoreCase) >= 0;
                        if (isResistance) entries.Add(nome + ": " + desc);
                    }
            }
            return string.Join("\n", entries);
        }
        catch { return ""; }
    }

    private void FiltrarHabilidades()
    {
        foreach (var go in habilidadesInstanciadas) Destroy(go);
        habilidadesInstanciadas.Clear();

        if (habilidadesContent == null) return;

        string filtroTexto = buscaHabilidadesInput ? buscaHabilidadesInput.text.Trim().ToLowerInvariant() : "";

        var tagsFonteAtivas = tagsAtivasHabilidades.Where(t => !TagsDanoHabilidades.Contains(t)).ToList();
        var tagsDanoAtivas = tagsAtivasHabilidades.Where(t => TagsDanoHabilidades.Contains(t)).ToList();

        var lista = habilidades
            .Where(h => h != null)
            .Where(h => tagsFonteAtivas.Count == 0 || tagsFonteAtivas.Contains(ObterOrigemExibicao(h)))
            .Where(h => tagsDanoAtivas.Count == 0
                || (tagsDanoAtivas.Contains("Dano") && h.causaDano)
                || (tagsDanoAtivas.Contains("Não Dano") && !h.causaDano))
            .Where(h => string.IsNullOrEmpty(filtroTexto)
                || (h.nome ?? "").ToLowerInvariant().Contains(filtroTexto)
                || (h.descricao ?? "").ToLowerInvariant().Contains(filtroTexto)
                || (h.origem ?? "").ToLowerInvariant().Contains(filtroTexto))
            .OrderBy(h => h.nivel)
            .ThenBy(h => h.ordem)
            .ToList();

        if (habilidadePrefab == null) return;

        foreach (var hab in lista)
        {
            GameObject go = UIUtils.Spawn(habilidadePrefab, habilidadesContent, false);
            go.SetActive(true);
            habilidadesInstanciadas.Add(go);

            HabilidadesItemUI habUI = go.GetComponent<HabilidadesItemUI>();
            if (habUI != null)
            {
                string chaveHab = ChaveHabilidade(hab);
                habUI.Setup(
                    chaveHab,
                    hab.nome,
                    hab.tipoAcao,
                    hab.grupo,
                    ObterFonteExibicao(hab),
                    hab.acaoResumo,
                    hab.recarga,
                    $"Nível {hab.nivel}",
                    string.IsNullOrWhiteSpace(hab.descricao) ? "Sem descrição." : hab.descricao);

                habUI.RegistrarAutoSave(() =>
                {
                    habilidadesOverridesAtuais[chaveHab] = habUI.ObterDadosAtuais();
                    StartCoroutine(SalvarHabilidadesOverrides());
                });
            }

            // O Tooltip agora mostra tudo!
            // Titulo: Nome
            // Subtitulo: Categoria (Ex: Raça) + Nível + Tipo de Ação/Recarga
            // Descrição: O texto completo da habilidade
            string subtituloTooltip = $"{hab.categoria} (Nv {hab.nivel}) | {hab.tipoAcao} • {hab.recarga}";
            string descricaoTooltip = string.IsNullOrWhiteSpace(hab.descricao) ? "Sem descrição." : hab.descricao;

            ConfigurarHoverTooltip(go, hab.nome, subtituloTooltip, descricaoTooltip);
        }

        if (habilidadesContent.gameObject.activeInHierarchy)
            LayoutRebuilder.ForceRebuildLayoutImmediate(habilidadesContent.GetComponent<RectTransform>());
    }

    private string ObterOrigemExibicao(HabilidadeEntry habilidade)
    {
        if (habilidade == null) return "-";

        if (string.Equals(habilidade.categoria, "Talentos", StringComparison.OrdinalIgnoreCase))
            return "Talento";

        if (string.Equals(habilidade.categoria, "Raça", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(habilidade.origem) || string.Equals(habilidade.origem, "Raça", StringComparison.OrdinalIgnoreCase))
                return "Raça";
            return "Sub-raça";
        }

        if (string.Equals(habilidade.categoria, "Classe", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(habilidade.origem)
                || string.Equals(habilidade.origem, "Classe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(habilidade.origem, "Escolha de Classe", StringComparison.OrdinalIgnoreCase))
                return "Classe";

            return "Subclasse";
        }

        return habilidade.categoria;
    }

    // Fonte exibida na UI: usa o override manual do jogador quando presente; senão cai no
    // valor estrutural de ObterOrigemExibicao. O filtro por chips sempre usa o valor estrutural
    // (ObterOrigemExibicao), nunca este, para não quebrar o casamento com as tags fixas.
    private string ObterFonteExibicao(HabilidadeEntry habilidade)
    {
        if (habilidade == null) return "-";
        return string.IsNullOrEmpty(habilidade.fonteOverride) ? ObterOrigemExibicao(habilidade) : habilidade.fonteOverride;
    }

    private string ChaveHabilidade(string nome, string categoria, int nivel, string origem)
    {
        return $"{NormalizarTexto(nome)}|{NormalizarTexto(categoria)}|{nivel}|{NormalizarTexto(origem)}";
    }

    private string ChaveHabilidade(HabilidadeEntry h) => ChaveHabilidade(h.nome, h.categoria, h.nivel, h.origem);

    private void AdicionarOuMesclarHabilidade(string nome, string categoria, int nivel, string origem, string descricao, string tipoAcao, string recarga)
    {
        string nomeLimpo = LimparRotuloNivel(nome);
        string chave = ChaveHabilidade(nomeLimpo, categoria, nivel, origem);

        var existente = habilidades.FirstOrDefault(h => ChaveHabilidade(h) == chave);

        if (existente != null)
        {
            if (string.IsNullOrWhiteSpace(existente.descricao) && !string.IsNullOrWhiteSpace(descricao))
            {
                existente.descricao = descricao;
                existente.acaoResumo = ClassificarAcaoResumo(existente.nome, descricao);
                existente.grupo = ClassificarGrupoEfeito(existente.nome, descricao);
                existente.causaDano = ClassificarCausaDano(descricao);
            }
            if ((string.IsNullOrWhiteSpace(existente.tipoAcao) || existente.tipoAcao == "Passiva") && !string.IsNullOrWhiteSpace(tipoAcao))
                existente.tipoAcao = tipoAcao;
            if ((string.IsNullOrWhiteSpace(existente.recarga) || existente.recarga == "Ilimitado") && !string.IsNullOrWhiteSpace(recarga))
                existente.recarga = recarga;
            return;
        }

        habilidades.Add(new HabilidadeEntry
        {
            nome = nomeLimpo,
            categoria = categoria,
            nivel = Mathf.Max(1, nivel),
            origem = origem,
            descricao = descricao,
            tipoAcao = string.IsNullOrWhiteSpace(tipoAcao) ? "Passiva" : tipoAcao,
            recarga = string.IsNullOrWhiteSpace(recarga) ? "Ilimitado" : recarga,
            acaoResumo = ClassificarAcaoResumo(nomeLimpo, descricao),
            grupo = ClassificarGrupoEfeito(nomeLimpo, descricao),
            causaDano = ClassificarCausaDano(descricao),
            ordem = habilidadeOrdemAtual++
        });
    }

    // Carrega os overrides persistidos (edições manuais do jogador) num dicionário em memória,
    // chaveado pela identidade estrutural da habilidade (nome/categoria/nível/origem calculados
    // a partir da fonte), para serem aplicados por cima da lista recém-recalculada.
    private void CarregarHabilidadesOverrides(string habilidadesOverridesJson)
    {
        habilidadesOverridesAtuais.Clear();
        if (string.IsNullOrWhiteSpace(habilidadesOverridesJson)) return;

        try
        {
            var wrapper = JsonUtility.FromJson<HabilidadesOverridesWrapper>(habilidadesOverridesJson);
            if (wrapper?.overrides == null) return;

            foreach (var o in wrapper.overrides)
            {
                if (o == null || string.IsNullOrEmpty(o.chave)) continue;
                habilidadesOverridesAtuais[o.chave] = o;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[FichaGeralManager] Falha ao carregar habilidades_overrides_json: " + e.Message);
        }
    }

    // Aplica os overrides já carregados em cada HabilidadeEntry cuja chave bata.
    private void AplicarHabilidadesOverrides()
    {
        if (habilidadesOverridesAtuais.Count == 0) return;

        foreach (var h in habilidades)
        {
            if (!habilidadesOverridesAtuais.TryGetValue(ChaveHabilidade(h), out var o) || o == null) continue;

            if (!string.IsNullOrEmpty(o.nome)) h.nome = o.nome;
            if (!string.IsNullOrEmpty(o.acao) && TiposAcaoValidos.Contains(o.acao)) h.tipoAcao = o.acao;
            if (!string.IsNullOrEmpty(o.grupo)) h.grupo = o.grupo;
            if (!string.IsNullOrEmpty(o.fonte)) h.fonteOverride = o.fonte;
            if (!string.IsNullOrEmpty(o.tipo) && ResumosAcaoValidos.Contains(o.tipo)) h.acaoResumo = o.tipo;
            if (!string.IsNullOrEmpty(o.usos)) h.recarga = o.usos;
            if (!string.IsNullOrEmpty(o.descricao)) h.descricao = o.descricao;
        }
    }

    private IEnumerator SalvarHabilidadesOverrides()
    {
        if (GlobalFichaData.fichasDndId <= 0) yield break;

        var wrapper = new HabilidadesOverridesWrapper { overrides = habilidadesOverridesAtuais.Values.ToList() };
        string json = JsonUtility.ToJson(wrapper);

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        LocalFichaStorage.SaveHabilidadesOverrides(steamId, playerId, GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, json);

        yield return null;
    }

    private bool TentarMesclarAsiNaHabilidadeExistente(int nivel, string textoAsi)
    {
        var existente = habilidades.FirstOrDefault(h =>
            h.nivel == nivel
            && string.Equals(h.categoria, "Classe", StringComparison.OrdinalIgnoreCase)
            && NormalizarTexto(h.nome).Contains("melhoriadeatributo"));

        if (existente == null) return false;

        string complemento = LimparRotuloNivel(textoAsi);
        if (!string.IsNullOrWhiteSpace(complemento) && !existente.descricao.Contains(complemento, StringComparison.OrdinalIgnoreCase))
            existente.descricao = string.IsNullOrWhiteSpace(existente.descricao) ? complemento : existente.descricao + "\n\n" + complemento;

        return true;
    }

    private string LimparRotuloNivel(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";

        string resultado = texto.Trim();
        if (resultado.StartsWith("Nível", StringComparison.OrdinalIgnoreCase))
        {
            int idx = resultado.IndexOf(':');
            if (idx >= 0 && idx + 1 < resultado.Length)
                resultado = resultado.Substring(idx + 1).Trim();
        }

        resultado = resultado.Replace("AUMENTO DE ATRIBUTO", "Melhoria de Atributo");
        resultado = resultado.Replace("TALENTO PENDENTE", "Talento Pendente");
        resultado = resultado.Replace("TALENTO=", "Talento: ");
        return resultado;
    }

    private int ExtrairNivelDoTexto(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 1;
        int idx = texto.IndexOf("Nível", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 1;

        string recorte = texto.Substring(idx + 5);
        string numeros = new string(recorte.TakeWhile(c => !char.IsDigit(c)).Concat(recorte.Where(char.IsDigit)).Take(2).ToArray());
        if (int.TryParse(numeros, out int nivel) && nivel > 0) return nivel;
        return 1;
    }

    private static readonly Dictionary<string, string> _tradutorPericias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        {"Acrobatics",       "Acrobacia"},
        {"Animal Handling",  "Lidar com Animais"},
        {"Arcana",           "Arcanismo"},
        {"Athletics",        "Atletismo"},
        {"Deception",        "Enganação"},
        {"History",          "História"},
        {"Insight",          "Intuição"},
        {"Intimidation",     "Intimidação"},
        {"Investigation",    "Investigação"},
        {"Medicine",         "Medicina"},
        {"Nature",           "Natureza"},
        {"Perception",       "Percepção"},
        {"Performance",      "Atuação"},
        {"Persuasion",       "Persuasão"},
        {"Religion",         "Religião"},
        {"Sleight of Hand",  "Prestidigitação"},
        {"Stealth",          "Furtividade"},
        {"Survival",         "Sobrevivência"},
    };

    private string TraduzirNomePericia(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return nome;
        return _tradutorPericias.TryGetValue(nome.Trim(), out var pt) ? pt : nome.Trim();
    }

    private string NormalizarTexto(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "";
        string semAcento = valor.Normalize(System.Text.NormalizationForm.FormD);
        return new string(semAcento.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray())
            .ToLowerInvariant()
            .Trim();
    }

    public void AoAbrirAbaAcoes()
    {
        Debug.Log("[Acoes] AoAbrirAbaAcoes chamado.");
        Debug.Log($"[Acoes] Estado UI | ativoNaHierarquia={gameObject.activeInHierarchy} | fichaId={GlobalFichaData.fichasDndId}");
        ultimaPosicaoTooltipValida = false;
        ConfigurarDropdownAcoes();
        FiltrarAcoes();
        StartCoroutine(RotinaAbrirAbaAcoes());
    }

    private IEnumerator RotinaAbrirAbaAcoes()
    {
        Debug.Log("[Acoes] RotinaAbrirAbaAcoes iniciada.");
        yield return null;
        Debug.Log("[Acoes] Apos 1 frame.");
        yield return new WaitForEndOfFrame();
        Debug.Log("[Acoes] Apos WaitForEndOfFrame. Chamando recomendacao.");
        FiltrarAcoes();
        RecomendarAcoesAoAbrirAba();
    }

    private void ConfigurarDropdownAcoes()
    {
        if (tipoAcaoDropdown == null) return;

        tipoAcaoDropdown.onValueChanged.RemoveAllListeners();
        tipoAcaoDropdown.ClearOptions();
        tipoAcaoDropdown.AddOptions(new List<string> { "Todos", "Corpo a Corpo", "Distância" });
        tipoAcaoDropdown.value = 0;
        tipoAcaoDropdown.onValueChanged.AddListener((_) => FiltrarAcoes());
    }

    private void RecomendarAcoesAoAbrirAba()
    {
        Debug.Log("[Acoes] RecomendarAcoesAoAbrirAba iniciou.");
        if (actionRecommender == null || tooltipSugestaoPrefab == null || acaoItemPrefab == null || acoesContentArea == null)
        {
            Debug.LogWarning($"[Acoes] Recomendacao abortada por referencia nula | recommender={(actionRecommender != null)} | tooltip={(tooltipSugestaoPrefab != null)} | prefabAcao={(acaoItemPrefab != null)} | content={(acoesContentArea != null)}");
            return;
        }

        List<ItemAcaoEntrada> itens = ConverterInventarioParaEntradaAcao();
        Debug.Log($"[Acoes] Itens candidatos no inventario: {itens.Count}");
        if (itens.Count == 0)
        {
            Debug.LogWarning("[Acoes] Nenhum item candidato encontrado no inventario para recomendacao.");
            return;
        }

        var sugestoes = actionRecommender.GerarSugestoes(
            itens,
            ExtrairProficienciasArmas(),
            habilidades.Select(h => h.nome));

        Debug.Log($"[Acoes] Sugestoes geradas: {sugestoes.Count}");

        foreach (var s in sugestoes)
        {
            string chave = $"{NormalizarTexto(s.nomeAcao)}|{NormalizarTexto(s.tipoAcao)}";
            if (acoesSugeridasIgnoradas.Contains(chave))
            {
                Debug.Log($"[Acoes] Sugestao ignorada anteriormente: {s.nomeAcao} ({s.tipoAcao})");
                continue;
            }
            if (JaExisteAcaoSemelhante(s.nomeAcao, s.tipoAcao) || JaExisteAcaoSalvaSemelhante(s.nomeAcao, s.tipoAcao))
            {
                Debug.Log($"[Acoes] Acao semelhante ja existe: {s.nomeAcao} ({s.tipoAcao})");
                continue;
            }

            Debug.Log($"[Acoes] Exibindo sugestao: {s.nomeAcao} | dano={s.danoBase} | tipo={s.tipoAcao}");
            ExibirSugestao(s.nomeAcao, s.danoBase, s.tipoDano, s.distancia, s.critico, s.tipoAcao, s.resumo);
            break;
        }
    }

    private void CarregarAcoesSalvas(AcoesInfoData info)
    {
        foreach (var go in acoesInstanciadas)
        {
            if (go != null) Destroy(go);
        }

        acoesInstanciadas.Clear();
        tipoAcaoPorItem.Clear();

        if (info == null || info.actions == null) return;

        foreach (var acao in info.actions)
        {
            if (acao == null) continue;
            CriarAcaoUI(
                string.IsNullOrWhiteSpace(acao.nome) ? "Nova Ação" : acao.nome,
                string.IsNullOrWhiteSpace(acao.dano) ? "1d6" : acao.dano,
                string.IsNullOrWhiteSpace(acao.tipoDano) ? "dano" : acao.tipoDano,
                string.IsNullOrWhiteSpace(acao.distancia) ? "1,5m" : acao.distancia,
                string.IsNullOrWhiteSpace(acao.critico) ? "20" : acao.critico,
                InferirTipoAcao(acao.distancia),
                false,
                acao.ataque ?? "",
                acao.cd ?? "",
                acao.detalhes,
                acao.danos);
        }
    }

    private List<ItemAcaoEntrada> ConverterInventarioParaEntradaAcao()
    {
        List<ItemAcaoEntrada> itens = new List<ItemAcaoEntrada>();
        if (meuInventario == null) return itens;

        if (catalogoItens.Count == 0) CarregarCatalogoItensLocal();

        foreach (var inv in meuInventario)
        {
            if (inv.id == -999) continue; // item de controle oculto (import_flag)

            if (catalogoItens.TryGetValue(inv.id, out ItemData item))
            {
                itens.Add(new ItemAcaoEntrada
                {
                    id = inv.id,
                    nome = string.IsNullOrWhiteSpace(inv.customName) ? item.nome : inv.customName,
                    categoria = item.categoria,
                    xml = item.xml,
                    equipSlot = inv.equipSlot
                });
            }
            else if (!string.IsNullOrWhiteSpace(inv.customName))
            {
                // Sem correspondência no catálogo carregado (id=-2 ou id desatualizado):
                // ainda entra na lista para o ActionRecommender tentar resolver pelo nome
                // no cache local de armas, em vez de sumir silenciosamente.
                itens.Add(new ItemAcaoEntrada
                {
                    id = inv.id,
                    nome = inv.customName,
                    categoria = "",
                    xml = "",
                    equipSlot = inv.equipSlot
                });
            }
        }

        return itens;
    }

    private List<string> ExtrairProficienciasArmas()
    {
        List<string> lista = new List<string>();

        if (armasTxt != null && !string.IsNullOrWhiteSpace(armasTxt.text))
        {
            string bruto = armasTxt.text.Replace("•", "\n");
            var linhas = bruto.Split(new[] { '\n', '\r', ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var l in linhas)
            {
                string v = l.Trim();
                if (!string.IsNullOrWhiteSpace(v)) lista.Add(v);
            }
        }

        return lista;
    }

    private bool JaExisteAcaoSemelhante(string nome, string tipo)
    {
        string alvoNome = NormalizarTexto(nome);

        foreach (var go in acoesInstanciadas)
        {
            if (go == null) continue;
            AcoesUIItem ui = go.GetComponent<AcoesUIItem>();
            if (ui == null || ui.nomeInput == null) continue;

            string nomeAtual = NormalizarTexto(ui.nomeInput.text);
            if (nomeAtual == alvoNome) return true;
        }

        return false;
    }

    private bool JaExisteAcaoSalvaSemelhante(string nome, string tipo)
    {
        string alvoNome = NormalizarTexto(nome);

        if (detalhesPrincipaisCache != null
            && detalhesPrincipaisCache.acoes_info != null
            && detalhesPrincipaisCache.acoes_info.actions != null)
        {
            foreach (var acao in detalhesPrincipaisCache.acoes_info.actions)
            {
                if (acao == null) continue;
                if (NormalizarTexto(acao.nome) == alvoNome) return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(GlobalFichaData.detalhesPrincipaisJson))
        {
            try
            {
                var detalhes = JsonUtility.FromJson<DetalhesPrincipaisWrapper>(GlobalFichaData.detalhesPrincipaisJson);
                var lista = detalhes != null && detalhes.acoes_info != null ? detalhes.acoes_info.actions : null;
                if (lista != null)
                {
                    foreach (var acao in lista)
                    {
                        if (acao == null) continue;
                        if (NormalizarTexto(acao.nome) == alvoNome) return true;
                    }
                }
            }
            catch { }
        }

        return false;
    }

    public void ExibirSugestao(string nomeArma, string calculoDano, string tipoDano, string range, string crit, string tipoAcao, string resumo = "")
    {
        Debug.Log($"[Acoes] ExibirSugestao chamado para {nomeArma}.");
        if (tooltipSugestaoPrefab == null || acoesContentArea == null || acaoItemPrefab == null) return;

        if (tooltipSugestaoInstancia != null)
        {
            Debug.Log("[Acoes] Destruindo tooltip anterior antes de criar novo.");
            Destroy(tooltipSugestaoInstancia.gameObject);
            tooltipSugestaoInstancia = null;
        }

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("[Acoes] Nenhum Canvas encontrado para criar tooltip.");
            return;
        }

        Debug.Log($"[Acoes] Canvas encontrado: {canvas.name}");

        GameObject tooltipGo = Instantiate(tooltipSugestaoPrefab, canvas.transform, false);
        TooltipSugestaoAcaoUI tooltipUI = tooltipGo.GetComponent<TooltipSugestaoAcaoUI>();
        if (tooltipUI == null)
        {
            Debug.LogError("[Acoes] O prefab tooltipSugestaoPrefab nao possui TooltipSugestaoAcaoUI.");
            Destroy(tooltipGo);
            return;
        }

        tooltipSugestaoInstancia = tooltipUI;
        tooltipGo.SetActive(true);
        tooltipGo.transform.SetAsLastSibling();
        Debug.Log($"[Acoes] Tooltip instanciado com sucesso: {tooltipGo.name}");

        if (ultimaPosicaoTooltipValida)
        {
            tooltipUI.posicionarNoMouseAoAbrir = false;
            tooltipUI.DefinirPosicaoManual(ultimaPosicaoTooltip);
            Debug.Log($"[Acoes] Reutilizando posição do tooltip anterior: {ultimaPosicaoTooltip}");
        }
        else
        {
            tooltipUI.posicionarNoMouseAoAbrir = true;
        }

        string descricao = $"Deseja adicionar {nomeArma} ({calculoDano} {tipoDano}) Ã s suas Ações?";
        if (!string.IsNullOrWhiteSpace(resumo)) descricao += "\n\n" + resumo;

        tooltipUI.Setup(
            titulo: "Nova Arma Detectada!",
            descricao: descricao,
            aoConfirmar: () =>
            {
                if (tooltipSugestaoInstancia != null)
                {
                    ultimaPosicaoTooltip = tooltipSugestaoInstancia.ObterPosicaoAtual();
                    ultimaPosicaoTooltipValida = true;
                }

                CriarAcaoUI(nomeArma, calculoDano, tipoDano, range, crit, tipoAcao);
                tooltipSugestaoInstancia = null;
                StartCoroutine(RotinaProximaSugestaoAcoes());
            },
            aoIgnorar: () =>
            {
                if (tooltipSugestaoInstancia != null)
                {
                    ultimaPosicaoTooltip = tooltipSugestaoInstancia.ObterPosicaoAtual();
                    ultimaPosicaoTooltipValida = true;
                }

                string chave = $"{NormalizarTexto(nomeArma)}|{NormalizarTexto(tipoAcao)}";
                acoesSugeridasIgnoradas.Add(chave);
                tooltipSugestaoInstancia = null;
                StartCoroutine(RotinaProximaSugestaoAcoes());
            }
        );
    }

    private IEnumerator RotinaProximaSugestaoAcoes()
    {
        yield return null;
        yield return new WaitForEndOfFrame();
        RecomendarAcoesAoAbrirAba();
    }

    private void CriarAcaoUI(string nomeArma, string calculoDano, string tipoDano, string range, string crit, string tipoAcao,
        bool salvarDepois = true, string ataque = "", string cd = "", string detalhes = null, List<AcaoDanoData> danos = null)
    {
        if (acaoItemPrefab == null || acoesContentArea == null) return;

        GameObject novaAcaoGo = Instantiate(acaoItemPrefab, acoesContentArea);
        AcoesUIItem acaoUI = novaAcaoGo.GetComponent<AcoesUIItem>();
        if (acaoUI == null)
        {
            Destroy(novaAcaoGo);
            return;
        }

        acaoUI.Setup(nomeArma, calculoDano, tipoDano, range, crit, ataque, cd);
        if (detalhes != null || danos != null) acaoUI.CarregarDetalhesEDanos(detalhes, danos);
        acoesInstanciadas.Add(novaAcaoGo);
        tipoAcaoPorItem[novaAcaoGo] = string.IsNullOrWhiteSpace(tipoAcao) ? "Corpo a Corpo" : tipoAcao;

        RegistrarEventosDeAutoSave(acaoUI);

        if (acaoUI.btnRemoverAcao != null)
        {
            acaoUI.btnRemoverAcao.onClick.RemoveAllListeners();
            acaoUI.btnRemoverAcao.onClick.AddListener(() =>
            {
                acoesInstanciadas.Remove(novaAcaoGo);
                tipoAcaoPorItem.Remove(novaAcaoGo);
                Destroy(novaAcaoGo);
                StartCoroutine(SalvarAcoesNoDetalhesPrincipais());
            });
        }

        FiltrarAcoes();
        if (salvarDepois) StartCoroutine(SalvarAcoesNoDetalhesPrincipais());
    }

    // Cria uma ação em branco para o jogador preencher — os campos ficam vazios
    // (exceto o crítico, que o próprio AcoesUIItem.Setup preenche com "20" por padrão)
    // e cada edição já dispara o autosave normal (RegistrarEventosDeAutoSave).
    public void CriarAcaoVazia()
    {
        CriarAcaoUI("", "", "", "", "", "Corpo a Corpo");
    }

    private void FiltrarAcoes()
    {
        string filtroTexto = buscaAcoesInput ? buscaAcoesInput.text.Trim().ToLowerInvariant() : "";
        string filtroTipo = "Todos";
        if (tipoAcaoDropdown != null && tipoAcaoDropdown.options.Count > 0)
            filtroTipo = tipoAcaoDropdown.options[tipoAcaoDropdown.value].text;

        foreach (var go in acoesInstanciadas)
        {
            if (go == null) continue;
            AcoesUIItem ui = go.GetComponent<AcoesUIItem>();
            if (ui == null)
            {
                go.SetActive(true);
                continue;
            }

            string nome = ui.nomeInput ? ui.nomeInput.text : "";
            string dano = ui.danoInput ? ui.danoInput.text : "";
            string tipo = tipoAcaoPorItem.TryGetValue(go, out var tipoValor) ? tipoValor : "Corpo a Corpo";

            bool passaTipo = filtroTipo == "Todos" || string.Equals(filtroTipo, tipo, StringComparison.OrdinalIgnoreCase);
            bool passaTexto = string.IsNullOrEmpty(filtroTexto)
                || nome.ToLowerInvariant().Contains(filtroTexto)
                || dano.ToLowerInvariant().Contains(filtroTexto)
                || tipo.ToLowerInvariant().Contains(filtroTexto);

            go.SetActive(passaTipo && passaTexto);
        }

        if (acoesContentArea != null && acoesContentArea.gameObject.activeInHierarchy)
            LayoutRebuilder.ForceRebuildLayoutImmediate(acoesContentArea.GetComponent<RectTransform>());
    }

    private void RegistrarEventosDeAutoSave(AcoesUIItem acaoUI)
    {
        if (acaoUI == null) return;

        // Cabeçalho, detalhes e as 3 abas de dano usam o autosave genérico do item.
        acaoUI.RegistrarAutoSave(() => StartCoroutine(SalvarAcoesNoDetalhesPrincipais()));

        // distanciaInput fica de fora do autosave genérico pois também precisa reclassificar
        // o tipo da ação (Corpo a Corpo / Distância) para o filtro por tipo.
        if (acaoUI.distanciaInput != null)
        {
            acaoUI.distanciaInput.onEndEdit.RemoveAllListeners();
            acaoUI.distanciaInput.onEndEdit.AddListener((valor) =>
            {
                tipoAcaoPorItem[acaoUI.gameObject] = InferirTipoAcao(valor);
                FiltrarAcoes();
                StartCoroutine(SalvarAcoesNoDetalhesPrincipais());
            });
        }
    }

    private string InferirTipoAcao(string distancia)
    {
        string normalizado = NormalizarTexto(distancia);
        if (normalizado.Contains("range") || normalizado.Contains("ft") || normalizado.Contains("m") || normalizado.Contains("distancia"))
        {
            if (!normalizado.Contains("15") && !normalizado.Contains("5ft") && !normalizado.Contains("corpoacorpo"))
                return "Distância";
        }

        return "Corpo a Corpo";
    }

    private IEnumerator SalvarAcoesNoDetalhesPrincipais()
    {
        if (GlobalFichaData.fichasDndId <= 0) yield break;

        DetalhesPrincipaisWrapper detalhes = detalhesPrincipaisCache ?? new DetalhesPrincipaisWrapper();
        if (detalhes.acoes_info == null) detalhes.acoes_info = new AcoesInfoData();
        detalhes.acoes_info.actions = ColetarAcoesAtuais();

        string acoesJson = JsonUtility.ToJson(detalhes.acoes_info);
        GlobalFichaData.detalhesPrincipaisJson = JsonUtility.ToJson(detalhes);
        detalhesPrincipaisCache = detalhes;

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        LocalFichaStorage.SaveAcoesInfo(steamId, playerId, GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, acoesJson);

        yield return null;
    }

    private List<AcaoData> ColetarAcoesAtuais()
    {
        List<AcaoData> lista = new List<AcaoData>();

        foreach (var go in acoesInstanciadas)
        {
            if (go == null) continue;
            AcoesUIItem ui = go.GetComponent<AcoesUIItem>();
            if (ui == null) continue;
            lista.Add(ui.ObterDadosAtuais());
        }

        return lista;
    }
}