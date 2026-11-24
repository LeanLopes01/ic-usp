using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Item da galeria de mapas (cenas) de uma campanha — mesmo padrão visual de Salas/Grid/MapButtonUI.cs,
/// mas com dois botões separados: "Carregar" troca a cena ativa no grid (CampanhaMapManager.CarregarMapa),
/// "Editar" abre o formulário de importação pré-preenchido pra alterar nome/imagem/vetorização dessa
/// cena (CampanhaMapImportUI.EditarMapa) sem trocar a cena ativa até o usuário confirmar.
/// </summary>
public class CampanhaMapItemUI : MonoBehaviour
{
    [SerializeField] private Image miniaturaImage;
    [SerializeField] private TMP_Text nomeText;
    [Tooltip("Sistema da CAMPANHA (não do mapa — todo mapa de uma campanha compartilha o mesmo sistema), repassado por quem chama Configurar (ver CampanhaMapGaleriaUI.Carregar).")]
    [SerializeField] private TMP_Text sistemaText;
    [Tooltip("Descrição da CAMPANHA (não do mapa — CampanhaMapaData não tem descrição própria), mesma lógica de sistemaText.")]
    [SerializeField] private TMP_Text descricaoText;
    [Tooltip("Data de criação desta CENA/MAPA específica (CampanhaMapaData.criadoEmUtc), não da campanha.")]
    [SerializeField] private TMP_Text dataCriacaoText;
    [SerializeField] private Button botaoCarregar;
    [SerializeField] private Button botaoEditar;
    [SerializeField] private Button botaoEnviar;

    private Action _onCarregar;
    private Action _onEditar;
    private Action _onEnviar;
    private Texture2D _textura;
    private Sprite _sprite;

    /// <param name="sistema">Sistema da campanha dona deste mapa (CampanhaLocalSnapshot.sistema) —
    /// mesmo valor pra todo mapa da mesma campanha.</param>
    /// <param name="descricao">Descrição da campanha dona deste mapa (CampanhaLocalSnapshot.descricao)
    /// — mesmo valor pra todo mapa da mesma campanha, já que a cena em si não tem descrição própria.</param>
    /// <param name="onEnviar">Opcional. Chamado quando o botão "Enviar" de um item é clicado — abre
    /// o painel de seleção de sala (ver CampanhaMapGaleriaUI.AbrirPainelEnvio).</param>
    public void Configurar(CampanhaMapaData mapa, string sistema, string descricao,
        Action onCarregar, Action onEditar, Action onEnviar = null)
    {
        _onCarregar = onCarregar;
        _onEditar = onEditar;
        _onEnviar = onEnviar;

        if (nomeText != null) nomeText.text = mapa != null ? mapa.nome : "";
        CarregarMiniatura(mapa != null ? mapa.imagemPath : null);

        if (sistemaText != null)
            sistemaText.text = SafeOrDefault(sistema, "Sistema não definido");

        if (descricaoText != null)
            descricaoText.text = SafeOrDefault(descricao, "Sem descrição.");

        if (dataCriacaoText != null)
        {
            dataCriacaoText.text = mapa != null && DateTime.TryParse(mapa.criadoEmUtc, out DateTime dataCriacao)
                ? $"Criado {dataCriacao:dd/MM/yyyy}"
                : "-";
        }

        Religar(botaoCarregar, () => _onCarregar?.Invoke());
        Religar(botaoEditar, () => _onEditar?.Invoke());
        Religar(botaoEnviar, () => _onEnviar?.Invoke());
    }

    private static string SafeOrDefault(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

    private static void Religar(Button btn, Action acao)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => acao());
    }

    private void OnDestroy() => LimparRuntime();

    private void CarregarMiniatura(string caminho)
    {
        if (miniaturaImage == null) return;
        LimparRuntime();

        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return;

        byte[] bytes = File.ReadAllBytes(caminho);
        var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!tex.LoadImage(bytes)) { Destroy(tex); return; }

        _textura = tex;
        _sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        miniaturaImage.sprite = _sprite;
        miniaturaImage.preserveAspect = true;
    }

    private void LimparRuntime()
    {
        if (miniaturaImage != null) miniaturaImage.sprite = null;
        if (_sprite != null) Destroy(_sprite);
        if (_textura != null) Destroy(_textura);
        _sprite = null;
        _textura = null;
    }
}
