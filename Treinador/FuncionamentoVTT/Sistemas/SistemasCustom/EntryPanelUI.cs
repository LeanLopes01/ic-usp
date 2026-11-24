using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Painel colapsável que representa uma entrada completa do tópico
/// (ex: uma raça, uma classe, uma magia).
///
/// Prefab deve conter:
///   - Header: TMP_Text (número/título) + Button collapse + Button delete
///   - content: GameObject pai dos FieldInputUI instanciados dinamicamente
///   - prefabFieldInput: prefab de FieldInputUI
/// </summary>
public class EntryPanelUI : MonoBehaviour
{
    [Header("Header")]
    [SerializeField] private TMP_Text   headerText;
    [SerializeField] private Button     btnCollapse;
    [SerializeField] private Button     btnDelete;
    [SerializeField] private GameObject collapseIcon;
    [SerializeField] private GameObject expandIcon;

    [Header("Conteúdo")]
    [SerializeField] private GameObject content;
    [SerializeField] private Transform  fieldsContainer;

    [Header("Prefabs")]
    [SerializeField] private GameObject prefabFieldInput;

    private string                      _topicTag;
    private int                         _index;
    private bool                        _collapsed;
    private readonly List<FieldInputUI> _fieldInputs = new List<FieldInputUI>();

    // ── Setup ─────────────────────────────────────────────────────────────────

    public void Setup(int index, string topicTag, List<XmlFieldDef> activeFields, Action<EntryPanelUI> onDelete)
    {
        _index     = index;
        _topicTag  = topicTag;
        _collapsed = false;

        if (content != null) content.SetActive(true);
        AtualizarHeader();
        SetCollapseIcons(false);

        if (btnCollapse != null)
        {
            btnCollapse.onClick.RemoveAllListeners();
            btnCollapse.onClick.AddListener(ToggleCollapse);
        }
        if (btnDelete != null)
        {
            btnDelete.gameObject.SetActive(true);
            btnDelete.onClick.RemoveAllListeners();
            btnDelete.onClick.AddListener(() => onDelete?.Invoke(this));
        }

        foreach (XmlFieldDef def in activeFields)
            AdicionarCampo(def);

        // Filhos instanciados em runtime dentro de um Layout Group não recalculam tamanho/posição
        // sozinhos — sem isso o painel fica com os campos sobrepostos/espremidos até algo forçar um
        // rebuild (ex: redimensionar a janela).
        if (fieldsContainer is RectTransform rt)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    private void AdicionarCampo(XmlFieldDef def)
    {
        if (prefabFieldInput == null || fieldsContainer == null) return;
        GameObject go = UIUtils.Spawn(prefabFieldInput, fieldsContainer);
        FieldInputUI fi = go.GetComponent<FieldInputUI>();
        if (fi != null)
        {
            fi.Setup(def);
            _fieldInputs.Add(fi);
        }
    }

    // ── Coleta de dados ───────────────────────────────────────────────────────

    public void LoadData(EntryData data)
    {
        foreach (FieldInputUI fi in _fieldInputs)
        {
            if (fi == null) continue;
            FieldValue match = data.Fields.Find(fv => fv.Definition.FieldId == fi.FieldDefId);
            if (match != null) fi.LoadData(match);
        }
    }

    public EntryData GetEntryData()
    {
        EntryData data = new EntryData { TopicTag = _topicTag };
        foreach (FieldInputUI fi in _fieldInputs)
            if (fi != null) data.Fields.Add(fi.GetValue());
        return data;
    }

    // ── UI ───────────────────────────────────────────────────────────────────

    public void AtualizarIndice(int novoIndice)
    {
        _index = novoIndice;
        AtualizarHeader();
    }

    private void AtualizarHeader()
    {
        if (headerText != null)
            headerText.text = $"{_topicTag} #{_index + 1}";
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
