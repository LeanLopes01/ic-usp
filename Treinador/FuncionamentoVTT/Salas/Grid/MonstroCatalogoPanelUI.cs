using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MonstroCatalogoPanelUI : MonoBehaviour
{
    private const string SISTEMA_D5E = "quinta-edicao-srd";
    private const int LOTE = 8;
    private const int MAX_RENDERIZADOS = 24; // 3 lotes

    [Header("Painel")]
    [SerializeField] private GameObject painel;
    [SerializeField] private Button botaoFechar;

    [Header("Adicionar ao Tabuleiro")]
    [SerializeField] private MapManager mapManager;

    [Header("Filtros")]
    [SerializeField] private TMP_InputField campoPesquisa;
    [SerializeField] private TMP_Dropdown dropdownCategoria;

    [Header("Lista")]
    [SerializeField] private ScrollRect scrollView;
    [SerializeField] private Transform contentListagem;
    [SerializeField] private GameObject prefabItemMonstro;
    [SerializeField] private float alturaEstimadaItem = 85f;

    [Header("Spacers (opcional — arraste LayoutElements da cena se quiser scrollbar precisa)")]
    [SerializeField] private LayoutElement espacadorTopo;
    [SerializeField] private LayoutElement espacadorFundo;

    // Dados carregados
    private string _sistemaSalaAtual;
    private readonly List<MonstroDado> _todosMonstros = new List<MonstroDado>();
    private readonly List<string> _nomesNormalizados = new List<string>(); // pré-calculado para busca rápida
    private readonly List<string> _todasCategorias = new List<string>();
    private readonly Dictionary<string, Sprite> _spritesTokens = new Dictionary<string, Sprite>();
    private readonly List<Texture2D> _texturasCarregadas = new List<Texture2D>();
    private bool _carregado;
    private Coroutine _corotinaCarregamento;

    // Estado do virtual scroll
    private readonly List<MonstroDado> _filtrados = new List<MonstroDado>();
    private readonly List<MonstroCatalogoItemUI> _itensVisiveis = new List<MonstroCatalogoItemUI>();
    private int _vInicio;
    private int _vFim;
    private bool _ignorarScroll;
    private bool _carregandoLote;
    private Coroutine _corotinaDebounce;

    private void Awake()
    {
        if (painel != null) painel.SetActive(false);

        if (botaoFechar != null)
        {
            botaoFechar.onClick.RemoveAllListeners();
            botaoFechar.onClick.AddListener(FecharPainel);
        }

        if (campoPesquisa != null)
        {
            campoPesquisa.onValueChanged.RemoveAllListeners();
            campoPesquisa.onValueChanged.AddListener(_ => AgendarFiltro());
        }

        if (dropdownCategoria != null)
        {
            dropdownCategoria.onValueChanged.RemoveAllListeners();
            dropdownCategoria.onValueChanged.AddListener(_ => AplicarFiltros());
        }

        if (scrollView != null)
        {
            scrollView.onValueChanged.RemoveAllListeners();
            scrollView.onValueChanged.AddListener(OnScroll);
        }
    }

    private void OnDestroy()
    {
        LiberarTexturas();
    }

    // ── API pública ──────────────────────────────────────────────────────────

    public void DefinirSistemaSala(string sistemaSala)
    {
        _sistemaSalaAtual = sistemaSala;
        _carregado = false;
        LimparEstado();
    }

    public bool EstaAbertoPainel() => painel != null && painel.activeSelf;

    public void AbrirPainel()
    {
        Debug.Log($"[MonstroCatalogo] AbrirPainel — _sistemaSalaAtual='{_sistemaSalaAtual}' esperado='{SISTEMA_D5E}'");
        if (_sistemaSalaAtual != SISTEMA_D5E)
        {
            Debug.LogWarning($"[MonstroCatalogo] Sistema '{_sistemaSalaAtual}' não é D&D 5ª Ed. — catálogo indisponível.");
            return;
        }

        if (painel != null) painel.SetActive(true);

        if (!_carregado && _corotinaCarregamento == null)
            _corotinaCarregamento = StartCoroutine(CarregarTudo());
        else if (_carregado && _itensVisiveis.Count == 0)
            AplicarFiltros();
    }

    public void FecharPainel()
    {
        if (painel != null) painel.SetActive(false);
    }

    public void TogglePainel()
    {
        if (EstaAbertoPainel()) FecharPainel();
        else AbrirPainel();
    }

    // ── Carregamento ─────────────────────────────────────────────────────────

    private IEnumerator CarregarTudo()
    {
        string basePath    = Path.Combine(Application.persistentDataPath, "sistemas", SISTEMA_D5E);
        string pastaTokens = Path.Combine(basePath, "tokens");

        string[] pastasFonte =
        {
            Path.Combine(basePath, "criaturas"),
            Path.Combine(basePath, "monstros")
        };

        // Coleta todos os XMLs das duas pastas, sem duplicatas por nome de arquivo
        var arquivosSet  = new HashSet<string>();
        var arquivosList = new List<string>();
        foreach (string pasta in pastasFonte)
        {
            if (!Directory.Exists(pasta)) continue;
            foreach (string arq in ImportadorConteudoUniversal.EnumerarArquivosXmlEJson(pasta))
            {
                string nomeArq = Path.GetFileName(arq);
                if (arquivosSet.Add(nomeArq))
                    arquivosList.Add(arq);
            }
        }

        if (arquivosList.Count == 0)
        {
            Debug.LogWarning($"[MonstroCatalogo] Nenhum XML/JSON encontrado em 'criaturas' nem em 'monstros' dentro de: {basePath}");
            _corotinaCarregamento = null;
            yield break;
        }

        int totalArquivos = arquivosList.Count;

        if (PopupManager.Instance == null)
            Debug.LogWarning("[MonstroCatalogo] PopupManager.Instance é null — popup não será exibido. Verifique se o PopupManager está na cena.");
        PopupManager.Instance?.MostrarCarregamento("Carregando Catálogo", "Lendo criaturas…");

        bool carregouComSucesso = false;
        try
        {
            for (int idx = 0; idx < arquivosList.Count; idx++)
            {
                MonstroDado dado = ParsearCreatura(arquivosList[idx]);
                if (dado == null) continue;

                _todosMonstros.Add(dado);
                _nomesNormalizados.Add(NormalizarTexto(dado.nome) + " " + NormalizarTexto(dado.meta));
                foreach (string cat in dado.categorias)
                    if (!_todasCategorias.Contains(cat))
                        _todasCategorias.Add(cat);

                float prog = (idx + 1f) / totalArquivos * 0.6f;
                PopupManager.Instance?.AtualizarProgresso(prog, $"Carregando: {idx + 1}/{totalArquivos}");
            }

            _todasCategorias.Sort(System.StringComparer.CurrentCultureIgnoreCase);
            carregouComSucesso = true;
        }
        finally
        {
            if (!carregouComSucesso)
            {
                PopupManager.Instance?.Fechar();
                _corotinaCarregamento = null;
            }
        }

        // Tokens (fase assíncrona separada — sem try/finally pois yield não é permitido dentro)
        if (Directory.Exists(pastaTokens))
        {
            int totalTokens = _todosMonstros.Count;
            int tokenIdx = 0;
            foreach (MonstroDado dado in _todosMonstros)
            {
                tokenIdx++;
                string caminho = Path.Combine(pastaTokens, dado.id + ".png");
                if (!File.Exists(caminho)) { yield return null; continue; }

                byte[] bytes = File.ReadAllBytes(caminho);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(bytes))
                {
                    Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                    _spritesTokens[dado.id] = sprite;
                    _texturasCarregadas.Add(tex);
                }
                else Destroy(tex);

                float prog = 0.6f + tokenIdx / (float)totalTokens * 0.4f;
                PopupManager.Instance?.AtualizarProgresso(prog, $"Tokens: {tokenIdx}/{totalTokens}");
                yield return null;
            }
        }

        PopupManager.Instance?.Fechar();
        PopularDropdownCategorias();
        _carregado = true;
        _corotinaCarregamento = null;
        AplicarFiltros();
    }

    // ── Virtual Scroll ────────────────────────────────────────────────────────

    private void IniciarVirtualScroll(List<MonstroDado> lista)
    {
        _filtrados.Clear();
        _filtrados.AddRange(lista);
        _carregandoLote = false;

        LimparItens();
        _vInicio = 0;
        _vFim    = 0;

        AtualizarEspacadores();

        _ignorarScroll = true;
        if (scrollView != null) scrollView.verticalNormalizedPosition = 1f;
        _ignorarScroll = false;

        CarregarProxima();
    }

    private void CarregarProxima()
    {
        if (_carregandoLote || _vFim >= _filtrados.Count) return;

        _carregandoLote = true;
        int novoFim = Mathf.Min(_vFim + LOTE, _filtrados.Count);
        for (int i = _vFim; i < novoFim; i++)
            AdicionarItemFundo(_filtrados[i]);
        _vFim = novoFim;

        DescartarExcedenteTopo();
        AtualizarEspacadores();
        _carregandoLote = false;
    }

    private void CarregarAnterior()
    {
        if (_carregandoLote || _vInicio <= 0) return;

        _carregandoLote = true;
        int novoInicio = Mathf.Max(0, _vInicio - LOTE);
        for (int i = _vInicio - 1; i >= novoInicio; i--)
            AdicionarItemTopo(_filtrados[i]);
        _vInicio = novoInicio;

        DescartarExcedenteFundo();
        AtualizarEspacadores();
        _carregandoLote = false;
    }

    private void AdicionarItemFundo(MonstroDado dado)
    {
        if (prefabItemMonstro == null || contentListagem == null) return;
        GameObject go = UIUtils.Spawn(prefabItemMonstro, contentListagem);
        // Antes do SpacerFundo (se existir), senão vai para o final
        if (espacadorFundo != null)
            go.transform.SetSiblingIndex(espacadorFundo.transform.GetSiblingIndex());
        MonstroCatalogoItemUI item = go.GetComponent<MonstroCatalogoItemUI>();
        if (item == null) return;
        _spritesTokens.TryGetValue(dado.id, out Sprite token);
        item.Setup(dado, token, mapManager);
        _itensVisiveis.Add(item);
    }

    private void AdicionarItemTopo(MonstroDado dado)
    {
        if (prefabItemMonstro == null || contentListagem == null) return;
        GameObject go = UIUtils.Spawn(prefabItemMonstro, contentListagem);
        // Depois do SpacerTopo (se existir), senão vai para o início
        if (espacadorTopo != null)
            go.transform.SetSiblingIndex(espacadorTopo.transform.GetSiblingIndex() + 1);
        else
            go.transform.SetAsFirstSibling();
        MonstroCatalogoItemUI item = go.GetComponent<MonstroCatalogoItemUI>();
        if (item == null) return;
        _spritesTokens.TryGetValue(dado.id, out Sprite token);
        item.Setup(dado, token, mapManager);
        _itensVisiveis.Insert(0, item);
    }

    private void DescartarExcedenteTopo()
    {
        while (_itensVisiveis.Count > MAX_RENDERIZADOS)
        {
            Destroy(_itensVisiveis[0].gameObject);
            _itensVisiveis.RemoveAt(0);
            _vInicio++;
        }
    }

    private void DescartarExcedenteFundo()
    {
        while (_itensVisiveis.Count > MAX_RENDERIZADOS)
        {
            int ultimo = _itensVisiveis.Count - 1;
            Destroy(_itensVisiveis[ultimo].gameObject);
            _itensVisiveis.RemoveAt(ultimo);
            _vFim--;
        }
    }

    private void AtualizarEspacadores()
    {
        if (espacadorTopo  != null) espacadorTopo.preferredHeight  = _vInicio                   * alturaEstimadaItem;
        if (espacadorFundo != null) espacadorFundo.preferredHeight = (_filtrados.Count - _vFim) * alturaEstimadaItem;
        Canvas.ForceUpdateCanvases();
    }

    private void OnScroll(Vector2 pos)
    {
        if (_ignorarScroll || _carregandoLote) return;
        // pos.y: 1 = topo, 0 = fundo
        if (pos.y < 0.15f) CarregarProxima();
        else if (pos.y > 0.85f) CarregarAnterior();
    }

    // ── Filtros ───────────────────────────────────────────────────────────────

    private void PopularDropdownCategorias()
    {
        if (dropdownCategoria == null) return;
        dropdownCategoria.ClearOptions();
        var opcoes = new List<TMP_Dropdown.OptionData> { new TMP_Dropdown.OptionData("Todas") };
        foreach (string cat in _todasCategorias)
            opcoes.Add(new TMP_Dropdown.OptionData(cat));
        dropdownCategoria.AddOptions(opcoes);
        dropdownCategoria.value = 0;
    }

    private void AgendarFiltro()
    {
        if (_corotinaDebounce != null) StopCoroutine(_corotinaDebounce);
        _corotinaDebounce = StartCoroutine(DebounceAplicarFiltros());
    }

    private IEnumerator DebounceAplicarFiltros()
    {
        yield return new WaitForSeconds(0.2f);
        AplicarFiltros();
        _corotinaDebounce = null;
    }

    private void AplicarFiltros()
    {
        if (!_carregado) return;

        string pesquisa = campoPesquisa != null
            ? NormalizarTexto(campoPesquisa.text.Trim())
            : string.Empty;

        int catIdx = dropdownCategoria != null ? dropdownCategoria.value : 0;
        string categoriaFiltro = catIdx > 0 ? _todasCategorias[catIdx - 1] : string.Empty;

        var resultado = new List<MonstroDado>();
        for (int i = 0; i < _todosMonstros.Count; i++)
        {
            if (!string.IsNullOrEmpty(pesquisa))
            {
                bool nomeOk = _nomesNormalizados[i].Contains(pesquisa);
                bool catOk  = false;
                if (!nomeOk)
                    foreach (string c in _todosMonstros[i].categorias)
                        if (NormalizarTexto(c).Contains(pesquisa)) { catOk = true; break; }
                if (!nomeOk && !catOk) continue;
            }
            if (!string.IsNullOrEmpty(categoriaFiltro) && !_todosMonstros[i].categorias.Contains(categoriaFiltro)) continue;
            resultado.Add(_todosMonstros[i]);
        }

        IniciarVirtualScroll(resultado);
    }

    private static string NormalizarTexto(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return string.Empty;
        string d = texto.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (char c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString().ToLowerInvariant();
    }

    // ── Limpeza ───────────────────────────────────────────────────────────────

    private void LimparItens()
    {
        foreach (MonstroCatalogoItemUI item in _itensVisiveis)
            if (item != null) Destroy(item.gameObject);
        _itensVisiveis.Clear();
    }

    private void LimparEstado()
    {
        if (_corotinaCarregamento != null) { StopCoroutine(_corotinaCarregamento); _corotinaCarregamento = null; PopupManager.Instance?.Fechar(); }
        if (_corotinaDebounce    != null) { StopCoroutine(_corotinaDebounce);    _corotinaDebounce    = null; }

        _carregandoLote = false;
        LimparItens();
        _todosMonstros.Clear();
        _nomesNormalizados.Clear();
        _todasCategorias.Clear();
        _filtrados.Clear();
        LiberarTexturas();
        _spritesTokens.Clear();

        if (dropdownCategoria != null) dropdownCategoria.ClearOptions();
        if (campoPesquisa     != null) campoPesquisa.text = string.Empty;
    }

    private void LiberarTexturas()
    {
        foreach (Texture2D tex in _texturasCarregadas)
            if (tex != null) Destroy(tex);
        _texturasCarregadas.Clear();
    }

    // ── Parser XML ────────────────────────────────────────────────────────────

    private static MonstroDado ParsearCreatura(string caminho)
    {
        try
        {
            XElement root;
            if (ImportadorConteudoUniversal.EhArquivoJson(caminho))
            {
                string json = ImportadorConteudoUniversal.LerTextoComFallbackDeEncoding(caminho);
                string xml = json != null ? ImportadorConteudoUniversal.ConverterJsonParaXml(json, "monster") : null;
                if (xml == null)
                {
                    Debug.LogWarning($"[MonstroCatalogo] JSON inválido em '{Path.GetFileName(caminho)}' — ignorando.");
                    return null;
                }
                root = XElement.Parse(xml);
            }
            else
            {
                root = XElement.Load(caminho);
            }
            MonstroDado dado = new MonstroDado
            {
                id   = (string)root.Attribute("id") ?? Path.GetFileNameWithoutExtension(caminho),
                nome = root.Element("name")?.Value  ?? Path.GetFileNameWithoutExtension(caminho),
                meta = root.Element("meta")?.Value  ?? string.Empty
            };

            XElement cats = root.Element("categories");
            if (cats != null)
                foreach (XElement cat in cats.Elements("category"))
                    if (!string.IsNullOrWhiteSpace(cat.Value))
                        dado.categorias.Add(cat.Value.Trim());

            XElement stats = root.Element("stats");
            if (stats != null)
            {
                dado.ca         = stats.Element("armor-class")?.Value ?? string.Empty;
                dado.hp         = stats.Element("hit-points")?.Value  ?? string.Empty;
                dado.velocidade = stats.Element("speed")?.Value       ?? string.Empty;
            }

            XElement attrs = root.Element("attributes");
            if (attrs != null)
            {
                foreach (XElement attr in attrs.Elements("attribute"))
                {
                    string attrName = ((string)attr.Attribute("name") ?? string.Empty).ToUpperInvariant();
                    string val      = attr.Value ?? string.Empty;
                    switch (attrName)
                    {
                        case "STR": dado.str   = val; break;
                        case "DEX": dado.dex   = val; break;
                        case "CON": dado.con   = val; break;
                        case "INT": dado.intel = val; break;
                        case "WIS": dado.wis   = val; break;
                        case "CHA": dado.cha   = val; break;
                    }
                }
            }

            XElement details = root.Element("details");
            if (details != null)
            {
                dado.pericias = details.Element("skills")?.Value    ?? string.Empty;
                dado.sentidos = details.Element("senses")?.Value    ?? string.Empty;
                dado.idiomas  = details.Element("languages")?.Value ?? string.Empty;
                dado.desafio  = details.Element("challenge")?.Value ?? string.Empty;
            }

            XElement actions = root.Element("actions");
            if (actions != null)
            {
                foreach (XElement action in actions.Elements("action"))
                {
                    dado.acoes.Add(new MonstroAcao
                    {
                        nome      = action.Element("name")?.Value        ?? string.Empty,
                        descricao = action.Element("description")?.Value ?? string.Empty
                    });
                }
            }

            return dado;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MonstroCatalogo] Erro ao parsear '{Path.GetFileName(caminho)}': {e.Message}");
            return null;
        }
    }
}
