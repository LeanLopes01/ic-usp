using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Linha de uma sala na lista do painel "Enviar para Sala" (ver CampanhaMapGaleriaUI) — imagem,
/// nome, sistema, descrição e data de criação da sala + botão "Enviar aqui". Lista só mostra salas
/// onde o usuário é mestre (filtrado antes de chegar aqui, ver CampanhaMapGaleriaUI.AbrirPainelEnvio).
/// </summary>
public class CampanhaEnvioSalaItemUI : MonoBehaviour
{
    [SerializeField] private Image imagemSala;
    [SerializeField] private Sprite imagemFallback;
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text sistemaText;
    [SerializeField] private TMP_Text descricaoText;
    [Tooltip("Data em que a sala foi criada/adicionada localmente (SalaLocalSnapshot.criadoEmUtc).")]
    [SerializeField] private TMP_Text dataAdicionadaText;
    [SerializeField] private Button botaoEnviarAqui;

    private Action _onEnviarAqui;
    private Texture2D _texturaCarregada;
    private Sprite _spriteCarregado;

    public void Configurar(SalaLocalSnapshot sala, Action onEnviarAqui)
    {
        _onEnviarAqui = onEnviarAqui;

        if (nomeText != null)
            nomeText.text = SafeOrDefault(sala?.nomeSala, "Sala sem nome");

        if (sistemaText != null)
            sistemaText.text = SafeOrDefault(sala?.sistemaSala, "Sistema não definido");

        if (descricaoText != null)
            descricaoText.text = SafeOrDefault(sala?.descricaoSala, "Sem descrição.");

        if (dataAdicionadaText != null)
        {
            dataAdicionadaText.text = sala != null && DateTime.TryParse(sala.criadoEmUtc, out DateTime dataAdicionada)
                ? $"Adicionada {dataAdicionada:dd/MM/yyyy}"
                : "-";
        }

        CarregarImagemSala(sala != null ? sala.imagemSalaPath : null);

        if (botaoEnviarAqui != null)
        {
            botaoEnviarAqui.onClick.RemoveAllListeners();
            botaoEnviarAqui.onClick.AddListener(() => _onEnviarAqui?.Invoke());
        }
    }

    private void OnDestroy() => LimparImagemRuntime();

    private void CarregarImagemSala(string imagePath)
    {
        if (imagemSala == null) return;
        LimparImagemRuntime();

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            DefinirFallback();
            return;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(imagePath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            if (!tex.LoadImage(bytes))
            {
                Destroy(tex);
                DefinirFallback();
                return;
            }

            _texturaCarregada = tex;
            _spriteCarregado = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            imagemSala.sprite = _spriteCarregado;
            imagemSala.preserveAspect = true;
            imagemSala.color = Color.white;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CampanhaEnvioSalaItemUI] Falha ao carregar imagem da sala '{imagePath}': {e.Message}");
            DefinirFallback();
        }
    }

    private void DefinirFallback()
    {
        if (imagemSala == null) return;
        imagemSala.sprite = imagemFallback;
        imagemSala.preserveAspect = true;
        imagemSala.color = Color.white;
    }

    private void LimparImagemRuntime()
    {
        if (imagemSala != null) imagemSala.sprite = null;
        if (_spriteCarregado != null) Destroy(_spriteCarregado);
        if (_texturaCarregada != null) Destroy(_texturaCarregada);
        _spriteCarregado = null;
        _texturaCarregada = null;
    }

    private static string SafeOrDefault(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
