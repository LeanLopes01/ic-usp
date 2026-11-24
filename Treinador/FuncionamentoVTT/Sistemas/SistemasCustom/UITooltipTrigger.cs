using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Gerenciador de tooltips por hover.
/// Coloque este componente em qualquer GameObject da cena.
///
/// Configure os arrays em paralelo:
///   objectsButtons[0] → objectsToShow[0]
///   objectsButtons[1] → objectsToShow[1]  ...
///
/// Cada objectsToShow deve ser um prefab/painel com RectTransform,
/// filho de um Canvas (ou de um panel fullscreen dentro do Canvas).
/// Em Awake todos os painéis são desativados; ao hover eles ativam
/// e seguem o mouse, igual ao TooltipUI.
/// </summary>
public class UITooltipTrigger : MonoBehaviour
{
    [Header("Pares Gatilho → Painel (por índice)")]
    public GameObject[] objectsButtons;
    public GameObject[] objectsToShow;

    [Header("Posicionamento")]
    public Vector2 offsetMouse = new Vector2(15f, -15f);

    // ── Estado ────────────────────────────────────────────────────────────────

    private Canvas        _canvas;
    private GameObject    _tooltipAtivo;
    private RectTransform _rectAtivo;
    private RectTransform _parentRectAtivo;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        Canvas canvasAtual = GetComponentInParent<Canvas>() ?? FindObjectOfType<Canvas>();

        if (canvasAtual == null)
        {
            VTTLog.UIError("TooltipTrigger: nenhum Canvas encontrado na cena.");
            return;
        }

        for (int i = 0; i < objectsToShow.Length; i++)
        {
            var go = objectsToShow[i];
            if (go == null) continue;

            if (!go.scene.IsValid())
            {
                go = Instantiate(go, canvasAtual.transform);
                objectsToShow[i] = go;
            }

            go.SetActive(false);
            var cg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            cg.interactable   = false;
            cg.blocksRaycasts = false;
        }
    }

    private void Start()
    {
        for (int i = 0; i < objectsButtons.Length; i++)
        {
            if (objectsButtons[i] == null) continue;
            var helper = objectsButtons[i].GetComponent<UITooltipButtonHelper>()
                      ?? objectsButtons[i].AddComponent<UITooltipButtonHelper>();
            helper.Inicializar(i, this);
        }
    }

    private void Update()
    {
        if (_tooltipAtivo == null || !_tooltipAtivo.activeSelf) return;
        AtualizarPosicao();
    }

    // ── API chamada pelo helper ───────────────────────────────────────────────

    // ── API chamada pelo helper ───────────────────────────────────────────────

    public void MostrarTooltip(int index)
    {
        EsconderTooltip();

        if (index < 0 || index >= objectsToShow.Length || objectsToShow[index] == null)
            return;

        _tooltipAtivo    = objectsToShow[index];
        _rectAtivo       = _tooltipAtivo.GetComponent<RectTransform>();
        _parentRectAtivo = _tooltipAtivo.transform.parent?.GetComponent<RectTransform>();
        _canvas          = _tooltipAtivo.GetComponentInParent<Canvas>();

        _tooltipAtivo.SetActive(true);
        _tooltipAtivo.transform.SetAsLastSibling();

        AtualizarPosicao();
    }

    public void EsconderTooltip()
    {
        _tooltipAtivo?.SetActive(false);
        _tooltipAtivo    = null;
        _rectAtivo       = null;
        _parentRectAtivo = null;
    }

    // ── Posicionamento (igual ao TooltipUI) ───────────────────────────────────

    private void AtualizarPosicao()
    {
        if (_rectAtivo == null || _canvas == null) return;

        Vector2 posMouse = Mouse.current.position.ReadValue();

        // Pivot dinâmico: evita que o tooltip saia da tela
        float pivotX = posMouse.x / Screen.width  > 0.5f ? 1f : 0f;
        float pivotY = posMouse.y / Screen.height > 0.5f ? 1f : 0f;
        _rectAtivo.pivot = new Vector2(pivotX, pivotY);

        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _canvas.worldCamera;

        RectTransform parentRect = _parentRectAtivo != null
            ? _parentRectAtivo
            : (RectTransform)_canvas.transform;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                parentRect, posMouse, cam, out Vector2 pontoLocal))
        {
            pontoLocal.x += pivotX == 0f ?  offsetMouse.x : -offsetMouse.x;
            pontoLocal.y += pivotY == 0f ?  Mathf.Abs(offsetMouse.y) : -Mathf.Abs(offsetMouse.y);
            _rectAtivo.localPosition = pontoLocal;
        }
    }
}
