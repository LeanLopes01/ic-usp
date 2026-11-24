using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Tooltip para tokens no mapa. Singleton leve — coloque um único GameObject
/// com este script no Canvas da sala (começa inativo).
/// Chamado por MapTokenDraggable após 3 segundos de hover.
/// </summary>
public class TokenTooltipUI : MonoBehaviour
{
    public static TokenTooltipUI Instance { get; private set; }

    [Header("Textos")]
    [SerializeField] private TMP_Text tituloTxt;
    [SerializeField] private TMP_Text infoTxt;

    [Header("Configurações")]
    [SerializeField] private Vector2 offsetMouse = new Vector2(15f, -15f);

    private RectTransform _rt;
    private Canvas        _canvas;

    private void Awake()
    {
        Instance = this;
        enabled  = true; // garante ativo independente do estado no prefab
        _rt      = GetComponent<RectTransform>();
        _canvas  = GetComponentInParent<Canvas>();

        CanvasGroup cg = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
        cg.interactable   = false;
        cg.blocksRaycasts = false;

        gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (gameObject.activeSelf)
            AtualizarPosicao();
    }

    public void Mostrar(string nome, int vidaAtual, int vidaMaxima)
    {
        if (tituloTxt != null)
            tituloTxt.text = $"<b>{nome}</b>";

        if (infoTxt != null)
        {
            if (vidaMaxima > 0)
                infoTxt.text = $"PV: {vidaAtual} / {vidaMaxima}";
            else
                infoTxt.text = string.Empty;
        }

        gameObject.SetActive(true);
        AtualizarPosicao();
    }

    public void Esconder() => gameObject.SetActive(false);

    private void AtualizarPosicao()
    {
        if (_canvas == null || Mouse.current == null) return;

        Vector2 posicaoMouse = Mouse.current.position.ReadValue();

        float pivotX = posicaoMouse.x / Screen.width  > 0.5f ? 1f : 0f;
        float pivotY = posicaoMouse.y / Screen.height > 0.5f ? 1f : 0f;
        _rt.pivot = new Vector2(pivotX, pivotY);

        RectTransform parentRect = transform.parent?.GetComponent<RectTransform>();
        if (parentRect == null) return;

        Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, posicaoMouse, cam, out Vector2 local))
        {
            local.x += pivotX == 0f ?  offsetMouse.x : -offsetMouse.x;
            local.y += pivotY == 0f ?  Mathf.Abs(offsetMouse.y) : -Mathf.Abs(offsetMouse.y);
            _rt.localPosition = local;
        }
    }
}
