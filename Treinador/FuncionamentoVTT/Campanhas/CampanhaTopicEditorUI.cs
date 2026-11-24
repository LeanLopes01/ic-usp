using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SimpleFileBrowser;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Editor de uma única entrada de tópico de campanha (hoje só "npcs"). Diferente de
/// Sistemas/SistemasCustom/TopicEditorUI.cs — que lista várias entradas com navegação entre
/// tópicos — aqui só existe UMA entrada por sessão de criação/edição.
///
/// A listagem de entradas já salvas NÃO fica aqui — é um painel próprio e separado
/// (CampanhaNpcGaleriaUI, ver CampanhasManager), já que ocupa o mesmo espaço visual desta tela.
/// Este editor só tem dois estados mutuamente exclusivos:
///   1. Toggles (padrão ao chamar Setup): escolher quais campos usar. Cabeçalho (nome + imagem)
///      sempre visível, tratado direto aqui — não passa pelo sistema de toggles/FieldInputUI (ver
///      Dnd5eNpcFieldCatalog.NomeField/RetratoField).
///   2. Campos: botão "Continuar" nos toggles congela os campos escolhidos e mostra os
///      FieldInputUI preenchíveis (direto, sem o wrapper de múltiplas entradas do EntryPanelUI).
///      Botão "Editar Campos" volta para os toggles, preservando os valores já digitados nos
///      campos que continuarem marcados.
/// Salvar (botão externo, ver CampanhasManager) persiste a entrada atual e dispara OnSaved —
/// CampanhasManager usa isso pra trocar de volta pro painel de listagem.
/// EditarEntrada(EntryData) é chamado de fora (CampanhasManager.AbrirNpcParaEdicao, a partir do
/// botão "Editar" de um card na listagem ou do menu de atalho de um token no grid) pra carregar
/// uma entrada existente direto no painel de campos.
/// </summary>
public class CampanhaTopicEditorUI : MonoBehaviour
{
    [Header("Cabeçalho")]
    [SerializeField] private TMP_Text       topicTitleText;
    [SerializeField] private TMP_InputField inputNome;
    [SerializeField] private Button         botaoSelecionarImagem;
    [SerializeField] private GameObject     previewImagemGO;
    [SerializeField] private Image          previewImagem;

    [Header("Toggles")]
    [SerializeField] private GameObject togglesPanel;
    [SerializeField] private Transform  togglesContainer;
    [SerializeField] private GameObject prefabFieldToggle;
    [SerializeField] private GameObject prefabCategoryLabel; // opcional
    [SerializeField] private Button     botaoContinuar;

    [Header("Campos")]
    [SerializeField] private GameObject camposPanel;
    [SerializeField] private Transform  fieldsContainer;
    [SerializeField] private GameObject prefabFieldInput;
    [SerializeField] private Button     botaoEditarCampos;
    [Tooltip("Segundo botão que faz exatamente a mesma coisa que botaoEditarCampos (volta pros " +
        "toggles) — útil quando a tela precisa do mesmo atalho em dois lugares diferentes do layout " +
        "(ex: topo e rodapé). Opcional, deixe vazio se não precisar.")]
    [SerializeField] private Button     botaoEditarCamposSecundario;

    /// <summary>Disparado só quando o salvamento é bem-sucedido (não em falhas de validação) — ver
    /// CampanhasManager, que assina isso pra trocar de volta pro painel de listagem.</summary>
    public event Action OnSaved;

    private string             _topicId;
    private string             _topicFolderPath;
    private List<XmlFieldDef>  _catalog = new List<XmlFieldDef>();

    private readonly List<FieldToggleUI> _toggles     = new List<FieldToggleUI>();
    private readonly List<FieldInputUI>  _fieldInputs = new List<FieldInputUI>();

    // Campos que a entrada carregada tem mas que não pertencem ao catálogo de toggles desta tela
    // (ex: lore — ver CampanhaNpcLoreUI). SalvarTudo reconstrói o EntryData do zero a partir só dos
    // toggles marcados; sem re-adicionar esses campos aqui, salvar o stat block apagaria a lore
    // silenciosamente (DynamicXmlBuilder.SaveEntriesAsXml sempre reescreve o arquivo inteiro, sem
    // merge). Genérico de propósito — não hardcoda "lore", preserva qualquer campo fora do catálogo.
    private readonly List<FieldValue> _camposExtrasPreservados = new List<FieldValue>();

    private string     _imagemSelecionadaPath = "";
    private Texture2D  _texturaPreviewRuntime;
    private Sprite     _spritePreviewRuntime;

    // ── Setup ─────────────────────────────────────────────────────────────────

    /// <summary>Começa uma sessão nova (formulário em branco) para o tópico informado.</summary>
    public void Setup(string topicId, string folderPath, List<XmlFieldDef> catalog)
    {
        _topicId         = topicId;
        _topicFolderPath = folderPath;
        _catalog         = catalog ?? new List<XmlFieldDef>();

        if (topicTitleText != null)
            topicTitleText.text = topicId.Replace("-", " ").ToUpperInvariant();

        Religar(botaoContinuar,               Continuar);
        Religar(botaoEditarCampos,            MostrarToggles);
        Religar(botaoEditarCamposSecundario,  MostrarToggles);
        Religar(botaoSelecionarImagem,        OnClickSelecionarImagem);

        LimparFormulario();
        ConstruirToggles();
        MostrarToggles();
    }

    private void OnDestroy() => LimparPreviewRuntime();

    // ── Cabeçalho: nome + imagem ─────────────────────────────────────────────

    private void OnClickSelecionarImagem()
    {
        FileBrowser.SetFilters(true, new FileBrowser.Filter("Imagens", ".jpg", ".png", ".jpeg"));
        FileBrowser.ShowLoadDialog(
            (paths) =>
            {
                if (paths == null || paths.Length == 0) return;
                _imagemSelecionadaPath = paths[0];
                AtualizarPreviewImagem(_imagemSelecionadaPath);
            },
            null,
            FileBrowser.PickMode.Files, false, null, null, "Selecione uma Imagem", "Selecionar");
    }

    private void AtualizarPreviewImagem(string caminho)
    {
        if (previewImagemGO != null) previewImagemGO.SetActive(false);
        if (previewImagem == null || string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return;

        byte[] bytes = File.ReadAllBytes(caminho);
        var novaTextura = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!novaTextura.LoadImage(bytes))
        {
            Destroy(novaTextura);
            return;
        }

        LimparPreviewRuntime();
        _texturaPreviewRuntime = novaTextura;
        _spritePreviewRuntime = Sprite.Create(
            _texturaPreviewRuntime,
            new Rect(0f, 0f, _texturaPreviewRuntime.width, _texturaPreviewRuntime.height),
            new Vector2(0.5f, 0.5f),
            100f);

        previewImagem.sprite = _spritePreviewRuntime;
        if (previewImagemGO != null) previewImagemGO.SetActive(true);
    }

    private void LimparPreviewRuntime()
    {
        if (previewImagem != null) previewImagem.sprite = null;
        if (_spritePreviewRuntime != null) Destroy(_spritePreviewRuntime);
        if (_texturaPreviewRuntime != null) Destroy(_texturaPreviewRuntime);
        _spritePreviewRuntime = null;
        _texturaPreviewRuntime = null;
    }

    // ── Toggles ───────────────────────────────────────────────────────────────

    private void ConstruirToggles()
    {
        if (togglesContainer != null)
            for (int i = togglesContainer.childCount - 1; i >= 0; i--)
                Destroy(togglesContainer.GetChild(i).gameObject);
        _toggles.Clear();

        foreach (IGrouping<string, XmlFieldDef> grupo in _catalog.GroupBy(f => f.Category))
        {
            if (prefabCategoryLabel != null && togglesContainer != null)
            {
                GameObject lbl = UIUtils.Spawn(prefabCategoryLabel, togglesContainer);
                TMP_Text   txt = lbl.GetComponentInChildren<TMP_Text>(true);
                if (txt != null) txt.text = grupo.Key;
            }

            foreach (XmlFieldDef def in grupo)
            {
                if (prefabFieldToggle == null || togglesContainer == null) continue;
                GameObject go = UIUtils.Spawn(prefabFieldToggle, togglesContainer);
                FieldToggleUI t = go.GetComponent<FieldToggleUI>();
                if (t == null) continue;
                t.Setup(def);
                _toggles.Add(t);
            }
        }
    }

    private List<XmlFieldDef> ObterCamposAtivos()
        => _toggles.Where(t => t != null && t.IsOn).Select(t => t.FieldDef).ToList();

    // ── Painéis (mutuamente exclusivos) ──────────────────────────────────────

    private void MostrarToggles()
    {
        SetAtivo(togglesPanel, true);
        SetAtivo(camposPanel, false);
    }

    private void MostrarCampos()
    {
        SetAtivo(togglesPanel, false);
        SetAtivo(camposPanel, true);
    }

    /// <summary>Carrega uma entrada já salva direto no painel de campos — chamado de fora (ver
    /// CampanhasManager.AbrirNpcParaEdicao) pelo botão "Editar" de um card da listagem separada ou
    /// pelo menu de atalho de um token no grid.</summary>
    public void EditarEntrada(EntryData data)
    {
        if (data == null) return;

        // Só os campos que pertencem ao catálogo desta tela (_catalog) viram Campos editáveis aqui.
        // Tudo mais (exceto Nome/Retrato, tratados à parte) é "extra" — fica fora da tela, mas
        // precisa sobreviver a um Salvar (ver _camposExtrasPreservados e SalvarTudo).
        var catalogIds = new HashSet<string>(_catalog.Select(c => c.FieldId));

        List<XmlFieldDef> campos = data.Fields
            .Select(fv => fv.Definition)
            .Where(def => def.FieldId != Dnd5eNpcFieldCatalog.NomeField.FieldId
                       && def.FieldId != Dnd5eNpcFieldCatalog.RetratoField.FieldId
                       && catalogIds.Contains(def.FieldId))
            .ToList();

        _camposExtrasPreservados.Clear();
        _camposExtrasPreservados.AddRange(data.Fields.Where(fv =>
            fv.Definition.FieldId != Dnd5eNpcFieldCatalog.NomeField.FieldId
            && fv.Definition.FieldId != Dnd5eNpcFieldCatalog.RetratoField.FieldId
            && !catalogIds.Contains(fv.Definition.FieldId)));

        var fieldIds = new HashSet<string>(campos.Select(c => c.FieldId));
        foreach (FieldToggleUI t in _toggles)
            if (t != null) t.SetOn(fieldIds.Contains(t.FieldDef.FieldId));

        ConstruirCampos(campos);

        foreach (FieldInputUI fi in _fieldInputs)
        {
            FieldValue match = data.Fields.Find(fv => fv.Definition.FieldId == fi.FieldDefId);
            if (match != null) fi.LoadData(match);
        }

        FieldValue nomeF = data.Fields.Find(fv => fv.Definition.FieldId == Dnd5eNpcFieldCatalog.NomeField.FieldId);
        if (inputNome != null) inputNome.text = nomeF != null ? nomeF.TextValue : "";

        FieldValue retratoF = data.Fields.Find(fv => fv.Definition.FieldId == Dnd5eNpcFieldCatalog.RetratoField.FieldId);
        _imagemSelecionadaPath = retratoF != null ? retratoF.TextValue : "";
        AtualizarPreviewImagem(_imagemSelecionadaPath);

        MostrarCampos();
    }

    // ── Campos (após confirmar os toggles) ───────────────────────────────────

    private void Continuar()
    {
        if (inputNome == null || string.IsNullOrWhiteSpace(inputNome.text))
        {
            VTTLog.ForjaError("Digite um nome antes de continuar.");
            return;
        }

        // Preserva valores já preenchidos para campos que continuam marcados após uma edição.
        EntryData dadosAnteriores = _fieldInputs.Count > 0 ? ColetarCamposAtuais() : null;

        ConstruirCampos(ObterCamposAtivos());

        if (dadosAnteriores != null)
            foreach (FieldInputUI fi in _fieldInputs)
            {
                FieldValue match = dadosAnteriores.Fields.Find(fv => fv.Definition.FieldId == fi.FieldDefId);
                if (match != null) fi.LoadData(match);
            }

        MostrarCampos();
    }

    private void ConstruirCampos(List<XmlFieldDef> campos)
    {
        if (fieldsContainer != null)
            for (int i = fieldsContainer.childCount - 1; i >= 0; i--)
                Destroy(fieldsContainer.GetChild(i).gameObject);
        _fieldInputs.Clear();

        foreach (XmlFieldDef def in campos)
        {
            if (prefabFieldInput == null || fieldsContainer == null) continue;
            GameObject go = UIUtils.Spawn(prefabFieldInput, fieldsContainer);
            FieldInputUI fi = go.GetComponent<FieldInputUI>();
            if (fi == null) continue;
            fi.Setup(def);
            _fieldInputs.Add(fi);
        }
    }

    private EntryData ColetarCamposAtuais()
    {
        EntryData data = new EntryData { TopicTag = _topicId };
        foreach (FieldInputUI fi in _fieldInputs)
            if (fi != null) data.Fields.Add(fi.GetValue());
        return data;
    }

    // ── Salvar ────────────────────────────────────────────────────────────────

    /// <param name="imagePersister">
    /// Opcional. Chamado com (entrada, campo) para o FieldValue de retrato antes de salvar, para
    /// copiar o arquivo de origem para o destino definitivo; o retorno substitui o TextValue salvo.
    /// </param>
    public void SalvarTudo(Func<EntryData, FieldValue, string> imagePersister = null)
    {
        // _topicId vira a tag raiz do XML (DynamicXmlBuilder.BuildDocument) — precisa ser um
        // identificador válido (sem espaço/acento), não o texto de um botão. Falha aqui com uma
        // mensagem clara em vez de deixar o XmlDocument estourar exceção mais abaixo.
        if (string.IsNullOrWhiteSpace(_topicId) || !Regex.IsMatch(_topicId, @"^[A-Za-z_][A-Za-z0-9_\-]*$"))
        {
            VTTLog.ForjaError($"Topic Id inválido: '{_topicId}'. Deve ser um identificador sem espaços/acentos " +
                "(ex: 'npcs') — confira o campo 'Topic Id' configurado no CampanhasManager.");
            return;
        }

        if (inputNome == null || string.IsNullOrWhiteSpace(inputNome.text))
        {
            VTTLog.ForjaError("Digite um nome antes de salvar.");
            return;
        }

        EntryData data = new EntryData { TopicTag = _topicId };
        data.Fields.Add(new FieldValue { Definition = Dnd5eNpcFieldCatalog.NomeField, TextValue = inputNome.text.Trim() });
        if (!string.IsNullOrWhiteSpace(_imagemSelecionadaPath))
            data.Fields.Add(new FieldValue { Definition = Dnd5eNpcFieldCatalog.RetratoField, TextValue = _imagemSelecionadaPath });

        foreach (FieldInputUI fi in _fieldInputs)
            if (fi != null) data.Fields.Add(fi.GetValue());

        // Recoloca campos fora do catálogo desta tela (ex: lore) que a entrada já tinha — sem isso,
        // salvar o stat block apagaria a lore, já que SaveEntriesAsXml reescreve o arquivo inteiro.
        data.Fields.AddRange(_camposExtrasPreservados);

        if (imagePersister != null)
            foreach (FieldValue fv in data.Fields)
                if (fv.Definition.Type == FieldType.Image && !string.IsNullOrWhiteSpace(fv.TextValue))
                    fv.TextValue = imagePersister(data, fv) ?? fv.TextValue;

        DynamicXmlBuilder.SaveEntriesAsXml(new List<EntryData> { data }, _topicFolderPath, _topicId);
        VTTLog.Forja($"Entrada de '{_topicId}' salva ({data.Fields.Count} campo(s)).");

        LimparFormulario();
        MostrarToggles();

        OnSaved?.Invoke();
    }

    // ── Reset de sessão ───────────────────────────────────────────────────────

    private void LimparFormulario()
    {
        if (inputNome != null) inputNome.text = "";
        _imagemSelecionadaPath = "";
        AtualizarPreviewImagem(null);

        foreach (FieldToggleUI t in _toggles)
            if (t != null) t.SetOn(false);

        if (fieldsContainer != null)
            for (int i = fieldsContainer.childCount - 1; i >= 0; i--)
                Destroy(fieldsContainer.GetChild(i).gameObject);
        _fieldInputs.Clear();
        _camposExtrasPreservados.Clear();
    }

    // ── Utilitários ───────────────────────────────────────────────────────────

    private static void SetAtivo(GameObject go, bool ativo)
    {
        if (go != null) go.SetActive(ativo);
    }

    private static void Religar(Button btn, Action acao)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => acao());
    }
}
