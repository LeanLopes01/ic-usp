using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Item de lista da sub-aba "Cena de Campanha" do painel Conteudo (BarraFerramentasManager) — uma
/// cena do módulo Campanhas, agregada de qualquer campanha salva na máquina. Clique único já importa
/// a cena pra sala atual (BarraFerramentasManager.ImportarCenaDeCampanha), sem confirmação — mesmo
/// padrão de carregamento de miniatura de Campanhas/Grid/CampanhaMapItemUI.cs.
/// </summary>
public class ConteudoCenaCampanhaItemUI : MonoBehaviour
{
    [SerializeField] private Image miniaturaImage;
    [SerializeField] private TMP_Text nomeText;
    [Tooltip("Opcional — de qual campanha é essa cena, já que a lista agrega várias campanhas juntas.")]
    [SerializeField] private TMP_Text campanhaNomeText;
    [Tooltip("Clique no botão inteiro importa a cena — sem card de confirmação separado.")]
    [SerializeField] private Button botaoImportar;

    private CampanhaMapaData _cena;
    private string _campanhaId;
    private Action<CampanhaMapaData, string> _onImportar;
    private Texture2D _textura;
    private Sprite _sprite;

    public void Setup(CampanhaMapaData cena, string campanhaId, string nomeCampanha, Action<CampanhaMapaData, string> onImportar)
    {
        _cena = cena;
        _campanhaId = campanhaId;
        _onImportar = onImportar;

        if (nomeText != null) nomeText.text = cena != null ? cena.nome : "";
        if (campanhaNomeText != null) campanhaNomeText.text = nomeCampanha ?? "";
        CarregarMiniatura(cena != null ? cena.imagemPath : null);

        if (botaoImportar != null)
        {
            botaoImportar.onClick.RemoveAllListeners();
            botaoImportar.onClick.AddListener(() => _onImportar?.Invoke(_cena, _campanhaId));
        }
    }

    private void OnDestroy() => LimparRuntime();

    private void CarregarMiniatura(string caminho)
    {
        LimparRuntime();
        if (miniaturaImage == null || string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return;

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
