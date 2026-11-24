using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>Ferramenta ativa no editor de fog manual — None desliga qualquer interação de mouse.</summary>
public enum FogToolType
{
    None,
    Desenhar,
    Apagar,
}

/// <summary>
/// Tool Manager de fog manual (FogRect — ver Salas/Grid/FogRect.cs) sobre o mapa ativo de um
/// IGerenciadorDeParedes — CampanhaMapManager (offline) ou Salas/Grid/MapManager (online). Mesmo
/// padrão de Campanhas/Grid/WallEditorTools.cs (mapManagerBehaviour + cast pra interface no Awake).
///
/// Desenhar E Apagar usam a MESMA interação: CLIQUE-E-ARRASTA (não ponto-a-ponto como as paredes) —
/// pressionar grava a célula inicial, arrastar atualiza um preview retangular (4 segmentos, sempre
/// preso ao grid via MundoParaCelula/CelulaParaBoundsMundo), soltar aplica o retângulo (min/max das
/// duas células). A diferença entre os dois modos é só o QUE acontece ao soltar — Desenhar chama
/// IGerenciadorDeParedes.AdicionarFogRect (adiciona um retângulo de fog novo), Apagar chama
/// ApagarFogNoRetangulo (SUBTRAI essa área de qualquer fog já existente que a sobreponha, mantendo
/// o resto de cada retângulo afetado — ver comentário de ApagarFogNoRetangulo/SubtrairRetangulo nos
/// managers) — mesma ferramenta, resultado oposto. O preview troca de cor conforme o modo (ver
/// corPreviewDesenhar/corPreviewApagar), pra ficar claro o que o arrasto vai fazer antes de soltar.
///
/// Não mexe no dado de fog diretamente — só chama a API pública de IGerenciadorDeParedes
/// (AdicionarFogRect/ApagarFogNoRetangulo/MundoParaCelula/CelulaParaBoundsMundo), que segue sendo o
/// único dono do dado e da reconstrução visual.
/// </summary>
public class FogEditorTools : MonoBehaviour
{
    [Header("Referências")]
    [Tooltip("Precisa ser um MonoBehaviour que implemente IGerenciadorDeParedes — CampanhaMapManager " +
        "(Campanhas) ou MapManager (Salas).")]
    [SerializeField] private MonoBehaviour mapManagerBehaviour;

    private IGerenciadorDeParedes mapManager;

    [Header("Preview do Retângulo")]
    [SerializeField] private Color corPreviewDesenhar = Color.yellow;
    [Tooltip("Cor do preview enquanto arrasta em modo Apagar — diferente de Desenhar, pra ficar claro " +
        "que esse arrasto vai REMOVER fog, não adicionar.")]
    [SerializeField] private Color corPreviewApagar = new Color(1f, 0.2f, 0.2f, 1f);
    [SerializeField, Range(0.005f, 0.2f)] private float espessuraPreview = 0.05f;

    // ── Estado ────────────────────────────────────────────────────────────────

    public FogToolType FerramentaAtiva { get; private set; } = FogToolType.None;
    private bool arrastandoRetangulo;
    private (int col, int lin) celulaInicio;

    private LineRenderer _previewRetangulo;
    private Camera _camera;
    private Material _materialPreview;

    private void Awake()
    {
        mapManager = mapManagerBehaviour as IGerenciadorDeParedes;
        if (mapManager == null && mapManagerBehaviour != null)
            Debug.LogWarning("[FogEditorTools] mapManagerBehaviour não implementa IGerenciadorDeParedes.");

        _camera = mapManager != null ? mapManager.CameraAtiva : null;
        if (_camera == null) _camera = Camera.main;

        CriarPreviewRetangulo();
    }

    private void OnDestroy()
    {
        if (_materialPreview != null) Destroy(_materialPreview);
    }

    // ── API pública (ligar aos botões de ferramenta pelo Inspector) ─────────

    /// <summary>Sobrecarga em int pra poder ser chamada direto por um Button.onClick no Inspector
    /// (UnityEvent não expõe enums customizados na lista de parâmetros persistentes). 0 = None,
    /// 1 = Desenhar, 2 = Apagar.</summary>
    public void SelecionarFerramenta(int tipo) => SelecionarFerramenta((FogToolType)tipo);

    public void SelecionarFerramenta(FogToolType tipo)
    {
        // Clicar de novo no botão da ferramenta já ativa desliga (volta pra None) — mesmo toggle de
        // WallEditorTools.SelecionarFerramenta.
        FogToolType novaFerramenta = FerramentaAtiva == tipo ? FogToolType.None : tipo;

        if (arrastandoRetangulo) CancelarRetangulo();
        FerramentaAtiva = novaFerramenta;
    }

    /// <summary>Desliga a ferramenta (volta pra None) — chamado ao fechar o content da barra de
    /// ferramentas (mesmo padrão de ReguaMedicaoManager.Desativar).</summary>
    public void Desativar() => SelecionarFerramenta(FogToolType.None);

    // ── Update ────────────────────────────────────────────────────────────────

    private void Update()
    {
        if (FerramentaAtiva == FogToolType.None || mapManager == null || _camera == null) return;

        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        bool escPressionado = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        if (arrastandoRetangulo && (escPressionado || mouse.rightButton.wasPressedThisFrame))
        {
            CancelarRetangulo();
            return;
        }

        bool sobreUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        Vector3 mundo = ObterPosicaoMouseNoMundo(mouse);

        AtualizarFerramentaRetangulo(mouse, mundo, sobreUI, FerramentaAtiva);
    }

    private Vector3 ObterPosicaoMouseNoMundo(Mouse mouse)
    {
        Vector2 tela2D = mouse.position.ReadValue();
        Vector3 tela = new Vector3(tela2D.x, tela2D.y, Mathf.Abs(_camera.transform.position.z));
        Vector3 mundo = _camera.ScreenToWorldPoint(tela);
        mundo.z = 0f;
        return mundo;
    }

    // ── Ferramenta de retângulo (clique-e-arrasta) — compartilhada por Desenhar e Apagar ────────

    private void AtualizarFerramentaRetangulo(Mouse mouse, Vector3 mundo, bool sobreUI, FogToolType modo)
    {
        if (arrastandoRetangulo)
        {
            AtualizarPreviewRetangulo(celulaInicio, mapManager.MundoParaCelula(mundo));

            if (mouse.leftButton.wasReleasedThisFrame)
                ConcluirRetangulo(mundo, modo);

            return;
        }

        // Só começa um arrasto se o clique inicial não caiu em cima de UI (botão/painel).
        if (mouse.leftButton.wasPressedThisFrame && !sobreUI)
            IniciarRetangulo(mundo, modo);
    }

    private void IniciarRetangulo(Vector3 mundo, FogToolType modo)
    {
        celulaInicio = mapManager.MundoParaCelula(mundo);
        arrastandoRetangulo = true;

        AplicarCorPreview(modo);
        AtualizarPreviewRetangulo(celulaInicio, celulaInicio);
        _previewRetangulo.enabled = true;
    }

    private void ConcluirRetangulo(Vector3 mundo, FogToolType modo)
    {
        (int col, int lin) celulaFim = mapManager.MundoParaCelula(mundo);

        arrastandoRetangulo = false;
        _previewRetangulo.enabled = false;

        int colMin = Mathf.Min(celulaInicio.col, celulaFim.col);
        int linMin = Mathf.Min(celulaInicio.lin, celulaFim.lin);
        int colMax = Mathf.Max(celulaInicio.col, celulaFim.col);
        int linMax = Mathf.Max(celulaInicio.lin, celulaFim.lin);

        if (modo == FogToolType.Desenhar)
            mapManager.AdicionarFogRect(new FogRect { colMin = colMin, linMin = linMin, colMax = colMax, linMax = linMax });
        else
            mapManager.ApagarFogNoRetangulo(colMin, linMin, colMax, linMax);
    }

    private void CancelarRetangulo()
    {
        arrastandoRetangulo = false;
        _previewRetangulo.enabled = false;
    }

    private void AplicarCorPreview(FogToolType modo)
    {
        Color cor = modo == FogToolType.Desenhar ? corPreviewDesenhar : corPreviewApagar;
        _previewRetangulo.startColor = cor;
        _previewRetangulo.endColor = cor;
    }

    // ── Preview (gerado em código — nenhum prefab necessário) ────────────────

    private void CriarPreviewRetangulo()
    {
        var go = new GameObject("FogEditor_PreviewRetangulo");
        go.transform.SetParent(transform, false);

        _previewRetangulo = go.AddComponent<LineRenderer>();
        _previewRetangulo.useWorldSpace = true;
        _previewRetangulo.loop = true; // fecha o retângulo sozinho — 4 posições bastam, sem repetir a primeira
        _previewRetangulo.positionCount = 4;
        _materialPreview = new Material(Shader.Find("Sprites/Default"));
        _previewRetangulo.material = _materialPreview;
        _previewRetangulo.startColor = corPreviewDesenhar;
        _previewRetangulo.endColor = corPreviewDesenhar;
        _previewRetangulo.startWidth = espessuraPreview;
        _previewRetangulo.endWidth = espessuraPreview;
        _previewRetangulo.numCapVertices = 2;
        _previewRetangulo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _previewRetangulo.receiveShadows = false;
        _previewRetangulo.generateLightingData = false;
        _previewRetangulo.sortingOrder = 1000; // sempre por cima de mapa/grid/tokens/paredes enquanto edita
        _previewRetangulo.enabled = false;
    }

    private void AtualizarPreviewRetangulo((int col, int lin) a, (int col, int lin) b)
    {
        Bounds boundsA = mapManager.CelulaParaBoundsMundo(a.col, a.lin);
        Bounds boundsB = mapManager.CelulaParaBoundsMundo(b.col, b.lin);

        float minX = Mathf.Min(boundsA.min.x, boundsB.min.x);
        float minY = Mathf.Min(boundsA.min.y, boundsB.min.y);
        float maxX = Mathf.Max(boundsA.max.x, boundsB.max.x);
        float maxY = Mathf.Max(boundsA.max.y, boundsB.max.y);
        float z = Mathf.Min(boundsA.center.z, boundsB.center.z) - 0.01f;

        _previewRetangulo.SetPosition(0, new Vector3(minX, minY, z));
        _previewRetangulo.SetPosition(1, new Vector3(maxX, minY, z));
        _previewRetangulo.SetPosition(2, new Vector3(maxX, maxY, z));
        _previewRetangulo.SetPosition(3, new Vector3(minX, maxY, z));
    }
}
