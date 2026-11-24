using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Card de um NPC na listagem de CampanhaTopicEditorUI — cabeçalho com token+nome sempre visível,
/// conteúdo expansível com todos os campos possíveis do catálogo (ver Dnd5eNpcFieldCatalog),
/// mostrando só os que essa entrada realmente tem. Expandir/colapsar segue o mesmo padrão de
/// Fichas/D&D/FichasVizualização/UIs gerais/HabilidadesItemUI.cs (dois botões que se alternam +
/// UIAnimacao.AnimarExpansao).
///
/// Todo conteúdo aqui é somente leitura — para editar, o botão "Editar" devolve o EntryData
/// completo para quem chamou Configurar (ver CampanhaTopicEditorUI.EditarEntrada), que leva o
/// usuário de volta ao painel de campos preenchível.
///
/// Os detalhes expandidos são 100% dinâmicos: pra cada campo de Dnd5eNpcFieldCatalog.Fields que a
/// entrada realmente tem, spawna um prefabTextoItem (CampanhaNpcCardTextoItemUI, só título + corpo)
/// dentro de camposContent — sem linha fixa por campo no Inspector, já que aqui é só listar dado
/// (ver NpcEntryFormatter pra formatação de valor único vs. lista).
///
/// Também é arrastável pro grid de battle map (ver CampanhaMapManager) — mesmo padrão de
/// Salas/Grid/TokenButtonUI.cs (arrasta o ícone em screen-space, e só no OnEndDrag delega a
/// posição de soltura pra quem configurou o card via onDropNoGrid).
/// </summary>
public class CampanhaNpcCardUI : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("Cabeçalho")]
    [SerializeField] private Image      token;
    [SerializeField] private Sprite     tokenFallback;
    [SerializeField] private TMP_Text   nomeText;
    [SerializeField] private Button     botaoEditar;
    [SerializeField] private Button     botaoLore;

    [Header("Controles de Expansão")]
    [SerializeField] private Button     btnExpandirDetalhes;
    [SerializeField] private Button     btnColapsarDetalhes;
    [SerializeField] private GameObject conteudoDetalhes;

    [Header("Campos (um item por campo presente, título + corpo)")]
    [SerializeField] private Transform camposContent;

    [Header("Prefab (reaproveitado para todo campo — título + corpo do texto)")]
    [Tooltip("GameObject com CampanhaNpcCardTextoItemUI — um item por campo do catálogo que a entrada tiver preenchido.")]
    [SerializeField] private GameObject prefabTextoItem;

    private EntryData         _dados;
    private Action<EntryData> _onEditar;
    private Action<EntryData, Vector2> _onDropNoGrid;
    private Action<EntryData> _onAbrirLore;
    private Texture2D         _texturaCarregada;
    private Sprite            _spriteCarregado;

    private RectTransform _rectTransform;
    private CanvasGroup   _canvasGroup;
    private Canvas        _canvasRaiz;
    private Vector2       _posicaoOriginal;

    private void Awake()
    {
        _rectTransform = transform as RectTransform;
        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null) _canvasGroup = gameObject.AddComponent<CanvasGroup>();
        _canvasRaiz = GetComponentInParent<Canvas>();
    }

    // ── Setup ─────────────────────────────────────────────────────────────────

    /// <param name="onDropNoGrid">Opcional. Se informado, o card fica arrastável pro grid de battle
    /// map — chamado com (dados do NPC, posição de tela) ao soltar (ver CampanhaMapManager.TentarSoltarTokenNoMapa).</param>
    /// <param name="onAbrirLore">Opcional. Chamado com os dados do NPC ao clicar no botão "Lore" —
    /// abre o painel de lore separado do stat block (ver CampanhasManager.AbrirLoreDoNpc).</param>
    public void Configurar(EntryData data, Action<EntryData> onEditar, Action<EntryData, Vector2> onDropNoGrid = null,
        Action<EntryData> onAbrirLore = null)
    {
        _dados = data;
        _onEditar = onEditar;
        _onDropNoGrid = onDropNoGrid;
        _onAbrirLore = onAbrirLore;
        if (data == null) return;

        var camposPresentes = new Dictionary<string, FieldValue>();
        foreach (FieldValue fv in data.Fields)
            if (fv?.Definition != null) camposPresentes[fv.Definition.FieldId] = fv;

        string nome = NpcEntryFormatter.ObterNome(data);
        if (nomeText != null) nomeText.text = string.IsNullOrWhiteSpace(nome) ? "NPC sem nome" : nome;

        CarregarToken(NpcEntryFormatter.ObterRetratoPath(data));
        PreencherCampos(camposPresentes);

        ConfigurarExpansao();
        Religar(botaoEditar, () => _onEditar?.Invoke(_dados));
        Religar(botaoLore, () => _onAbrirLore?.Invoke(_dados));
    }

    private void OnDestroy() => LimparTokenRuntime();

    // ── Arrastar pro grid ────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (_onDropNoGrid == null || _rectTransform == null) return;
        _posicaoOriginal = _rectTransform.anchoredPosition;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.alpha = 0.7f;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (_onDropNoGrid == null || _rectTransform == null || _canvasRaiz == null) return;
        float escala = Mathf.Max(0.0001f, _canvasRaiz.scaleFactor);
        _rectTransform.anchoredPosition += eventData.delta / escala;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_onDropNoGrid == null || _rectTransform == null) return;
        _rectTransform.anchoredPosition = _posicaoOriginal;
        _canvasGroup.blocksRaycasts = true;
        _canvasGroup.alpha = 1f;
        _onDropNoGrid.Invoke(_dados, eventData.position);
    }

    // ── Expansão (mesmo padrão de HabilidadesItemUI) ────────────────────────────

    private void ConfigurarExpansao()
    {
        RectTransform conteudoRect = conteudoDetalhes != null ? conteudoDetalhes.GetComponent<RectTransform>() : null;
        RectTransform listaParaReconstruir = transform.parent as RectTransform;

        if (conteudoDetalhes != null) conteudoDetalhes.SetActive(false);
        if (btnColapsarDetalhes != null) btnColapsarDetalhes.gameObject.SetActive(false);
        if (btnExpandirDetalhes != null) btnExpandirDetalhes.gameObject.SetActive(true);

        if (btnExpandirDetalhes != null)
        {
            btnExpandirDetalhes.onClick.RemoveAllListeners();
            btnExpandirDetalhes.onClick.AddListener(() =>
            {
                btnExpandirDetalhes.gameObject.SetActive(false);
                if (btnColapsarDetalhes != null) btnColapsarDetalhes.gameObject.SetActive(true);
                UIAnimacao.AnimarExpansao(this, conteudoRect, true, listaParaReconstruir);
            });
        }

        if (btnColapsarDetalhes != null)
        {
            btnColapsarDetalhes.onClick.RemoveAllListeners();
            btnColapsarDetalhes.onClick.AddListener(() =>
            {
                btnColapsarDetalhes.gameObject.SetActive(false);
                if (btnExpandirDetalhes != null) btnExpandirDetalhes.gameObject.SetActive(true);
                UIAnimacao.AnimarExpansao(this, conteudoRect, false, listaParaReconstruir);
            });
        }
    }

    // ── Token (imagem) ───────────────────────────────────────────────────────

    private void CarregarToken(string imagePath)
    {
        if (token == null) return;
        LimparTokenRuntime();

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            DefinirTokenFallback();
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
                DefinirTokenFallback();
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
            Debug.LogWarning($"[CampanhaNpcCardUI] Falha ao carregar token '{imagePath}': {e.Message}");
            DefinirTokenFallback();
        }
    }

    private void DefinirTokenFallback()
    {
        if (token == null) return;
        token.sprite = tokenFallback;
        token.preserveAspect = true;
        token.color = Color.white;
    }

    private void LimparTokenRuntime()
    {
        if (token != null) token.sprite = null;
        if (_spriteCarregado != null) Destroy(_spriteCarregado);
        if (_texturaCarregada != null) Destroy(_texturaCarregada);
        _spriteCarregado = null;
        _texturaCarregada = null;
    }

    // ── Campos (ver NpcEntryFormatter) ───────────────────────────────────────

    // Um prefabTextoItem por campo do catálogo que a entrada tem preenchido — título = Label do
    // campo, corpo = valor único formatado (Text/Number/UnitValue/Group) ou as linhas de uma lista
    // (StringList/ObjectList) juntas numa só caixa de texto.
    private void PreencherCampos(Dictionary<string, FieldValue> camposPresentes)
    {
        if (camposContent == null) return;
        for (int i = camposContent.childCount - 1; i >= 0; i--)
            Destroy(camposContent.GetChild(i).gameObject);

        if (prefabTextoItem == null) return;

        foreach (XmlFieldDef campo in Dnd5eNpcFieldCatalog.Fields)
        {
            if (!camposPresentes.TryGetValue(campo.FieldId, out FieldValue fv)) continue;
            if (!NpcEntryFormatter.TemValor(fv)) continue;

            string corpo = campo.Type == FieldType.StringList || campo.Type == FieldType.ObjectList
                ? string.Join("\n", NpcEntryFormatter.ObterLinhas(fv))
                : NpcEntryFormatter.FormatarValorTexto(fv);
            if (string.IsNullOrWhiteSpace(corpo)) continue;

            GameObject go = UIUtils.Spawn(prefabTextoItem, camposContent);
            go.GetComponent<CampanhaNpcCardTextoItemUI>()?.Configurar(campo.Label, corpo);
        }
    }

    // ── Utilitários ───────────────────────────────────────────────────────────

    private static void Religar(Button btn, Action acao)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() => acao());
    }
}
