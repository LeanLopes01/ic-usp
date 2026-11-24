using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Linha de um item já anexado à lore de um NPC (CampanhaNpcLoreUI) — só nome + botão de remover,
/// sem os toggles de equipar/sintonizar de Fichas/D&amp;D/CriarFicha/EquipmentRowUI.cs (não fazem
/// sentido pra um item de lore de NPC).
/// </summary>
public class CampanhaNpcLoreItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private Button   botaoRemover;

    private string _itemId;
    private Action<string> _onRemover;

    public void Setup(string itemId, string nome, Action<string> onRemover)
    {
        _itemId = itemId;
        _onRemover = onRemover;
        if (nomeText != null) nomeText.text = nome;

        if (botaoRemover != null)
        {
            botaoRemover.onClick.RemoveAllListeners();
            botaoRemover.onClick.AddListener(() => _onRemover?.Invoke(_itemId));
        }
    }
}
