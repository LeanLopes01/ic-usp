using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Item da lista de Propriedades do Motor de Dados, em CustomSystemManager — mostra o label + o
/// valor (base fixo ou fórmula) de uma DefinicaoPropriedade, com um botão pra remover.
///
/// Prefab precisa ter:
///   - TMP_Text: textoLabel, textoValor
///   - Button: botaoRemover
/// </summary>
public class PropriedadeListItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text textoLabel;
    [SerializeField] private TMP_Text textoValor;
    [SerializeField] private Button   botaoRemover;

    public void Configurar(DefinicaoPropriedade propriedade, Action aoRemover)
    {
        if (propriedade == null) return;

        if (textoLabel != null) textoLabel.text = propriedade.label;
        if (textoValor != null)
            textoValor.text = !string.IsNullOrEmpty(propriedade.formula)
                ? $"Fórmula: {propriedade.formula}"
                : $"Base: {propriedade.valorBase:0.##}";

        if (botaoRemover != null)
        {
            botaoRemover.onClick.RemoveAllListeners();
            botaoRemover.onClick.AddListener(() => aoRemover?.Invoke());
        }
    }
}
