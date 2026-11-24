using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SalvaguardaUIItem : MonoBehaviour
{
    [Header("Referências de UI")]
    public TMP_Text nomeText;
    public Toggle   treinadoToggle;
    public TMP_Text valorText;

    public void Setup(string nome, bool treinado, int valor)
    {
        if (nomeText != null)
            nomeText.text = nome ?? "";

        if (treinadoToggle != null)
        {
            treinadoToggle.SetIsOnWithoutNotify(treinado);
            treinadoToggle.interactable = false;
        }

        if (valorText != null)
            valorText.text = valor >= 0 ? $"+{valor}" : valor.ToString();
    }
}
