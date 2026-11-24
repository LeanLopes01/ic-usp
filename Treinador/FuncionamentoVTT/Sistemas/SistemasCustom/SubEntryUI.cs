using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Representa um sub-item dentro de uma ObjectList (ex: um Traço dentro de Traços).
/// Prefab: header (label + collapse + delete) + content (inputs dos sub-campos).
/// </summary>
public class SubEntryUI : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private TMP_Text   headerText;
    [SerializeField] private Button     btnCollapse;
    [SerializeField] private Button     btnDelete;
    [SerializeField] private GameObject collapseIcon;
    [SerializeField] private GameObject expandIcon;

    [Header("Conteúdo")]
    [SerializeField] private GameObject content;
    [SerializeField] private Transform  fieldsContent;

    [Header("Prefabs")]
    [SerializeField] private GameObject prefabFieldInput;

    private readonly List<FieldInputUI> _fields    = new List<FieldInputUI>();
    private bool                        _collapsed = false;

    public void Setup(List<XmlFieldDef> subFields, string label, Action onDelete)
    {
        if (headerText != null) headerText.text = label;

        if (btnCollapse != null)
        {
            btnCollapse.onClick.RemoveAllListeners();
            btnCollapse.onClick.AddListener(ToggleCollapse);
        }
        if (btnDelete != null)
        {
            btnDelete.gameObject.SetActive(true);
            btnDelete.onClick.RemoveAllListeners();
            btnDelete.onClick.AddListener(() => onDelete?.Invoke());
        }

        SetCollapseIcons(false);

        foreach (XmlFieldDef def in subFields)
            AdicionarCampo(def);
    }

    private void AdicionarCampo(XmlFieldDef def)
    {
        if (prefabFieldInput == null || fieldsContent == null) return;
        GameObject go = UIUtils.Spawn(prefabFieldInput, fieldsContent);
        FieldInputUI fi = go.GetComponent<FieldInputUI>();
        if (fi != null)
        {
            fi.Setup(def);
            _fields.Add(fi);
        }
    }

    public void LoadData(EntryData data)
    {
        foreach (FieldInputUI fi in _fields)
        {
            if (fi == null) continue;
            FieldValue match = data.Fields.Find(fv => fv.Definition.FieldId == fi.FieldDefId);
            if (match != null) fi.LoadData(match);
        }
    }

    public EntryData GetEntryData()
    {
        EntryData data = new EntryData();
        foreach (FieldInputUI fi in _fields)
            if (fi != null) data.Fields.Add(fi.GetValue());
        return data;
    }

    private void ToggleCollapse()
    {
        _collapsed = !_collapsed;
        if (content != null) content.SetActive(!_collapsed);
        SetCollapseIcons(_collapsed);
    }

    private void SetCollapseIcons(bool collapsed)
    {
        if (collapseIcon != null) collapseIcon.SetActive(!collapsed);
        if (expandIcon   != null) expandIcon.SetActive(collapsed);
    }
}
