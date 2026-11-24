using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class FichaCatalogoDadoItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text   tituloText;
    [SerializeField] private Button     botaoExpandir;
    [SerializeField] private GameObject painelDescricao;
    [SerializeField] private TMP_Text   descricaoText;

    private bool _expandido;

    private void Awake()
    {
        if (painelDescricao != null) painelDescricao.SetActive(false);

        if (botaoExpandir != null)
        {
            botaoExpandir.onClick.RemoveAllListeners();
            botaoExpandir.onClick.AddListener(ToggleDescricao);
        }
    }

    public void Setup(string titulo, string descricao)
    {
        if (tituloText    != null) tituloText.text    = titulo    ?? string.Empty;
        if (descricaoText != null) descricaoText.text = descricao ?? string.Empty;

        _expandido = false;
        if (painelDescricao != null) painelDescricao.SetActive(false);
    }

    private void ToggleDescricao()
    {
        _expandido = !_expandido;
        if (painelDescricao != null) painelDescricao.SetActive(_expandido);
    }
}
