using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Item de galeria do painel Tabletop Audio — imagem + nome + slider de volume (0..1, 0 =
/// desligado). Instanciado em massa por AudioTabletopPainelUI.PopularCategoria.</summary>
public class SomTabletopItemUI : MonoBehaviour
{
    [SerializeField] private Image imagemIcone;
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private Slider sliderVolume;
    [SerializeField] private TMP_Text textoValor;

    private int indiceGlobal = -1;
    private Action<int, float> onVolumeAlterado;
    private bool atualizandoSlider;

    public void Configurar(SomTabletopEntry entrada, int indiceGlobal, float volumeAtual, bool interativo,
        Action<int, float> onVolumeAlterado)
    {
        this.indiceGlobal = indiceGlobal;
        this.onVolumeAlterado = onVolumeAlterado;

        if (imagemIcone != null) { imagemIcone.sprite = entrada?.imagem; imagemIcone.enabled = entrada?.imagem != null; }
        if (nomeText != null) nomeText.text = entrada?.nome ?? "";

        if (sliderVolume != null)
        {
            sliderVolume.minValue = 0f;
            sliderVolume.maxValue = 1f;
            sliderVolume.onValueChanged.RemoveListener(OnSliderAlterado);
            sliderVolume.onValueChanged.AddListener(OnSliderAlterado);
        }
        AtualizarValor(volumeAtual, interativo);
    }

    /// <summary>Atualização em vivo (mudança recebida de outro peer, ou eco da própria mudança
    /// local) sem destruir/recriar o GameObject.</summary>
    public void AtualizarValor(float volume, bool interativo)
    {
        if (sliderVolume != null)
        {
            atualizandoSlider = true;
            sliderVolume.value = volume;
            sliderVolume.interactable = interativo;
            atualizandoSlider = false;
        }
        if (textoValor != null) textoValor.text = FormatarVolume(volume);
    }

    private void OnSliderAlterado(float v)
    {
        if (atualizandoSlider) return;
        onVolumeAlterado?.Invoke(indiceGlobal, v);
    }

    private void OnDestroy()
    {
        if (sliderVolume != null) sliderVolume.onValueChanged.RemoveListener(OnSliderAlterado);
    }

    private static string FormatarVolume(float v) => v <= 0f ? "Desligado" : $"{Mathf.RoundToInt(v * 100f)}%";
}
