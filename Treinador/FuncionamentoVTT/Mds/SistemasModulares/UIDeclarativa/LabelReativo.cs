using TMPro;
using UnityEngine;

/// <summary>Componente do prefab de &lt;Label&gt; — texto estático (text="...") OU reativo
/// (bind="idPropriedade"), inscrevendo em EstadoPersonagem.OnValorPropriedadeAlterado pra
/// atualizar sozinho sem polling (ver dicevtt-ui-declarativa.md §4).</summary>
public class LabelReativo : MonoBehaviour
{
    [SerializeField] private TMP_Text texto;

    private EstadoPersonagem _estado;
    private string _idPropriedadeVinculada;

    public void DefinirTexto(string valor)
    {
        if (texto != null) texto.text = valor;
    }

    public void VincularPropriedade(string idPropriedade, EstadoPersonagem estado)
    {
        Desvincular();
        if (estado == null || string.IsNullOrEmpty(idPropriedade)) return;

        _estado = estado;
        _idPropriedadeVinculada = idPropriedade;
        _estado.OnValorPropriedadeAlterado += AoValorMudar;

        AoValorMudar(idPropriedade, estado.ObterValor(idPropriedade));
    }

    private void AoValorMudar(string idPropriedade, float novoValor)
    {
        if (idPropriedade == _idPropriedadeVinculada) DefinirTexto(novoValor.ToString());
    }

    private void Desvincular()
    {
        if (_estado != null) _estado.OnValorPropriedadeAlterado -= AoValorMudar;
        _estado = null;
    }

    private void OnDestroy() => Desvincular();
}
