using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Painel de Lore de um NPC — separado do stat block mecânico (CampanhaTopicEditorUI). Aberto a
/// partir do botão "Lore" num CampanhaNpcCardUI (ver CampanhasManager.AbrirLoreDoNpc). Contém um
/// texto livre (multiline) e uma lista de itens anexados, resolvidos contra o catálogo real de
/// itens SRD (CampanhaItemCatalogoLoader — cópia isolada do carregamento usado por
/// Fichas/D&amp;D/CriarFicha/EquipmentSelector.cs, sem tocar naquele arquivo).
///
/// IMPORTANTE: Salvar() muta o EntryData ORIGINAL recebido em Setup() em vez de construir um novo
/// do zero — DynamicXmlBuilder.SaveEntriesAsXml sempre reescreve o arquivo inteiro a partir do
/// EntryData.Fields recebido (sem merge), então construir um EntryData só com os campos de lore
/// apagaria o stat block inteiro do NPC. Mesmo cuidado espelhado em CampanhaTopicEditorUI
/// (_camposExtrasPreservados), na direção oposta.
/// </summary>
public class CampanhaNpcLoreUI : MonoBehaviour
{
    [Header("Cabeçalho (somente leitura)")]
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private Image    token;
    [SerializeField] private Sprite   tokenFallback;

    [Header("Texto de Lore")]
    [Tooltip("Content onde o FieldInputUI de lore é instanciado (mesmo padrão de CampanhaTopicEditorUI.fieldsContainer/prefabFieldInput) — só um item, sempre presente, sem toggle.")]
    [SerializeField] private Transform  loreDescricaoContent;
    [SerializeField] private GameObject prefabFieldInput; // com FieldInputUI

    [Header("Catálogo de Itens")]
    [SerializeField] private string pastaItensRelativa = "sistemas/quinta-edicao-srd/itens";
    [Tooltip("Painel com o filtro/dropdown/lista/detalhes do catálogo — mostrado/escondido pelos botões de expandir/colapsar abaixo (mesma lógica de Fichas/D&D/CriarFicha/EquipmentSelector.searchContentPanel).")]
    [SerializeField] private GameObject     catalogoPainelExpansivel;
    [SerializeField] private Button         botaoExpandirCatalogo;
    [SerializeField] private Button         botaoColapsarCatalogo;
    [SerializeField] private TMP_InputField filtroPesquisaInput;
    [SerializeField] private TMP_Dropdown   categoriaDropdown;
    [SerializeField] private Transform      catalogoContent;
    [SerializeField] private GameObject     prefabCatalogoItem; // com CampanhaItemCatalogoRowUI
    [SerializeField] private TMP_Text       detalhesText;

    [Header("Itens Anexados")]
    [SerializeField] private Transform  itensAnexadosContent;
    [SerializeField] private GameObject prefabItemAnexado; // com CampanhaNpcLoreItemUI

    /// <summary>Disparado só quando o salvamento é bem-sucedido — CampanhasManager assina isso pra
    /// voltar pra listagem de NPCs.</summary>
    public event Action OnSaved;

    private EntryData _data;
    private string    _folderPath;
    private string    _topicTag;

    private FieldInputUI _loreDescricaoInput;

    private readonly List<(string id, string nome)> _itensAnexados = new List<(string, string)>();

    private Dictionary<int, CampanhaItemCatalogoLoader.ItemData>            _catalogo;
    private Dictionary<string, List<CampanhaItemCatalogoLoader.ItemData>>   _catalogoPorCategoria;

    private Texture2D _texturaCarregada;
    private Sprite     _spriteCarregado;

    private void Awake()
    {
        if (filtroPesquisaInput != null)
            filtroPesquisaInput.onValueChanged.AddListener(_ => AplicarFiltro());
        if (categoriaDropdown != null)
            categoriaDropdown.onValueChanged.AddListener(_ => AplicarFiltro());

        if (botaoExpandirCatalogo != null)
        {
            botaoExpandirCatalogo.onClick.RemoveAllListeners();
            botaoExpandirCatalogo.onClick.AddListener(ExpandirCatalogo);
        }
        if (botaoColapsarCatalogo != null)
        {
            botaoColapsarCatalogo.onClick.RemoveAllListeners();
            botaoColapsarCatalogo.onClick.AddListener(ColapsarCatalogo);
        }

        // Estado inicial: catálogo expandido (mesmo padrão de EquipmentSelector.searchContentPanel).
        ExpandirCatalogo();
    }

    // ── Expandir/colapsar catálogo (mesma lógica de EquipmentSelector.searchContentPanel) ──────

    public void ExpandirCatalogo()
    {
        if (catalogoPainelExpansivel != null) catalogoPainelExpansivel.SetActive(true);
        if (botaoExpandirCatalogo != null) botaoExpandirCatalogo.gameObject.SetActive(false);
        if (botaoColapsarCatalogo != null) botaoColapsarCatalogo.gameObject.SetActive(true);
    }

    public void ColapsarCatalogo()
    {
        if (catalogoPainelExpansivel != null) catalogoPainelExpansivel.SetActive(false);
        if (botaoExpandirCatalogo != null) botaoExpandirCatalogo.gameObject.SetActive(true);
        if (botaoColapsarCatalogo != null) botaoColapsarCatalogo.gameObject.SetActive(false);
    }

    private void OnDestroy() => LimparTokenRuntime();

    // ── Setup ─────────────────────────────────────────────────────────────────

    public void Setup(EntryData data, string folderPath, string topicTag)
    {
        if (data == null) return;

        // Salva automaticamente qualquer edição pendente do NPC anterior antes de trocar — sem
        // isso, abrir a lore de outro NPC (ou reabrir este painel) descartava silenciosamente texto
        // já digitado que ainda não tinha sido salvo.
        SalvarSeCarregado();

        _data       = data;
        _folderPath = folderPath;
        _topicTag   = topicTag;

        if (nomeText != null) nomeText.text = NpcEntryFormatter.ObterNome(data);
        CarregarToken(NpcEntryFormatter.ObterRetratoPath(data));

        ConstruirCampoDeLore();
        FieldValue loreFv = data.Fields.Find(fv => fv.Definition.FieldId == Dnd5eNpcFieldCatalog.LoreDescricaoField.FieldId);
        if (loreFv != null) _loreDescricaoInput?.LoadData(loreFv);

        _itensAnexados.Clear();
        LimparListaItensAnexadosUI();
        FieldValue itensFv = data.Fields.Find(fv => fv.Definition.FieldId == Dnd5eNpcFieldCatalog.LoreItensField.FieldId);
        if (itensFv != null)
            foreach (EntryData sub in itensFv.SubEntries)
            {
                string id   = sub.Fields.Find(f => f.Definition.FieldId == Dnd5eNpcFieldCatalog.LoreItemIdField.FieldId)?.TextValue ?? "";
                string nome = sub.Fields.Find(f => f.Definition.FieldId == Dnd5eNpcFieldCatalog.LoreItemNomeField.FieldId)?.TextValue ?? "";
                if (string.IsNullOrWhiteSpace(id)) continue;
                _itensAnexados.Add((id, nome));
            }
        RedesenharListaItensAnexados();

        CarregarCatalogoDeItens();
        if (filtroPesquisaInput != null) filtroPesquisaInput.SetTextWithoutNotify("");
        if (categoriaDropdown != null) categoriaDropdown.SetValueWithoutNotify(0);
        if (detalhesText != null) detalhesText.text = "";
        AplicarFiltro();
    }

    // Instancia (ou reinstancia) o FieldInputUI de lore-descricao dentro de loreDescricaoContent —
    // mesmo padrão de CampanhaTopicEditorUI.ConstruirCampos, só que sempre com exatamente 1 campo
    // fixo (sem toggle, sem lista dinâmica de vários campos).
    private void ConstruirCampoDeLore()
    {
        if (loreDescricaoContent == null || prefabFieldInput == null) return;

        for (int i = loreDescricaoContent.childCount - 1; i >= 0; i--)
            Destroy(loreDescricaoContent.GetChild(i).gameObject);

        GameObject go = UIUtils.Spawn(prefabFieldInput, loreDescricaoContent);
        _loreDescricaoInput = go.GetComponent<FieldInputUI>();
        _loreDescricaoInput?.Setup(Dnd5eNpcFieldCatalog.LoreDescricaoField);
    }

    // ── Catálogo de itens ─────────────────────────────────────────────────────

    private void CarregarCatalogoDeItens()
    {
        if (_catalogo != null) return; // já carregado nesta sessão (o loader também cacheia internamente)
        _catalogo = CampanhaItemCatalogoLoader.Carregar(pastaItensRelativa, out _catalogoPorCategoria);

        if (categoriaDropdown != null)
        {
            var opcoes = new List<string> { "Todas" };
            opcoes.AddRange(_catalogoPorCategoria.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
            categoriaDropdown.ClearOptions();
            categoriaDropdown.AddOptions(opcoes);
            categoriaDropdown.value = 0;
        }
    }

    private void AplicarFiltro()
    {
        if (catalogoContent == null || prefabCatalogoItem == null || _catalogo == null) return;

        for (int i = catalogoContent.childCount - 1; i >= 0; i--)
            Destroy(catalogoContent.GetChild(i).gameObject);

        string categoriaSelecionada = categoriaDropdown != null && categoriaDropdown.options.Count > categoriaDropdown.value
            ? categoriaDropdown.options[categoriaDropdown.value].text
            : "Todas";

        IEnumerable<CampanhaItemCatalogoLoader.ItemData> fonte = _catalogo.Values;
        if (!string.IsNullOrWhiteSpace(categoriaSelecionada) && categoriaSelecionada != "Todas"
            && _catalogoPorCategoria.TryGetValue(categoriaSelecionada, out List<CampanhaItemCatalogoLoader.ItemData> lista))
            fonte = lista;

        string filtro = filtroPesquisaInput != null ? filtroPesquisaInput.text.Trim() : "";

        foreach (CampanhaItemCatalogoLoader.ItemData item in fonte.OrderBy(i => i.nome, StringComparer.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(filtro) && item.nome.IndexOf(filtro, StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            GameObject go = UIUtils.Spawn(prefabCatalogoItem, catalogoContent);
            CampanhaItemCatalogoLoader.ItemData itemCapturado = item;
            go.GetComponent<CampanhaItemCatalogoRowUI>()?.Setup(item.id, item.nome, item.categoria,
                onClique: _ => { if (detalhesText != null) detalhesText.text = $"{itemCapturado.nome}\n{itemCapturado.categoria}"; },
                onDuploClique: _ => AnexarItem(itemCapturado));
        }
    }

    // ── Itens anexados ────────────────────────────────────────────────────────

    private void AnexarItem(CampanhaItemCatalogoLoader.ItemData item)
    {
        string idStr = item.id.ToString();
        if (_itensAnexados.Any(i => i.id == idStr)) return; // já anexado

        _itensAnexados.Add((idStr, item.nome));
        RedesenharListaItensAnexados();
        SalvarSeCarregado(); // autosave — anexar item já persiste na hora, sem depender de um clique em "Salvar"
    }

    private void RemoverItem(string itemId)
    {
        _itensAnexados.RemoveAll(i => i.id == itemId);
        RedesenharListaItensAnexados();
        SalvarSeCarregado(); // autosave — mesma lógica de AnexarItem
    }

    private void RedesenharListaItensAnexados()
    {
        LimparListaItensAnexadosUI();
        if (itensAnexadosContent == null || prefabItemAnexado == null) return;

        foreach ((string id, string nome) in _itensAnexados)
        {
            GameObject go = UIUtils.Spawn(prefabItemAnexado, itensAnexadosContent);
            go.GetComponent<CampanhaNpcLoreItemUI>()?.Setup(id, nome, RemoverItem);
        }
    }

    private void LimparListaItensAnexadosUI()
    {
        if (itensAnexadosContent == null) return;
        for (int i = itensAnexadosContent.childCount - 1; i >= 0; i--)
            Destroy(itensAnexadosContent.GetChild(i).gameObject);
    }

    // ── Salvar ────────────────────────────────────────────────────────────────

    /// <summary>Salva e dispara OnSaved — uso explícito (botão "Salvar" do próprio painel, ou o
    /// botão "Salvar" compartilhado de CampanhasManager quando este painel está aberto). Dispara a
    /// navegação de volta pra listagem (ver CampanhasManager: npcLoreEditor.OnSaved += AbrirListaNpcsForcado),
    /// então NÃO deve ser chamado a partir de autosave (ver SalvarSeCarregado).</summary>
    public void Salvar()
    {
        if (!SalvarInterno()) return;
        OnSaved?.Invoke();
    }

    /// <summary>Autosave silencioso — persiste sem navegar de volta pra listagem nem logar erro se
    /// nada estiver carregado ainda. Chamado ao anexar/remover item e ao trocar de NPC (Setup),
    /// pra nunca perder edição pendente sem depender do usuário lembrar de clicar em "Salvar".</summary>
    public void SalvarSeCarregado()
    {
        if (_data != null) SalvarInterno();
    }

    private bool SalvarInterno()
    {
        if (_data == null || string.IsNullOrWhiteSpace(_folderPath) || string.IsNullOrWhiteSpace(_topicTag))
        {
            VTTLog.ForjaError("[CampanhaNpcLoreUI] Nenhum NPC carregado — nada pra salvar.");
            return false;
        }

        // Muta o EntryData ORIGINAL — nunca reconstruir do zero (ver comentário de classe).
        _data.Fields.RemoveAll(fv =>
            fv.Definition.FieldId == Dnd5eNpcFieldCatalog.LoreDescricaoField.FieldId ||
            fv.Definition.FieldId == Dnd5eNpcFieldCatalog.LoreItensField.FieldId);

        if (_loreDescricaoInput != null)
        {
            FieldValue loreFv = _loreDescricaoInput.GetValue();
            if (!string.IsNullOrWhiteSpace(loreFv.TextValue))
                _data.Fields.Add(loreFv);
        }

        if (_itensAnexados.Count > 0)
        {
            var itensFv = new FieldValue { Definition = Dnd5eNpcFieldCatalog.LoreItensField };
            foreach ((string id, string nome) in _itensAnexados)
            {
                var sub = new EntryData { TopicTag = "item" };
                sub.Fields.Add(new FieldValue { Definition = Dnd5eNpcFieldCatalog.LoreItemIdField,   TextValue = id });
                sub.Fields.Add(new FieldValue { Definition = Dnd5eNpcFieldCatalog.LoreItemNomeField, TextValue = nome });
                itensFv.SubEntries.Add(sub);
            }
            _data.Fields.Add(itensFv);
        }

        DynamicXmlBuilder.SaveEntriesAsXml(new List<EntryData> { _data }, _folderPath, _topicTag);
        VTTLog.Forja($"[CampanhaNpcLoreUI] Lore de '{NpcEntryFormatter.ObterNome(_data)}' salva ({_itensAnexados.Count} item(ns)).");
        return true;
    }

    // ── Token (imagem, mesmo padrão de CampanhaNpcCardUI) ───────────────────────

    private void CarregarToken(string imagePath)
    {
        if (token == null) return;
        LimparTokenRuntime();

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            token.sprite = tokenFallback;
            token.preserveAspect = true;
            token.color = Color.white;
            return;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(imagePath);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            if (!tex.LoadImage(bytes))
            {
                Destroy(tex);
                token.sprite = tokenFallback;
                token.preserveAspect = true;
                token.color = Color.white;
                return;
            }

            _texturaCarregada = tex;
            _spriteCarregado = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            token.sprite = _spriteCarregado;
            token.preserveAspect = true;
            token.color = Color.white;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CampanhaNpcLoreUI] Falha ao carregar token '{imagePath}': {e.Message}");
            token.sprite = tokenFallback;
            token.preserveAspect = true;
            token.color = Color.white;
        }
    }

    private void LimparTokenRuntime()
    {
        if (token != null) token.sprite = null;
        if (_spriteCarregado != null) Destroy(_spriteCarregado);
        if (_texturaCarregada != null) Destroy(_texturaCarregada);
        _spriteCarregado = null;
        _texturaCarregada = null;
    }
}
