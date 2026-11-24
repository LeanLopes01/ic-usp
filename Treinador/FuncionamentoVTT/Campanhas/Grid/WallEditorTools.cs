using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Ferramenta ativa no editor de paredes — None desliga qualquer interação de mouse.</summary>
public enum WallToolType
{
    None,
    DrawLine,
    Eraser,
}

/// <summary>
/// Tool Manager de edição manual de paredes vetoriais (WallLine) sobre o mapa ativo de um
/// IGerenciadorDeParedes — CampanhaMapManager (offline) ou Salas/Grid/MapManager (online). Duas
/// ferramentas, mutuamente exclusivas via FerramentaAtiva:
///
/// - DrawLine: ponto-a-ponto (NÃO é click-and-drag). Primeiro clique esquerdo grava o ponto inicial
///   e liga o preview "elástico" (LineRenderer que segue o mouse a cada frame); segundo clique
///   esquerdo grava o ponto final e persiste a parede. Botão direito ou ESC cancela o traço em
///   andamento.
/// - Eraser: enquanto o botão esquerdo estiver pressionado, varre Physics2D contra a layer de
///   paredes num raio configurável (eraserRadius/eraserSizeSlider) e remove qualquer parede
///   encostada. Um indicador circular semitransparente acompanha o mouse mostrando o alcance atual.
///
/// Não mexe no dado de paredes diretamente — só chama a API pública de IGerenciadorDeParedes
/// (AdicionarParedeManual/ApagarParedesNoRaio/MundoParaNormalizado/AlternarOverlayParedes), que
/// segue sendo o único dono do dado e da reconstrução visual/física.
///
/// mapManagerBehaviour é [SerializeField] como MonoBehaviour (não como a interface diretamente)
/// porque o Inspector do Unity não sabe arrastar/exibir uma referência de interface — Awake faz o
/// cast pra IGerenciadorDeParedes e avisa no log se o objeto arrastado não implementar o contrato.
/// </summary>
public class WallEditorTools : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Precisa ser um MonoBehaviour que implemente IGerenciadorDeParedes — CampanhaMapManager " +
        "(Campanhas) ou MapManager (Salas).")]
    [SerializeField] private MonoBehaviour mapManagerBehaviour;

    private IGerenciadorDeParedes mapManager;

    [Header("Ferramenta de Desenho")]
    [SerializeField] private Color corPreviewLinha = Color.yellow;
    [SerializeField, Range(0.005f, 0.2f)] private float espessuraPreviewLinha = 0.05f;

    [Header("Ferramenta Borracha")]
    [Tooltip("Layer dos colisores de parede (ver CampanhaMapManager.nomeLayerVisionBlocker) — só " +
        "colisores nessa layer são considerados pela borracha.")]
    [SerializeField] private LayerMask layerParedes;
    [Tooltip("Opcional — se atribuído, o raio da borracha passa a ser controlado por este Slider " +
        "(ver instruções de wiring no cabeçalho da classe / resposta do chat).")]
    [SerializeField] private Slider eraserSizeSlider;
    [SerializeField] private float eraserRadius = 0.5f;
    [SerializeField] private Color corIndicadorBorracha = new Color(1f, 0f, 0f, 0.35f);

    // ── Estado ────────────────────────────────────────────────────────────────

    public WallToolType FerramentaAtiva { get; private set; } = WallToolType.None;
    private bool isDrawingLine;
    private Vector3 startPointMundo;
    private bool algoApagadoNesteArrasto;

    private LineRenderer _previewLinha;
    private SpriteRenderer _indicadorBorracha;
    private Camera _camera;
    private Material _materialPreview;

    // Buffer reaproveitado a cada chamada de OverlapCircleNonAlloc — evita alocar um array novo por
    // frame enquanto o botão da borracha fica pressionado (a diferença real de performance vs.
    // OverlapCircleAll, que aloca sempre).
    private readonly Collider2D[] _bufferErase = new Collider2D[32];

    private void Awake()
    {
        mapManager = mapManagerBehaviour as IGerenciadorDeParedes;
        if (mapManager == null && mapManagerBehaviour != null)
            Debug.LogWarning("[WallEditorTools] mapManagerBehaviour não implementa IGerenciadorDeParedes.");

        _camera = mapManager != null ? mapManager.CameraAtiva : null;
        if (_camera == null) _camera = Camera.main;

        CriarPreviewLinha();
        CriarIndicadorBorracha();

        if (eraserSizeSlider != null)
        {
            eraserSizeSlider.value = eraserRadius;
            eraserSizeSlider.onValueChanged.AddListener(DefinirRaioBorracha);
        }

        AtualizarVisibilidadeIndicadores();
    }

    // ── API pública (ligar aos botões de ferramenta pelo Inspector) ─────────

    /// <summary>Sobrecarga em int pra poder ser chamada direto por um Button.onClick no Inspector
    /// (UnityEvent não expõe enums customizados na lista de parâmetros persistentes — só tipos
    /// primitivos). 0 = None, 1 = DrawLine, 2 = Eraser.</summary>
    public void SelecionarFerramenta(int tipo) => SelecionarFerramenta((WallToolType)tipo);

    public void SelecionarFerramenta(WallToolType tipo)
    {
        // Clicar de novo no botão da ferramenta já ativa desliga (volta pra None) em vez de
        // simplesmente reatribuir a mesma ferramenta — isso é o que faz o botão funcionar como um
        // toggle liga/desliga, tanto pra DrawLine quanto pra Eraser.
        WallToolType novaFerramenta = FerramentaAtiva == tipo ? WallToolType.None : tipo;

        if (isDrawingLine) CancelarDesenho();
        FerramentaAtiva = novaFerramenta;
        AtualizarVisibilidadeIndicadores();
    }

    /// <summary>Ligar ao OnValueChanged do Slider no Inspector (ver eraserSizeSlider) — ou chamar
    /// direto se preferir controlar o raio por outro tipo de UI.</summary>
    public void DefinirRaioBorracha(float valor)
    {
        eraserRadius = Mathf.Max(0.01f, valor);
        AtualizarEscalaIndicadorBorracha();
    }

    /// <summary>Liga um botão a este método pra mostrar/esconder as linhas de depuração das paredes
    /// (IGerenciadorDeParedes.AlternarOverlayParedes) — os colisores continuam funcionando normalmente
    /// mesmo escondidas, isso só afeta se dá pra VER onde elas estão. Não é uma ferramenta de mouse
    /// como DrawLine/Eraser (não captura clique nem tem estado próprio), por isso fica fora do enum
    /// WallToolType — é só um toggle de visibilidade, independente de qual ferramenta está ativa.</summary>
    public void AlternarVisibilidadeParedes()
    {
        mapManager?.AlternarOverlayParedes();
    }

    // ── Update (uma leitura de mouse por frame, reaproveitada pelas duas ferramentas) ───────────

    private void Update()
    {
        if (FerramentaAtiva == WallToolType.None || mapManager == null || _camera == null) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        bool escPressionado = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        if (isDrawingLine && (escPressionado || mouse.rightButton.wasPressedThisFrame))
        {
            CancelarDesenho();
            return;
        }

        bool sobreUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        Vector3 mundo = ObterPosicaoMouseNoMundo(mouse);

        switch (FerramentaAtiva)
        {
            case WallToolType.DrawLine:
                AtualizarFerramentaDesenho(mouse, mundo, sobreUI);
                break;
            case WallToolType.Eraser:
                AtualizarFerramentaBorracha(mouse, mundo, sobreUI);
                break;
        }
    }

    private Vector3 ObterPosicaoMouseNoMundo(Mouse mouse)
    {
        Vector2 tela2D = mouse.position.ReadValue();
        Vector3 tela = new Vector3(tela2D.x, tela2D.y, Mathf.Abs(_camera.transform.position.z));
        Vector3 mundo = _camera.ScreenToWorldPoint(tela);
        mundo.z = 0f;
        return mundo;
    }

    // ── Ferramenta de Desenho (ponto-a-ponto) ────────────────────────────────

    private void AtualizarFerramentaDesenho(Mouse mouse, Vector3 mundo, bool sobreUI)
    {
        if (isDrawingLine)
        {
            // "Elástico": a ponta final do preview segue o mouse todo frame, sem precisar segurar
            // o botão — só o clique inicial e o clique final importam.
            _previewLinha.SetPosition(1, mundo);

            if (mouse.leftButton.wasPressedThisFrame && !sobreUI)
                ConcluirDesenho(mundo);

            return;
        }

        // O primeiro clique nunca deve começar um traço em cima de um botão/painel de UI.
        if (mouse.leftButton.wasPressedThisFrame && !sobreUI)
            IniciarDesenho(mundo);
    }

    private void IniciarDesenho(Vector3 mundo)
    {
        startPointMundo = mundo;
        isDrawingLine = true;

        _previewLinha.SetPosition(0, mundo);
        _previewLinha.SetPosition(1, mundo);
        _previewLinha.enabled = true;
    }

    private void ConcluirDesenho(Vector3 mundo)
    {
        Vector2 pontoA = mapManager.MundoParaNormalizado(startPointMundo);
        Vector2 pontoB = mapManager.MundoParaNormalizado(mundo);

        isDrawingLine = false;
        _previewLinha.enabled = false;

        if (Vector2.Distance(pontoA, pontoB) < 0.0005f) return; // dois cliques no mesmo lugar — ignora, não vira parede de comprimento zero

        mapManager.AdicionarParedeManual(new WallLine { pontoA = pontoA, pontoB = pontoB });
    }

    private void CancelarDesenho()
    {
        isDrawingLine = false;
        _previewLinha.enabled = false;
    }

    // ── Ferramenta Borracha ───────────────────────────────────────────────────

    private void AtualizarFerramentaBorracha(Mouse mouse, Vector3 mundo, bool sobreUI)
    {
        PosicionarIndicadorBorracha(mundo);

        if (mouse.leftButton.isPressed && !sobreUI)
            ApagarNaPosicao(mundo);

        // Só grava em disco UMA VEZ, ao soltar o botão — persistir a cada frame durante um arrasto
        // contínuo seria I/O de disco a 60fps, o que a CarregarMapa/SaveOrUpdate não foi feito pra
        // aguentar. Entre o clique e o soltar, a remoção já está refletida em memória/visualmente
        // (ReconstruirParedes roda a cada segmento apagado), só a gravação é adiada.
        if (mouse.leftButton.wasReleasedThisFrame && algoApagadoNesteArrasto)
        {
            mapManager.PersistirMapaAtivo();
            algoApagadoNesteArrasto = false;
        }
    }

    private void ApagarNaPosicao(Vector3 mundo)
    {
        int hits = Physics2D.OverlapCircleNonAlloc(mundo, eraserRadius, _bufferErase, layerParedes);
        if (hits <= 0) return;

        // Um mesmo WallLine nunca aparece em duas cadeias diferentes, mas o HashSet protege contra
        // duplicata se o buffer trouxer o mesmo GameObject/collider mais de uma vez.
        HashSet<WallLine> candidatas = null;
        for (int i = 0; i < hits; i++)
        {
            CampanhaParedeInstancia instancia = _bufferErase[i] != null ? _bufferErase[i].GetComponent<CampanhaParedeInstancia>() : null;
            if (instancia == null || instancia.LinhasOrigem == null) continue;

            candidatas ??= new HashSet<WallLine>();
            foreach (WallLine linha in instancia.LinhasOrigem) candidatas.Add(linha);
        }

        if (candidatas == null) return;

        // ApagarParedesNoRaio recorta cada WallLine tocada contra o círculo da borracha — só a
        // porção dentro do raio some, o resto do traço continua existindo (não é mais "tudo ou nada"
        // por cadeia inteira).
        if (mapManager.ApagarParedesNoRaio(candidatas, mundo, eraserRadius))
            algoApagadoNesteArrasto = true;
    }

    // ── Indicadores visuais (gerados em código — nenhum prefab necessário) ───

    private void CriarPreviewLinha()
    {
        var go = new GameObject("WallEditor_PreviewLinha");
        go.transform.SetParent(transform, false);

        _previewLinha = go.AddComponent<LineRenderer>();
        _previewLinha.useWorldSpace = true;
        _previewLinha.positionCount = 2;
        _materialPreview = new Material(Shader.Find("Sprites/Default"));
        _previewLinha.material = _materialPreview;
        _previewLinha.startColor = corPreviewLinha;
        _previewLinha.endColor = corPreviewLinha;
        _previewLinha.startWidth = espessuraPreviewLinha;
        _previewLinha.endWidth = espessuraPreviewLinha;
        _previewLinha.numCapVertices = 4;
        _previewLinha.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _previewLinha.receiveShadows = false;
        _previewLinha.generateLightingData = false;
        _previewLinha.sortingOrder = 1000; // sempre por cima de mapa/grid/tokens/paredes enquanto edita
        _previewLinha.enabled = false;
    }

    private void CriarIndicadorBorracha()
    {
        var go = new GameObject("WallEditor_IndicadorBorracha");
        go.transform.SetParent(transform, false);

        _indicadorBorracha = go.AddComponent<SpriteRenderer>();
        _indicadorBorracha.sprite = TokenMascaraCircular.Obter(); // reaproveita o círculo já usado pros tokens
        _indicadorBorracha.color = corIndicadorBorracha;
        _indicadorBorracha.sortingOrder = 1000;
        AtualizarEscalaIndicadorBorracha();
        _indicadorBorracha.enabled = false;
    }

    private void PosicionarIndicadorBorracha(Vector3 mundo)
    {
        if (_indicadorBorracha != null) _indicadorBorracha.transform.position = mundo;
    }

    private void AtualizarEscalaIndicadorBorracha()
    {
        if (_indicadorBorracha == null) return;
        float diametro = eraserRadius * 2f;
        _indicadorBorracha.transform.localScale = new Vector3(diametro, diametro, 1f);
    }

    private void AtualizarVisibilidadeIndicadores()
    {
        if (_previewLinha != null && FerramentaAtiva != WallToolType.DrawLine)
        {
            isDrawingLine = false;
            _previewLinha.enabled = false;
        }

        if (_indicadorBorracha != null)
            _indicadorBorracha.enabled = FerramentaAtiva == WallToolType.Eraser;
    }

    private void OnDestroy()
    {
        if (_materialPreview != null) Destroy(_materialPreview);
    }
}
