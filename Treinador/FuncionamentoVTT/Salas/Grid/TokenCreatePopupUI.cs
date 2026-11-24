using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TokenCreatePopupUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image previewImage;
    [SerializeField] private TMP_InputField nomeTokenInput;
    [SerializeField] private Button botaoImportar;
    [SerializeField] private Button botaoConfirmar;
    [SerializeField] private Button botaoFechar;

    private MapManager manager;

    public void Setup(MapManager mapManager)
    {
        manager = mapManager;

        if (botaoImportar != null)
        {
            botaoImportar.onClick.RemoveListener(OnClickImportar);
            botaoImportar.onClick.AddListener(OnClickImportar);
        }

        if (botaoConfirmar != null)
        {
            botaoConfirmar.onClick.RemoveListener(OnClickConfirmar);
            botaoConfirmar.onClick.AddListener(OnClickConfirmar);
        }

        if (botaoFechar != null)
        {
            botaoFechar.onClick.RemoveListener(OnClickFechar);
            botaoFechar.onClick.AddListener(OnClickFechar);
        }
    }

    public void LimparUI()
    {
        if (previewImage != null)
            previewImage.sprite = null;

        if (nomeTokenInput != null)
            nomeTokenInput.text = string.Empty;
    }

    public void AtualizarPreview(Sprite preview)
    {
        if (previewImage != null)
            previewImage.sprite = preview;
    }

    public string ObterNomeToken(string fallback)
    {
        if (nomeTokenInput == null)
            return fallback;

        string nome = nomeTokenInput.text != null ? nomeTokenInput.text.Trim() : string.Empty;
        return string.IsNullOrWhiteSpace(nome) ? fallback : nome;
    }

    private void OnClickImportar()
    {
        if (manager != null)
            manager.ImportarImagemParaNovoToken();
    }

    private void OnClickConfirmar()
    {
        if (manager != null)
            manager.ConfirmarCriacaoNovoToken();
    }

    private void OnClickFechar()
    {
        if (manager != null)
            manager.FecharPopupCriarToken();
    }
}
