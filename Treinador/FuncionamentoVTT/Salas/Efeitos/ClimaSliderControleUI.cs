using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Um slider + label + texto de valor, reutilizado 5x no ClimaPainelUI (tamanho/ângulo/quantidade/
/// velocidade de queda/vento) — evita duplicar o guard de reentrância e o formato "bounds + valor +
/// texto" que GridAjustePanelUI faz inline pra um único slider.
/// </summary>
public class ClimaSliderControleUI : MonoBehaviour
{
    [SerializeField] private TMP_Text textoLabel;
    [SerializeField] private Slider slider;
    [SerializeField] private TMP_Text textoValor;

    private bool atualizandoSlider;
    private Action<float> onAlterado;
    private Func<float, string> formatarValor;

    public void Configurar(string label, float min, float max, Action<float> onAlterado, Func<float, string> formatarValor)
    {
        if (textoLabel != null) textoLabel.text = label;
        if (slider != null) { slider.minValue = min; slider.maxValue = max; }

        this.onAlterado = onAlterado;
        this.formatarValor = formatarValor ?? (v => v.ToString("0.00"));

        if (slider != null)
        {
            slider.onValueChanged.RemoveListener(OnSliderAlterado);
            slider.onValueChanged.AddListener(OnSliderAlterado);
        }
    }

    public void AtualizarValor(float valor, bool interativo)
    {
        if (slider != null)
        {
            atualizandoSlider = true;
            slider.value = valor;
            slider.interactable = interativo;
            atualizandoSlider = false;
        }
        if (textoValor != null) textoValor.text = formatarValor(valor);
    }

    private void OnSliderAlterado(float v)
    {
        if (atualizandoSlider) return;
        onAlterado?.Invoke(v);
    }

    private void OnDestroy()
    {
        if (slider != null) slider.onValueChanged.RemoveListener(OnSliderAlterado);
    }
}
