using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Posiciona um RectTransform num canto de um ponto de tela (ex: clique ou posição do mouse) em
/// vez de centralizado nele. Tenta lower-left primeiro (o ponto fica no canto superior-direito do
/// painel, que cresce pra baixo-esquerda) e cai pras outras 3 combinações se não couber inteiro na
/// tela; se nenhuma couber, usa a última tentativa e empurra pra dentro da tela.
/// Compartilhado por MenuAtalhoUI e TokenStatBlockTooltipUI.
/// </summary>
public static class RectTransformCornerPositioner
{
    private static readonly Vector2[] PivotsPorPreferencia =
    {
        new Vector2(1f, 1f), // lower-left (preferido)
        new Vector2(0f, 1f), // lower-right
        new Vector2(1f, 0f), // upper-left
        new Vector2(0f, 0f), // upper-right
    };

    public static void PosicionarNoCantoDoPonto(RectTransform rectTransform, Vector2 posicaoTela)
    {
        if (rectTransform == null) return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

        for (int i = 0; i < PivotsPorPreferencia.Length; i++)
        {
            rectTransform.pivot = PivotsPorPreferencia[i];
            rectTransform.position = posicaoTela;

            if (CabeNaTela(rectTransform) || i == PivotsPorPreferencia.Length - 1)
                break;
        }

        rectTransform.position += CalcularAjustePraCaberNaTela(rectTransform);
    }

    private static bool CabeNaTela(RectTransform rectTransform)
    {
        Vector3[] cantos = new Vector3[4];
        rectTransform.GetWorldCorners(cantos);
        return cantos[0].x >= 0f && cantos[2].x <= Screen.width
            && cantos[0].y >= 0f && cantos[2].y <= Screen.height;
    }

    private static Vector3 CalcularAjustePraCaberNaTela(RectTransform rectTransform)
    {
        Vector3[] cantos = new Vector3[4];
        rectTransform.GetWorldCorners(cantos);

        Vector3 ajuste = Vector3.zero;
        if (cantos[2].x > Screen.width) ajuste.x -= cantos[2].x - Screen.width;
        if (cantos[0].x < 0f) ajuste.x -= cantos[0].x;
        if (cantos[2].y > Screen.height) ajuste.y -= cantos[2].y - Screen.height;
        if (cantos[0].y < 0f) ajuste.y -= cantos[0].y;

        return ajuste;
    }
}
