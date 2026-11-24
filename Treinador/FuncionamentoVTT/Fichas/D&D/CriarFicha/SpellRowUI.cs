using UnityEngine;
using TMPro;
using UnityEngine.EventSystems;
using System;
using UnityEngine.UI;
using System.Collections.Generic;

public class SpellRowUI : Selectable, IPointerClickHandler
{
    [Header("Referências UI Básicas")]
    public TMP_Text labelText;

    [Header("Referências UI Detalhadas")]
    public TMP_Text nivelText;
    public TMP_Text schoolText;
    public TMP_Text timeText;
    public TMP_Text durationText;

    [Header("Controles do Grimório")]
    public Toggle preparadaToggle;

    [Header("Visual de Seleção")]
    public GameObject selectionHighlight;
    public Image background;
    public Color normalColor   = new Color(1f, 1f, 1f, 0f);
    public Color selectedColor = new Color(0.2f, 0.6f, 1f, 0.5f);

    public int    ItemId      { get; private set; }
    public string CategoryName { get; set; }
    public string InstanceId  { get; private set; }
    public bool   IsGrimorio  { get; private set; }
    public bool   IsCategory  { get; private set; }

    private Action<SpellRowUI>    onSingleClick;
    private Action<SpellRowUI>    onDoubleClick;
    private Action<string, bool>  onPreparadaChanged;

    private static readonly List<CanvasGroup> canvasGroupsCache = new List<CanvasGroup>();
    private static readonly List<Graphic>     graphicsCache     = new List<Graphic>();

    public void SetupCategory(string catName, bool isSelected, Action<SpellRowUI> singleClick)
    {
        ForcarVisibilidadeRow();

        IsCategory  = true;
        IsGrimorio  = false;
        CategoryName = catName;
        onSingleClick      = singleClick;
        onDoubleClick      = null;
        onPreparadaChanged = null;

        if (labelText)       labelText.text = catName.ToUpper();
        if (nivelText)       nivelText.gameObject.SetActive(false);
        if (schoolText)      schoolText.gameObject.SetActive(false);
        if (timeText)        timeText.gameObject.SetActive(false);
        if (durationText)    durationText.gameObject.SetActive(false);
        if (preparadaToggle) preparadaToggle.gameObject.SetActive(false);

        if (selectionHighlight) selectionHighlight.SetActive(isSelected);
        if (background)         background.color = isSelected ? selectedColor : normalColor;
    }

    public void UpdateCategorySelection(string currentSelectedCat)
    {
        if (!IsCategory) return;
        bool isSelected = string.Equals(CategoryName ?? string.Empty, currentSelectedCat, StringComparison.OrdinalIgnoreCase);
        if (selectionHighlight) selectionHighlight.SetActive(isSelected);
        if (background)         background.color = isSelected ? selectedColor : normalColor;
    }

    public void SetupItem(
        int id, string name, string nivel, string school, string time, string duration,
        bool isGrimorio, string instanceId, bool preparada,
        Action<SpellRowUI> singleClick, Action<SpellRowUI> doubleClick,
        Action<string, bool> onPrepChange)
    {
        ForcarVisibilidadeRow();

        IsCategory  = false;
        ItemId      = id;
        IsGrimorio  = isGrimorio;
        InstanceId  = instanceId;
        onSingleClick      = singleClick;
        onDoubleClick      = doubleClick;
        onPreparadaChanged = onPrepChange;

        if (labelText)    labelText.text = name;
        if (nivelText)    { nivelText.gameObject.SetActive(true);    nivelText.text    = nivel; }
        if (schoolText)   { schoolText.gameObject.SetActive(true);   schoolText.text   = school; }
        if (timeText)     { timeText.gameObject.SetActive(true);     timeText.text     = time;   }
        if (durationText) { durationText.gameObject.SetActive(true); durationText.text = duration; }

        if (preparadaToggle)
        {
            preparadaToggle.gameObject.SetActive(isGrimorio);
            preparadaToggle.onValueChanged.RemoveAllListeners();
            if (isGrimorio)
            {
                preparadaToggle.SetIsOnWithoutNotify(preparada);
                preparadaToggle.onValueChanged.AddListener(ligado =>
                    onPreparadaChanged?.Invoke(InstanceId, ligado));
            }
        }

        if (selectionHighlight) selectionHighlight.SetActive(false);
        if (background)         background.color = normalColor;
    }

    private void ForcarVisibilidadeRow()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);

        GetComponentsInChildren(true, canvasGroupsCache);
        for (int i = 0; i < canvasGroupsCache.Count; i++)
        {
            CanvasGroup cg = canvasGroupsCache[i];
            if (cg == null) continue;
            cg.alpha          = 1f;
            cg.interactable   = true;
            cg.blocksRaycasts = true;
        }
        canvasGroupsCache.Clear();

        GetComponentsInChildren(true, graphicsCache);
        for (int i = 0; i < graphicsCache.Count; i++)
        {
            Graphic g = graphicsCache[i];
            if (g == null) continue;
            Color c = g.color;
            if (c.a < 0.95f) { c.a = 1f; g.color = c; }
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
            onDoubleClick?.Invoke(this);
        else if (eventData.clickCount == 1)
            onSingleClick?.Invoke(this);
    }
}
