using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SalaListaItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image imagemSala;
    [SerializeField] private TMP_Text tituloSalaText;
    [SerializeField] private TMP_Text sistemaSalaText;
    [SerializeField] private TMP_Text descricaoSalaText;
    [SerializeField] private TMP_Text pessoasSalaText;
    [SerializeField] private TMP_Text papelUsuarioText;
    [SerializeField] private TMP_Text dataCriacaoText;
    [SerializeField] private Button botaoEntrar;

    [Header("Visual")]
    [SerializeField] private Sprite imagemFallback;

    private static readonly Color32 CorMestre = new Color32(0xFF, 0x00, 0x00, 0xFF);
    private static readonly Color32 CorJogador = new Color32(0x48, 0xFF, 0x00, 0xFF);

    private Action onEntrar;
    private Texture2D texturaCarregada;
    private Sprite spriteCarregado;

    private void Awake()
    {
        if (botaoEntrar == null)
            botaoEntrar = GetComponentInChildren<Button>(true);

        if (botaoEntrar == null)
            Debug.LogWarning($"[SalaListaItemUI] Botao Entrar nao foi encontrado em '{name}'.");
    }

    public void Configurar(SalaLocalSnapshot sala, Action onEntrarClick)
    {
        onEntrar = onEntrarClick;

        Debug.Log($"[SalaListaItemUI] Configurar sala='{sala?.nomeSala}' steamId='{sala?.salaSteamId}' imagemPath='{sala?.imagemSalaPath}'");

        if (tituloSalaText != null)
            tituloSalaText.text = SafeOrDefault(sala?.nomeSala, "Sala sem nome");

        if (sistemaSalaText != null)
            sistemaSalaText.text = SafeOrDefault(sala?.sistemaSala, "Sistema não definido");

        if (descricaoSalaText != null)
            descricaoSalaText.text = SafeOrDefault(sala?.descricaoSala, "Sem descrição.");

        if (pessoasSalaText != null)
        {
            int membros = sala != null ? Mathf.Max(0, sala.totalMembros) : 0;
            int maxMembros = sala != null ? Mathf.Max(0, sala.maxMembros) : 0;
            pessoasSalaText.text = $"{membros}/{maxMembros}";
        }

        if (papelUsuarioText != null)
        {
            bool ehMestre = string.Equals(sala?.meuPapel, "Mestre", StringComparison.OrdinalIgnoreCase);
            papelUsuarioText.text = ehMestre ? "Mestre" : "Jogador";
            papelUsuarioText.color = ehMestre ? CorMestre : CorJogador;
        }

        if (dataCriacaoText != null)
        {
            dataCriacaoText.text = sala != null && DateTime.TryParse(sala.criadoEmUtc, out DateTime dataCriacao)
                ? $"Adicionado {dataCriacao:dd/MM/yyyy}"
                : "-";
        }

        if (botaoEntrar != null)
        {
            botaoEntrar.onClick.RemoveListener(OnClickEntrar);
            botaoEntrar.onClick.AddListener(OnClickEntrar);
            bool ehMestre = string.Equals(sala?.meuPapel, "Mestre", StringComparison.OrdinalIgnoreCase);
            botaoEntrar.interactable = ehMestre && !string.IsNullOrWhiteSpace(sala?.salaSteamId);
            Debug.Log($"[SalaListaItemUI] Botao Entrar vinculado em '{name}' interactable={botaoEntrar.interactable} papel={sala?.meuPapel}");
        }

        CarregarImagemSala(sala != null ? sala.imagemSalaPath : string.Empty);
    }

    private void OnDestroy()
    {
        if (botaoEntrar != null)
            botaoEntrar.onClick.RemoveListener(OnClickEntrar);

        LimparImagemRuntime();
    }

    private void OnClickEntrar()
    {
        Debug.Log($"[SalaListaItemUI] Clique em Entrar no item '{name}'.");
        onEntrar?.Invoke();
    }

    private void CarregarImagemSala(string imagePath)
    {
        if (imagemSala == null)
            return;

        LimparImagemRuntime();

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            Debug.LogWarning($"[SalaListaItemUI] Caminho de imagem invalido/inexistente: '{imagePath}'");
            DefinirFallbackImagem();
            return;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(imagePath);
            if (bytes == null || bytes.Length == 0)
            {
                Debug.LogWarning($"[SalaListaItemUI] Arquivo de imagem vazio: '{imagePath}'");
                DefinirFallbackImagem();
                return;
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                Debug.LogWarning($"[SalaListaItemUI] LoadImage falhou para: '{imagePath}'");
                DefinirFallbackImagem();
                return;
            }

            texturaCarregada = tex;
            spriteCarregado = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            imagemSala.sprite = spriteCarregado;
            imagemSala.preserveAspect = true;
            imagemSala.color = Color.white;
            Debug.Log($"[SalaListaItemUI] Imagem carregada com sucesso path='{imagePath}' bytes={bytes.Length} size={tex.width}x{tex.height}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SalaListaItemUI] Falha ao carregar imagem da sala '{imagePath}': {e.Message}");
            DefinirFallbackImagem();
        }
    }

    private void DefinirFallbackImagem()
    {
        if (imagemSala == null)
            return;

        imagemSala.sprite = imagemFallback;
        imagemSala.preserveAspect = true;
        imagemSala.color = Color.white;
    }

    private void LimparImagemRuntime()
    {
        if (imagemSala != null)
            imagemSala.sprite = null;

        if (spriteCarregado != null)
            UnityEngine.Object.Destroy(spriteCarregado);

        if (texturaCarregada != null)
            UnityEngine.Object.Destroy(texturaCarregada);

        spriteCarregado = null;
        texturaCarregada = null;
    }

    private static string SafeOrDefault(string value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
