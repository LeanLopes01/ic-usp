using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class PericiaUIItem : MonoBehaviour
{
    [Header("Referências de UI")]
    public TMP_Text nomeText;
    public TMP_Text atributoBaseText; // Ex: (FOR), (DEX)
    public TMP_Text valorTotalText;
    public Toggle treinadoToggle;

    /// <summary>
    /// Configura os dados visuais do Prefab de Perícia.
    /// </summary>
    public void Setup(string nome, string atributoSigla, int valorTotal, bool treinado)
    {
        if (nomeText != null) 
            nomeText.text = nome;

        if (atributoBaseText != null) 
            atributoBaseText.text = atributoSigla;

        if (valorTotalText != null) 
        {
            // Adiciona o sinal de "+" se for positivo ou 0
            string sinal = valorTotal >= 0 ? "+" : "";
            valorTotalText.text = sinal + valorTotal.ToString();
        }

        if (treinadoToggle != null) 
        {
            treinadoToggle.isOn = treinado;
            treinadoToggle.interactable = false; // Mantém como somente leitura na Ficha Geral
        }
    }
}