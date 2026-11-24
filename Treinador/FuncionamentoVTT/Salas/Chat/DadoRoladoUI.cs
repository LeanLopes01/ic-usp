using TMPro;
using UnityEngine;

// Um dado individual já rolado, dentro do tooltip de detalhe de rolagem (ver
// DadosRolagemTooltipUI). Um script SÓ — o visual "de d20" vs "de d6" etc. é decidido por QUAL
// prefab é instanciado (ver DadosRolagemTooltipUI.prefabsPorFace), não por este script, já que a
// lógica de valor/cor é idêntica pra qualquer tamanho de dado.
public class DadoRoladoUI : MonoBehaviour
{
    [SerializeField] private TMP_Text valorText;

    public void Configurar(DadoRolado dado)
    {
        if (dado == null) return;
        if (valorText == null)
        {
            Debug.LogError($"[DadoRoladoUI] valorText não está arrastado no Inspector do prefab '{name}' — d{dado.faces}={dado.valor} não é exibido.");
            return;
        }

        valorText.text = dado.valor.ToString();

        // Mesma convenção de ChatCriticoUI — verde no valor máximo do dado, vermelho no mínimo (1).
        if (dado.valor == dado.faces)
            valorText.color = ChatCriticoUI.CorSucesso;
        else if (dado.valor == 1)
            valorText.color = ChatCriticoUI.CorFalha;
    }
}
