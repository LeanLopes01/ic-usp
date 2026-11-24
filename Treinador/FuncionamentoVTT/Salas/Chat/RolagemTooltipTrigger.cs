using UnityEngine;
using UnityEngine.EventSystems;

// Vai no MESMO GameObject do TMP_Text de um total (ataque/dano/teste) nos prefabs de mensagem de
// chat — hover mostra o DadosRolagemTooltipManager com o detalhe por dado dessa rolagem específica.
// Mesmo padrão de IPointerEnter/ExitHandler já usado em TokenStatBlockTooltipUI (modo Hover).
public class RolagemTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private string _nome;
    private ResultadoRolagem _resultado;

    public void Configurar(string nome, ResultadoRolagem resultado)
    {
        _nome = nome;
        _resultado = resultado;
    }

    public void OnPointerEnter(PointerEventData eventData) =>
        DadosRolagemTooltipManager.Instance?.Mostrar(_nome, _resultado);

    public void OnPointerExit(PointerEventData eventData) =>
        DadosRolagemTooltipManager.Instance?.Esconder();

    private void OnDisable() => DadosRolagemTooltipManager.Instance?.Esconder();
}
