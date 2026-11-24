using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Um passo do tutorial guiado — autorado inteiramente no Inspector. "alvo" é o MESMO
/// GameObject já referenciado em BarraFerramentasManager (arraste o mesmo objeto); nulo = passo
/// centralizado, sem caixa de destaque (útil pra uma introdução/despedida).</summary>
[Serializable]
public class TutorialPassoData
{
    public string titulo;
    [TextArea(2, 5)] public string corpo;
    [Tooltip("GameObject alvo deste passo — o retângulo de destaque é calculado a partir de TODOS os " +
        "RectTransforms visíveis dentro dele (BarraFerramentasManager.ObterRetanguloTela(GameObject)), " +
        "não de uma RectTransform específica — mais robusto quando anchor/pivot do objeto não refletem " +
        "exatamente a área renderizada (evita o balão ficar por cima do próprio alvo).")]
    public GameObject alvo;
    public DirecaoTooltip ladoPreferido = DirecaoTooltip.Direita;
    [Tooltip("Distância em pixels entre a borda do alvo e a borda do balão, específica deste passo.")]
    public float distanciaAlvo = 12f;
}

/// <summary>Passos de tutorial de UMA tela específica — a tela de nível superior é a mesma
/// registrada em ToggleObjects.objectsToToggle (arraste o mesmo GameObject). IniciarTutorial()
/// resolve qual lista tocar comparando ToggleObjects.TelaAtual contra "tela" de cada elemento
/// desta lista.</summary>
[Serializable]
public class TutorialTelaData
{
    public GameObject tela;
    public List<TutorialPassoData> passos = new List<TutorialPassoData>();
}

/// <summary>
/// Orquestra o tutorial guiado (passo a passo, sequencial) e as dicas contextuais avulsas
/// (mostradas uma vez por chave). Não desenha nada — só decide QUAL passo/dica mostrar e QUANDO,
/// delegando a renderização pro TutorialOverlayUI (uGUI, instanciado sob demanda a partir de
/// overlayPrefab — ver GarantirOverlayInstanciado). Dispara sozinho na primeira vez que
/// o jogador entra numa sala (Steam ou local), e pode ser reaberto a qualquer momento via
/// IniciarTutorial() — o tutorial é GERAL (qualquer tela pode ter o seu, ver tutoriaisPorTela):
/// tanto o botão "?" da barra da Sala (BarraFerramentasManager.AbrirTutorial) quanto o botão geral
/// próprio deste manager (botaoTutorialGeral, visível em qualquer tela) chamam o mesmo
/// IniciarTutorial(), que sempre resolve os passos da tela ATUAL (ToggleObjects.TelaAtual) no
/// momento do clique.
/// </summary>
public class TutorialManager : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Prefab do overlay (Assets/Scripts/UI/Tutorial) — instanciado sob demanda dentro de " +
        "canvasPrincipal (ver abaixo) na primeira vez que o tutorial for aberto, e reaproveitado " +
        "depois disso.")]
    [SerializeField] private TutorialOverlayUI overlayPrefab;
    [Tooltip("Canvas onde o overlay do tutorial é instanciado — PRECISA ser o Canvas principal da " +
        "UI (o mesmo que hospeda BarraFerramentasManager/ToggleObjects), não o Canvas do grid da " +
        "Sala. Deixe vazio só se a cena realmente tiver um Canvas só; em Salas há pelo menos dois " +
        "(o principal + o do grid), e FindFirstObjectByType<Canvas>() (fallback abaixo) não garante " +
        "pegar o certo — foi isso que fazia o overlay aparecer 'dentro' do canvas do grid em vez do " +
        "canvas principal.")]
    [SerializeField] private Canvas canvasPrincipal;
    private TutorialOverlayUI overlay; // instância em runtime — ver GarantirOverlayInstanciado()

    [SerializeField] private SteamLobbyManager steamLobbyManager;
    [SerializeField] private BarraFerramentasManager barraFerramentas;
    [SerializeField] private ToggleObjects toggleObjects;
    [Tooltip("Botão geral (visível em qualquer tela, ex: cabeçalho persistente) que dispara o " +
        "tutorial da tela atual — opcional, além (não em vez) do botão \"?\" já existente na barra " +
        "da Sala.")]
    [SerializeField] private Button botaoTutorialGeral;

    [Header("Passos por tela (Inspector-autorado)")]
    [Tooltip("Um elemento por tela de nível superior (ToggleObjects.objectsToToggle) que deve ter " +
        "tutorial guiado próprio. IniciarTutorial() toca os passos do elemento cuja 'tela' bate com " +
        "ToggleObjects.TelaAtual no momento do clique.")]
    [SerializeField] private List<TutorialTelaData> tutoriaisPorTela = new List<TutorialTelaData>();

    [Header("Gatilho automático")]
    [Tooltip("Espera esse tempo depois de entrar na sala antes de abrir sozinho, pra dar tempo da " +
        "cena/UI se estabilizar (mesmo cuidado de TelaSalaUIManager com callbacks do Steam).")]
    [SerializeField] private float atrasoAutoInicio = 0.75f;

    private const string KEY_TUTORIAL_VISTO = "tutorial_guiado_visto";

    public static TutorialManager Instancia { get; private set; }

    private int passoAtual = -1;
    private List<TutorialPassoData> passosAtuais;
    // Setado nos callbacks do Steam (que não devem fazer trabalho síncrono), consumido em Update() —
    // mesmo padrão de TelaSalaUIManager._deveMudarParaSala/_salaPendente.
    private bool devoAutoIniciar;
    private string chaveDicaAtual;

    private void Awake()
    {
        Instancia = this;
        if (toggleObjects == null) toggleObjects = FindFirstObjectByType<ToggleObjects>();
    }

    private void OnDestroy()
    {
        if (Instancia == this) Instancia = null;

        if (overlay != null)
        {
            overlay.OnProximo     -= AvancarPasso;
            overlay.OnVoltar      -= VoltarPasso;
            overlay.OnPular       -= EncerrarTutorial;
            overlay.OnEntendiDica -= EncerrarDicaAtual;
        }
    }

    private void OnEnable()
    {
        if (steamLobbyManager == null) steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();
        if (barraFerramentas == null) barraFerramentas = FindFirstObjectByType<BarraFerramentasManager>();

        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso       += OnSalaDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso      += OnSalaDisponivel;
            steamLobbyManager.OnEntrouEmSalaLocalComSucesso += OnSalaLocalDisponivel;
        }

        if (botaoTutorialGeral != null)
        {
            botaoTutorialGeral.onClick.RemoveListener(IniciarTutorial);
            botaoTutorialGeral.onClick.AddListener(IniciarTutorial);
        }
    }

    private void OnDisable()
    {
        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso       -= OnSalaDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso      -= OnSalaDisponivel;
            steamLobbyManager.OnEntrouEmSalaLocalComSucesso -= OnSalaLocalDisponivel;
        }

        if (botaoTutorialGeral != null)
            botaoTutorialGeral.onClick.RemoveListener(IniciarTutorial);
    }

    // Instancia o prefab do overlay como filho DIRETO de canvasPrincipal na PRIMEIRA vez que for
    // preciso, e reaproveita a mesma instância depois disso (não recria a cada tutorial). Se
    // canvasPrincipal não foi arrastado no Inspector, cai no fallback FindFirstObjectByType<Canvas>()
    // — mas em Salas isso é ambíguo (Canvas principal + Canvas do grid coexistem), então é sempre
    // melhor arrastar explicitamente. "Nascer ao lado do alvo" é responsabilidade do próprio
    // TutorialOverlayUI.PosicionarTudo() em runtime, não de onde nasce.
    private TutorialOverlayUI GarantirOverlayInstanciado()
    {
        if (overlay != null) return overlay;

        if (canvasPrincipal == null) canvasPrincipal = FindFirstObjectByType<Canvas>();

        if (overlayPrefab == null || canvasPrincipal == null)
        {
            Debug.LogError("[TutorialManager] 'overlayPrefab' não atribuído no Inspector, ou nenhum Canvas foi encontrado na cena — não é possível criar o overlay.");
            return null;
        }

        overlay = Instantiate(overlayPrefab, canvasPrincipal.transform, false);
        overlay.transform.SetAsLastSibling(); // renderiza por cima de tudo mais nesse Canvas

        overlay.OnProximo     += AvancarPasso;
        overlay.OnVoltar      += VoltarPasso;
        overlay.OnPular       += EncerrarTutorial;
        overlay.OnEntendiDica += EncerrarDicaAtual;

        return overlay;
    }

    private void OnSalaDisponivel(Steamworks.Data.Lobby _) => devoAutoIniciar = !PlayerPrefs.HasKey(KEY_TUTORIAL_VISTO);
    private void OnSalaLocalDisponivel(SalaLocalSnapshot _) => devoAutoIniciar = !PlayerPrefs.HasKey(KEY_TUTORIAL_VISTO);

    private void Update()
    {
        if (!devoAutoIniciar) return;
        devoAutoIniciar = false;
        StartCoroutine(AutoIniciarAposAtraso());
    }

    private IEnumerator AutoIniciarAposAtraso()
    {
        yield return new WaitForSeconds(atrasoAutoInicio);
        IniciarTutorial();
        // Marca ao INICIAR, não ao terminar — evita reabrir sozinho toda sessão se o jogador sair
        // no meio do tutorial (ver botão "?" pra rever depois, isso não é a única forma de acesso).
        PlayerPrefs.SetInt(KEY_TUTORIAL_VISTO, 1);
        PlayerPrefs.Save();
    }

    // ── Tutorial guiado (sequencial) ─────────────────────────────────────────

    public void IniciarTutorial()
    {
        if (GarantirOverlayInstanciado() == null) return; // erro já logado

        List<TutorialPassoData> passosDaTela = ResolverPassosDaTelaAtual();
        if (passosDaTela == null || passosDaTela.Count == 0)
        {
            GameObject telaAtual = toggleObjects != null ? toggleObjects.TelaAtual : null;
            Debug.LogWarning($"[TutorialManager] IniciarTutorial ABORTADO — nenhum passo encontrado pra tela atual ('{(telaAtual != null ? telaAtual.name : "NULL — toggleObjects não atribuído ou nenhuma tela ativa ainda")}'). " +
                $"Confira se existe um elemento em 'tutoriaisPorTela' cujo campo 'tela' seja EXATAMENTE esse GameObject, com passos preenchidos.");
            return;
        }

        barraFerramentas?.FecharTodosOsPaineis();
        passosAtuais = passosDaTela;
        passoAtual = 0;
        MostrarPassoAtual();
    }

    // Compara ToggleObjects.TelaAtual (a tela de nível superior visível agora) contra a "tela" de
    // cada elemento de tutoriaisPorTela — cada tela pode ter seu próprio tutorial guiado, sem
    // precisar de um TutorialManager por tela.
    private List<TutorialPassoData> ResolverPassosDaTelaAtual()
    {
        GameObject telaAtual = toggleObjects != null ? toggleObjects.TelaAtual : null;
        if (telaAtual == null) return null;

        foreach (TutorialTelaData t in tutoriaisPorTela)
            if (t != null && t.tela == telaAtual) return t.passos;

        return null;
    }

    private void MostrarPassoAtual() =>
        overlay.MostrarPasso(passosAtuais[passoAtual], passoAtual + 1, passosAtuais.Count, modoDica: false);

    private void AvancarPasso()
    {
        if (passosAtuais == null) return;
        if (passoAtual < passosAtuais.Count - 1) { passoAtual++; MostrarPassoAtual(); }
        else EncerrarTutorial();
    }

    private void VoltarPasso()
    {
        if (passoAtual > 0) { passoAtual--; MostrarPassoAtual(); }
    }

    private void EncerrarTutorial()
    {
        overlay.Esconder();
        passosAtuais = null;
    }

    // ── Dicas contextuais avulsas ─────────────────────────────────────────────

    /// <summary>Mostra uma dica de um passo só (sem Voltar/Próximo/contador) apontando pra "alvo",
    /// uma única vez por "chavePersistencia" (PlayerPrefs) — chame na primeira vez que uma tela/painel
    /// relevante abre. Não faz nada se a chave já foi vista antes.</summary>
    public void MostrarDica(GameObject alvo, string texto, string chavePersistencia, float distanciaAlvo = 12f)
    {
        if (string.IsNullOrWhiteSpace(chavePersistencia)) return;
        if (PlayerPrefs.HasKey(chavePersistencia)) return;
        if (GarantirOverlayInstanciado() == null) return; // erro já logado

        chaveDicaAtual = chavePersistencia;
        overlay.MostrarDica(alvo, texto, distanciaAlvo);
    }

    private void EncerrarDicaAtual()
    {
        if (!string.IsNullOrWhiteSpace(chaveDicaAtual))
        {
            PlayerPrefs.SetInt(chaveDicaAtual, 1);
            PlayerPrefs.Save();
        }
        chaveDicaAtual = null;
        overlay.Esconder();
    }
}
