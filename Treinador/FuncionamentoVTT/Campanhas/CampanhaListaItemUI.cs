using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Item da lista de campanhas salvas — mesmo padrão de Salas/SalaListaItemUI.cs, simplificado
/// (campanha é local/single-user: sem contagem de membros nem papel de mestre/jogador).
/// </summary>
public class CampanhaListaItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image     imagemCampanha;
    [SerializeField] private TMP_Text  tituloCampanhaText;
    [SerializeField] private TMP_Text  sistemaCampanhaText;
    [SerializeField] private TMP_Text  descricaoCampanhaText;
    [SerializeField] private TMP_Text  dataCriacaoText;
    [SerializeField] private Button    botaoAbrir;

    [Header("Visual")]
    [SerializeField] private Sprite imagemFallback;

    private Action onAbrir;
    private Texture2D texturaCarregada;
    private Sprite spriteCarregado;

    private void Awake()
    {
        if (botaoAbrir == null)
            botaoAbrir = GetComponentInChildren<Button>(true);
    }

    public void Configurar(CampanhaLocalSnapshot campanha, Action onAbrirClick)
    {
        onAbrir = onAbrirClick;

        if (tituloCampanhaText != null)
            tituloCampanhaText.text = SafeOrDefault(campanha?.nome, "Campanha sem nome");

        if (sistemaCampanhaText != null)
            sistemaCampanhaText.text = SafeOrDefault(campanha?.sistema, "Sistema não definido");

        if (descricaoCampanhaText != null)
            descricaoCampanhaText.text = SafeOrDefault(campanha?.descricao, "Sem descrição.");

        if (dataCriacaoText != null)
        {
            dataCriacaoText.text = campanha != null && DateTime.TryParse(campanha.criadoEmUtc, out DateTime dataCriacao)
                ? $"Criada {dataCriacao:dd/MM/yyyy}"
                : "-";
        }

        if (botaoAbrir != null)
        {
            botaoAbrir.onClick.RemoveListener(OnClickAbrir);
            botaoAbrir.onClick.AddListener(OnClickAbrir);
        }

        CarregarImagemCampanha(campanha != null ? campanha.imagemPath : string.Empty);
    }

    private void OnDestroy()
    {
        if (botaoAbrir != null)
            botaoAbrir.onClick.RemoveListener(OnClickAbrir);

        LimparImagemRuntime();
    }

    private void OnClickAbrir()
    {
        onAbrir?.Invoke();
    }

    private void CarregarImagemCampanha(string imagePath)
    {
        if (imagemCampanha == null) return;

        LimparImagemRuntime();

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            DefinirFallbackImagem();
            return;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(imagePath);
            if (bytes == null || bytes.Length == 0)
            {
                DefinirFallbackImagem();
                return;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            if (!tex.LoadImage(bytes))
            {
                Destroy(tex);
                DefinirFallbackImagem();
                return;
            }

            texturaCarregada = tex;
            spriteCarregado = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            imagemCampanha.sprite = spriteCarregado;
            imagemCampanha.preserveAspect = true;
            imagemCampanha.color = Color.white;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CampanhaListaItemUI] Falha ao carregar imagem da campanha '{imagePath}': {e.Message}");
            DefinirFallbackImagem();
        }
    }

    private void DefinirFallbackImagem()
    {
        if (imagemCampanha == null) return;
        imagemCampanha.sprite = imagemFallback;
        imagemCampanha.preserveAspect = true;
        imagemCampanha.color = Color.white;
    }

    private void LimparImagemRuntime()
    {
        if (imagemCampanha != null) imagemCampanha.sprite = null;
        if (spriteCarregado != null) Destroy(spriteCarregado);
        if (texturaCarregada != null) Destroy(texturaCarregada);
        spriteCarregado = null;
        texturaCarregada = null;
    }

    private static string SafeOrDefault(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
