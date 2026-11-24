using System;
using System.Globalization;
using TMPro;
using UnityEngine;

/// <summary>
/// Item de uma propriedade do EstadoPersonagem de RASCUNHO, no painel de teste do Motor de Dados
/// (CustomSystemManager.AbrirPainelTeste) — campo editável ligado ao valor atual, exceto
/// propriedades derivadas por fórmula (nunca aceitam edição direta, mesma regra de
/// EstadoPersonagem.DefinirValorBase — o campo fica desabilitado nesse caso, pra não mentir sobre o
/// que é editável).
///
/// Prefab precisa ter:
///   - TMP_Text: textoLabel
///   - TMP_InputField: inputValor
/// </summary>
public class PropriedadeTesteItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text       textoLabel;
    [SerializeField] private TMP_InputField inputValor;

    private string _idPropriedade;
    private EstadoPersonagem _estado;
    private Action _aoEditado;

    public void Configurar(string label, PropriedadePersonagem propriedade, EstadoPersonagem estado, Action aoEditado)
    {
        if (propriedade == null || estado == null) return;

        _idPropriedade = propriedade.id;
        _estado = estado;
        _aoEditado = aoEditado;

        bool ehFormula = !string.IsNullOrEmpty(propriedade.formula);

        if (textoLabel != null)
            textoLabel.text = ehFormula ? $"{label} (fórmula)" : label;

        if (inputValor != null)
        {
            inputValor.SetTextWithoutNotify(estado.ObterValor(propriedade.id).ToString("0.##", CultureInfo.InvariantCulture));
            inputValor.interactable = !ehFormula;

            inputValor.onEndEdit.RemoveListener(AoValorEditado);
            inputValor.onEndEdit.AddListener(AoValorEditado);
        }
    }

    private void AoValorEditado(string texto)
    {
        if (_estado == null || string.IsNullOrWhiteSpace(_idPropriedade)) return;
        if (!float.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out float valor)) return;

        _estado.DefinirValorBase(_idPropriedade, valor);
        _aoEditado?.Invoke();
    }
}
