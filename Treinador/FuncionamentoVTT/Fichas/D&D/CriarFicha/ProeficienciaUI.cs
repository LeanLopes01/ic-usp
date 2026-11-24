using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ProeficienciaUI : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text labelText;
    public Button myButton;

    private void Awake()
    {
        if (myButton == null) myButton = GetComponent<Button>();
    }

    public void SetTexto(string texto)
    {
        if (labelText == null) labelText = GetComponent<TMP_Text>();
        if (labelText == null) labelText = GetComponentInChildren<TMP_Text>();
        if (labelText != null) labelText.text = texto;
    }

    // Configura o que acontece ao clicar neste item
    public void SetupButton(System.Action onClickAction)
    {
        if (myButton != null)
        {
            myButton.onClick.RemoveAllListeners();
            myButton.onClick.AddListener(() => onClickAction?.Invoke());
        }
    }
}