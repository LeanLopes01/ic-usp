using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Widget de input para um campo XML.
/// Prefab deve conter:
///   - labelText          : TMP_Text
///   - panelText          : TMP_InputField linha simples (Text, Attribute) + TMP_InputField
///                           multiline (TextArea, StringList — itens separados por vírgula/linha)
///   - panelNumber        : inputNumber + dropdownOperador + btnAdicionarUnidade  (Number)
///   - panelUnit          : inputUnitValue + inputUnitName  (UnitValue)
///   - panelObjList       : objectListContent + btnAddSubEntry + prefabSubEntry
///   - panelGroup         : groupContent + prefabFieldInput
/// </summary>
public class FieldInputUI : MonoBehaviour
{
    [Header("Label")]
    [SerializeField] private TMP_Text labelText;

    [Header("Texto (Text, Attribute, TextArea, StringList)")]
    [SerializeField] private GameObject     panelText;
    [SerializeField] private TMP_InputField inputTextSimples;
    [SerializeField] private TMP_InputField inputTextLongo;

    [Header("Numérico (Number)")]
    [SerializeField] private GameObject     panelNumber;
    [SerializeField] private TMP_InputField inputNumber;
    [SerializeField] private TMP_Dropdown   dropdownOperador;
    [SerializeField] private Button         btnAdicionarUnidade;

    [Header("Valor com Unidade (UnitValue)")]
    [SerializeField] private GameObject     panelUnit;
    [SerializeField] private TMP_InputField inputUnitValue;
    [SerializeField] private TMP_InputField inputUnitName;

    [Header("Lista de Objetos (ObjectList)")]
    [SerializeField] private GameObject panelObjList;
    [SerializeField] private Transform  objectListContent;
    [SerializeField] private Button     btnAddSubEntry;
    [SerializeField] private GameObject prefabSubEntry;

    [Header("Grupo (Group)")]
    [SerializeField] private GameObject panelGroup;
    [SerializeField] private Transform  groupContent;
    [SerializeField] private GameObject prefabFieldInput;

    private static readonly List<string> OperadoresOpcoes = new List<string> { "—", "+", "-", "*", "/" };
    private static readonly char[] SeparadoresLista = { ',', '\n', '\r' };

    public string FieldDefId => _def?.FieldId;

    private XmlFieldDef                  _def;
    private readonly List<SubEntryUI>     _subEntries  = new List<SubEntryUI>();
    private readonly List<FieldInputUI>   _groupInputs = new List<FieldInputUI>();
    private int                           _subEntryCount;

    public void Setup(XmlFieldDef def)
    {
        _def = def;
        if (labelText != null) labelText.text = def.Label;

        Painel(panelText,   false);
        Painel(panelNumber, false);
        Painel(panelUnit,     false);
        Painel(panelObjList,  false);
        Painel(panelGroup,    false);

        switch (def.Type)
        {
            case FieldType.Text:
            case FieldType.Attribute:
                Painel(panelText, true);
                if (inputTextSimples != null) inputTextSimples.gameObject.SetActive(true);
                if (inputTextLongo   != null) inputTextLongo.gameObject.SetActive(false);
                break;

            case FieldType.TextArea:
            case FieldType.StringList:
                Painel(panelText, true);
                if (inputTextSimples != null) inputTextSimples.gameObject.SetActive(false);
                if (inputTextLongo   != null) inputTextLongo.gameObject.SetActive(true);
                DefinirPlaceholder(inputTextLongo, def.Type == FieldType.StringList
                    ? "Separe os itens por vírgula ou linha"
                    : "");
                break;

            case FieldType.Number:
                Painel(panelNumber, true);
                ConfigurarInputNumerico();
                break;

            case FieldType.UnitValue:
                Painel(panelUnit, true);
                if (inputUnitName != null)
                    inputUnitName.text = def.DefaultUnit ?? "";
                break;

            case FieldType.ObjectList:
                Painel(panelObjList, true);
                if (btnAddSubEntry != null)
                {
                    btnAddSubEntry.onClick.RemoveAllListeners();
                    btnAddSubEntry.onClick.AddListener(AdicionarSubEntry);
                }
                AdicionarSubEntry();
                break;

            case FieldType.Group:
                Painel(panelGroup, true);
                foreach (XmlFieldDef sub in def.SubFields)
                    AdicionarGroupInput(sub);
                break;
        }
    }

    // ── Numérico ──────────────────────────────────────────────────────────────

    private void ConfigurarInputNumerico()
    {
        if (inputNumber != null)
        {
            inputNumber.contentType = TMP_InputField.ContentType.DecimalNumber;
            inputNumber.onValidateInput -= ValidarApenasNumerico;
            inputNumber.onValidateInput += ValidarApenasNumerico;
        }

        if (dropdownOperador != null)
        {
            dropdownOperador.ClearOptions();
            dropdownOperador.AddOptions(OperadoresOpcoes);
            dropdownOperador.value = 0;
        }

        if (btnAdicionarUnidade != null)
        {
            btnAdicionarUnidade.onClick.RemoveAllListeners();
            // Funcionalidade futura
        }
    }

    private static char ValidarApenasNumerico(string text, int charIndex, char addedChar)
    {
        if (char.IsDigit(addedChar)) return addedChar;
        if (addedChar == '.' || addedChar == ',')
        {
            if (!text.Contains('.') && !text.Contains(','))
                return '.';
        }
        if (addedChar == '-' && charIndex == 0 && !text.StartsWith('-'))
            return addedChar;
        return '\0';
    }

    // ── Adição de itens ───────────────────────────────────────────────────────

    private void AdicionarSubEntry()
    {
        if (prefabSubEntry == null || objectListContent == null) return;
        _subEntryCount++;
        int        idx = _subEntryCount;
        GameObject go  = UIUtils.Spawn(prefabSubEntry, objectListContent);
        SubEntryUI sub = go.GetComponent<SubEntryUI>();
        if (sub == null) return;

        string label = $"{_def.XmlItemTag ?? "item"} {idx}";
        sub.Setup(_def.SubFields, label, () => { _subEntries.Remove(sub); Destroy(go); ForcarRebuildLayout(objectListContent); });
        _subEntries.Add(sub);

        ForcarRebuildLayout(objectListContent);
    }

    private void AdicionarGroupInput(XmlFieldDef subDef)
    {
        if (prefabFieldInput == null || groupContent == null) return;
        GameObject   go = UIUtils.Spawn(prefabFieldInput, groupContent);
        FieldInputUI fi = go.GetComponent<FieldInputUI>();
        if (fi != null) { fi.Setup(subDef); _groupInputs.Add(fi); }

        ForcarRebuildLayout(groupContent);
    }

    // Filhos instanciados em runtime dentro de um Layout Group não recalculam tamanho/posição
    // sozinhos — sem isso os campos ficam sobrepostos/espremidos até algo forçar um rebuild.
    private static void ForcarRebuildLayout(Transform t)
    {
        if (t is not RectTransform rt) return;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    // ── Carregamento de dados ─────────────────────────────────────────────────

    public void LoadData(FieldValue fv)
    {
        switch (fv.Definition.Type)
        {
            case FieldType.Text:
            case FieldType.Attribute:
                if (inputTextSimples != null) inputTextSimples.text = fv.TextValue;
                break;

            case FieldType.TextArea:
                if (inputTextLongo != null) inputTextLongo.text = fv.TextValue;
                break;

            case FieldType.StringList:
                // Quebra de linha em vez de ", " — cada item numa linha própria fica muito mais
                // legível numa caixa multiline do que um bloco só de texto emendado; GetValue() já
                // aceita '\n' como separador de volta (ver SeparadoresLista), então o roundtrip
                // continua funcionando igual.
                if (inputTextLongo != null) inputTextLongo.text = string.Join("\n", fv.ListValues);
                break;

            case FieldType.Number:
                if (inputNumber != null) inputNumber.text = fv.TextValue;
                break;

            case FieldType.UnitValue:
                if (inputUnitValue != null) inputUnitValue.text = fv.TextValue;
                if (inputUnitName  != null) inputUnitName.text  = fv.UnitValue;
                break;

            case FieldType.ObjectList:
                if (objectListContent != null)
                    for (int i = objectListContent.childCount - 1; i >= 0; i--)
                        Destroy(objectListContent.GetChild(i).gameObject);
                _subEntries.Clear();
                _subEntryCount = 0;
                if (fv.SubEntries.Count == 0)
                    AdicionarSubEntry();
                else
                    foreach (EntryData subData in fv.SubEntries)
                    {
                        int prevCount = _subEntries.Count;
                        AdicionarSubEntry();
                        if (_subEntries.Count > prevCount)
                            _subEntries[_subEntries.Count - 1].LoadData(subData);
                    }
                break;

            case FieldType.Group:
                for (int i = 0; i < _groupInputs.Count && i < fv.GroupFields.Count; i++)
                    if (_groupInputs[i] != null)
                        _groupInputs[i].LoadData(fv.GroupFields[i]);
                break;
        }
    }

    // ── Leitura dos valores ───────────────────────────────────────────────────

    public FieldValue GetValue()
    {
        FieldValue fv = new FieldValue { Definition = _def };

        switch (_def.Type)
        {
            case FieldType.Text:
            case FieldType.Attribute:
                fv.TextValue = inputTextSimples != null ? inputTextSimples.text.Trim() : "";
                break;

            case FieldType.TextArea:
                fv.TextValue = inputTextLongo != null ? inputTextLongo.text.Trim() : "";
                break;

            case FieldType.StringList:
                if (inputTextLongo != null)
                    fv.ListValues = inputTextLongo.text
                        .Split(SeparadoresLista, StringSplitOptions.RemoveEmptyEntries)
                        .Select(item => item.Trim())
                        .Where(item => !string.IsNullOrWhiteSpace(item))
                        .ToList();
                break;

            case FieldType.Number:
                fv.TextValue = inputNumber != null ? inputNumber.text.Trim() : "";
                break;

            case FieldType.UnitValue:
                fv.TextValue = inputUnitValue != null ? inputUnitValue.text.Trim() : "";
                fv.UnitValue = inputUnitName  != null ? inputUnitName.text.Trim()  : _def.DefaultUnit ?? "";
                break;

            case FieldType.ObjectList:
                foreach (SubEntryUI sub in _subEntries)
                    if (sub != null) fv.SubEntries.Add(sub.GetEntryData());
                break;

            case FieldType.Group:
                foreach (FieldInputUI gi in _groupInputs)
                    if (gi != null) fv.GroupFields.Add(gi.GetValue());
                break;
        }
        return fv;
    }

    private static void Painel(GameObject go, bool ativo)
    {
        if (go != null) go.SetActive(ativo);
    }

    private static void DefinirPlaceholder(TMP_InputField input, string texto)
    {
        if (input == null || input.placeholder == null) return;
        if (input.placeholder is TMP_Text tmp) tmp.text = texto;
    }
}
