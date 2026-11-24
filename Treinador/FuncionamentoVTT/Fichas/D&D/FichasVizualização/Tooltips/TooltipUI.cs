using UnityEngine;
using TMPro;
using UnityEngine.InputSystem;

public class TooltipUI : MonoBehaviour
{
    [Header("Textos do Tooltip")]
    public TMP_Text tituloTxt;
    public TMP_Text descricaoTxt;

    [Header("Configurações")]
    public Vector2 offsetMouse = new Vector2(15f, -15f); // Distância do mouse

    private RectTransform rectTransform;
    private Canvas canvasPrincipal;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvasPrincipal = GetComponentInParent<Canvas>();
        
        // Desativa a interferência do mouse no próprio tooltip
        // Isso evita aquele problema do tooltip ficar "piscando" se o mouse encostar nele
        CanvasGroup cg = GetComponent<CanvasGroup>();
        if (cg == null) cg = gameObject.AddComponent<CanvasGroup>();
        cg.interactable = false;
        cg.blocksRaycasts = false;
    }

    void Update()
    {
        AtualizarPosicao();
    }

    public void Mostrar(string titulo, string subtitulo, string descricao)
    {
        gameObject.SetActive(true);
        if (tituloTxt) tituloTxt.text = $"<b>{titulo}</b>\n<size=80%><color=#aaaaaa>{subtitulo}</color></size>";
        if (descricaoTxt) descricaoTxt.text = descricao;
        
        AtualizarPosicao();
    }

    public void Esconder()
    {
        gameObject.SetActive(false);
    }

    private void AtualizarPosicao()
    {
        if (canvasPrincipal == null) return;

        Vector2 posicaoMouse = Mouse.current.position.ReadValue();

        // 1. Define o Pivot para o Tooltip não vazar para fora da tela
        float pivotX = posicaoMouse.x / Screen.width > 0.5f ? 1f : 0f;
        float pivotY = posicaoMouse.y / Screen.height > 0.5f ? 1f : 0f;
        rectTransform.pivot = new Vector2(pivotX, pivotY);

        // 2. A MÁGICA: Converte o pixel da tela para a coordenada local do Canvas
        RectTransform parentRect = transform.parent.GetComponent<RectTransform>();
        
        // Descobre se precisa usar a câmera (se o canvas não for Overlay)
        Camera cam = canvasPrincipal.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvasPrincipal.worldCamera;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, posicaoMouse, cam, out Vector2 pontoLocal))
        {
            // Aplica o offset corrigido pelo pivot
            pontoLocal.x += pivotX == 0 ? offsetMouse.x : -offsetMouse.x;
            pontoLocal.y += pivotY == 0 ? Mathf.Abs(offsetMouse.y) : -Mathf.Abs(offsetMouse.y);

            // Usa localPosition em vez de position!
            rectTransform.localPosition = pontoLocal;
        }
    }
}