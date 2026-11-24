// Item de linha do painel "Content de Visão" — nome do token + slider de raio de visão (metros).
// Instanciado uma vez por token de jogador por VisaoPainelUI.PopularSlots. Mesmo formato de
// SomTabletopItemUI (Assets/Scripts/Salas/Audio/SomTabletopItemUI.cs), só trocando volume por raio
// de visão — reaproveita ITokenNoMapa.RaioVisaoMetros/DefinirRaioVisao, que já existem.
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class VisaoTokenSliderSlot : MonoBehaviour
{
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Text textoValor;

    // Mesma convenção de MapManager/TokenButtonUI: 1 quadrado de grid = 1.5m — range do slider vai de
    // 1 até 40 quadrados, igual ao slider individual já existente em TokenButtonUI.
    private const float MetrosPorQuadrado = 1.5f;

    private MapTokenDraggable _token;
    private MapManager        _mapManager;
    private bool _atualizandoSlider;

    public void Configurar(MapTokenDraggable token, MapManager mapManager, bool interativo)
    {
        _token = token;
        _mapManager = mapManager;

        if (nomeText != null) nomeText.text = token != null ? token.NomeToken : string.Empty;

        if (slider != null)
        {
            slider.minValue = MetrosPorQuadrado;
            slider.maxValue = MetrosPorQuadrado * 40f;
            slider.wholeNumbers = false;
            slider.onValueChanged.RemoveListener(OnSliderAlterado);
            slider.onValueChanged.AddListener(OnSliderAlterado);
        }

        AtualizarValor(token != null ? token.RaioVisaoMetros : 0f, interativo);
    }

    /// <summary>Atualização em vivo (ex: slider global do painel mudou todos de uma vez) sem
    /// destruir/recriar o GameObject — mesmo papel de SomTabletopItemUI.AtualizarValor.</summary>
    public void AtualizarValor(float metros, bool interativo)
    {
        if (slider != null)
        {
            _atualizandoSlider = true;
            slider.value = metros;
            slider.interactable = interativo;
            _atualizandoSlider = false;
        }
        if (textoValor != null) textoValor.text = $"{metros:0.0}m";
    }

    private void OnSliderAlterado(float metros)
    {
        if (_atualizandoSlider || _token == null || _mapManager == null) return;

        _token.DefinirRaioVisao(metros);
        if (textoValor != null) textoValor.text = $"{metros:0.0}m";
        // NotificarRaioVisaoAlterado (MapManager) já recalcula visão local, persiste E envia rede —
        // sem isso, mexer aqui no raio de um token de OUTRO jogador não teria efeito nenhum na visão
        // real dele.
        _mapManager.NotificarRaioVisaoAlterado(_token);
    }

    private void OnDestroy()
    {
        if (slider != null) slider.onValueChanged.RemoveListener(OnSliderAlterado);
    }
}
