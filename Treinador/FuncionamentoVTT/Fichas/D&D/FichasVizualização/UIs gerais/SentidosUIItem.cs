using UnityEngine;
using TMPro;

public class SentidosUIItem : MonoBehaviour
{
    [Header("Referências de UI")]
    public TMP_Text nomeText;
    public TMP_Text valorText;

    public void Setup(string nome, int valor)
    {
        if (nomeText != null)
            nomeText.text = string.IsNullOrWhiteSpace(nome) ? "Sentido" : nome;

        if (valorText != null)
            valorText.text = valor.ToString();
    }

    public string GetNome()
    {
        return nomeText != null ? nomeText.text : string.Empty;
    }

    public int GetValor()
    {
        if (valorText == null) return 0;
        return int.TryParse(valorText.text, out int parsed) ? parsed : 0;
    }
}
