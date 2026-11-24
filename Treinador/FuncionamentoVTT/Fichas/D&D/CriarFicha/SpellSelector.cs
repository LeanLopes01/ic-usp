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

public class SpellSelector : MonoBehaviour
{
    // Mesmo motivo de EquipmentSelector.Instance: uma referência de Inspector fixa (ex:
    // FichaSaveManager.spellSelector) pode ficar sem ser arrastada, ou apontar pra uma instância
    // errada se a cena tiver mais de uma — Instance rastreia a última instância habilitada, a que o
    // jogador realmente está usando. Confirmado em log/cena real: FichaSaveManager.spellSelector
    // estava null (nunca arrastado no Inspector), fazendo o passo "Salvando grimório" do Finalizar
    // ser pulado inteiramente.
    public static SpellSelector Instance { get; private set; }

    [Header("Fonte de Dados Local")]
    public string pastaClassesLocalRelativa = "sistemas/quinta-edicao-srd/classes";
    public string pastaMagiasLocalRelativa  = "sistemas/quinta-edicao-srd/magias";

    [Header("Configurações de Teste")]
    [Tooltip("Ative para ignorar o filtro de magias permitidas da classe e mostrar todas.")]
    [SerializeField] private bool showAllSpellsForTestingEditor = false;

    [Header("UI - Listas")]
    public Transform catalogContent;
    public Transform grimorioContent;

    [Header("UI - Painel de Pesquisa")]
    public GameObject searchContentPanel;
    public Button     expandButton;
    public Button     collapseButton;

    [Header("UI - Filtros (Catálogo)")]
    public TMP_InputField filtroPesquisaInput;
    public Button         pesquisarButton;
    [Tooltip("Prefab de chip (toggle+texto) reutilizado nos dois painéis de filtro abaixo — mesmo componente FiltroChipToggleUI que FichaGeralManager.cs já usa.")]
    public GameObject     filtroChipPrefab;
    public Button         btnExpandirFiltroNivel;
    public Button         btnColapsarFiltroNivel;
    public Transform      conteudoFiltroNivel;

    [Header("UI - Filtros (Grimório)")]
    public TMP_InputField filtroGrimorioInput;
    public Button         pesquisarGrimorioButton;
    public Button         btnExpandirFiltroNivelGrimorio;
    public Button         btnColapsarFiltroNivelGrimorio;
    public Transform      conteudoFiltroNivelGrimorio;

    [Header("UI - Prefabs")]
    public GameObject spellPrefab;
    public GameObject grimorioPrefab;

    [Header("UI - Detalhes e Controles")]
    public TMP_Text detailsText;
    public TMP_Text warningText;
    public TMP_Text slotsText;

    [Header("UI - Tooltip (Prefab)")]
    [Tooltip("Mesmo componente TooltipUI usado em FichaGeralManager.cs — mostra nome/nível/tempo/alcance/duração/componentes e descrição ao passar o mouse sobre uma magia, aqui na tela de criação.")]
    public GameObject tooltipPrefab;
    private TooltipUI tooltipInstancia;

    [Header("UI - Abas de Classe (Multiclasse)")]
    [Tooltip("Prefab simples: um GameObject pai com Image+Button, e um filho com TMP_Text — SpellSelector escreve o nome da classe nele (sem componente de UI dedicado). Só aparece quando o personagem tem 2+ classes conjuradoras.")]
    public GameObject abaClassePrefab;
    public Transform  abaClasseContent;
    private readonly List<GameObject> abasClasseInstanciadas = new List<GameObject>();

    // Dados carregados
    private Dictionary<int, MagiaData>           todasAsMagias  = new Dictionary<int, MagiaData>();
    private Dictionary<string, List<MagiaData>>  magiasPorNivel = new Dictionary<string, List<MagiaData>>();
    private List<GrimorioSaveItem>                meuGrimorio    = new List<GrimorioSaveItem>();

    private List<SpellRowUI> listaUI_MagiasVisiveis = new List<SpellRowUI>();

    private string filtroPesquisaAtual       = "";
    private string filtroGrimorioAtual       = "";
    // Multi-seleção (chips) — vazio = mostra todos os níveis, sem filtro.
    private readonly HashSet<string> niveisAtivos = new HashSet<string>();
    private readonly HashSet<string> niveisGrimorioAtivos = new HashSet<string>();
    private readonly List<GameObject> chipsFiltroNivelInstanciados = new List<GameObject>();
    private readonly List<GameObject> chipsFiltroNivelGrimorioInstanciados = new List<GameObject>();
    // Tags fixas (iguais em espírito a FichaGeralManager.TagsNivelMagia) — mesmo conjunto usado
    // no catálogo e no grimório, independente do que já foi carregado/escolhido.
    private static readonly string[] TagsFiltroNivelMagia =
    {
        "Truques", "Nível 1", "Nível 2", "Nível 3", "Nível 4", "Nível 5",
        "Nível 6", "Nível 7", "Nível 8", "Nível 9"
    };
    private Dictionary<int, int> spellSlotsDisponiveis = new Dictionary<int, int>();

    // Limites lidos do XML da classe
    private int    limiteTruques           = 0;
    private int    limiteMagiasConhecidas  = 999;
    private int    limiteMagiasPreparadas  = 999;
    private string textoMagiasPreparadas   = "Ilimitado";
    private int    maiorNivelMagiaPermitido = 0;
    private bool   classeConjuraMagias     = false;

    private readonly List<CanvasGroup> _canvasGroupsTmp = new List<CanvasGroup>();
    private Coroutine _reativacaoMagiasCoroutine;

    [HideInInspector] public bool isLoaded = false;
    private int    lastLoadedFichaDndId  = -1;
    private string ultimaClasseCarregada = "";
    public static bool PrecisaCarregarMagias = false;

    // ── Multiclasse: uma DadosConjuracaoClasse por classe conjuradora do personagem, resolvida com
    // o nível PRÓPRIO de cada classe. classeAtivaId é a aba selecionada — o resto do arquivo
    // (RenderizarMagiasNivelAtual, AtualizarTextoSlots, OnClickDuplaMagia, etc.) continua operando
    // sobre os campos de instância de sempre (spellSlotsDisponiveis, limiteTruques, ...), copiados da
    // classe ativa por AplicarClasseAtiva() — não precisam saber que existe multiclasse por trás.
    private readonly Dictionary<string, DadosConjuracaoClasse> classesConjuradorasCache = new Dictionary<string, DadosConjuracaoClasse>();
    private string classeAtivaId = "";
    // Rastreia qual classe+nível teve o catálogo carregado por último, pra AplicarClasseAtiva() só
    // recarregar do disco quando a classe ativa realmente mudou (troca de aba ou de ficha).
    private string ultimoIdentificadorClasseCatalogo = "";

    private bool EmModoTesteMagias => showAllSpellsForTestingEditor;

    [Serializable] public class MagiaData { public int id; public string nome; public int level; public string xml; }
    // GrimorioWrapper/GrimorioSaveItem agora são as classes canônicas e públicas de
    // LocalFichaStorage.cs — não são mais declaradas aqui (evita a duplicata privada que só
    // "funcionava" por coincidência de nomes de campo via JsonUtility).

    // Dados de conjuração de UMA classe do personagem (resolvidos com o nível PRÓPRIO dessa classe,
    // nunca o nível total do personagem — essa era a causa do bug de multiclasse).
    private class DadosConjuracaoClasse
    {
        public string classId = "";
        public string className = "";
        public int    nivel;
        public Dictionary<int, int> slots = new Dictionary<int, int>();
        public int    limiteTruques;
        public int    limiteConhecidas  = 999;
        public int    limitePreparadas  = 999;
        public string textoPreparadas   = "Ilimitado";
        public int    maiorNivelPermitido;
        public bool   conjuraMagias;
        // Bruxo: nunca tem mais de 1 slot simultâneo em nenhum nível — conhece magias de vários
        // níveis mas só tem ~2 slots (sempre no nível mais alto). O limite por-nível-de-slot não se
        // aplica a ele, só o limite total de conhecidas.
        public bool   pactMagic;
        // Clérigo/Druida: preparam direto da lista inteira da classe, sem "aprender" magias.
        public bool   listaCompleta;
        public HashSet<string> nomesPermitidos = new HashSet<string>();
    }

    // ================================================================
    // INICIALIZAÇÃO
    // ================================================================

    void Awake()
    {
        if (filtroPesquisaInput != null) filtroPesquisaInput.onValueChanged.AddListener(OnFiltroPesquisaMudou);
        if (pesquisarButton != null)     pesquisarButton.onClick.AddListener(AplicarFiltroPesquisaAtual);
        if (expandButton != null)        expandButton.onClick.AddListener(ExpandirPainelPesquisa);
        if (collapseButton != null)      collapseButton.onClick.AddListener(RecolherPainelPesquisa);
        if (filtroGrimorioInput != null)
        {
            filtroGrimorioInput.onValueChanged.AddListener(OnFiltroGrimorioMudou);
            filtroGrimorioInput.onSubmit.AddListener(_ => AplicarFiltroGrimorio());
        }
        if (pesquisarGrimorioButton != null)  pesquisarGrimorioButton.onClick.AddListener(AplicarFiltroGrimorio);
        // Os chips de nível (catálogo/grimório) se auto-configuram via
        // FiltroChipsHelper.ConfigurarExpansao, chamado de InicializarListasFixas()/CarregarTudo()
        // — não precisam de wiring manual aqui.

        if (searchContentPanel != null) searchContentPanel.SetActive(true);
        if (expandButton != null)       expandButton.gameObject.SetActive(false);
        if (collapseButton != null)     collapseButton.gameObject.SetActive(true);

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
        Instance = this;
        GlobalFichaData.OnSessaoDeFichaIniciada += OnSessaoDeFichaIniciada;

        LimparDetalhes();
        EsconderTooltip();
        if (warningText) warningText.text = "";
        if (!gameObject.activeInHierarchy) return;

        TentarCarregarOuAtualizar();
    }

    void OnDisable()
    {
        GlobalFichaData.OnSessaoDeFichaIniciada -= OnSessaoDeFichaIniciada;
    }

    /// <summary>
    /// Gatilho independente de OnEnable/Start, pro caso deste passo virar um GameObject sempre
    /// ativo (mesma correção já aplicada em ClassSelector pro bug de CanvasGroup/layout preso após
    /// SetActive(false)-&gt;(true)) — nesse cenário OnEnable nunca mais dispara de novo, e sem isto
    /// o grimório/magias de uma ficha ANTERIOR continuariam em memória mesmo depois de criar/abrir
    /// outra ficha. GlobalFichaData.OnSessaoDeFichaIniciada dispara em TODA troca de ficha,
    /// independente do ciclo de vida do GameObject.
    /// </summary>
    private void OnSessaoDeFichaIniciada()
    {
        PrecisaCarregarMagias = true;
        if (gameObject.activeInHierarchy) TentarCarregarOuAtualizar();
    }

    // Chamado por CriacaoFichaProgressaoDnd.MostrarEtapa() sempre que a etapa de Magias fica ativa.
    // TentarCarregarOuAtualizar() já sabe detectar sozinho se a classe mudou desde o último
    // carregamento (ver comentário logo abaixo), mas só se for chamado de novo — sem isto, ele nunca
    // roda uma segunda vez se este GameObject for (ou vier a ser) sempre ativo, mesmo bug de
    // "OnEnable não redispara" já visto em ClassSelector/RaceSelector/FichaGeralManager nesta sessão.
    // Sem essa segunda chamada, o catálogo fica travado no estado "sem classe ainda" carregado pela
    // ativação prévia de 1 frame de CriacaoFichaProgressaoDnd.IniciarComRestauracaoCoroutine, mesmo
    // depois da classe (ex.: Bruxo) já ter sido escolhida e salva.
    public void ForcarRecarregamento()
    {
        if (gameObject.activeInHierarchy) TentarCarregarOuAtualizar();
    }

    private void TentarCarregarOuAtualizar()
    {
        int    fichaAtualId = GlobalFichaData.fichasDndId;
        bool   fichaMudou   = lastLoadedFichaDndId != fichaAtualId;
        // CriacaoFichaProgressaoDnd ativa todos os passos brevemente ao iniciar a criação da
        // ficha (pra pré-carregar dados), o que roda CarregarTudo() UMA vez antes do jogador
        // sequer ter escolhido uma classe — cacheando isLoaded=true com o catálogo vazio (nenhuma
        // magia passa no filtro sem classe) e ultimaClasseCarregada="". Sem checar a classe aqui
        // também (CarregarTudo já faz essa mesma checagem, mas só DEPOIS de decidirmos entrar
        // nele), a próxima vez que o jogador chega neste passo com a classe já escolhida cai no
        // atalho "já carregado" abaixo, que só atualiza o grimório — o catálogo de magias
        // disponíveis nunca é re-renderizado com o filtro certo, ficando vazio até sair e voltar
        // a editar a ficha (que passa pelo portão central e força PrecisaCarregarMagias=true).
        string classeAtual = GlobalFichaData.classePersonagem ?? "";
        bool   classeMudou = !string.Equals(classeAtual, ultimaClasseCarregada, StringComparison.OrdinalIgnoreCase);

        if (PrecisaCarregarMagias || !isLoaded || fichaMudou || classeMudou)
        {
            PrecisaCarregarMagias = false;
            StartCoroutine(CarregarTudo());
        }
        else
        {
            AtualizarListaGrimorio();
        }
    }

    // ================================================================
    // PAINEL DE PESQUISA
    // ================================================================

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

    // ================================================================
    // CARREGAMENTO
    // ================================================================

    private void LimparEstadoGrimorioLocal()
    {
        meuGrimorio.Clear();
    }

    public IEnumerator CarregarTudo()
    {
        int  fichaAtualId = GlobalFichaData.fichasDndId;
        bool fichaMudou   = lastLoadedFichaDndId != fichaAtualId;

        if (fichaMudou)
        {
            LimparEstadoGrimorioLocal();
            classeAtivaId = "";
        }

        meuGrimorio = new List<GrimorioSaveItem>();

        LocalFichaData snapshotAtual = null;
        int    pid = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string sid = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        if (fichaAtualId > 0)
            LocalFichaStorage.TryGetFichaSnapshot(sid, pid, GlobalFichaData.fichasGeneralId, fichaAtualId, out snapshotAtual);

        // Restaura classe e nível do snapshot antes de resolver as classes conjuradoras,
        // para o caso de carregar uma ficha existente sem passar pelo ClassSelector.
        if ((string.IsNullOrWhiteSpace(GlobalFichaData.classePersonagem) || GlobalFichaData.levelPersonagem <= 0)
            && snapshotAtual != null)
        {
            if (string.IsNullOrWhiteSpace(GlobalFichaData.classePersonagem))
            {
                var classPrimaria = snapshotAtual.classes?.PrimaryClass()?.class_name;
                if (!string.IsNullOrWhiteSpace(classPrimaria))
                    GlobalFichaData.classePersonagem = classPrimaria;
            }

            if (GlobalFichaData.levelPersonagem <= 0 && snapshotAtual.level_pc > 0)
                GlobalFichaData.levelPersonagem = snapshotAtual.level_pc;
        }

        ultimaClasseCarregada = GlobalFichaData.classePersonagem ?? "";

        ReconstruirCacheDeClassesConjuradoras(snapshotAtual);
        CarregarMagiasSalvasLocalmente(fichaAtualId);
        MigrarClasseOrigemAusente(snapshotAtual);

        lastLoadedFichaDndId = fichaAtualId;
        isLoaded = true;

        AplicarClasseAtiva();
        RenderizarAbasDeClasse();
        InicializarListasFixas();
        AtualizarListaGrimorio();
        yield return null;
    }

    // Monta classesConjuradorasCache a partir de snap.classes (fonte confiável de multiclasse — ver
    // MulticlassData.cs) — resolve o XML e os limites de CADA classe com o nível PRÓPRIO dela, nunca
    // o nível total do personagem. Preserva a aba já selecionada se ainda existir; senão prioriza a
    // classe primária, senão a primeira conjuradora encontrada.
    private void ReconstruirCacheDeClassesConjuradoras(LocalFichaData snapshot)
    {
        classesConjuradorasCache.Clear();

        List<ClasseData> classesDoPersonagem = snapshot?.classes;
        if (classesDoPersonagem == null || classesDoPersonagem.Count == 0)
        {
            // Sem snapshot ainda (ficha em criação, antes do primeiro save de classe) — usa a classe
            // corrente do fluxo de criação como única candidata, mesmo dado que ClassSelector já
            // publica em GlobalFichaData antes de persistir no disco.
            string nomeAtual  = GlobalFichaData.classePersonagem ?? "";
            int    nivelAtual = GlobalFichaData.levelPersonagem > 0 ? GlobalFichaData.levelPersonagem : 1;
            classesDoPersonagem = string.IsNullOrWhiteSpace(nomeAtual)
                ? new List<ClasseData>()
                : new List<ClasseData> { new ClasseData { class_id = nomeAtual, class_name = nomeAtual, level = nivelAtual, is_primary = true } };
        }

        foreach (var classeData in classesDoPersonagem)
        {
            if (classeData == null || string.IsNullOrWhiteSpace(classeData.class_name)) continue;

            string xml = TentarCarregarClasseLocalPorNome(classeData.class_name);
            if (string.IsNullOrEmpty(xml)) continue;

            int nivel = classeData.level > 0 ? classeData.level : 1;
            DadosConjuracaoClasse dados = ExtrairDadosConjuracao(xml, nivel);
            if (!dados.conjuraMagias) continue;

            dados.classId   = string.IsNullOrWhiteSpace(classeData.class_id) ? classeData.class_name : classeData.class_id;
            dados.className = classeData.class_name;
            dados.nivel     = nivel;
            dados.nomesPermitidos = ObterNomesMagiasPermitidas(xml, classeData.subclass_id ?? "");

            classesConjuradorasCache[dados.classId] = dados;
        }

        if (!classesConjuradorasCache.ContainsKey(classeAtivaId))
        {
            string primariaId = classesDoPersonagem.FirstOrDefault(c => c != null && c.is_primary)?.class_id;
            if (!string.IsNullOrWhiteSpace(primariaId) && classesConjuradorasCache.ContainsKey(primariaId))
                classeAtivaId = primariaId;
            else
                classeAtivaId = classesConjuradorasCache.Keys.FirstOrDefault() ?? "";
        }
    }

    // Copia os dados da classe conjuradora ATIVA (aba selecionada) pros campos de instância que o
    // resto do arquivo já usa (spellSlotsDisponiveis, limiteTruques, etc.) — assim
    // RenderizarMagiasNivelAtual/AtualizarTextoSlots/MostrarDetalhesMagia/OnClickDuplaMagia continuam
    // funcionando sem precisar saber que existe multiclasse por trás.
    private void AplicarClasseAtiva()
    {
        if (!classesConjuradorasCache.TryGetValue(classeAtivaId, out DadosConjuracaoClasse ativa))
        {
            spellSlotsDisponiveis.Clear();
            limiteTruques = 0; limiteMagiasConhecidas = 999; limiteMagiasPreparadas = 999;
            textoMagiasPreparadas = "Ilimitado"; maiorNivelMagiaPermitido = 0; classeConjuraMagias = false;
            todasAsMagias.Clear();
            magiasPorNivel.Clear();
            AtualizarTextoSlots();
            return;
        }

        spellSlotsDisponiveis    = new Dictionary<int, int>(ativa.slots);
        limiteTruques            = ativa.limiteTruques;
        limiteMagiasConhecidas   = ativa.limiteConhecidas;
        limiteMagiasPreparadas   = ativa.limitePreparadas;
        textoMagiasPreparadas    = ativa.textoPreparadas;
        maiorNivelMagiaPermitido = ativa.maiorNivelPermitido;
        classeConjuraMagias      = ativa.conjuraMagias;
        AtualizarTextoSlots();

        string identificadorAtual = ativa.classId + "|" + ativa.nivel + "|" + GlobalFichaData.fichasDndId;
        if (todasAsMagias.Count == 0 || ultimoIdentificadorClasseCatalogo != identificadorAtual)
        {
            CarregarMagiasLocaisFiltradas(ativa.nomesPermitidos);
            ultimoIdentificadorClasseCatalogo = identificadorAtual;
            Debug.Log("[SpellSelector] Catálogo carregado p/ " + ativa.className + " nível " + ativa.nivel + ". Total=" + todasAsMagias.Count + " Exibidos=" + magiasPorNivel.Values.Sum(x => x.Count));
        }

        if (ativa.listaCompleta)
            AutoSemearGrimorioListaCompleta(ativa);
    }

    // Clérigo/Druida (classes que preparam direto da lista completa, sem "aprender"): garante que
    // toda magia elegível já exista no grimório (preparada=false por padrão) — o jogador só liga o
    // toggle de preparo. Idempotente: só adiciona o que ainda não existe pra essa classe. O guard já
    // existente em OnClickDuplaMagia ("já está no seu grimório") cuida do resto sozinho.
    private void AutoSemearGrimorioListaCompleta(DadosConjuracaoClasse ativa)
    {
        var jaConhecidas = new HashSet<int>(meuGrimorio.Where(m => m.classeOrigemId == ativa.classId).Select(m => m.id));

        foreach (var lista in magiasPorNivel.Values)
        {
            foreach (var magia in lista)
            {
                if (jaConhecidas.Contains(magia.id)) continue;
                meuGrimorio.Add(new GrimorioSaveItem { instanceId = Guid.NewGuid().ToString(), id = magia.id, preparada = false, classeOrigemId = ativa.classId });
                jaConhecidas.Add(magia.id);
            }
        }
    }

    // Fichas salvas antes de classeOrigemId existir eram sempre monoclasse — atribui essas entradas
    // antigas (classeOrigemId vazio) à classe primária, uma única vez por carregamento.
    private void MigrarClasseOrigemAusente(LocalFichaData snapshot)
    {
        if (meuGrimorio == null || meuGrimorio.Count == 0) return;
        if (!meuGrimorio.Any(m => string.IsNullOrEmpty(m.classeOrigemId))) return;

        string idPrimaria = snapshot?.classes?.PrimaryClass()?.class_id;
        if (string.IsNullOrWhiteSpace(idPrimaria)) idPrimaria = classesConjuradorasCache.Keys.FirstOrDefault() ?? "";
        if (string.IsNullOrWhiteSpace(idPrimaria)) return;

        foreach (var item in meuGrimorio)
            if (string.IsNullOrEmpty(item.classeOrigemId))
                item.classeOrigemId = idPrimaria;
    }

    // Mesmo padrão de EquipmentSelector.AtualizarPaginacaoCatalogo/IrParaPaginaCatalogo — sem
    // componente de UI dedicado: só GetComponentInChildren<TMP_Text>/<Button> no prefab simples
    // (botão pai + texto filho). Só aparece com 2+ classes conjuradoras.
    private void RenderizarAbasDeClasse()
    {
        foreach (var go in abasClasseInstanciadas)
            if (go != null) Destroy(go);
        abasClasseInstanciadas.Clear();

        if (abaClasseContent == null || abaClassePrefab == null) return;
        if (classesConjuradorasCache.Count < 2) return;

        foreach (var dados in classesConjuradorasCache.Values.OrderBy(d => d.className))
        {
            string idCapturado = dados.classId;
            GameObject go = Instantiate(abaClassePrefab, abaClasseContent, false);
            go.SetActive(true);
            abasClasseInstanciadas.Add(go);

            TMP_Text texto = go.GetComponentInChildren<TMP_Text>();
            if (texto != null)
                texto.text = (idCapturado == classeAtivaId) ? $"<b><u>{dados.className}</u></b>" : dados.className;

            Button botao = go.GetComponentInChildren<Button>();
            if (botao != null)
            {
                botao.onClick.RemoveAllListeners();
                botao.onClick.AddListener(() => SelecionarClasseAtiva(idCapturado));
            }
        }

        Canvas.ForceUpdateCanvases();
        var rectAbas = abaClasseContent as RectTransform;
        if (rectAbas != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rectAbas);
    }

    public void SelecionarClasseAtiva(string classId)
    {
        if (classId == classeAtivaId || !classesConjuradorasCache.ContainsKey(classId)) return;
        classeAtivaId = classId;

        AplicarClasseAtiva();
        RenderizarAbasDeClasse();
        InicializarListasFixas();
        AtualizarListaGrimorio();
    }

    private void CarregarMagiasSalvasLocalmente(int fichaAtualId)
    {
        if (fichaAtualId <= 0) return;

        int    playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId  = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        if (!LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId, GlobalFichaData.fichasGeneralId, fichaAtualId, out LocalFichaData snapshot))
            return;

        Debug.Log($"[SpellSelector] Snapshot encontrado. fichaDndId={fichaAtualId} magiasVazio={snapshot.magias_json?.magias == null || snapshot.magias_json.magias.Count == 0} slots='{snapshot.slots_magias_json}'");

        if (snapshot.magias_json?.magias != null)
        {
            meuGrimorio = snapshot.magias_json.magias;
            GlobalFichaData.magiasJson = snapshot.magias_json;
            Debug.Log($"[SpellSelector] Grimório carregado. Total={meuGrimorio.Count} Preparadas={meuGrimorio.Count(m => m != null && m.preparada)}");
        }

        if (!string.IsNullOrWhiteSpace(snapshot.slots_magias_json))
            GlobalFichaData.slotsMagiasJson = snapshot.slots_magias_json;
    }

    private void CarregarMagiasLocaisFiltradas(HashSet<string> magiasPermitidas)
    {
        try
        {
            todasAsMagias.Clear();
            magiasPorNivel.Clear();

            string pasta = DndSrdLocalPath.Resolver(pastaMagiasLocalRelativa);
            if (!Directory.Exists(pasta))
            {
                Debug.LogError("[SpellSelector] Pasta local de magias não encontrada: " + pasta);
                return;
            }

            bool possuiRestricaoPorNome  = magiasPermitidas != null && magiasPermitidas.Count > 0;
            int  nivelMaximoPermitido    = maiorNivelMagiaPermitido;
            if (classeConjuraMagias && nivelMaximoPermitido <= 0) nivelMaximoPermitido = 1;

            foreach (string arquivo in Directory.GetFiles(pasta, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    string xml  = File.ReadAllText(arquivo, Encoding.UTF8).Trim().Replace("﻿", "");
                    XDocument doc  = XDocument.Parse(xml);
                    XElement  root = doc.Element("spell") ?? doc.Root;
                    if (root == null) continue;

                    string nome = root.Element("name")?.Value?.Trim();
                    if (string.IsNullOrEmpty(nome)) continue;

                    int level = 0;
                    string levelStr = root.Element("metadata")?.Element("level")?.Value ?? root.Element("level")?.Value;
                    int.TryParse(levelStr, out level);

                    int       id    = GerarIdDeterministico(root.Attribute("id")?.Value, nome);
                    MagiaData magia = new MagiaData { id = id, nome = nome, level = level, xml = xml };
                    todasAsMagias[id] = magia;

                    bool permitidoPorNome  = !possuiRestricaoPorNome || magiasPermitidas.Contains(nome.ToLower().Trim());
                    bool permitidoPorNivel = classeConjuraMagias
                        ? level <= Mathf.Max(0, nivelMaximoPermitido)
                        : level == 0 && limiteTruques > 0;

                    if (!EmModoTesteMagias && !(permitidoPorNome && permitidoPorNivel)) continue;

                    string catNivel = level == 0 ? "Truques" : $"Nível {level}";
                    if (!magiasPorNivel.ContainsKey(catNivel)) magiasPorNivel[catNivel] = new List<MagiaData>();
                    magiasPorNivel[catNivel].Add(magia);
                }
                catch (Exception eArquivo)
                {
                    Debug.LogWarning("[SpellSelector] Falha ao ler magia em '" + arquivo + "': " + eArquivo.Message);
                }
            }

            Debug.Log("[SpellSelector] Total=" + todasAsMagias.Count + " Exibidas=" + magiasPorNivel.Values.Sum(x => x.Count)
                + " RestricaoPorNome=" + possuiRestricaoPorNome + " NivelMaximo=" + nivelMaximoPermitido
                + " ClasseConjura=" + classeConjuraMagias + " TruquesLimite=" + limiteTruques);
        }
        catch (Exception e)
        {
            Debug.LogError("[SpellSelector] Erro ao carregar magias locais: " + e.Message);
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

    // ================================================================
    // XML DA CLASSE
    // ================================================================

    private string TentarCarregarClasseLocalPorNome(string nomeClasse)
    {
        try
        {
            string pasta = DndSrdLocalPath.Resolver(pastaClassesLocalRelativa);
            if (!Directory.Exists(pasta)) return "";

            string alvo = NormalizarChave(nomeClasse);
            if (string.IsNullOrWhiteSpace(alvo)) return "";

            foreach (string arquivo in Directory.GetFiles(pasta, "*.xml", SearchOption.TopDirectoryOnly))
            {
                if (Path.GetFileName(arquivo).ToLowerInvariant().Contains("template")) continue;
                string    xml  = File.ReadAllText(arquivo, Encoding.UTF8).Trim().Replace("﻿", "");
                XDocument doc  = XDocument.Parse(xml);
                XElement  root = doc.Element("class") ?? doc.Root;
                if (root == null) continue;
                if (NormalizarChave(root.Element("name")?.Value ?? "") == alvo) return xml;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SpellSelector] Falha ao carregar classe local: " + e.Message);
        }
        return "";
    }

    private HashSet<string> ObterNomesMagiasPermitidas(string classXml, string subclassId)
    {
        HashSet<string> permitidas = new HashSet<string>();
        if (string.IsNullOrEmpty(classXml)) return permitidas;
        try
        {
            XDocument doc = XDocument.Parse(classXml);

            // Fonte 1 (legado): <spell name="X"/> embutido no próprio XML da classe
            // (spells-per-level/all-available-spells). Mantido como reforço/override manual —
            // convive em união com a Fonte 2 abaixo, nunca reduz o que a classe pode aprender.
            foreach (var node in doc.Descendants("spell"))
            {
                var nameAttr = node.Attribute("name");
                if (nameAttr != null) permitidas.Add(nameAttr.Value.ToLower().Trim());
            }

            // Fonte 2: cada magia da pasta magias/ que lista esta classe no seu próprio
            // <metadata><classes><class>NomeEmInglês</class></classes></metadata> — a lista de quem
            // pode aprender uma magia vive uma vez só, na própria magia, em vez de duplicada e
            // redigitada em cada arquivo de classe.
            XElement root = doc.Element("class") ?? doc.Root;
            string classIdIngles = root?.Attribute("id")?.Value;
            if (!string.IsNullOrWhiteSpace(classIdIngles))
            {
                string classNomeIngles = string.Join(" ", classIdIngles.Split('-')
                    .Where(p => p.Length > 0)
                    .Select(p => char.ToUpperInvariant(p[0]) + p.Substring(1)));

                foreach (string nomePermitido in ObterNomesMagiasPorClasseTag(classNomeIngles))
                    permitidas.Add(nomePermitido);
            }
        }
        catch { }
        return permitidas;
    }

    // Varre magias/ inteira (mesma pasta/formato que CarregarMagiasLocaisFiltradas já varre pra
    // montar o catálogo) e devolve, em minúsculo, o <name> de toda magia cujo
    // <metadata><classes><class>...</class></classes> contenha nomeClasseIngles — comparação sem
    // acento/maiúscula via NormalizarChave, igual ao resto do arquivo.
    private HashSet<string> ObterNomesMagiasPorClasseTag(string nomeClasseIngles)
    {
        HashSet<string> resultado = new HashSet<string>();
        string alvo = NormalizarChave(nomeClasseIngles);
        if (string.IsNullOrEmpty(alvo)) return resultado;

        string pasta = DndSrdLocalPath.Resolver(pastaMagiasLocalRelativa);
        if (!Directory.Exists(pasta)) return resultado;

        foreach (string arquivo in Directory.GetFiles(pasta, "*.xml", SearchOption.AllDirectories))
        {
            try
            {
                string xml  = File.ReadAllText(arquivo, Encoding.UTF8).Trim().Replace("﻿", "");
                XDocument doc  = XDocument.Parse(xml);
                XElement  root = doc.Element("spell") ?? doc.Root;
                if (root == null) continue;

                bool listaEssaClasse = root.Element("metadata")?.Element("classes")?
                    .Elements("class").Any(c => NormalizarChave(c.Value) == alvo) ?? false;
                if (!listaEssaClasse) continue;

                string nome = root.Element("name")?.Value?.Trim();
                if (!string.IsNullOrEmpty(nome)) resultado.Add(nome.ToLower().Trim());
            }
            catch { }
        }
        return resultado;
    }

    // Extrai slots/limites de UMA classe no nível informado (sempre o nível PRÓPRIO dessa classe,
    // nunca o total do personagem) — substitui o antigo ExtrairSpellSlotsELimites, que escrevia
    // direto nos campos de instância e só entendia uma classe por vez.
    private DadosConjuracaoClasse ExtrairDadosConjuracao(string classXml, int nivelDaClasse)
    {
        var dados = new DadosConjuracaoClasse();
        if (string.IsNullOrEmpty(classXml)) return dados;

        try
        {
            XDocument doc = XDocument.Parse(classXml);

            // Bloco <spellcasting> — ignora tags vazias (<spellcasting />) usadas como placeholder.
            // <preparation-type>/<known-spells count="X"> nunca aparecem em classe real (só no
            // templateClasse.xml não usado) — os limites de verdade vêm da progressão por nível abaixo.
            var spellcastingNode = doc.Descendants("spellcasting").FirstOrDefault();
            if (spellcastingNode != null && spellcastingNode.HasElements)
                dados.conjuraMagias = true;

            dados.pactMagic = DetectarPactMagic(doc);

            var lvlNode = doc.Descendants("level-progression").Descendants("level")
                .FirstOrDefault(l => (string)l.Attribute("number") == nivelDaClasse.ToString());

            if (lvlNode != null)
            {
                var slotsNode = lvlNode.Element("spell-slots");
                if (slotsNode != null)
                {
                    foreach (var slotNode in slotsNode.Elements("slot"))
                    {
                        if (int.TryParse(slotNode.Attribute("level")?.Value, out int spellLvl) &&
                            int.TryParse(slotNode.Value, out int count) && count > 0)
                        {
                            dados.slots[spellLvl] = count;
                            dados.conjuraMagias = true;
                            if (spellLvl > dados.maiorNivelPermitido) dados.maiorNivelPermitido = spellLvl;
                        }
                    }
                }

                // Bruxo (Magia de Pacto): tag dedicada em vez do <spell-slots><slot> genérico, já que
                // ele nunca tem mais de um nível de slot simultâneo — <pact-magic-slots count="X"
                // slot-level="Y" />. Convive com <spell-slots> acima (uma classe usa uma ou outra).
                var pactSlotsNode = lvlNode.Element("pact-magic-slots");
                if (pactSlotsNode != null &&
                    int.TryParse(pactSlotsNode.Attribute("slot-level")?.Value, out int pactSpellLvl) &&
                    int.TryParse(pactSlotsNode.Attribute("count")?.Value, out int pactCount) && pactCount > 0)
                {
                    dados.slots[pactSpellLvl] = pactCount;
                    dados.conjuraMagias = true;
                    dados.pactMagic = true;
                    if (pactSpellLvl > dados.maiorNivelPermitido) dados.maiorNivelPermitido = pactSpellLvl;
                }

                var knownLvlNode = lvlNode.Element("known-spells");
                if (knownLvlNode != null)
                {
                    if (int.TryParse(knownLvlNode.Element("cantrips")?.Value, out int t)) dados.limiteTruques = t;
                    if (dados.limiteTruques > 0) dados.conjuraMagias = true;

                    var skNode = knownLvlNode.Element("spells-known");
                    if (skNode != null)
                    {
                        if (int.TryParse(skNode.Value, out int mk)) dados.limiteConhecidas = mk;
                    }
                    else
                    {
                        // Sem <spells-known>: classe prepara direto da lista completa (Clérigo/Druida)
                        // em vez de "aprender" um número fixo de magias.
                        dados.listaCompleta = true;
                    }

                    var prepNode = knownLvlNode.Element("prepared");
                    if (prepNode != null)
                    {
                        dados.textoPreparadas = prepNode.Value;
                        if (int.TryParse(prepNode.Value, out int mp))
                            dados.limitePreparadas = mp;
                        else if (dados.listaCompleta)
                            dados.limitePreparadas = AvaliarFormulaPreparadas(prepNode.Value, nivelDaClasse);
                    }

                    // Override explícito opcional — sem isto o teto de nível já é derivado do maior
                    // <slot>/<pact-magic-slots> encontrado acima (suficiente pro Bruxo, onde slot-level
                    // e nível máximo de magia conhecida são sempre iguais); só importa se algum dia
                    // existir uma classe onde os dois divergem.
                    var maxLvlNode = knownLvlNode.Element("max-spell-level");
                    if (maxLvlNode != null && int.TryParse(maxLvlNode.Value, out int maxLvlOverride))
                        dados.maiorNivelPermitido = maxLvlOverride;
                }
            }

            // Fallback formato XML antigo
            if (dados.slots.Count == 0)
            {
                var oldSlotsNode = doc.Descendants("spell-slots").FirstOrDefault()?.Element($"level-{nivelDaClasse}");
                if (oldSlotsNode != null)
                {
                    foreach (var slotNode in oldSlotsNode.Elements())
                    {
                        string name = slotNode.Name.LocalName;
                        if (name.StartsWith("spell-level-") && int.TryParse(name.Replace("spell-level-", ""), out int spellLvl))
                        {
                            if (int.TryParse(slotNode.Value, out int count) && count > 0)
                            {
                                dados.slots[spellLvl] = count;
                                dados.conjuraMagias = true;
                                if (spellLvl > dados.maiorNivelPermitido) dados.maiorNivelPermitido = spellLvl;
                            }
                        }
                    }
                }
            }

            if (dados.conjuraMagias && dados.maiorNivelPermitido <= 0)
                dados.maiorNivelPermitido = 1;
        }
        catch { }

        return dados;
    }

    // Bruxo (Pact Magic) nunca tem mais de 1 <slot> simultâneo em nenhum nível (1 a 20) — casters
    // normais (Bardo/Clérigo/Feiticeiro/Mago/Druida) já mostram 2+ <slot> a partir do nível de classe
    // 3. Não dá pra detectar olhando só o nível ATUAL do personagem (todo mundo tem 1 slot só nos
    // níveis 1-2) — precisa varrer a progressão inteira da classe.
    private static bool DetectarPactMagic(XDocument doc)
    {
        // <pact-magic-slots> é inequívoco — só existe pra classes de Magia de Pacto.
        if (doc.Descendants("level-progression").Descendants("level").Any(l => l.Element("pact-magic-slots") != null))
            return true;

        bool encontrouSlots = false;
        foreach (var lvl in doc.Descendants("level-progression").Descendants("level"))
        {
            var slotsNode = lvl.Element("spell-slots");
            if (slotsNode == null) continue;
            int count = slotsNode.Elements("slot").Count();
            if (count == 0) continue;
            encontrouSlots = true;
            if (count >= 2) return false;
        }
        return encontrouSlots;
    }

    // Avalia fórmulas como "SAB + Nível de Clérigo" (Clérigo/Druida) — reconhece um código de
    // atributo (soma o modificador) e a palavra "Nível" (soma o nível DESSA classe — já correto em
    // multiclasse, porque nivelDaClasse é sempre o nível próprio da classe, não o total). Se não
    // reconhecer nada na fórmula, devolve 999 (sem limite) em vez de travar a UI num valor errado.
    private int AvaliarFormulaPreparadas(string formula, int nivelDaClasse)
    {
        if (string.IsNullOrWhiteSpace(formula)) return 999;

        string formulaUpper       = formula.ToUpperInvariant();
        string formulaNormalizada = NormalizarChave(formula);
        int    total          = 0;
        bool   reconheceuAlgo = false;

        string[] codigosAtributo = { "FOR", "DEX", "CON", "INT", "SAB", "CAR" };
        foreach (string codigo in codigosAtributo)
        {
            if (formulaUpper.Contains(codigo))
            {
                total += ObterModificadorAtributo(codigo);
                reconheceuAlgo = true;
                break;
            }
        }

        if (formulaNormalizada.Contains("nivel"))
        {
            total += nivelDaClasse;
            reconheceuAlgo = true;
        }

        return reconheceuAlgo ? Mathf.Max(0, total) : 999;
    }

    // Sem helper genérico de modificador de atributo no projeto — replica o mesmo somatório manual
    // já usado em BackgroundSelector.cs/ClassSelector.cs (base + bônus de raça/classe/antecedente/
    // talento) e aplica a fórmula padrão de 5e: piso((total-10)/2).
    private int ObterModificadorAtributo(string codigo)
    {
        int totalAtributo = codigo switch
        {
            "FOR" => GlobalFichaData.forca        + GlobalFichaData.bonusRacaDict["FOR"] + GlobalFichaData.bonusClasseDict["FOR"] + GlobalFichaData.bonusAntecedenteDict["FOR"] + GlobalFichaData.bonusTalentoDict["FOR"],
            "DEX" => GlobalFichaData.destreza     + GlobalFichaData.bonusRacaDict["DEX"] + GlobalFichaData.bonusClasseDict["DEX"] + GlobalFichaData.bonusAntecedenteDict["DEX"] + GlobalFichaData.bonusTalentoDict["DEX"],
            "CON" => GlobalFichaData.constituicao + GlobalFichaData.bonusRacaDict["CON"] + GlobalFichaData.bonusClasseDict["CON"] + GlobalFichaData.bonusAntecedenteDict["CON"] + GlobalFichaData.bonusTalentoDict["CON"],
            "INT" => GlobalFichaData.inteligencia + GlobalFichaData.bonusRacaDict["INT"] + GlobalFichaData.bonusClasseDict["INT"] + GlobalFichaData.bonusAntecedenteDict["INT"] + GlobalFichaData.bonusTalentoDict["INT"],
            "SAB" => GlobalFichaData.sabedoria    + GlobalFichaData.bonusRacaDict["SAB"] + GlobalFichaData.bonusClasseDict["SAB"] + GlobalFichaData.bonusAntecedenteDict["SAB"] + GlobalFichaData.bonusTalentoDict["SAB"],
            "CAR" => GlobalFichaData.carisma      + GlobalFichaData.bonusRacaDict["CAR"] + GlobalFichaData.bonusClasseDict["CAR"] + GlobalFichaData.bonusAntecedenteDict["CAR"] + GlobalFichaData.bonusTalentoDict["CAR"],
            _ => 10
        };
        return Mathf.FloorToInt((totalAtributo - 10) / 2f);
    }

    private void ExtractMagiaDetails(string xml, out string escola, out string tempo, out string duracao)
    {
        ExtractMagiaDetailsCompletos(xml, out escola, out tempo, out duracao, out _, out _, out _);
    }

    // Versão completa usada pelo tooltip (ConfigurarHoverTooltip) — mesmos campos que
    // FichaGeralManager.cs mostra na visualização da ficha (nível/tempo/alcance/duração/componentes
    // + descrição completa).
    private void ExtractMagiaDetailsCompletos(string xml, out string escola, out string tempo, out string duracao, out string alcance, out string componentes, out string descricao)
    {
        escola = "-"; tempo = "-"; duracao = "-"; alcance = "-"; componentes = "-"; descricao = "Sem detalhes.";
        if (string.IsNullOrEmpty(xml)) return;
        try
        {
            XDocument doc = XDocument.Parse(xml);
            escola      = doc.Descendants("school").FirstOrDefault()?.Value ?? "-";
            tempo       = doc.Descendants("time").FirstOrDefault()?.Value ?? doc.Descendants("casting-time").FirstOrDefault()?.Value ?? "-";
            duracao     = doc.Descendants("duration").FirstOrDefault()?.Value ?? "-";
            alcance     = doc.Descendants("range").FirstOrDefault()?.Value ?? "-";
            componentes = doc.Descendants("components").FirstOrDefault()?.Value ?? "-";
            descricao   = doc.Descendants("description").FirstOrDefault()?.Value ?? doc.Descendants("summary").FirstOrDefault()?.Value ?? "Sem detalhes no XML.";
        }
        catch { }
    }

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

    // ================================================================
    // FILTROS E SELEÇÃO DE NÍVEL
    // ================================================================

    private void OnFiltroPesquisaMudou(string texto)
    {
        filtroPesquisaAtual = NormalizarChave(texto);
        AplicarFiltroPesquisaAtual();
    }

    private void OnFiltroGrimorioMudou(string texto)
    {
        filtroGrimorioAtual = NormalizarChave(texto);
        AtualizarListaGrimorio();
    }

    private void AplicarFiltroGrimorio()
    {
        if (filtroGrimorioInput != null)
            filtroGrimorioAtual = NormalizarChave(filtroGrimorioInput.text);
        AtualizarListaGrimorio();
    }

    private void AplicarFiltroPesquisaAtual()
    {
        if (filtroPesquisaInput != null)
            filtroPesquisaAtual = NormalizarChave(filtroPesquisaInput.text);
        RenderizarMagiasNivelAtual();
    }

    private bool MagiaCombinaFiltro(MagiaData magia, string filtroNormalizado)
    {
        if (string.IsNullOrWhiteSpace(filtroNormalizado)) return true;
        return NormalizarChave(magia.nome).Contains(filtroNormalizado)
            || NormalizarChave(magia.level == 0 ? "truques" : $"nivel {magia.level}").Contains(filtroNormalizado)
            || NormalizarChave(magia.xml).Contains(filtroNormalizado);
    }

    private static string NormalizarChave(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "";
        string semAcento = valor.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(semAcento.Length);
        foreach (char c in semAcento)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    // ================================================================
    // RENDERIZAÇÃO
    // ================================================================

    private void InicializarListasFixas()
    {
        // Tags fixas de nível (Truques, Nível 1-9) — construídas uma vez por ficha carregada,
        // independente do que a classe realmente pode conjurar (mesmo espírito das tags fixas de
        // FichaGeralManager.TagsFiltroMagias). Grimório usa o mesmo conjunto de tags.
        ConfigurarFiltroChipsNivel();
        ConfigurarFiltroChipsNivelGrimorio();
        RenderizarMagiasNivelAtual();
    }

    private void ConfigurarFiltroChipsNivel()
    {
        FiltroChipsHelper.ConfigurarExpansao(
            btnExpandirFiltroNivel, btnColapsarFiltroNivel, conteudoFiltroNivel,
            filtroChipPrefab, TagsFiltroNivelMagia,
            chipsFiltroNivelInstanciados, niveisAtivos, RenderizarMagiasNivelAtual);
    }

    private void ConfigurarFiltroChipsNivelGrimorio()
    {
        FiltroChipsHelper.ConfigurarExpansao(
            btnExpandirFiltroNivelGrimorio, btnColapsarFiltroNivelGrimorio, conteudoFiltroNivelGrimorio,
            filtroChipPrefab, TagsFiltroNivelMagia,
            chipsFiltroNivelGrimorioInstanciados, niveisGrimorioAtivos, AtualizarListaGrimorio);
    }

    private void RenderizarMagiasNivelAtual()
    {
        foreach (Transform child in catalogContent) Destroy(child.gameObject);
        listaUI_MagiasVisiveis.Clear();

        GarantirVisibilidadeContainer(catalogContent, "SpellCatalog");

        // Multi-seleção: itera todos os níveis presentes no catálogo carregado (Truques primeiro,
        // depois Nível 1..9), pulando os que não estão nos chips ativos — lista plana, sem
        // separador visual entre níveis (mesmo padrão do EquipmentSelector).
        foreach (var nivel in magiasPorNivel.Keys.OrderBy(k => k == "Truques" ? 0 : int.Parse(k.Replace("Nível ", ""))))
        {
            if (niveisAtivos.Count > 0 && !niveisAtivos.Contains(nivel)) continue;

            foreach (var magia in magiasPorNivel[nivel])
            {
                if (!MagiaCombinaFiltro(magia, filtroPesquisaAtual)) continue;

                GameObject go       = Instantiate(spellPrefab, catalogContent, false);
                go.SetActive(true);
                go.transform.localScale = Vector3.one;
                SpellRowUI magiaRow = go.GetComponent<SpellRowUI>();
                ExtractMagiaDetailsCompletos(magia.xml, out string escola, out string tempo, out string duracao, out string alcance, out string componentes, out string descricaoMagia);
                magiaRow.SetupItem(magia.id, magia.nome, nivel, escola, tempo, duracao, false, string.Empty, false, OnClickUnicaMagia, OnClickDuplaMagia, null);
                magiaRow.CategoryName = nivel;
                listaUI_MagiasVisiveis.Add(magiaRow);

                string subtituloTooltipCatalogo = $"{nivel} | {tempo} | {alcance} | {duracao} | {componentes}";
                ConfigurarHoverTooltip(go, magia.nome, subtituloTooltipCatalogo, descricaoMagia);
            }
        }

        Debug.Log("[SpellSelector] Níveis='" + string.Join(",", niveisAtivos) + "' Magias=" + listaUI_MagiasVisiveis.Count + " Filtro='" + filtroPesquisaAtual + "'");

        Canvas.ForceUpdateCanvases();
        var rect = catalogContent as RectTransform;
        if (rect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

        if (_reativacaoMagiasCoroutine != null) StopCoroutine(_reativacaoMagiasCoroutine);
        _reativacaoMagiasCoroutine = StartCoroutine(ReforcarAtivacaoMagiasPorFrames());
    }

    private void AtualizarListaGrimorio()
    {
        foreach (Transform child in grimorioContent) Destroy(child.gameObject);

        foreach (var mItem in meuGrimorio)
        {
            if (mItem.classeOrigemId != classeAtivaId) continue;
            if (!todasAsMagias.TryGetValue(mItem.id, out MagiaData dbMagia)) continue;
            if (!string.IsNullOrWhiteSpace(filtroGrimorioAtual) &&
                !NormalizarChave(dbMagia.nome).Contains(filtroGrimorioAtual)) continue;
            string nivelGrimorio = dbMagia.level == 0 ? "Truques" : $"Nível {dbMagia.level}";
            if (niveisGrimorioAtivos.Count > 0 && !niveisGrimorioAtivos.Contains(nivelGrimorio)) continue;

            GameObject go  = Instantiate(grimorioPrefab != null ? grimorioPrefab : spellPrefab, grimorioContent, false);
            go.SetActive(true);
            ExtractMagiaDetailsCompletos(dbMagia.xml, out string escola, out string tempo, out string duracao, out string alcance, out string componentes, out string descricaoMagia);
            SpellRowUI row = go.GetComponent<SpellRowUI>();
            row.SetupItem(dbMagia.id, dbMagia.nome, nivelGrimorio, escola, tempo, duracao, true, mItem.instanceId, mItem.preparada, OnClickUnicaMagia, OnClickDuplaMagia, OnMudarStatusPreparacao);

            string subtituloTooltipGrimorio = $"{nivelGrimorio} | {tempo} | {alcance} | {duracao} | {componentes}";
            ConfigurarHoverTooltip(go, dbMagia.nome, subtituloTooltipGrimorio, descricaoMagia);
        }

        // Contador de magias conhecidas/truques em slotsText — atualiza ao vivo sempre que o
        // grimório muda (adicionar/remover magia), não só na troca de aba/classe.
        AtualizarTextoSlots();
    }

    // ================================================================
    // CALLBACKS DE CLIQUE
    // ================================================================

    private void OnClickUnicaMagia(SpellRowUI row) { MostrarDetalhesMagia(row.ItemId); }

    private void OnClickDuplaMagia(SpellRowUI row)
    {
        if (row.IsGrimorio)
        {
            meuGrimorio.RemoveAll(m => m.instanceId == row.InstanceId);
            if (warningText) warningText.text = "";
        }
        else
        {
            if (meuGrimorio.Any(m => m.id == row.ItemId && m.classeOrigemId == classeAtivaId))
            {
                MostrarAviso("Esta magia já está no seu grimório!");
                return;
            }

            bool isTruque           = (row.CategoryName == "Truques");
            int  countTruques       = meuGrimorio.Count(m => m.classeOrigemId == classeAtivaId && todasAsMagias.ContainsKey(m.id) && todasAsMagias[m.id].level == 0);
            int  countMagiasNormais = meuGrimorio.Count(m => m.classeOrigemId == classeAtivaId && todasAsMagias.ContainsKey(m.id) && todasAsMagias[m.id].level > 0);

            // Limite de magias conhecidas é sempre por TOTAL (truques e magias, cada um com seu
            // próprio teto vindo do XML da classe — os mesmos valores mostrados em slotsText) —
            // nunca por quantidade de slots de um nível específico. Slots servem só pra CONJURAR,
            // não limitam quantas magias a classe pode CONHECER (regra real de 5e); o sub-limite
            // antigo "magias desse nível <= slots desse nível" nunca foi correto pra nenhuma classe,
            // só ficou mais óbvio com o Bruxo (poucos slots, sempre no nível mais alto).
            if (!EmModoTesteMagias)
            {
                if (isTruque)
                {
                    if (limiteTruques > 0 && countTruques >= limiteTruques)
                    {
                        MostrarAviso($"Você já atingiu o limite de {limiteTruques} truque(s)!");
                        return;
                    }
                }
                else
                {
                    if (limiteMagiasConhecidas < 999 && countMagiasNormais >= limiteMagiasConhecidas)
                    {
                        MostrarAviso($"Você já atingiu o limite de {limiteMagiasConhecidas} magias conhecidas!");
                        return;
                    }
                }
            }

            meuGrimorio.Add(new GrimorioSaveItem { instanceId = Guid.NewGuid().ToString(), id = row.ItemId, preparada = false, classeOrigemId = classeAtivaId });
            if (warningText) warningText.text = "";
        }

        AtualizarListaGrimorio();
        SalvarMagiasGeral();
    }

    private void OnMudarStatusPreparacao(string instanceId, bool querPreparar)
    {
        if (querPreparar && !EmModoTesteMagias)
        {
            int  countPreparadas   = meuGrimorio.Count(m => m.classeOrigemId == classeAtivaId && m.preparada && todasAsMagias.ContainsKey(m.id) && todasAsMagias[m.id].level > 0);
            var  magiaSendoAlterada = meuGrimorio.FirstOrDefault(i => i.instanceId == instanceId);
            bool ehTruque          = magiaSendoAlterada != null && todasAsMagias.ContainsKey(magiaSendoAlterada.id) && todasAsMagias[magiaSendoAlterada.id].level == 0;

            if (!ehTruque && countPreparadas >= limiteMagiasPreparadas && limiteMagiasPreparadas < 999)
            {
                MostrarAviso($"Você só pode preparar {limiteMagiasPreparadas} magias simultaneamente!");
                AtualizarListaGrimorio();
                return;
            }
        }

        var item = meuGrimorio.FirstOrDefault(i => i.instanceId == instanceId);
        if (item != null)
        {
            item.preparada = querPreparar;
            AtualizarListaGrimorio();
            SalvarMagiasGeral();
        }
    }

    // ================================================================
    // DETALHES E AVISOS
    // ================================================================

    private void MostrarDetalhesMagia(int id)
    {
        if (!todasAsMagias.TryGetValue(id, out MagiaData magia)) return;
        var sb = new StringBuilder();
        sb.AppendLine($"<size=150%><b>{magia.nome.ToUpper()}</b></size>");
        sb.AppendLine($"<color=#777777><i>{(magia.level == 0 ? "Truque" : $"Magia de Nível {magia.level}")}</i></color>\n");
        if (!string.IsNullOrEmpty(magia.xml))
        {
            try
            {
                XDocument doc     = XDocument.Parse(magia.xml);
                string    tempo   = doc.Descendants("time").FirstOrDefault()?.Value ?? doc.Descendants("casting-time").FirstOrDefault()?.Value;
                string    alcance = doc.Descendants("range").FirstOrDefault()?.Value;
                string    comp    = doc.Descendants("components").FirstOrDefault()?.Value;
                string    duracao = doc.Descendants("duration").FirstOrDefault()?.Value;
                if (!string.IsNullOrEmpty(tempo))   sb.AppendLine($"<b>Tempo:</b> {tempo}");
                if (!string.IsNullOrEmpty(alcance)) sb.AppendLine($"<b>Alcance:</b> {alcance}");
                if (!string.IsNullOrEmpty(comp))    sb.AppendLine($"<b>Componentes:</b> {comp}");
                if (!string.IsNullOrEmpty(duracao)) sb.AppendLine($"<b>Duração:</b> {duracao}\n");
                string desc = doc.Descendants("description").FirstOrDefault()?.Value ?? doc.Descendants("summary").FirstOrDefault()?.Value;
                if (!string.IsNullOrEmpty(desc)) sb.AppendLine(desc);
            }
            catch { sb.AppendLine("Detalhes indisponíveis."); }
        }
        detailsText.text = sb.ToString();
    }

    private void MostrarAviso(string msg)
    {
        if (warningText)
        {
            warningText.text = $"<color=red>Atenção:</color> {msg}";
            StopCoroutine(nameof(LimparAviso));
            StartCoroutine(LimparAviso());
        }
        else Debug.LogWarning("Aviso de Limite: " + msg);
    }

    private IEnumerator LimparAviso() { yield return new WaitForSeconds(3.5f); if (warningText) warningText.text = ""; }

    private void LimparDetalhes() { if (detailsText) detailsText.text = "Selecione uma magia."; }

    private void AtualizarTextoSlots()
    {
        if (slotsText == null) return;

        if (!classeConjuraMagias)
        {
            slotsText.text = "Sem slots de magia.";
            return;
        }

        // Contador de magias conhecidas/truques (aba ativa) — mesma contagem que
        // OnClickDuplaMagia já usa pra checar o limite, só pra exibição aqui.
        int countTruques    = meuGrimorio.Count(m => m.classeOrigemId == classeAtivaId && todasAsMagias.ContainsKey(m.id) && todasAsMagias[m.id].level == 0);
        int countConhecidas = meuGrimorio.Count(m => m.classeOrigemId == classeAtivaId && todasAsMagias.ContainsKey(m.id) && todasAsMagias[m.id].level > 0);

        var partes = new List<string>();
        if (limiteTruques > 0)
            partes.Add($"Truques: {countTruques}/{limiteTruques}");
        if (limiteMagiasConhecidas < 999)
            partes.Add($"Magias conhecidas: {countConhecidas}/{limiteMagiasConhecidas}");
        foreach (int nivel in spellSlotsDisponiveis.Keys.OrderBy(k => k))
            partes.Add($"<b>{nivel}°</b> ×{spellSlotsDisponiveis[nivel]}");

        slotsText.text = partes.Count > 0 ? string.Join(" - ", partes) : "Sem slots de magia.";
    }

    // ================================================================
    // HELPERS DE LAYOUT
    // ================================================================

    private IEnumerator ReforcarAtivacaoMagiasPorFrames()
    {
        for (int frame = 0; frame < 8; frame++)
        {
            yield return null;
            for (int i = 0; i < listaUI_MagiasVisiveis.Count; i++)
            {
                var row = listaUI_MagiasVisiveis[i];
                if (row == null) continue;
                bool estavaInativo = !row.gameObject.activeSelf;
                row.ForceVisibleState();
                if (estavaInativo && row.gameObject.activeSelf)
                    Debug.Log("[SpellSelector] Row reativado no frame " + frame + " idx=" + i + " nome='" + row.name + "'");
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
                cg.alpha = 1f; cg.interactable = true; cg.blocksRaycasts = true;
            }
            _canvasGroupsTmp.Clear();
            atual = atual.parent;
            nivel++;
        }
        Debug.Log("[" + tag + "] Container visibilidade reforçada.");
    }

    // ================================================================
    // SALVAR
    // ================================================================

    public void SalvarMagiasGeral() { StartCoroutine(RotinaSalvarMagias()); }

    public IEnumerator RotinaSalvarMagias()
    {
        if (GlobalFichaData.fichasDndId <= 0) yield break;

        GrimorioWrapper grimorioParaSalvar = new GrimorioWrapper { magias = meuGrimorio };

        // Soma os slots de TODAS as classes conjuradoras (não só a aba ativa) — FichaGeralManager lê
        // este campo pra mostrar o total de slots disponíveis do personagem inteiro, não de uma
        // classe isolada.
        Dictionary<int, int> slotsCombinados = new Dictionary<int, int>();
        foreach (var dadosClasse in classesConjuradorasCache.Values)
            foreach (var kvp in dadosClasse.slots)
                slotsCombinados[kvp.Key] = slotsCombinados.TryGetValue(kvp.Key, out int atual) ? atual + kvp.Value : kvp.Value;

        string jsonSlots  = "{";
        foreach (var kvp in slotsCombinados) jsonSlots += $"\"{kvp.Key}\":{kvp.Value},";
        jsonSlots = jsonSlots.Length > 1 ? jsonSlots.TrimEnd(',') + "}" : "{}";

        GlobalFichaData.magiasJson      = grimorioParaSalvar;
        GlobalFichaData.slotsMagiasJson = jsonSlots;

        Debug.Log($"[SpellSelector] Salvando. Total={meuGrimorio.Count} Preparadas={meuGrimorio.Count(m => m != null && m.preparada)} SlotsJson={jsonSlots}");

        int    playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId  = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        bool   ok       = LocalFichaStorage.SaveSpells(steamId, playerId, GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, jsonSlots, grimorioParaSalvar);

        if (ok)
        {
            Debug.Log("Grimório salvo localmente!");
            if (LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId, GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId, out LocalFichaData snapshot))
                Debug.Log($"[SpellSelector] Snapshot após salvar. slots='{snapshot.slots_magias_json}' magias='{JsonUtility.ToJson(snapshot.magias_json)}'");
        }
        else
            Debug.LogError("Erro ao salvar magias localmente.");

        yield return null;
    }
}
