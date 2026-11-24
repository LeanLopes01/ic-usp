using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public class TooltipSugestaoAcaoUI : MonoBehaviour, IBeginDragHandler, IDragHandler
{
    [Header("Textos")]
    public TMP_Text tituloTxt;
    public TMP_Text descricaoTxt;

    [Header("Botões")]
    public Button btnAdicionar;
    public Button btnIgnorar;

    [Header("Posicionamento")]
    public Vector2 offsetMouse = new Vector2(24f, -24f);
    public bool posicionarNoMouseAoAbrir = true;

    [Header("Interação")]
    public bool permitirArrastar = true;

    private RectTransform rectTransform;
    private Canvas canvasPai;

    public Vector2 ObterPosicaoAtual()
    {
        if (rectTransform == null) rectTransform = transform as RectTransform;
        return rectTransform != null ? rectTransform.anchoredPosition : Vector2.zero;
    }

    public void DefinirPosicaoManual(Vector2 anchoredPosition)
    {
        if (rectTransform == null) rectTransform = transform as RectTransform;
        if (rectTransform == null) return;

        rectTransform.anchoredPosition = anchoredPosition;
        rectTransform.SetAsLastSibling();
    }

    /// <summary>
    /// Configura o tooltip. Recebe as ações (funções) que serão executadas ao clicar nos botões.
    /// </summary>
    public void Setup(string titulo, string descricao, Action aoConfirmar, Action aoIgnorar)
    {
        if (rectTransform == null) rectTransform = transform as RectTransform;
        if (canvasPai == null) canvasPai = GetComponentInParent<Canvas>();

        if (tituloTxt) tituloTxt.text = titulo;
        if (descricaoTxt) descricaoTxt.text = descricao;

        // Limpa listeners antigos para evitar bugs de cliques duplicados
        btnAdicionar.onClick.RemoveAllListeners();
        btnAdicionar.onClick.AddListener(() => {
            aoConfirmar?.Invoke();
            Destroy(gameObject); // Fecha o tooltip após aceitar
        });

        btnIgnorar.onClick.RemoveAllListeners();
        btnIgnorar.onClick.AddListener(() => {
            aoIgnorar?.Invoke();
            Destroy(gameObject); // Fecha o tooltip após ignorar
        });

        if (posicionarNoMouseAoAbrir)
            PosicionarAoLadoDoMouse();
    }

    public void PosicionarAoLadoDoMouse()
    {
        if (rectTransform == null) return;

        if (canvasPai == null) canvasPai = GetComponentInParent<Canvas>();
        if (canvasPai == null) return;

        Vector2 mousePos = ObterPosicaoMouseTela();

        Camera uiCamera = canvasPai.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvasPai.worldCamera;
        RectTransform canvasRect = canvasPai.transform as RectTransform;
        if (canvasRect == null) return;

        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, mousePos, uiCamera, out localPoint))
        {
            rectTransform.anchoredPosition = localPoint + offsetMouse;
            rectTransform.SetAsLastSibling();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (!permitirArrastar || rectTransform == null) return;
        rectTransform.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!permitirArrastar || rectTransform == null) return;
        if (canvasPai == null) canvasPai = GetComponentInParent<Canvas>();

        float scale = (canvasPai != null && canvasPai.scaleFactor > 0f) ? canvasPai.scaleFactor : 1f;
        rectTransform.anchoredPosition += eventData.delta / scale;
    }

    private Vector2 ObterPosicaoMouseTela()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
        return Input.mousePosition;
#else
        return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
#endif
    }
}