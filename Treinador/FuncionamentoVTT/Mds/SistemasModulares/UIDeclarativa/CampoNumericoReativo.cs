using TMPro;
using UnityEngine;

/// <summary>Componente do prefab de &lt;InputNumber bind="..." min="..." max="..."&gt; — sincroniza
/// bidirecionalmente com EstadoPersonagem: digitar no campo grava o valor base (clampado em
/// [min,max]) via EstadoPersonagem.DefinirValorBase, e qualquer Modificador que altere a mesma
/// propriedade atualiza o campo de volta (ver dicevtt-ui-declarativa.md §2/§4).</summary>
public class CampoNumericoReativo : MonoBehaviour
{
    [SerializeField] private TMP_InputField campo;

    private EstadoPersonagem _estado;
    private string _idPropriedadeVinculada;
    private float _min;
    private float _max;
    private bool _atualizandoPorCodigo;

    public void Configurar(string idPropriedade, EstadoPersonagem estado, float min, float max)
    {
        Desvincular();
        if (estado == null || string.IsNullOrEmpty(idPropriedade) || campo == null) return;

        _estado = estado;
        _idPropriedadeVinculada = idPropriedade;
        _min = min;
        _max = max;

        _estado.OnValorPropriedadeAlterado += AoValorMudar;
        campo.onEndEdit.AddListener(AoUsuarioEditar);

        AtualizarCampoSemDispararEvento(estado.ObterValor(idPropriedade));
    }

    private void AoValorMudar(string idPropriedade, float novoValor)
    {
        if (idPropriedade == _idPropriedadeVinculada) AtualizarCampoSemDispararEvento(novoValor);
    }

    private void AoUsuarioEditar(string textoDigitado)
    {
        if (_atualizandoPorCodigo || _estado == null) return;
        if (!float.TryParse(textoDigitado, out float valor)) return;

        valor = Mathf.Clamp(valor, _min, _max);
        _estado.DefinirValorBase(_idPropriedadeVinculada, valor);
    }

    // _atualizandoPorCodigo evita que atualizar o texto por código (reação a um Modificador)
    // dispare AoUsuarioEditar de volta — TMP_InputField.text não distingue "usuário digitou" de
    // "código atribuiu", então a guarda precisa ser explícita.
    private void AtualizarCampoSemDispararEvento(float valor)
    {
        if (campo == null) return;
        _atualizandoPorCodigo = true;
        campo.text = valor.ToString();
        _atualizandoPorCodigo = false;
    }

    private void Desvincular()
    {
        if (_estado != null) _estado.OnValorPropriedadeAlterado -= AoValorMudar;
        if (campo != null) campo.onEndEdit.RemoveListener(AoUsuarioEditar);
        _estado = null;
    }

    private void OnDestroy() => Desvincular();
}
