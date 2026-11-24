using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(TMP_InputField))]
public class AutoResizeInputField : MonoBehaviour
{
    [Header("Arraste o Scroll View principal aqui")]
    public ScrollRect scrollRect;

    private TMP_InputField inputField;
    private RectTransform rectTransform;

    void Awake()
    {
        inputField = GetComponent<TMP_InputField>();
        rectTransform = GetComponent<RectTransform>();

        inputField.onValueChanged.AddListener(AoDigitarTexto);
    }

    private void AoDigitarTexto(string novoTexto)
    {
        // Força a atualização do tamanho da caixa de texto
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

        if (scrollRect != null)
        {
            // Atualiza os cálculos de tela do Canvas
            Canvas.ForceUpdateCanvases();
            
            // O pulo do gato: Só rola a tela para o final se o jogador
            // estiver digitando na última posição do texto.
            // Se ele clicar na primeira linha para editar, a tela não pula!
            if (inputField.caretPosition >= inputField.text.Length - 1)
            {
                scrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }
}