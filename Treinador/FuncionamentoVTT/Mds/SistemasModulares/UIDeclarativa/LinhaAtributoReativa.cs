using TMPro;
using UnityEngine;

/// <summary>Componente do prefab de &lt;AttributeRow label="Força" bind_val="for" bind_mod="mod_for"/&gt;
/// — linha combinada valor+modificador de um atributo de RPG (ex: "Força: 16 (+3)"), reativa nos
/// dois binds ao mesmo tempo.</summary>
public class LinhaAtributoReativa : MonoBehaviour
{
    [SerializeField] private TMP_Text labelTexto;
    [SerializeField] private TMP_Text valorTexto;
    [SerializeField] private TMP_Text modificadorTexto;

    private EstadoPersonagem _estado;
    private string _idValor;
    private string _idModificador;

    public void Configurar(string label, string idValor, string idModificador, EstadoPersonagem estado)
    {
        Desvincular();

        if (labelTexto != null) labelTexto.text = label ?? string.Empty;

        _estado = estado;
        _idValor = idValor;
        _idModificador = idModificador;
        if (_estado == null) return;

        _estado.OnValorPropriedadeAlterado += AoValorMudar;

        if (!string.IsNullOrEmpty(_idValor)) AtualizarValor(_estado.ObterValor(_idValor));
        if (!string.IsNullOrEmpty(_idModificador)) AtualizarModificador(_estado.ObterValor(_idModificador));
    }

    private void AoValorMudar(string idPropriedade, float novoValor)
    {
        if (idPropriedade == _idValor) AtualizarValor(novoValor);
        else if (idPropriedade == _idModificador) AtualizarModificador(novoValor);
    }

    private void AtualizarValor(float valor)
    {
        if (valorTexto != null) valorTexto.text = valor.ToString();
    }

    // Modificador de atributo em RPG sempre mostra o sinal, mesmo positivo ("+3", não "3") —
    // convenção universal do gênero, diferente do valor bruto ao lado.
    private void AtualizarModificador(float modificador)
    {
        if (modificadorTexto == null) return;
        modificadorTexto.text = modificador >= 0 ? $"+{modificador}" : modificador.ToString();
    }

    private void Desvincular()
    {
        if (_estado != null) _estado.OnValorPropriedadeAlterado -= AoValorMudar;
        _estado = null;
    }

    private void OnDestroy() => Desvincular();
}
