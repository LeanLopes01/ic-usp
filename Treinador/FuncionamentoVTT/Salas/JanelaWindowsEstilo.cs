using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class JanelaWindowsEstilo : MonoBehaviour, IDragHandler, IPointerDownHandler
{
    [Header("Referências")]
    public RectTransform rectTransform;
    public Canvas canvas;
    public TMP_Text tituloJanelaText;

    [Header("Áreas de Interação")]
    public RectTransform barraTitulo;
    public RectTransform alcaRedimensionar;

    [Header("Controles")]
    public Button botaoMaximizar;
    public Button botaoFechar;
    public RectTransform areaMaximizarNoPai;

    [Header("Limites")]
    public float larguraMinima = 200f;
    public float alturaMinima = 150f;

    public event Action<JanelaWindowsEstilo> OnCloseVisual;

    private bool estaMaximizada;
    private Vector2 posicaoAnterior;
    private Vector2 tamanhoAnterior;
    private Vector2 anchorMinAnterior;
    private Vector2 anchorMaxAnterior;
    private Vector2 offsetMinAnterior;
    private Vector2 offsetMaxAnterior;

    private void Awake()
    {
        if (rectTransform == null) rectTransform = GetComponent<RectTransform>();
        if (canvas == null) canvas = GetComponentInParent<Canvas>();

        if (botaoMaximizar != null)
        {
            botaoMaximizar.onClick.RemoveListener(ToggleMaximizar);
            botaoMaximizar.onClick.AddListener(ToggleMaximizar);
        }

        if (botaoFechar != null)
        {
            botaoFechar.onClick.RemoveListener(FecharVisual);
            botaoFechar.onClick.AddListener(FecharVisual);
        }
    }

    private void OnDestroy()
    {
        if (botaoMaximizar != null)
            botaoMaximizar.onClick.RemoveListener(ToggleMaximizar);

        if (botaoFechar != null)
            botaoFechar.onClick.RemoveListener(FecharVisual);
    }

    public void SetWindowTitle(string titulo)
    {
        if (tituloJanelaText != null)
            tituloJanelaText.text = string.IsNullOrWhiteSpace(titulo) ? "Ficha" : titulo.Trim();
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (rectTransform != null)
            rectTransform.SetAsLastSibling();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (rectTransform == null || canvas == null) return;
        if (estaMaximizada) return;

        if (barraTitulo != null && RectTransformUtility.RectangleContainsScreenPoint(barraTitulo, eventData.position, eventData.pressEventCamera))
        {
            rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
        }

        if (alcaRedimensionar != null && RectTransformUtility.RectangleContainsScreenPoint(alcaRedimensionar, eventData.position, eventData.pressEventCamera))
        {
            Vector2 mouseDelta = eventData.delta / canvas.scaleFactor;
            Vector2 novoTamanho = rectTransform.sizeDelta + new Vector2(mouseDelta.x, -mouseDelta.y);

            novoTamanho.x = Mathf.Max(novoTamanho.x, larguraMinima);
            novoTamanho.y = Mathf.Max(novoTamanho.y, alturaMinima);

            rectTransform.sizeDelta = novoTamanho;
        }
    }

    public void ToggleMaximizar()
    {
        if (rectTransform == null) return;

        if (!estaMaximizada)
            Maximizar();
        else
            Restaurar();
    }

    public void FecharVisual()
    {
        gameObject.SetActive(false);
        OnCloseVisual?.Invoke(this);
    }

    public void AbrirVisual()
    {
        gameObject.SetActive(true);
        if (rectTransform != null)
            rectTransform.SetAsLastSibling();
    }

    private void Maximizar()
    {
        RectTransform parentArea = areaMaximizarNoPai;
        if (parentArea == null)
            parentArea = rectTransform.parent as RectTransform;

        if (parentArea == null)
            return;

        posicaoAnterior = rectTransform.anchoredPosition;
        tamanhoAnterior = rectTransform.sizeDelta;
        anchorMinAnterior = rectTransform.anchorMin;
        anchorMaxAnterior = rectTransform.anchorMax;
        offsetMinAnterior = rectTransform.offsetMin;
        offsetMaxAnterior = rectTransform.offsetMax;

        rectTransform.anchorMin = new Vector2(0f, 0f);
        rectTransform.anchorMax = new Vector2(1f, 1f);
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.SetAsLastSibling();
        estaMaximizada = true;
    }

    private void Restaurar()
    {
        rectTransform.anchorMin = anchorMinAnterior;
        rectTransform.anchorMax = anchorMaxAnterior;
        rectTransform.offsetMin = offsetMinAnterior;
        rectTransform.offsetMax = offsetMaxAnterior;
        rectTransform.anchoredPosition = posicaoAnterior;
        rectTransform.sizeDelta = tamanhoAnterior;
        estaMaximizada = false;
    }
}
