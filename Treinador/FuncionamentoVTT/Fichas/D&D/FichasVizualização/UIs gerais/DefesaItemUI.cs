using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DefesaItemUI : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text tituloText;
    public TMP_Text descricaoText;
    public Button btnToggle;
    public Button btnRemover;

    private bool expandido = false;
    private System.Action onChanged;

    public void Setup(string titulo, string descricao, System.Action onChange = null)
    {
        onChanged = onChange;

        if (tituloText) tituloText.text = titulo;

        if (descricaoText)
        {
            descricaoText.text = descricao;
            descricaoText.gameObject.SetActive(false);
        }

        if (btnToggle)
            btnToggle.onClick.AddListener(ToggleExpansao);

        if (btnRemover)
            btnRemover.onClick.AddListener(() =>
            {
                Destroy(gameObject);
                onChanged?.Invoke();
            });
    }

    public void ToggleExpansao()
    {
        expandido = !expandido;
        if (descricaoText) descricaoText.gameObject.SetActive(expandido);
    }

    public string GetTitulo()    => tituloText    != null ? tituloText.text    : "";
    public string GetDescricao() => descricaoText != null ? descricaoText.text : "";
}
