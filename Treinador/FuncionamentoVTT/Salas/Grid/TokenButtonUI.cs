using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class TokenButtonUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("UI")]
    [SerializeField] private Image miniaturaImage;
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private Canvas canvasRaiz;
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Redimensionamento (só aparece com o token selecionado)")]
    [SerializeField] private Button botaoDiminuir;
    [SerializeField] private Button botaoAumentar;
    [Tooltip("Só aparece quando o jogador começa a redimensionar (clique em +/- ou arrasto de um handle) — não junto da seleção.")]
    [SerializeField] private TMP_Text tamanhoText;
    [SerializeField] private TokenResizeHandle[] handlesRedimensionar;

    [Header("Vida (sempre visível — igual ao nome)")]
    [Tooltip("Slider sem handle visível (remover/desativar o Handle Rect no prefab) — o fill mostra vida atual/máxima.")]
    [SerializeField] private Slider sliderVida;
    [SerializeField] private TMP_Text vidaText;

    [Header("Ferramentas (só aparece com o token selecionado)")]
    [SerializeField] private GameObject toolsContent;
    [Tooltip("Legenda troca entre \"Esconder\"/\"Mostrar\" — lida automaticamente do primeiro TMP_Text filho do botão, sem precisar de campo próprio.")]
    [SerializeField] private Button botaoEsconder;
    [Tooltip("Legenda troca entre \"Lock\"/\"Destravar\" — lida automaticamente do primeiro TMP_Text filho do botão.")]
    [SerializeField] private Button botaoLock;
    [SerializeField] private Button botaoDuplicar;
    [SerializeField] private Button botaoDeletar;

    [Header("Raio de Visão (só aparece com o token selecionado)")]
    [Tooltip("Clicar revela/esconde painelSliderVisao — mesmo padrão de clique-revela-slider já " +
        "usado em LightMarcadorUI, aplicado aqui ao raio de visão do PRÓPRIO token (ITokenNoMapa. " +
        "RaioVisaoMetros/DefinirRaioVisao — já existe nos dois draggables, só faltava esta UI).")]
    [SerializeField] private Button botaoVisao;
    [Tooltip("Container do slider de visão — começa OCULTO, revelado ao clicar em botaoVisao.")]
    [SerializeField] private GameObject painelSliderVisao;
    [SerializeField] private Slider sliderVisao;
    [SerializeField] private TMP_Text textoVisao;

    // Resolvido via FindFirstObjectByType em Awake se não atribuído no Inspector — mesmo padrão de
    // fallback já usado em MapManager/EfeitoClimaManager. Usado só pra gate "só Mestre" do slider de
    // visão do próprio token (ver AtualizarFerramentas).
    [SerializeField] private SteamLobbyManager steamLobbyManager;

    private static readonly float[] Passos = { 0.25f, 0.5f, 0.75f, 1f, 1.5f, 2f, 3f, 4f, 5f };
    private int _indiceEscala = 3; // padrão = 1×

    private bool _modoMapa = false;
    private float _escalaBase = 1f;

    // Referência CONCRETA — usada só pelo fluxo de arrastar-do-catálogo (OnBeginDrag/OnDrag/OnEndDrag
    // + TentarSoltarTokenNoMapa), que é Salas-only (a galeria de tokens arrastável não existe em
    // Campanhas, que solta NPCs por outro caminho — CampanhaNpcCardUI). Fica null pra tokens de
    // Campanhas, sem problema: _modoMapa==true faz esses três handlers retornarem cedo de qualquer
    // jeito.
    private MapManager manager;
    // Referência pela INTERFACE — usada por OnClickDeletar/OnClickDuplicar/o slider de visão, que
    // fazem sentido nos dois contextos (ver ITokenNoMapa/IGerenciadorDeTokens). Setada em AMBOS os
    // overloads de Setup (o de MapManager faz upcast implícito; o novo, direto).
    private IGerenciadorDeTokens gerenciadorTokens;
    private string nomeToken;
    private Sprite spriteToken;
    // Guarda o item de catálogo inteiro (não só nome+sprite) pra soltar no mapa sem precisar
    // re-resolver por nome — dois catálogos com nome igual (ex: duas fichas "Teste 1") não podem
    // mais se confundir entre si.
    private MapManager.TokenCatalogItem itemCatalogo;
    private RectTransform rectTransform;
    private Vector2 posicaoOriginal;

    // Mesma convenção usada em MapManager/CampanhaMapManager (1 quadrado de grid = 1.5m) — duplicada
    // aqui (ambas são 'private const' nas suas classes, inacessíveis daqui) só pra configurar o
    // range do sliderVisao em Awake.
    private const float MetrosPorQuadrado = 1.5f;

    private float _dragAcumulado;
    private const float PixelsPorPasso = 40f;

    // Token do mapa que este overlay representa — só existe pra tokens JÁ no mapa (_modoMapa==true);
    // resolvido em InicializarComoTokenDoMapa (não em Awake — o componente concreto ainda não foi
    // AddComponent'ado no GameObject nesse ponto, ver MapManager.SpawnTokenNoMapa/
    // CampanhaMapManager.SpawnTokenDeNpc) via GetComponentInParent, tentando os dois tipos
    // concretos que implementam ITokenNoMapa (MapTokenDraggable em Salas, CampanhaTokenDraggable em
    // Campanhas) — só um deles existe em qualquer GameObject dado.
    private ITokenNoMapa _meuToken;
    private bool _selecionado;
    private int  _vidaAtualExibida = -1;
    private int  _vidaMaximaExibida = -1;
    // Rótulo exibido vs. NomeToken atual — mesmo padrão de early-return de _vidaAtualExibida/
    // _vidaMaximaExibida, evita reescrever nomeText.text todo frame à toa.
    private string _nomeExibido;

    private void Awake()
    {
        rectTransform = transform as RectTransform;

        canvasGroup ??= GetComponent<CanvasGroup>();
        canvasGroup ??= gameObject.AddComponent<CanvasGroup>();
        canvasRaiz  ??= GetComponentInParent<Canvas>();

        TentarAutoConectar();
        ConfigurarMascaraRedonda();

        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();

        botaoDiminuir?.onClick.AddListener(Diminuir);
        botaoAumentar?.onClick.AddListener(Aumentar);
        foreach (TokenResizeHandle h in handlesRedimensionar)
            h.OnDeltaX += OnDragRedimensionar;

        botaoDeletar?.onClick.AddListener(OnClickDeletar);
        botaoDuplicar?.onClick.AddListener(OnClickDuplicar);
        botaoLock?.onClick.AddListener(OnClickLock);
        botaoEsconder?.onClick.AddListener(OnClickEsconder);
        botaoVisao?.onClick.AddListener(OnClickVisao);
        sliderVisao?.onValueChanged.AddListener(OnSliderVisaoAlterado);

        // O Slider do prefab vem com o range padrão da Unity (0..1) — sem configurar isso aqui, todo
        // valor de RaioVisaoMetros (tipicamente ~18m) fica fora do range e é grampeado em 1 por
        // SetValueWithoutNotify, deixando o campo de visão "preso" em ~1m e o handle sempre no fim da
        // barra (arrastar não muda quase nada). Mesmo range do slider de spotlight já existente em
        // MapManager (MetrosPorQuadrado..MetrosPorQuadrado*40 — 1 quadrado até 40 quadrados).
        if (sliderVisao != null)
        {
            sliderVisao.minValue = MetrosPorQuadrado;
            sliderVisao.maxValue = MetrosPorQuadrado * 40f;
            sliderVisao.wholeNumbers = false;
        }

        // "Sem handle" também reforçado em código, além do prefab — não custa nada e evita depender
        // só do passo manual no Editor.
        if (sliderVida != null) sliderVida.handleRect = null;

        AtualizarTextoTamanho();
        EsconderControlesDeSelecao();
        if (tamanhoText != null) tamanhoText.gameObject.SetActive(false);
        DefinirVisibilidadeVida(false);
        if (painelSliderVisao != null) painelSliderVisao.SetActive(false);

        // Assina os DOIS eventos estáticos de seleção — só um dos dois vai disparar de verdade pra
        // qualquer token dado (o outro não tem correspondência nenhuma nesse GameObject), custo zero
        // no que não se aplica. Mesmo raciocínio de _meuToken tentar os dois tipos concretos acima.
        MapTokenDraggable.OnSelecaoAlterada += OnSelecaoAlteradaSalas;
        CampanhaTokenDraggable.OnSelecaoAlterada += OnSelecaoAlteradaCampanha;
    }

    private void TentarAutoConectar()
    {
        if (miniaturaImage == null)
        {
            Image[] imgs = GetComponentsInChildren<Image>(true);
            foreach (Image img in imgs)
            {
                if (EhCandidataAMiniatura(img))
                {
                    miniaturaImage = img;
                    break;
                }
            }
        }

        if (nomeText == null || tamanhoText == null)
        {
            TMP_Text[] textos = GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text t in textos)
            {
                string n = t.gameObject.name.ToLower();
                if (nomeText == null && (n.Contains("nome") || n.Contains("name") || n.Contains("titulo")))
                    nomeText = t;
                else if (tamanhoText == null && (n.Contains("tamanho") || n.Contains("size") || n.Contains("escala")))
                    tamanhoText = t;
            }
            if (textos.Length == 1 && nomeText == null)
                nomeText = textos[0];
        }

        if (botaoDiminuir == null || botaoAumentar == null)
        {
            Button[] botoes = GetComponentsInChildren<Button>(true);
            foreach (Button b in botoes)
            {
                string n = b.gameObject.name.ToLower();
                if (botaoDiminuir == null && (n.Contains("diminui") || n.Contains("menos") || n.Contains('-') || n.Contains("minus")))
                    botaoDiminuir = b;
                else if (botaoAumentar == null && (n.Contains("aumenta") || n.Contains("mais") || n.Contains('+') || n.Contains("plus")))
                    botaoAumentar = b;
            }
        }

        if (handlesRedimensionar == null || handlesRedimensionar.Length == 0)
            handlesRedimensionar = GetComponentsInChildren<TokenResizeHandle>(true);
    }

    // Filtra Images que claramente não são a miniatura do token, pra achar "a primeira Image" com
    // segurança — antes do slider de vida/content de ferramentas existirem, a miniatura era a ÚNICA
    // Image na hierarquia, então "a primeira encontrada" sempre acertava. Agora um Slider tem
    // Background/Fill/Handle (todas Images) e cada botão de ferramenta pode ter um ícone (Image
    // FILHO do botão, não teria sido pego pelo filtro antigo "sem Button no próprio GameObject") —
    // sem excluir esses, a máscara redonda (ConfigurarMascaraRedonda) podia acabar envolvendo o
    // Image errado, deixando a miniatura de verdade sem máscara e o Image errado com o layout todo
    // quebrado (foi exatamente o bug reportado).
    private bool EhCandidataAMiniatura(Image img)
    {
        if (img.gameObject == gameObject) return false;
        if (img.GetComponentInParent<Button>(true) != null) return false;
        if (sliderVida != null && img.transform.IsChildOf(sliderVida.transform)) return false;
        if (toolsContent != null && img.transform.IsChildOf(toolsContent.transform)) return false;
        return true;
    }

    // Deixa a miniatura redonda mesmo que a imagem de origem seja um quadrado 1:1 sem bordas
    // transparentes: envolve miniaturaImage num wrapper com Image circular (gerada em código) +
    // Mask, e estica a miniatura original pra preencher esse wrapper. Idempotente — se o prefab já
    // tiver esse wrapper montado manualmente no Editor, não faz nada.
    private void ConfigurarMascaraRedonda()
    {
        if (miniaturaImage == null) return;
        if (miniaturaImage.transform.parent != null && miniaturaImage.transform.parent.GetComponent<Mask>() != null) return;

        RectTransform imgRect = miniaturaImage.rectTransform;
        Transform paiOriginal = imgRect.parent;
        int indiceOriginal = imgRect.GetSiblingIndex();

        GameObject mascaraGo = new GameObject("Mascara_Redonda", typeof(RectTransform));
        RectTransform mascaraRect = mascaraGo.GetComponent<RectTransform>();
        mascaraGo.transform.SetParent(paiOriginal, false);
        mascaraGo.transform.SetSiblingIndex(indiceOriginal);

        mascaraRect.anchorMin = imgRect.anchorMin;
        mascaraRect.anchorMax = imgRect.anchorMax;
        mascaraRect.pivot = imgRect.pivot;
        mascaraRect.sizeDelta = imgRect.sizeDelta;
        mascaraRect.anchoredPosition = imgRect.anchoredPosition;

        Image mascaraImg = mascaraGo.AddComponent<Image>();
        mascaraImg.sprite = TokenMascaraCircular.Obter();
        Mask mask = mascaraGo.AddComponent<Mask>();
        mask.showMaskGraphic = false;

        imgRect.SetParent(mascaraRect, false);
        imgRect.anchorMin = Vector2.zero;
        imgRect.anchorMax = Vector2.one;
        imgRect.offsetMin = Vector2.zero;
        imgRect.offsetMax = Vector2.zero;
    }

    private void OnDestroy()
    {
        botaoDiminuir?.onClick.RemoveListener(Diminuir);
        botaoAumentar?.onClick.RemoveListener(Aumentar);
        foreach (TokenResizeHandle h in handlesRedimensionar)
            h.OnDeltaX -= OnDragRedimensionar;

        botaoDeletar?.onClick.RemoveListener(OnClickDeletar);
        botaoDuplicar?.onClick.RemoveListener(OnClickDuplicar);
        botaoLock?.onClick.RemoveListener(OnClickLock);
        botaoEsconder?.onClick.RemoveListener(OnClickEsconder);
        botaoVisao?.onClick.RemoveListener(OnClickVisao);
        sliderVisao?.onValueChanged.RemoveListener(OnSliderVisaoAlterado);

        MapTokenDraggable.OnSelecaoAlterada -= OnSelecaoAlteradaSalas;
        CampanhaTokenDraggable.OnSelecaoAlterada -= OnSelecaoAlteradaCampanha;
    }

    public void Setup(MapManager mapManager, MapManager.TokenCatalogItem item, string nome)
    {
        Setup(mapManager, nome, item != null ? item.sprite : null);
        itemCatalogo = item;
    }

    // Usado pelo badge de redimensionar de um token JÁ no mapa em Salas (MapManager.SpawnTokenNoMapa)
    // — também dá a este overlay acesso ao fluxo de arrastar-do-catálogo (OnBeginDrag/OnDrag/
    // OnEndDrag), que só faz sentido aqui.
    public void Setup(MapManager mapManager, string nome, Sprite sprite)
    {
        manager = mapManager;
        gerenciadorTokens = mapManager; // MapManager implementa IGerenciadorDeTokens
        ConfigurarComum(nome, sprite);
    }

    // Overload pra token JÁ no mapa em Campanhas (CampanhaMapManager.SpawnTokenDeNpc) — sem acesso
    // ao fluxo de arrastar-do-catálogo (não existe em Campanhas), só ao que IGerenciadorDeTokens
    // cobre (Remover/Duplicar/notificar raio de visão).
    public void Setup(IGerenciadorDeTokens gerenciador, string nome, Sprite sprite)
    {
        manager = null;
        gerenciadorTokens = gerenciador;
        ConfigurarComum(nome, sprite);
    }

    private void ConfigurarComum(string nome, Sprite sprite)
    {
        itemCatalogo = null;
        nomeToken    = nome;
        spriteToken  = sprite;

        if (nomeText != null)
            nomeText.text = string.IsNullOrWhiteSpace(nome) ? "Token" : nome;

        if (miniaturaImage != null)
            miniaturaImage.sprite = spriteToken;
    }

    // ── Inicializar como token do mapa ──────────────────────

    public void InicializarComoTokenDoMapa()
    {
        _modoMapa     = true;
        _escalaBase   = transform.localScale.x;
        _indiceEscala = 3;
        AtualizarTextoTamanho();

        // Os dois componentes concretos que implementam ITokenNoMapa já rodaram Setup a esta altura
        // (MapManager.SpawnTokenNoMapa/CampanhaMapManager.SpawnTokenDeNpc chamam o Setup do
        // draggable ANTES de tokenUI.InicializarComoTokenDoMapa) — seguro resolver aqui.
        // GetComponentInParent (não GetComponent): TokenButtonUI vive no Canvas FILHO do token, não
        // no GameObject raiz onde o draggable/Collider2D estão. Só um dos dois candidatos existe em
        // qualquer GameObject dado — o outro sempre retorna null.
        _meuToken = GetComponentInParent<MapTokenDraggable>(true) as ITokenNoMapa
                 ?? GetComponentInParent<CampanhaTokenDraggable>(true) as ITokenNoMapa;
        _vidaAtualExibida  = -1;
        _vidaMaximaExibida = -1;

        OnSelecaoAlteradaSalas(MapTokenDraggable.TokenSelecionado);
        OnSelecaoAlteradaCampanha(CampanhaTokenDraggable.TokenSelecionado);
    }

    // ── Seleção (clique no token — ver MapTokenDraggable/CampanhaTokenDraggable.OnSelecaoAlterada) ──

    private void OnSelecaoAlteradaSalas(MapTokenDraggable tokenSelecionado) => AtualizarSelecao(tokenSelecionado);
    private void OnSelecaoAlteradaCampanha(CampanhaTokenDraggable tokenSelecionado) => AtualizarSelecao(tokenSelecionado);

    private void AtualizarSelecao(ITokenNoMapa tokenSelecionado)
    {
        _selecionado = _modoMapa && _meuToken != null && ReferenceEquals(tokenSelecionado, _meuToken);

        if (botaoDiminuir != null) botaoDiminuir.gameObject.SetActive(_selecionado);
        if (botaoAumentar != null) botaoAumentar.gameObject.SetActive(_selecionado);
        if (toolsContent != null) toolsContent.SetActive(_selecionado);
        if (botaoVisao != null) botaoVisao.gameObject.SetActive(_selecionado);

        if (!_selecionado)
        {
            if (tamanhoText != null) tamanhoText.gameObject.SetActive(false);
            if (painelSliderVisao != null) painelSliderVisao.SetActive(false);
        }

        if (_selecionado) AtualizarFerramentas();
    }

    private void EsconderControlesDeSelecao()
    {
        if (botaoDiminuir != null) botaoDiminuir.gameObject.SetActive(false);
        if (botaoAumentar != null) botaoAumentar.gameObject.SetActive(false);
        if (toolsContent != null) toolsContent.SetActive(false);
        if (botaoVisao != null) botaoVisao.gameObject.SetActive(false);
    }

    // ── Vida (HP) — polling leve, sem evento de mudança (ver MapTokenDraggable.VidaAtual) ────

    private void Update()
    {
        if (!_modoMapa || _meuToken == null) return;
        AtualizarVida();
        AtualizarNome();
    }

    /// <summary>Mantém o rótulo do token em sincronia com NomeToken — que agora (ver
    /// MapTokenDraggable.RefrescarDadosDaFichaSeNecessario) pode mudar depois do Setup inicial, ex:
    /// jogador renomeou o personagem na ficha. Cast concreto porque ITokenNoMapa não expõe NomeToken —
    /// só tem efeito pra token de Sala (CampanhaTokenDraggable cai no early-return).</summary>
    private void AtualizarNome()
    {
        if (_meuToken is not MapTokenDraggable mtd) return;
        if (mtd.NomeToken == _nomeExibido) return;

        _nomeExibido = mtd.NomeToken;
        if (nomeText != null)
            nomeText.text = string.IsNullOrWhiteSpace(_nomeExibido) ? "Token" : _nomeExibido;
    }

    private void AtualizarVida()
    {
        int atual  = _meuToken.VidaAtual;
        int maximo = _meuToken.VidaMaxima;
        if (atual == _vidaAtualExibida && maximo == _vidaMaximaExibida) return;

        _vidaAtualExibida  = atual;
        _vidaMaximaExibida = maximo;

        // Token sem ficha/monstro/NPC vinculado não tem vida nenhuma pra mostrar — some em vez de
        // exibir "0/0", que pareceria um token morto.
        bool temVida = maximo > 0;
        DefinirVisibilidadeVida(temVida);
        if (!temVida) return;

        if (sliderVida != null) sliderVida.value = (float)atual / maximo;
        if (vidaText != null) vidaText.text = $"{atual}/{maximo}";
    }

    private void DefinirVisibilidadeVida(bool visivel)
    {
        if (sliderVida != null) sliderVida.gameObject.SetActive(visivel);
        if (vidaText != null) vidaText.gameObject.SetActive(visivel);
    }

    // ── Ferramentas (Esconder/Lock/Duplicar/Deletar) ─────────────────────────

    private void OnClickDeletar()
    {
        if (_meuToken == null || gerenciadorTokens == null) return;
        gerenciadorTokens.RemoverTokenDoMapa(_meuToken.TokenId);
    }

    private void OnClickDuplicar()
    {
        if (_meuToken == null || gerenciadorTokens == null) return;
        gerenciadorTokens.DuplicarToken(_meuToken.TokenId);
    }

    private void OnClickLock()
    {
        _meuToken?.AlternarBloqueado();
        AtualizarFerramentas();
    }

    private void OnClickEsconder()
    {
        _meuToken?.AlternarEscondido();
        AtualizarFerramentas();
    }

    // Habilita/desabilita os 4 botões conforme a mesma permissão de mover (dono/host/sem dono em
    // Salas, sempre true em Campanhas — ver ITokenNoMapa.TenhoPermissaoSobreToken, propositalmente
    // SEM checar o lock, senão nunca daria pra destravar) e atualiza as legendas de Lock/Esconder
    // conforme o estado atual.
    private void AtualizarFerramentas()
    {
        if (_meuToken == null) return;

        bool permissao = _meuToken.TenhoPermissaoSobreToken();
        if (botaoLock != null) botaoLock.interactable = permissao;
        if (botaoEsconder != null) botaoEsconder.interactable = permissao;
        if (botaoDuplicar != null) botaoDuplicar.interactable = permissao;
        if (botaoDeletar != null) botaoDeletar.interactable = permissao;

        DefinirLegenda(botaoLock, _meuToken.Bloqueado ? "Destravar" : "Lock");
        DefinirLegenda(botaoEsconder, _meuToken.Escondido ? "Mostrar" : "Esconder");

        // Raio de visão do PRÓPRIO token passa a ser só-Mestre — antes, qualquer um que selecionasse
        // o token conseguia mexer no próprio raio de visão (regressão de permissão). Campanhas (sem
        // conceito de Mestre/jogador) continua sempre interactable, comportamento inalterado lá.
        if (botaoVisao != null && _meuToken is MapTokenDraggable)
            botaoVisao.interactable = SouMestre();
    }

    /// <summary>Mesmo padrão duplicado no projeto (BarraFerramentasManager, EfeitoClimaManager,
    /// ClimaPainelUI etc) — dono do lobby Steam == Mestre da sala.</summary>
    private bool SouMestre() =>
        Steamworks.SteamClient.IsValid && steamLobbyManager?.SalaAtual?.IsOwnedBy(Steamworks.SteamClient.SteamId) == true;

    private static void DefinirLegenda(Button botao, string texto)
    {
        if (botao == null) return;
        TMP_Text label = botao.GetComponentInChildren<TMP_Text>(true);
        if (label != null) label.text = texto;
    }

    // ── Raio de Visão (novo) ─────────────────────────────────

    private void OnClickVisao()
    {
        if (painelSliderVisao == null || _meuToken == null) return;

        bool vaiAbrir = !painelSliderVisao.activeSelf;
        painelSliderVisao.SetActive(vaiAbrir);
        if (vaiAbrir && sliderVisao != null)
        {
            sliderVisao.SetValueWithoutNotify(_meuToken.RaioVisaoMetros);
            AtualizarTextoVisao(_meuToken.RaioVisaoMetros);
        }
    }

    // Aplica e já persiste a cada tick de arrasto — mesmo comportamento do slider de raio JÁ
    // existente pro spotlight do host (MapManager.OnSliderRaioVisaoAlterado), que edita essa MESMA
    // propriedade (ITokenNoMapa.RaioVisaoMetros); mantém consistência com o slider irmão em vez de
    // introduzir um terceiro padrão de "só persiste ao soltar" só pra este.
    private void OnSliderVisaoAlterado(float metros)
    {
        if (_meuToken == null) return;

        _meuToken.DefinirRaioVisao(metros);
        AtualizarTextoVisao(metros);
        gerenciadorTokens?.NotificarRaioVisaoAlterado(_meuToken);
    }

    private void AtualizarTextoVisao(float metros)
    {
        if (textoVisao != null) textoVisao.text = $"Visão: {metros:0.0}m";
    }

    // ── Redimensionamento ────────────────────────────────────

    private void Aumentar()
    {
        _indiceEscala = Mathf.Min(_indiceEscala + 1, Passos.Length - 1);
        AplicarEscalaNoMapa();
        AtualizarTextoTamanho();
        if (tamanhoText != null) tamanhoText.gameObject.SetActive(true);
    }

    private void Diminuir()
    {
        _indiceEscala = Mathf.Max(_indiceEscala - 1, 0);
        AplicarEscalaNoMapa();
        AtualizarTextoTamanho();
        if (tamanhoText != null) tamanhoText.gameObject.SetActive(true);
    }

    private void AplicarEscalaNoMapa()
    {
        if (!_modoMapa) return;
        float novaEscala = _escalaBase * Passos[_indiceEscala];
        transform.localScale = new Vector3(novaEscala, novaEscala, transform.localScale.z);
    }

    private void OnDragRedimensionar(float deltaX)
    {
        _dragAcumulado += deltaX;
        while (_dragAcumulado >= PixelsPorPasso)  { Aumentar(); _dragAcumulado -= PixelsPorPasso; }
        while (_dragAcumulado <= -PixelsPorPasso) { Diminuir(); _dragAcumulado += PixelsPorPasso; }
    }

    private void AtualizarTextoTamanho()
    {
        if (tamanhoText == null) return;
        float v = Passos[_indiceEscala];
        tamanhoText.text = v < 1f ? $"×{v:0.##}" : $"×{v:0.#}";
    }

    // ── Arrastar token para o mapa (arrastar-do-catálogo — Salas only, ver campo 'manager') ──

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_modoMapa) return;
        if (spriteToken == null || rectTransform == null) return;

        _dragAcumulado  = 0f;
        posicaoOriginal = rectTransform.anchoredPosition;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0.7f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_modoMapa) return;
        if (spriteToken == null || rectTransform == null || canvasRaiz == null) return;

        float scale = Mathf.Max(0.0001f, canvasRaiz.scaleFactor);
        rectTransform.anchoredPosition += eventData.delta / scale;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_modoMapa) return;

        if (rectTransform != null)
            rectTransform.anchoredPosition = posicaoOriginal;

        canvasGroup.blocksRaycasts = true;
        canvasGroup.alpha = 1f;

        if (manager != null && itemCatalogo != null)
            manager.TentarSoltarTokenNoMapa(itemCatalogo, eventData.position, Passos[_indiceEscala]);
    }
}
