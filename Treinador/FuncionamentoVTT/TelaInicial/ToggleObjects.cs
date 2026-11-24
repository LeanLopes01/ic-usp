using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ToggleObjects : MonoBehaviour
{
    [Header("GameObjects a alternar (em ordem)")]
    public GameObject[] objectsToToggle;

    [Header("Botões de alternância")]
    public Button[] toggleButtons;

    [Header("Botões de Menu (Tabs)")]
    [Tooltip("Mesmo índice de objectsToToggle. Controla o visual (cor/underline) de cada aba.")]
    public MenuTabButton[] menuTabButtons;

    [Header("Animação de Transição")]
    [SerializeField, Min(0.05f)] private float duration = 0.25f;
    [SerializeField, Min(0.9f)] private float enterStartScale = 0.98f;
    [SerializeField, Min(1f)] private float exitEndScale = 1.01f;

    [Header("Opções de Fetch & Fichas")]
    [Tooltip("Índice da tela inicial (TelaInicio).")]
    public int startScreenIndex = 0;
    [Tooltip("Índice do objeto que representa a TelaFichas.")]
    public int fichasIndex = 0;
    [Tooltip("Índice da tela de criação/edição de ficha.")]
    public int criarFichaIndex = -1;
    public bool fetchOnShow = true;
    public bool fetchForce = true;

    [Header("Tela Sala Criada")]
    [Tooltip("Índice da TelaSalaCriada dentro de objectsToToggle.")]
    public int telaSalaCriadaIndex = -1;
    [Tooltip("Objeto de fundo que será desativado ao entrar na TelaSalaCriada.")]
    public GameObject fundoParaDesativarNaTelaSalaCriada;

    [Header("Background Geral")]
    [Tooltip("Background comum, ativado em qualquer tela, exceto na TelaSalaCriada.")]
    [SerializeField] private GameObject backgroundGeral;
    [Tooltip("Barra superior que será ocultada ao entrar na TelaSalaCriada.")]
    [SerializeField] private GameObject barraSuperior;
    [Tooltip("Botão que restaura a barra superior quando ela está oculta.")]
    [SerializeField] private Button botaoMostrarBarra;

    [Header("Recarregar (Reload)")]
    [Tooltip("Botão na barra superior que abre/fecha contentReload.")]
    [SerializeField] private Button botaoAbrirReload;
    [Tooltip("Content com os dois botões abaixo — começa fechado, alternado por botaoAbrirReload.")]
    [SerializeField] private GameObject contentReload;
    [Tooltip("Reinicia só a TELA ATUAL de forma suave. Caso especial pra tela de criação/edição de " +
        "ficha (índice criarFichaIndex): salva o progresso feito até agora (mesmo autosave síncrono " +
        "de FichaEditorManager.FlushAutoSavePendente) e reabre a MESMA ficha do zero, restaurando as " +
        "opções já escolhidas — não perde o que já foi preenchido. Pra qualquer outra tela, é um " +
        "fallback genérico: desativa e reativa o GameObject da tela (dispara OnDisable/OnEnable em " +
        "tudo que tiver dentro — mesmo mecanismo de salvar-ao-sair/recarregar-ao-entrar já usado em " +
        "outras telas, ex: EquipmentSelector).")]
    [SerializeField] private Button botaoReloadTelaAtual;
    [Tooltip("Recarrega a CENA inteira (equivalente a um F5 de navegador) — salva o que estiver " +
        "pendente antes de recarregar.")]
    [SerializeField] private Button botaoReloadTotal;
    [Tooltip("Índice da tela de Sala dentro de objectsToToggle. -1 desativa este caso especial do " +
        "reload de tela atual (MapManager.RecarregarMapaAtual).")]
    public int salaIndex = -1;
    [Tooltip("MapManager da tela de Sala — recarregado (grid/paredes/tokens, sem perder tokens) " +
        "quando o reload de tela atual acontece com salaIndex ativo.")]
    [SerializeField] private MapManager mapManager;
    [Tooltip("Índice da tela de Campanha dentro de objectsToToggle. -1 desativa este caso especial " +
        "do reload de tela atual (CampanhaMapManager.RecarregarMapaAtual).")]
    public int campanhaIndex = -1;
    [Tooltip("CampanhaMapManager da tela de Campanha — recarregado (grid/paredes/tokens, sem perder " +
        "tokens) quando o reload de tela atual acontece com campanhaIndex ativo.")]
    [SerializeField] private CampanhaMapManager campanhaMapManager;

    [Header("Overlay: Sidebar Expandida (esquerda → direita)")]
    [SerializeField] private RectTransform sidebarPanel;
    [SerializeField] private Button sidebarToggleButton;

    [Header("Overlay: Perfil (direita → esquerda)")]
    [SerializeField] private RectTransform profilePanel;
    [SerializeField] private Button profileToggleButton;

    [Header("Botões Fixos com Painel em Hover")]
    [Tooltip("Configure quantos botões quiser, igual ao padrão de objectsToToggle/toggleButtons.")]
    public List<BotaoHoverConfig> botoesHover = new List<BotaoHoverConfig>();
    [Tooltip("Tempo de crescimento/recolhimento do painel de hover.")]
    [SerializeField, Min(0.05f)] private float duracaoAnimacaoHover = 0.2f;
    [Tooltip("Atraso antes de fechar o painel após o mouse sair do botão/painel — evita fechar ao passar de um para o outro.")]
    [SerializeField, Min(0f)] private float atrasoFecharHover = 0.12f;

    private bool sidebarAberto;
    private bool profileAberto;
    private Coroutine sidebarRoutine;
    private Coroutine profileRoutine;
    private float sidebarShownX;
    private float sidebarHiddenX;
    private float profileShownX;
    private float profileHiddenX;

    private GameObject currentScreen;
    private Coroutine transitionRoutine;
    private bool initialized;

    /// <summary>Tela de nível superior visível agora (um dos elementos de objectsToToggle) — usado
    /// por sistemas globais que precisam saber "em que tela o jogador está" sem ficar assinando
    /// OnTelaAlterada só pra ler o valor uma vez (ex: TutorialManager resolvendo qual lista de
    /// passos tocar quando o botão de tutorial é clicado).</summary>
    public GameObject TelaAtual => currentScreen;

    /// <summary>Disparado sempre que a tela "atual" muda (via NavegarParaTela, transição animada ou
    /// NormalizeExternalActiveState) — assinantes comparam a tela recebida com a própria referência
    /// pra saber se acabaram de ficar visíveis ou de sair de cena (ver TelaSalaUIManager,
    /// CampanhasManager).</summary>
    public event System.Action<GameObject> OnTelaAlterada;
    private readonly Dictionary<RectTransform, Coroutine> hoverAnimRoutines = new Dictionary<RectTransform, Coroutine>();

    private System.DateTime _inicioTelaAtual;

    void Start()
    {
        InitializeLogic();
        ConfigurarOverlays();
        ConfigurarBotoesHover();
        ConfigurarReload();
        initialized = true;
    }

    private void OnEnable()
    {
        if (!initialized) return;
        NormalizeExternalActiveState();
    }

    private void LateUpdate()
    {
        if (transitionRoutine != null) return;

        if (currentScreen != null && currentScreen.activeSelf)
        {
            CanvasGroup group = EnsureCanvasGroup(currentScreen);
            if (group.alpha < 0.99f || !group.interactable || !group.blocksRaycasts)
            {
                SetScreenVisible(currentScreen, true);
            }
            return;
        }

        NormalizeExternalActiveState();
    }

    private void InitializeLogic()
    {
        DefinirTelaAtual(GetStartScreen());

        for (int i = 0; i < objectsToToggle.Length; i++)
        {
            if (objectsToToggle[i] == null) continue;

            CanvasGroup group = EnsureCanvasGroup(objectsToToggle[i]);
            bool isCurrent = (objectsToToggle[i] == currentScreen);

            objectsToToggle[i].SetActive(isCurrent);
            group.alpha = isCurrent ? 1f : 0f;
            group.interactable = isCurrent;
            group.blocksRaycasts = isCurrent;

            if (objectsToToggle[i].transform is RectTransform rect)
                rect.localScale = Vector3.one;
        }

        AtualizarEstadoDoFundoParaTelaSalaCriada();
        AtualizarMenuTabButtons(GetIndiceDaTelaAtual());
    }

    public void ShowScreen(int index)
    {
        if (index < 0 || index >= objectsToToggle.Length) return;

        GameObject nextScreen = objectsToToggle[index];
        if (nextScreen == null) return;

        AtualizarEstadoDoFundoParaTelaSalaCriada(index);
        AtualizarMenuTabButtons(index);
        
        if (index == criarFichaIndex && FichaEditorManager.Instance != null)
        {
            FichaEditorManager.Instance.OpenForNewFicha();
        }

        if (FichaEditorManager.Instance != null && index != criarFichaIndex)
        {
            FichaEditorManager.Instance.ClearSelectionContext();
        }

        if (fetchOnShow && index == fichasIndex)
        {
            TriggerFetch();
        }

        if (currentScreen == nextScreen)
        {
            SetScreenVisible(nextScreen, true);
            return;
        }

        if (transitionRoutine != null)
            StopCoroutine(transitionRoutine);

        transitionRoutine = StartCoroutine(TransitionRoutine(nextScreen));
    }

    private IEnumerator TransitionRoutine(GameObject nextScreen)
    {
        CanvasGroup nextGroup = EnsureCanvasGroup(nextScreen);
        RectTransform nextRect = nextScreen.transform as RectTransform;

        CanvasGroup currentGroup = null;
        RectTransform currentRect = null;

        if (currentScreen != null)
        {
            currentGroup = EnsureCanvasGroup(currentScreen);
            currentRect = currentScreen.transform as RectTransform;
            currentGroup.interactable = false;
            currentGroup.blocksRaycasts = false;
        }

        nextScreen.SetActive(true);
        if (!nextScreen.activeSelf)
            nextScreen.SetActive(true);

        nextGroup.alpha = 0f;
        nextGroup.interactable = false;
        nextGroup.blocksRaycasts = false;

        if (nextRect != null)
            nextRect.localScale = Vector3.one * enterStartScale;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            if (currentGroup != null) currentGroup.alpha = 1f - eased;
            nextGroup.alpha = eased;

            if (currentRect != null)
                currentRect.localScale = Vector3.Lerp(Vector3.one, Vector3.one * exitEndScale, eased);

            if (nextRect != null)
                nextRect.localScale = Vector3.Lerp(Vector3.one * enterStartScale, Vector3.one, eased);

            yield return null;
        }

        if (currentScreen != null)
        {
            currentGroup.alpha = 0f;
            currentScreen.SetActive(false);
        }

        nextGroup.alpha = 1f;
        nextGroup.interactable = true;
        nextGroup.blocksRaycasts = true;
        if (nextRect != null) nextRect.localScale = Vector3.one;

        DefinirTelaAtual(nextScreen);
        transitionRoutine = null;
    }

    private void TriggerFetch()
    {
        if (DataManager.Instance == null) return;
        if (!fetchForce && DataManager.Instance.HasData) return;

        int pid = DataManager.Instance.GetCurrentId();
        if (pid > 0)
            DataManager.Instance.StartFetch(pid, fetchForce);
    }

    private GameObject FindFirstActiveScreen()
    {
        foreach (var obj in objectsToToggle)
        {
            if (obj != null && obj.activeSelf) return obj;
        }
        return (objectsToToggle.Length > 0) ? objectsToToggle[0] : null;
    }

    private GameObject GetStartScreen()
    {
        if (objectsToToggle == null || objectsToToggle.Length == 0)
            return null;

        int idx = Mathf.Clamp(startScreenIndex, 0, objectsToToggle.Length - 1);
        if (objectsToToggle[idx] != null)
            return objectsToToggle[idx];

        for (int i = 0; i < objectsToToggle.Length; i++)
        {
            if (objectsToToggle[i] != null)
                return objectsToToggle[i];
        }

        return null;
    }

    private CanvasGroup EnsureCanvasGroup(GameObject target)
    {
        if (!target.TryGetComponent<CanvasGroup>(out CanvasGroup group))
            group = target.AddComponent<CanvasGroup>();
        return group;
    }

    private void SetScreenVisible(GameObject target, bool visible)
    {
        if (target == null) return;

        CanvasGroup group = EnsureCanvasGroup(target);
        target.SetActive(visible);
        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;

        if (target.transform is RectTransform rect)
            rect.localScale = Vector3.one;
    }

    private void DefinirTelaAtual(GameObject novaTela)
    {
        if (novaTela == currentScreen) return;

        RegistrarTempoNaTelaAtual();
        currentScreen = novaTela;
        _inicioTelaAtual = System.DateTime.UtcNow;
        if (novaTela != null) PostHogTracker.DefinirContextoAtual(novaTela.name);
        OnTelaAlterada?.Invoke(novaTela);
    }

    private void RegistrarTempoNaTelaAtual()
    {
        if (currentScreen == null) return;

        double segundos = (System.DateTime.UtcNow - _inicioTelaAtual).TotalSeconds;
        PostHogTracker.TrackTempoNaTela(currentScreen.name, segundos);
    }

    private void NormalizeExternalActiveState()
    {
        GameObject active = currentScreen;
        bool activeStillValid = active != null && active.activeInHierarchy;

        if (!activeStillValid)
            active = FindFirstActiveScreen();

        if (active == null)
        {
            active = GetStartScreen();
        }

        if (active == null) return;

        for (int i = 0; i < objectsToToggle.Length; i++)
        {
            GameObject obj = objectsToToggle[i];
            if (obj == null) continue;

            SetScreenVisible(obj, obj == active);
        }

        DefinirTelaAtual(active);
        int indiceAtivo = GetIndiceDaTelaAtual();
        AtualizarEstadoDoFundoParaTelaSalaCriada(indiceAtivo);
        AtualizarMenuTabButtons(indiceAtivo);
    }

    private void AtualizarMenuTabButtons(int indiceTela)
    {
        if (menuTabButtons == null) return;

        for (int i = 0; i < menuTabButtons.Length; i++)
        {
            if (menuTabButtons[i] == null) continue;

            if (i == indiceTela)
                menuTabButtons[i].SelectTab();
            else
                menuTabButtons[i].DeselectTab();
        }
    }

    private int GetIndiceDaTelaAtual()
    {
        if (currentScreen == null || objectsToToggle == null)
            return -1;

        for (int i = 0; i < objectsToToggle.Length; i++)
        {
            if (objectsToToggle[i] == currentScreen)
                return i;
        }

        return -1;
    }

    private void AtualizarEstadoDoFundoParaTelaSalaCriada()
    {
        AtualizarEstadoDoFundoParaTelaSalaCriada(GetIndiceDaTelaAtual());
    }

    private void AtualizarEstadoDoFundoParaTelaSalaCriada(int indiceTela)
    {
        bool estaNaTelaSalaCriada = telaSalaCriadaIndex >= 0 && indiceTela == telaSalaCriadaIndex;

        if (fundoParaDesativarNaTelaSalaCriada != null)
            fundoParaDesativarNaTelaSalaCriada.SetActive(!estaNaTelaSalaCriada);

        if (backgroundGeral != null)
            backgroundGeral.SetActive(!estaNaTelaSalaCriada);

        if (barraSuperior != null)
            barraSuperior.SetActive(!estaNaTelaSalaCriada);

        if (botaoMostrarBarra != null)
            botaoMostrarBarra.gameObject.SetActive(estaNaTelaSalaCriada && barraSuperior != null && !barraSuperior.activeSelf);
    }

    public void MostrarBarraSuperior()
    {
        if (barraSuperior != null)
            barraSuperior.SetActive(true);

        if (botaoMostrarBarra != null)
            botaoMostrarBarra.gameObject.SetActive(false);
    }

    public void AbrirLink(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        Application.OpenURL(url);
    }

    private void ConfigurarBotoesHover()
    {
        if (botoesHover == null) return;

        // Detecta configs diferentes acidentalmente apontando pro MESMO botaoFixo/painelHover (erro
        // comum ao duplicar uma entrada da lista no Inspector e esquecer de trocar a referência) —
        // isso faria os dois hovers ficarem fisicamente ligados ao mesmo GameObject, cada um com seu
        // próprio HoverZoneState reagindo aos mesmos eventos de ponteiro, explicando comportamento
        // cruzado entre botões que deveriam ser independentes.
        var botoesJaVistos = new Dictionary<Button, string>();
        var paineisJaVistos = new Dictionary<RectTransform, string>();

        foreach (var config in botoesHover)
        {
            if (config == null || config.botaoFixo == null) continue;

            string nomeConfig = string.IsNullOrWhiteSpace(config.nomeAnotacao) ? config.botaoFixo.name : config.nomeAnotacao;
            if (botoesJaVistos.TryGetValue(config.botaoFixo, out string outroNomeBotao))
                Debug.LogWarning($"[ToggleObjects] botaoFixo '{config.botaoFixo.name}' está referenciado em DUAS entradas de botoesHover ('{outroNomeBotao}' e '{nomeConfig}') — provavelmente um erro de cópia no Inspector, isso liga os dois hovers ao mesmo botão.");
            else
                botoesJaVistos[config.botaoFixo] = nomeConfig;

            if (config.painelHover != null)
            {
                if (paineisJaVistos.TryGetValue(config.painelHover, out string outroNomePainel))
                    Debug.LogWarning($"[ToggleObjects] painelHover '{config.painelHover.name}' está referenciado em DUAS entradas de botoesHover ('{outroNomePainel}' e '{nomeConfig}') — provavelmente um erro de cópia no Inspector, isso liga os dois hovers ao mesmo painel.");
                else
                    paineisJaVistos[config.painelHover] = nomeConfig;
            }

            int destino = config.telaDestinoIndex;
            
            config.botaoFixo.interactable = destino >= 0;
            if (destino >= 0)
                config.botaoFixo.onClick.AddListener(() => ShowScreen(destino));

            ConfigurarBotoesSala(config.botoesSala);

            if (config.painelHover == null) continue;

            SetPivotPreservandoPosicao(config.painelHover, new Vector2(config.painelHover.pivot.x, 1f));
            
            // CORREÇÃO APLICADA: 0.01f ao invés de 0f no Y inicial para não quebrar componentes
            config.painelHover.localScale = new Vector3(1f, 0.01f, 1f);
            config.painelHover.gameObject.SetActive(false);

            var estadoCompartilhado = new HoverZoneState();

            var zonaBotao = config.botaoFixo.gameObject.AddComponent<HoverZone>();
            zonaBotao.manager = this;
            zonaBotao.painel = config.painelHover;
            zonaBotao.estado = estadoCompartilhado;

            var zonaPainel = config.painelHover.gameObject.AddComponent<HoverZone>();
            zonaPainel.manager = this;
            zonaPainel.painel = config.painelHover;
            zonaPainel.estado = estadoCompartilhado;
        }
    }

    private void ConfigurarBotoesSala(List<BotaoSalaHoverConfig> botoesSala)
    {
        if (botoesSala == null) return;

        foreach (var sala in botoesSala)
        {
            if (sala == null || sala.botao == null) continue;

            GameObject alvo = sala.telaDestino;
            sala.botao.interactable = alvo != null;
            if (alvo == null) continue;

            sala.botao.onClick.AddListener(() => NavegarParaTela(alvo));
        }
    }

    public void NavegarParaTela(GameObject alvo)
    {
        if (alvo == null) return;

        int indice = System.Array.IndexOf(objectsToToggle, alvo);
        if (indice >= 0)
        {
            ShowScreen(indice);
            return;
        }

        foreach (var tela in objectsToToggle)
            if (tela != null) SetScreenVisible(tela, false);

        SetScreenVisible(alvo, true);
        DefinirTelaAtual(alvo);
    }

    public void MostrarTelaDiretamente(GameObject alvo)
    {
        if (alvo == null) return;

        foreach (var tela in objectsToToggle)
            if (tela != null) SetScreenVisible(tela, false);

        SetScreenVisible(alvo, true);
        DefinirTelaAtual(alvo);

        int indice = System.Array.IndexOf(objectsToToggle, alvo);
        AtualizarEstadoDoFundoParaTelaSalaCriada(indice);
        AtualizarMenuTabButtons(indice);
    }

    private static void SetPivotPreservandoPosicao(RectTransform rect, Vector2 novoPivot)
    {
        if (rect == null) return;

        Vector2 tamanho = rect.rect.size;
        Vector2 deltaPivot = rect.pivot - novoPivot;
        Vector3 deltaPosicao = new Vector3(deltaPivot.x * tamanho.x, deltaPivot.y * tamanho.y);

        rect.pivot = novoPivot;
        rect.localPosition -= deltaPosicao;
    }

    internal void IniciarAnimacaoHover(RectTransform painel, bool mostrar)
    {
        if (painel == null) return;

        if (hoverAnimRoutines.TryGetValue(painel, out Coroutine ativa) && ativa != null)
            StopCoroutine(ativa);

        hoverAnimRoutines[painel] = StartCoroutine(AnimarEscalaHover(painel, mostrar));
    }

    private IEnumerator AnimarEscalaHover(RectTransform painel, bool mostrar)
    {
        if (mostrar) painel.gameObject.SetActive(true);

        Vector3 escalaInicial = painel.localScale;
        
        // CORREÇÃO APLICADA: 0.01f ao invés de 0f para não quebrar layouts e botões
        Vector3 escalaFinal = mostrar ? Vector3.one : new Vector3(1f, 0.01f, 1f); 
        float elapsed = 0f;

        while (elapsed < duracaoAnimacaoHover)
        {
            elapsed += Time.unscaledDeltaTime;
            painel.localScale = Vector3.Lerp(escalaInicial, escalaFinal, Mathf.Clamp01(elapsed / duracaoAnimacaoHover));
            yield return null;
        }

        painel.localScale = escalaFinal;
        if (!mostrar) painel.gameObject.SetActive(false);
    }

    internal IEnumerator FecharHoverComAtraso(RectTransform painel, HoverZoneState estado)
    {
        // CORREÇÃO APLICADA: WaitForSecondsRealtime para não travar em caso de queda de frame ou pause
        yield return new WaitForSecondsRealtime(atrasoFecharHover);

        if (estado.refCount <= 0)
            IniciarAnimacaoHover(painel, false);

        estado.fecharCoroutine = null;
    }

    // ── Recarregar (Reload) ──────────────────────────────────────────────────────

    private void ConfigurarReload()
    {
        if (contentReload != null) contentReload.SetActive(false);

        if (botaoAbrirReload != null) botaoAbrirReload.onClick.AddListener(ToggleContentReload);
        if (botaoReloadTelaAtual != null) botaoReloadTelaAtual.onClick.AddListener(RecarregarTelaAtual);
        if (botaoReloadTotal != null) botaoReloadTotal.onClick.AddListener(RecarregarAplicativoTotal);
    }

    public void ToggleContentReload()
    {
        if (contentReload == null) return;
        contentReload.SetActive(!contentReload.activeSelf);
    }

    private void FecharContentReload()
    {
        if (contentReload != null) contentReload.SetActive(false);
    }

    /// <summary>Reinicia só a tela atual, tentando preservar o que o jogador já fez. Caso especial
    /// pra criação/edição de ficha (índice criarFichaIndex) — reaproveita
    /// FichaEditorManager.OpenFichaForEdit, que já salva de forma síncrona o que estiver pendente
    /// (FlushAutoSavePendente) ANTES de resetar e recarregar a mesma ficha do zero: efetivamente
    /// "salva até onde foi e recarrega mantendo as opções escolhidas", exatamente como pedido. Sem
    /// nenhuma ficha ainda salva (fichasDndId ainda <= 0 — jogador só preencheu o nome, digamos, mas
    /// nunca passou por um autosave) não tem o que restaurar de verdade, então só reabre limpo como
    /// ficha nova. Caso especial análogo pra Sala (salaIndex) e Campanha (campanhaIndex): chama
    /// MapManager.RecarregarMapaAtual / CampanhaMapManager.RecarregarMapaAtual, que salvam os tokens
    /// do mapa ativo ANTES de recarregar grid/paredes/tokens do zero — recarregar essas telas pelo
    /// fallback genérico abaixo (desativar/reativar o GameObject raiz) não bastaria, já que o mapa em
    /// si não é recriado só por isso. Qualquer outra tela cai no fallback genérico: desativa e
    /// reativa o GameObject raiz da tela, disparando OnDisable/OnEnable em tudo dentro dela — mesmo
    /// mecanismo de "salvar ao sair, recarregar ao entrar" que outras telas (ex: EquipmentSelector)
    /// já implementam por conta própria; pra telas que ainda não têm essa lógica, o efeito prático é
    /// só um refresh visual, sem garantia de preservar estado — não dá pra prometer mais que isso de
    /// forma genérica sem conhecer os detalhes de cada tela.</summary>
    public void RecarregarTelaAtual()
    {
        FecharContentReload();

        int indiceAtual = GetIndiceDaTelaAtual();
        if (indiceAtual == criarFichaIndex && criarFichaIndex >= 0 && FichaEditorManager.Instance != null)
        {
            if (GlobalFichaData.fichasDndId > 0)
            {
                FichaEditorManager.Instance.OpenFichaForEdit(
                    GlobalFichaData.fichasGeneralId, GlobalFichaData.fichasDndId,
                    GlobalFichaData.nomePersonagem, GlobalFichaData.levelPersonagem);
            }
            else
            {
                FichaEditorManager.Instance.OpenForNewFicha();
            }
            return;
        }

        if (indiceAtual == salaIndex && salaIndex >= 0 && mapManager != null)
        {
            mapManager.RecarregarMapaAtual();
            return;
        }

        if (indiceAtual == campanhaIndex && campanhaIndex >= 0 && campanhaMapManager != null)
        {
            campanhaMapManager.RecarregarMapaAtual();
            return;
        }

        if (currentScreen != null)
        {
            SetScreenVisible(currentScreen, false);
            SetScreenVisible(currentScreen, true);
        }
    }

    /// <summary>Recarrega a cena inteira — equivalente a um F5 de navegador. Salva de forma síncrona
    /// o que estiver pendente (mesmo autosave de ficha usado em RecarregarTelaAtual) antes de
    /// recarregar, já que a cena inteira reinicia do zero e não há "reabrir onde estava" possível
    /// aqui — o app volta pra tela inicial normal, não pra tela em que o jogador estava.
    ///
    /// Não precisa proteger o NetworkManager aqui: Salas/NetworkManagerPersistente.cs (componente no
    /// próprio GameObject do NetworkManager na cena) já garante que ele sobrevive à recarga e que
    /// nenhuma cópia duplicada chega a rodar Awake() — evitando o erro de SteamClient.Init duplicado
    /// (ver comentário desse arquivo pro raciocínio completo) sem deixar objetos órfãos.</summary>
    public void RecarregarAplicativoTotal()
    {
        FecharContentReload();

        if (GlobalFichaData.fichasDndId > 0)
            FichaSaveManager.Instance?.SalvarLocalSincronamente();

        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    private void ConfigurarOverlays()
    {
        if (sidebarPanel != null)
        {
            sidebarShownX  = sidebarPanel.anchoredPosition.x;
            sidebarHiddenX = sidebarShownX - LarguraPanel(sidebarPanel);
            MoverOverlay(sidebarPanel, sidebarHiddenX);
            sidebarPanel.gameObject.SetActive(false);
            sidebarAberto = false;
            sidebarToggleButton?.onClick.AddListener(ToggleSidebar);
        }

        if (profilePanel != null)
        {
            profileShownX  = profilePanel.anchoredPosition.x;
            profileHiddenX = profileShownX + LarguraPanel(profilePanel);
            MoverOverlay(profilePanel, profileHiddenX);
            profilePanel.gameObject.SetActive(false);
            profileAberto = false;
            profileToggleButton?.onClick.AddListener(ToggleProfile);
        }
    }

    private static float LarguraPanel(RectTransform panel)
    {
        float w = panel.sizeDelta.x > 0 ? panel.sizeDelta.x : 400f;
        return w + 20f;
    }

    private static void MoverOverlay(RectTransform panel, float x)
    {
        Vector2 pos = panel.anchoredPosition;
        pos.x = x;
        panel.anchoredPosition = pos;
    }

    public void ToggleSidebar()
    {
        if (sidebarAberto)
            FecharOverlay(ref sidebarRoutine, sidebarPanel, ref sidebarAberto, sidebarShownX, sidebarHiddenX);
        else
            AbrirOverlay(ref sidebarRoutine, sidebarPanel, ref sidebarAberto, sidebarHiddenX, sidebarShownX);
    }

    public void ToggleProfile()
    {
        if (profileAberto)
            FecharOverlay(ref profileRoutine, profilePanel, ref profileAberto, profileShownX, profileHiddenX);
        else
            AbrirOverlay(ref profileRoutine, profilePanel, ref profileAberto, profileHiddenX, profileShownX);
    }

    private void AbrirOverlay(ref Coroutine rotina, RectTransform panel,
                               ref bool aberto, float de, float para)
    {
        if (rotina != null) StopCoroutine(rotina);
        MoverOverlay(panel, de);
        panel.gameObject.SetActive(true);
        aberto = true;
        rotina = StartCoroutine(SlideOverlay(panel, de, para, onFim: null));
    }

    private void FecharOverlay(ref Coroutine rotina, RectTransform panel,
                                ref bool aberto, float de, float para)
    {
        if (rotina != null) StopCoroutine(rotina);
        aberto = false;
        RectTransform panelLocal = panel;
        rotina = StartCoroutine(SlideOverlay(panel, de, para,
            onFim: () => panelLocal.gameObject.SetActive(false)));
    }

    private IEnumerator SlideOverlay(RectTransform panel, float de, float para, System.Action onFim)
    {
        float elapsed = 0f;
        Vector2 pos = panel.anchoredPosition;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            pos.x = Mathf.Lerp(de, para, t);
            panel.anchoredPosition = pos;
            yield return null;
        }

        pos.x = para;
        panel.anchoredPosition = pos;
        onFim?.Invoke();
    }
}