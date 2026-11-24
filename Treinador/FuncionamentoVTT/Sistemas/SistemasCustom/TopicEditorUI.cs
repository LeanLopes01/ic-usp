using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Painel de edição de um único tópico (ex: "raca").
/// Exibe os toggles de campos disponíveis + lista de entradas.
///
/// Prefab / GameObject necessita:
///   - TMP_Text: topicTitleText, textoContagem
///   - Transform: togglesContainer (ScrollView > Content)
///   - Transform: entriesContainer (ScrollView > Content)
///   - Button: btnAddEntry, btnSaveAll, btnNext, btnBack
///   - GameObject: prefabFieldToggle (FieldToggleUI)
///   - GameObject: prefabEntryPanel  (EntryPanelUI)
///   - GameObject: prefabCategoryLabel (opcional — label de separação entre categorias)
/// </summary>
public class TopicEditorUI : MonoBehaviour
{
    [Header("Cabeçalho")]
    [SerializeField] private TMP_Text topicTitleText;
    [SerializeField] private TMP_Text textoContagem;

    [Header("Toggles")]
    [SerializeField] private Transform  togglesContainer;
    [SerializeField] private GameObject prefabFieldToggle;
    [SerializeField] private GameObject prefabCategoryLabel; // TMP_Text label separador (opcional)

    [Header("Entradas")]
    [SerializeField] private Transform  entriesContainer;
    [SerializeField] private GameObject prefabEntryPanel;

    [Header("Navegação de Tópicos")]
    [SerializeField] private Transform  topicosNavContent;
    [SerializeField] private GameObject prefabBotaoTopico;

    [Header("Botões")]
    [SerializeField] private Button btnAddEntry;
    [SerializeField] private Button btnSaveAll;
    [SerializeField] private Button btnNext;
    [SerializeField] private Button btnBack;

    public event Action OnNext;
    public event Action OnBack;
    public event Action OnSaved;
    public event Action<int> OnSelecionarTopico;

    private string                      _topicId;
    private string                      _topicFolderPath;
    private List<XmlFieldDef>           _catalog     = XmlFieldDef.Catalog;
    private List<XmlFieldDef>           _camposFixos = new List<XmlFieldDef>();
    private readonly List<FieldToggleUI> _toggles = new List<FieldToggleUI>();
    private readonly List<EntryPanelUI>  _entries = new List<EntryPanelUI>();

    // ── Setup ─────────────────────────────────────────────────────────────────

    /// <param name="catalogOverride">Catálogo de campos a usar; por padrão, o catálogo genérico da Forja.</param>
    /// <param name="camposFixos">Campos sempre incluídos em toda entrada (não aparecem como toggle).</param>
    public void Setup(string topicId, string folderPath, List<XmlFieldDef> catalogOverride = null, List<XmlFieldDef> camposFixos = null)
    {
        _topicId         = topicId;
        _topicFolderPath = folderPath;
        _catalog         = catalogOverride ?? XmlFieldDef.Catalog;
        _camposFixos     = camposFixos ?? new List<XmlFieldDef>();

        if (topicTitleText != null)
            topicTitleText.text = topicId.Replace("-", " ").ToUpperInvariant();

        LimparEntradas();
        ConstruirToggles();
        CarregarDadosExistentes();
        AtualizarContagem();

        Religar(btnAddEntry, AdicionarEntrada);
        Religar(btnSaveAll,  () => SalvarTudo());
        Religar(btnNext,     () => OnNext?.Invoke());
        Religar(btnBack,     () => OnBack?.Invoke());
    }

    public IEnumerator SetupCoroutine(string topicId, string folderPath, Action<float, string> onProgresso, List<XmlFieldDef> catalogOverride = null, List<XmlFieldDef> camposFixos = null)
    {
        _topicId         = topicId;
        _topicFolderPath = folderPath;
        _catalog         = catalogOverride ?? XmlFieldDef.Catalog;
        _camposFixos     = camposFixos ?? new List<XmlFieldDef>();

        if (topicTitleText != null)
            topicTitleText.text = topicId.Replace("-", " ").ToUpperInvariant();

        onProgresso?.Invoke(0.1f, "Limpando entradas anteriores...");
        yield return null;
        LimparEntradas();

        onProgresso?.Invoke(0.3f, "Construindo campos disponíveis...");
        yield return null;
        ConstruirToggles();

        onProgresso?.Invoke(0.6f, "Lendo dados existentes...");
        yield return null;
        CarregarDadosExistentes();

        onProgresso?.Invoke(0.9f, "Finalizando...");
        yield return null;
        AtualizarContagem();
        Religar(btnAddEntry, AdicionarEntrada);
        Religar(btnSaveAll,  () => SalvarTudo());
        Religar(btnNext,     () => OnNext?.Invoke());
        Religar(btnBack,     () => OnBack?.Invoke());

        onProgresso?.Invoke(1f, "Pronto!");
    }

    // ── Navegação de Tópicos ──────────────────────────────────────────────────

    public void ConfigurarNavegacao(List<string> topicIds, int indiceAtual)
    {
        if (topicosNavContent == null || prefabBotaoTopico == null) return;

        for (int i = topicosNavContent.childCount - 1; i >= 0; i--)
            Destroy(topicosNavContent.GetChild(i).gameObject);

        for (int i = 0; i < topicIds.Count; i++)
        {
            int idx   = i;
            string id = topicIds[i];

            GameObject       go  = UIUtils.Spawn(prefabBotaoTopico, topicosNavContent);
            TopicNavButtonUI nav = go.GetComponent<TopicNavButtonUI>();
            if (nav == null) continue;

            if (nav.textoTopico != null)
                nav.textoTopico.text = id.Replace("-", " ");

            if (nav.botao != null)
            {
                nav.botao.onClick.RemoveAllListeners();
                nav.botao.onClick.AddListener(() => OnSelecionarTopico?.Invoke(idx));

                ColorBlock colors = nav.botao.colors;
                colors.normalColor = idx == indiceAtual
                    ? new Color(0.25f, 0.55f, 0.95f)
                    : Color.white;
                nav.botao.colors = colors;
            }
        }
    }

    // ── Toggles ───────────────────────────────────────────────────────────────

    private void ConstruirToggles()
    {
        // Destroi todos os filhos (toggles + category labels)
        if (togglesContainer != null)
            for (int i = togglesContainer.childCount - 1; i >= 0; i--)
                Destroy(togglesContainer.GetChild(i).gameObject);
        _toggles.Clear();

        var fixedIds = new HashSet<string>(_camposFixos.Select(f => f.FieldId));

        // Agrupa por categoria
        IEnumerable<IGrouping<string, XmlFieldDef>> grupos =
            _catalog.GroupBy(f => f.Category);

        foreach (IGrouping<string, XmlFieldDef> grupo in grupos)
        {
            // Label de categoria (opcional)
            if (prefabCategoryLabel != null && togglesContainer != null && grupo.Any(f => !fixedIds.Contains(f.FieldId)))
            {
                GameObject lbl = UIUtils.Spawn(prefabCategoryLabel, togglesContainer);
                TMP_Text   txt = lbl.GetComponentInChildren<TMP_Text>(true);
                if (txt != null) txt.text = grupo.Key;
            }

            foreach (XmlFieldDef def in grupo)
            {
                if (fixedIds.Contains(def.FieldId)) continue;
                if (prefabFieldToggle == null || togglesContainer == null) continue;
                GameObject go  = UIUtils.Spawn(prefabFieldToggle, togglesContainer);
                FieldToggleUI t = go.GetComponent<FieldToggleUI>();
                if (t == null) continue;
                t.Setup(def);
                t.OnToggled += (_, __) => AtualizarContagem();
                _toggles.Add(t);
            }
        }
    }

    private List<XmlFieldDef> ObterCamposAtivos()
        => _toggles.Where(t => t != null && t.IsOn).Select(t => t.FieldDef).ToList();

    // ── Entradas ─────────────────────────────────────────────────────────────

    private void AdicionarEntrada()
    {
        List<XmlFieldDef> campos = new List<XmlFieldDef>(_camposFixos);
        campos.AddRange(ObterCamposAtivos());
        if (campos.Count == 0)
        {
            VTTLog.ForjaError("Ative pelo menos um campo nos toggles antes de adicionar uma entrada.");
            return;
        }
        if (prefabEntryPanel == null || entriesContainer == null) return;

        GameObject go    = UIUtils.Spawn(prefabEntryPanel, entriesContainer);
        EntryPanelUI ep  = go.GetComponent<EntryPanelUI>();
        if (ep == null) return;

        ep.Setup(_entries.Count, _topicId, campos, RemoverEntrada);
        _entries.Add(ep);
        AtualizarContagem();
    }

    private void CarregarDadosExistentes()
    {
        List<EntryData> existentes = DynamicXmlBuilder.LoadEntriesFromFolder(_topicFolderPath, _topicId, _catalog);
        if (existentes.Count == 0) return;

        // Ativa os toggles dos campos presentes nos dados
        var fieldIds = new HashSet<string>();
        foreach (EntryData entry in existentes)
            foreach (FieldValue fv in entry.Fields)
                fieldIds.Add(fv.Definition.FieldId);

        foreach (FieldToggleUI t in _toggles)
            if (t != null && fieldIds.Contains(t.FieldDef.FieldId))
                t.SetOn(true);

        // Cria os painéis de entrada com os dados carregados
        foreach (EntryData data in existentes)
            AdicionarEntradaComDados(data);
    }

    private void AdicionarEntradaComDados(EntryData data)
    {
        List<XmlFieldDef> campos = data.Fields.ConvertAll(fv => fv.Definition);
        if (campos.Count == 0) return;
        if (prefabEntryPanel == null || entriesContainer == null) return;

        GameObject   go = UIUtils.Spawn(prefabEntryPanel, entriesContainer);
        EntryPanelUI ep = go.GetComponent<EntryPanelUI>();
        if (ep == null) return;

        ep.Setup(_entries.Count, _topicId, campos, RemoverEntrada);
        ep.LoadData(data);
        _entries.Add(ep);
    }

    private void RemoverEntrada(EntryPanelUI ep)
    {
        _entries.Remove(ep);
        Destroy(ep.gameObject);

        // Reindexar
        for (int i = 0; i < _entries.Count; i++)
            _entries[i].AtualizarIndice(i);

        AtualizarContagem();
    }

    private void LimparEntradas()
    {
        foreach (EntryPanelUI ep in _entries) if (ep != null) Destroy(ep.gameObject);
        _entries.Clear();
    }

    // ── Salvar ────────────────────────────────────────────────────────────────

    /// <param name="imagePersister">
    /// Opcional. Chamado com (entrada, campo) para cada FieldValue do tipo Image antes de salvar,
    /// para copiar o arquivo de origem para o destino definitivo; o retorno substitui o TextValue
    /// salvo no XML.
    /// </param>
    public void SalvarTudo(Func<EntryData, FieldValue, string> imagePersister = null)
    {
        if (_entries.Count == 0)
        {
            VTTLog.ForjaError("Nenhuma entrada para salvar.");
            return;
        }

        List<EntryData> dados = _entries
            .Where(e => e != null)
            .Select(e => e.GetEntryData())
            .ToList();

        if (imagePersister != null)
        {
            foreach (EntryData entry in dados)
                foreach (FieldValue fv in entry.Fields)
                    if (fv.Definition.Type == FieldType.Image && !string.IsNullOrWhiteSpace(fv.TextValue))
                        fv.TextValue = imagePersister(entry, fv) ?? fv.TextValue;
        }

        DynamicXmlBuilder.SaveEntriesAsXml(dados, _topicFolderPath, _topicId);
        OnSaved?.Invoke();
        VTTLog.Forja($"{dados.Count} entrada(s) de '{_topicId}' salva(s).");
    }

    // ── UI ────────────────────────────────────────────────────────────────────

    private void AtualizarContagem()
    {
        if (textoContagem == null) return;
        int camposAtivos = ObterCamposAtivos().Count;
        textoContagem.text = $"{_entries.Count} entrada(s) · {camposAtivos} campo(s) ativo(s)";
    }

    private static void Religar(Button btn, Action acao)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => acao());
    }
}
