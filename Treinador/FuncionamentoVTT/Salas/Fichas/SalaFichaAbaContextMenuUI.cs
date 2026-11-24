using System;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Clique-direito no botão de uma aba de ficha — abre o menu de atribuição de dono (Mestre-only,
/// verificado por quem chama Configurar). Coexiste com o Button.onClick já wireado em
/// SalaAbasFichasManager.CriarOuSelecionarAbaDaFicha; o clique esquerdo continua selecionando a
/// aba normalmente.
/// </summary>
public class SalaFichaAbaContextMenuUI : MonoBehaviour, IPointerClickHandler
{
    private Action<Vector2> onSolicitarMenu;

    public void Configurar(Action<Vector2> onSolicitarMenu)
    {
        this.onSolicitarMenu = onSolicitarMenu;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;
        onSolicitarMenu?.Invoke(eventData.position);
    }
}
