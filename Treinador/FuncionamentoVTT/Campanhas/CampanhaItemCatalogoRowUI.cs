using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Linha de item na lista de busca do catálogo SRD dentro do painel de Lore de NPC
/// (CampanhaNpcLoreUI) — clique único mostra detalhes, duplo-clique anexa o item ao NPC. Mesma
/// convenção de interação de Fichas/D&amp;D/CriarFicha/EquipmentRowUI.cs, mas um componente próprio e
/// mais simples (sem toggles de equipar/sintonizar, que não fazem sentido aqui) pra manter este
/// recurso de campanha isolado de Fichas/D&amp;D/CriarFicha/.
/// </summary>
public class CampanhaItemCatalogoRowUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text categoriaText;

    public int ItemId { get; private set; }

    private Action<CampanhaItemCatalogoRowUI> _onClique;
    private Action<CampanhaItemCatalogoRowUI> _onDuploClique;

    public void Setup(int itemId, string nome, string categoria,
        Action<CampanhaItemCatalogoRowUI> onClique, Action<CampanhaItemCatalogoRowUI> onDuploClique)
    {
        ItemId = itemId;
        if (nomeText != null) nomeText.text = nome;
        if (categoriaText != null) categoriaText.text = categoria;
        _onClique = onClique;
        _onDuploClique = onDuploClique;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (eventData.clickCount == 2)
            _onDuploClique?.Invoke(this);
        else if (eventData.clickCount == 1)
            _onClique?.Invoke(this);
    }
}
