using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// Componente da linha de UI de um sistema de RPG.
/// </summary>
public class SystemUIItem : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text descriptionText;

    [Header("Selecao")]
    [SerializeField] private Button selectButton;
    [SerializeField] private Image selectionBackground;
    [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.12f);
    [SerializeField] private Color selectedColor = new Color(0.2f, 0.6f, 1f, 0.35f);

    private RPGSystemData currentData;

    /// <summary>
    /// Preenche os dados visuais e registra callback de seleção do sistema.
    /// </summary>
    public void Setup(RPGSystemData data, Action<RPGSystemData> onSelected, bool isSelected)
    {
        currentData = data;

        if (nameText != null)
        {
            nameText.text = string.IsNullOrWhiteSpace(data.systemName) ? "Sistema sem nome" : data.systemName;
        }

        if (descriptionText != null)
        {
            descriptionText.text = string.IsNullOrWhiteSpace(data.description)
                ? "Sem descrição."
                : data.description;
        }

        if (iconImage != null)
        {
            iconImage.sprite = data.icon;
            iconImage.enabled = data.icon != null;
        }

        if (selectButton != null)
        {
            selectButton.onClick.RemoveAllListeners();
            selectButton.onClick.AddListener(() => onSelected?.Invoke(currentData));
        }

        if (selectionBackground != null)
        {
            selectionBackground.color = isSelected ? selectedColor : normalColor;
        }
    }

    public RPGSystemData GetData()
    {
        return currentData;
    }
}
