using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Item de lista da sub-aba "Ficha" do painel Conteudo (BarraFerramentasManager) — uma ficha local do
/// jogador atual, já filtrada pelo sistema da sala (ver LocalFichaStorage.
/// LoadFichasForSistemaESteamId). Clique único importa a ficha pra sala (aba + token no mapa) —
/// substitui o antigo SalaFichaItemUI/SalaFichaLoaderUI, removidos.
/// </summary>
public class ConteudoFichaItemUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private Image tokenImagem;
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text cabecalhoText; // "Nível X • Raça • Classe"

    private LocalFichaData _snapshot;
    private Action<LocalFichaData> _onImportar;
    private Texture2D _textura;

    public void Setup(LocalFichaData snapshot, Action<LocalFichaData> onImportar)
    {
        _snapshot = snapshot;
        _onImportar = onImportar;

        string classe = !string.IsNullOrWhiteSpace(snapshot?.level_pc_detalhe)
            ? snapshot.level_pc_detalhe
            : (snapshot?.classes != null && snapshot.classes.Count > 0 ? snapshot.classes[0].class_name : "-");
        string raca = snapshot?.raca?.raca_nome ?? "-";

        if (nomeText != null) nomeText.text = string.IsNullOrWhiteSpace(snapshot?.name_pc) ? "Ficha" : snapshot.name_pc;
        if (cabecalhoText != null) cabecalhoText.text = $"Nível {snapshot?.level_pc ?? 0} • {raca} • {classe}";

        CarregarToken(snapshot?.token_image_path);
    }

    public void OnPointerClick(PointerEventData eventData) => _onImportar?.Invoke(_snapshot);

    private void OnDestroy()
    {
        if (_textura != null) Destroy(_textura);
    }

    private void CarregarToken(string caminho)
    {
        if (tokenImagem == null || string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return;

        byte[] bytes = File.ReadAllBytes(caminho);
        if (_textura != null) Destroy(_textura);
        _textura = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!_textura.LoadImage(bytes)) { Destroy(_textura); _textura = null; return; }

        tokenImagem.sprite = Sprite.Create(_textura,
            new Rect(0, 0, _textura.width, _textura.height), new Vector2(0.5f, 0.5f), 100f);
        tokenImagem.enabled = true;
    }
}
