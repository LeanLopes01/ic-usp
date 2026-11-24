using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using System;
using UnityEngine.UI;
using System.Collections.Generic;

public class EquipmentRowUI : Selectable, IPointerClickHandler
{
    [Header("Referências UI Básicas")]
    public TMP_Text labelText; 
    public TMP_Text amountText; 

    [Header("Referências UI Detalhadas (Itens)")]
    public TMP_Text costText;
    public TMP_Text weightText;
    public TMP_Text originText;
    public TMP_Text statsText;

    [Header("Controles do Inventário")]
    public Toggle equipToggle;
    public Toggle sintonizadoToggle;

    [Header("Visual de Seleção (Categorias)")]
    public GameObject selectionHighlight; 
    public Image background; 
    public Color normalColor = new Color(1f, 1f, 1f, 0f); 
    public Color selectedColor = new Color(0.2f, 0.6f, 1f, 0.5f); 
    public TMP_Text secondaryLabelText;
    public Button toggleChildrenButton;

    public int ItemId { get; private set; }
    public string CategoryName { get; set; }
    public string ParentCategoryName { get; private set; }
    public string ChildCategoryName { get; private set; }
    public string InstanceId { get; private set; } 
    public bool IsInventory { get; private set; }
    public bool IsCategory { get; private set; }
    public bool IsParentCategory { get; private set; }

    private Action<EquipmentRowUI> onSingleClick;
    private Action<EquipmentRowUI> onDoubleClick;
    private Action<string, string> onEquipSlotChanged;
    private Action<EquipmentRowUI> onToggleChildren;
    private static readonly List<CanvasGroup> canvasGroupsCache = new List<CanvasGroup>();
    private static readonly List<Graphic> graphicsCache = new List<Graphic>();

    public void SetupCategory(string catName, bool isSelected, Action<EquipmentRowUI> singleClick, bool isParentCategory = false, bool isExpanded = true, Action<EquipmentRowUI> toggleChildren = null, string secondaryLabel = "", string categoryKey = "", string parentCategoryName = "", string childCategoryName = "")
    {
        ForcarVisibilidadeRow();

        IsCategory = true;
        IsParentCategory = isParentCategory;
        CategoryName = catName;
        CategoryName = string.IsNullOrWhiteSpace(categoryKey) ? catName : categoryKey;
        ParentCategoryName = string.IsNullOrWhiteSpace(parentCategoryName) ? (isParentCategory ? CategoryName : string.Empty) : parentCategoryName;
        ChildCategoryName = string.IsNullOrWhiteSpace(childCategoryName) ? (isParentCategory ? string.Empty : catName) : childCategoryName;
        onSingleClick = singleClick;
        onDoubleClick = null;
        onToggleChildren = toggleChildren;

        if (labelText) labelText.text = catName.ToUpper();
        if (secondaryLabelText)
        {
            bool mostrarSecundario = !string.IsNullOrWhiteSpace(secondaryLabel);
            secondaryLabelText.gameObject.SetActive(mostrarSecundario);
            if (mostrarSecundario) secondaryLabelText.text = secondaryLabel;
        }
        if (amountText) amountText.gameObject.SetActive(false);
        if (costText) costText.gameObject.SetActive(false);
        if (weightText) weightText.gameObject.SetActive(false);
        if (originText) originText.gameObject.SetActive(false);
        if (statsText) statsText.gameObject.SetActive(false);
        if (equipToggle) equipToggle.gameObject.SetActive(false);
        if (sintonizadoToggle) sintonizadoToggle.gameObject.SetActive(false);
        if (toggleChildrenButton)
        {
            toggleChildrenButton.gameObject.SetActive(isParentCategory && onToggleChildren != null);
            toggleChildrenButton.onClick.RemoveAllListeners();
            if (isParentCategory && onToggleChildren != null)
            {
                toggleChildrenButton.onClick.AddListener(() => onToggleChildren?.Invoke(this));
            }
        }

        UpdateCategorySelection(isSelected ? CategoryName : string.Empty);
    }

    public void UpdateCategorySelection(string currentSelectedCat)
    {
        if (!IsCategory) return;
        bool isSelected = false;
        if (IsParentCategory)
        {
            string parentKey = CategoryName ?? string.Empty;
            isSelected = string.Equals(parentKey, currentSelectedCat, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(currentSelectedCat) && currentSelectedCat.StartsWith(parentKey + "|", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            isSelected = string.Equals(CategoryName ?? string.Empty, currentSelectedCat, StringComparison.OrdinalIgnoreCase);
        }
        if (selectionHighlight) selectionHighlight.SetActive(isSelected);
        if (background) background.color = isSelected ? selectedColor : normalColor;
    }

    public void SetupItem(int id, string name, string cost, string weight, string origin, bool isInv, string instanceId, string equipSlot, Action<EquipmentRowUI> singleClick, Action<EquipmentRowUI> doubleClick, Action<string, string> equipChange, string stats = "")
    {
        ForcarVisibilidadeRow();

        IsCategory = false;
        ItemId = id;
        IsInventory = isInv;
        InstanceId = instanceId;
        IsParentCategory = false;
        ParentCategoryName = string.Empty;
        ChildCategoryName = string.Empty;
        
        onSingleClick = singleClick;
        onDoubleClick = doubleClick;
        onEquipSlotChanged = equipChange;
        onToggleChildren = null;

        if (labelText) labelText.text = name;
        if (secondaryLabelText) secondaryLabelText.gameObject.SetActive(false);
        // amountText/statsText: nunca desativa o GameObject aqui — sem dado real (não existe
        // contagem de quantidade no modelo de inventário, ou o item não tem estatísticas de
        // dano/CA/efeito) só significa "não sobrescrever o texto", não "esconder o campo". Esconder
        // fazia o layout da linha mudar (coluna sumindo) dependendo do item renderizado.

        if (costText) { costText.gameObject.SetActive(true); costText.text = cost; }
        if (weightText) { weightText.gameObject.SetActive(true); weightText.text = weight; }
        if (originText) { originText.gameObject.SetActive(true); originText.text = origin; }
        if (statsText && !string.IsNullOrWhiteSpace(stats)) statsText.text = stats;

        bool sintonizadoAtual = string.Equals(equipSlot, "Sintonizado", StringComparison.OrdinalIgnoreCase);
        bool equipadoAtual = sintonizadoAtual || string.Equals(equipSlot, "Equipado", StringComparison.OrdinalIgnoreCase);

        if (equipToggle)
        {
            equipToggle.gameObject.SetActive(isInv);
            equipToggle.onValueChanged.RemoveAllListeners();
            if (isInv)
            {
                equipToggle.SetIsOnWithoutNotify(equipadoAtual);
                equipToggle.onValueChanged.AddListener((ligado) => {
                    if (sintonizadoToggle != null)
                    {
                        sintonizadoToggle.interactable = ligado;
                        if (!ligado) sintonizadoToggle.SetIsOnWithoutNotify(false);
                    }
                    onEquipSlotChanged?.Invoke(InstanceId, ligado ? "Equipado" : "Desequipado");
                });
            }
        }

        if (sintonizadoToggle)
        {
            sintonizadoToggle.gameObject.SetActive(isInv);
            sintonizadoToggle.onValueChanged.RemoveAllListeners();
            if (isInv)
            {
                sintonizadoToggle.interactable = equipadoAtual;
                sintonizadoToggle.SetIsOnWithoutNotify(sintonizadoAtual);
                sintonizadoToggle.onValueChanged.AddListener((ligado) => {
                    onEquipSlotChanged?.Invoke(InstanceId, ligado ? "Sintonizado" : "Equipado");
                });
            }
        }

        if (toggleChildrenButton) toggleChildrenButton.gameObject.SetActive(false);

        if (selectionHighlight) selectionHighlight.SetActive(false);
        if (background) background.color = normalColor;
    }

    private void ForcarVisibilidadeRow()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        GetComponentsInChildren(true, canvasGroupsCache);
        for (int i = 0; i < canvasGroupsCache.Count; i++)
        {
            CanvasGroup cg = canvasGroupsCache[i];
            if (cg == null) continue;
            cg.alpha = 1f;
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }
        canvasGroupsCache.Clear();

        GetComponentsInChildren(true, graphicsCache);
        for (int i = 0; i < graphicsCache.Count; i++)
        {
            Graphic g = graphicsCache[i];
            if (g == null) continue;
            Color c = g.color;
            if (c.a < 0.95f)
            {
                c.a = 1f;
                g.color = c;
            }
            if (!g.gameObject.activeSelf) g.gameObject.SetActive(true);
        }
        graphicsCache.Clear();
    }

    public void ForceVisibleState()
    {
        ForcarVisibilidadeRow();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (!IsCategory && eventData.clickCount == 2)
        {
            onDoubleClick?.Invoke(this);
        }
        else if (eventData.clickCount == 1)
        {
            onSingleClick?.Invoke(this);
        }
    }
}