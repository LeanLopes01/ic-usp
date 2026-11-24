using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lista os NPCs já salvos da campanha aberta, num painel próprio e separado do painel de
/// criação/edição — mesmo padrão de Campanhas/Grid/CampanhaMapGaleriaUI.cs. "Editar" num card
/// devolve pra CampanhasManager.AbrirNpcParaEdicao, que reabre o tópico "npcs" já carregado com
/// essa entrada.
///
/// Ordenação (dropdown) e filtro de origem (chips, mesmo padrão de FiltroChipToggleUI usado em
/// Fichas/D&D/FichasVizualização/FichaGeralManager.cs pras abas de Ações/Magias/Habilidades/
/// Inventário) são aplicados sobre uma lista completa mantida em memória (_todosOsNpcs) — cada
/// mudança de filtro/ordenação só refiltra/reordena e reinstancia os cards, sem recarregar do disco.
///
/// Três origens: NPC criado à mão pelo tópico "npcs" da campanha, NPC importado (CampanhasManager.
/// ImportarNpcsDeCaminho, marcado via Dnd5eNpcFieldCatalog.OrigemField) e NPC do SISTEMA ativo da
/// campanha (CampanhaSistemaNpcLoader — hoje só quinta-edicao-srd tem pastas "monstros"/"criaturas"
/// no formato esperado).
/// </summary>
public class CampanhaNpcGaleriaUI : MonoBehaviour
{
    private enum OrigemNpc { Criado, Importado, Sistema }

    private class NpcListagemItem
    {
        public EntryData Dados;
        public OrigemNpc Origem;
        public string Nome;
        public float? Dificuldade;
        public float? Vida;
    }

    [SerializeField] private Transform content;
    [SerializeField] private GameObject prefabNpcCard; // com CampanhaNpcCardUI

    [Header("Ordenação")]
    [SerializeField] private TMP_Dropdown dropdownOrdenacao;

    [Header("Filtro de Origem (Criados / Importados / Sistema)")]
    [Tooltip("Mesma lógica de ConfigurarExpansaoFiltro em FichaGeralManager — painel começa colapsado, alterna com btnColapsarFiltrosOrigem.")]
    [SerializeField] private Button btnExpandirFiltrosOrigem;
    [SerializeField] private Button btnColapsarFiltrosOrigem;
    [SerializeField] private Transform conteudoFiltrosOrigem;
    [Tooltip("Mesmo prefab de filtro (toggle + texto) usado nas abas de FichaGeralManager — componente FiltroChipToggleUI.")]
    [SerializeField] private GameObject filtroChipPrefab;

    [Header("Paginação")]
    [Tooltip("Mesmo padrão de Fichas/D&D/CriarFicha/EquipmentSelector.cs (paginaPrefab/paginacaoContent) — prefab simples de TMP_Text com o número da página; se também tiver um Button, clicar nele navega direto pra ela.")]
    [SerializeField] private GameObject paginaPrefab;
    [SerializeField] private Transform paginacaoContent;

    private const int ItensPorPagina = 25;
    private int _paginaAtual;
    private readonly List<GameObject> _paginasInstanciadas = new List<GameObject>();

    private static readonly string[] OpcoesOrdenacao =
    {
        "Nome (A-Z)", "Nome (Z-A)",
        "Dificuldade (menor → maior)", "Dificuldade (maior → menor)",
        "Vida (menor → maior)", "Vida (maior → menor)",
    };
    private static readonly string[] TagsOrigem = { "Criados", "Importados", "Sistema" };

    private readonly List<GameObject> _instanciados = new List<GameObject>();
    private readonly List<GameObject> _chipsOrigemInstanciados = new List<GameObject>();
    private readonly HashSet<string> _origensAtivas = new HashSet<string>();
    private readonly List<NpcListagemItem> _todosOsNpcs = new List<NpcListagemItem>();

    private Action<EntryData>          _onEditar;
    private Action<EntryData, Vector2> _onDropNoGrid;
    private Action<EntryData>          _onAbrirLore;

    private void Awake()
    {
        ConfigurarDropdownOrdenacao();
        ConfigurarChipsDeOrigem();
    }

    /// <param name="folderPath">Pasta de NPCs da campanha (criados + importados).</param>
    /// <param name="sistemaFolderName">Pasta do sistema ativo da campanha (ex: "quinta-edicao-srd")
    /// — usada só pra buscar NPCs de sistema via CampanhaSistemaNpcLoader; passe null/vazio pra não
    /// listar nenhum.</param>
    /// <param name="onDropNoGrid">Opcional. Repassado a cada CampanhaNpcCardUI.Configurar — se
    /// informado, todo card (criado, importado OU de sistema) fica arrastável pro grid de battle map
    /// (ver CampanhaMapManager.TentarSoltarTokenNoMapa). Um token soltar a partir de um NPC de
    /// sistema sobrevive a um reload porque CampanhaMapManager.RestaurarTokens também resolve contra
    /// CampanhaSistemaNpcLoader (ver CampanhaMapManager.DefinirSistemaCampanha), não só a pasta de
    /// NPCs da própria campanha.</param>
    /// <param name="onAbrirLore">Opcional. Repassado a cada CampanhaNpcCardUI.Configurar — abre o
    /// painel de Lore separado do stat block (ver CampanhasManager.AbrirLoreDoNpc).</param>
    public void Carregar(string folderPath, string sistemaFolderName, Action<EntryData> onEditar,
        Action<EntryData, Vector2> onDropNoGrid = null, Action<EntryData> onAbrirLore = null)
    {
        _onEditar     = onEditar;
        _onDropNoGrid = onDropNoGrid;
        _onAbrirLore  = onAbrirLore;

        _todosOsNpcs.Clear();

        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            List<EntryData> npcs = DynamicXmlBuilder.LoadEntriesFromFolder(folderPath, "npcs", Dnd5eNpcFieldCatalog.AllFields);
            foreach (EntryData data in npcs)
                _todosOsNpcs.Add(CriarItem(data, NpcEntryFormatter.EhImportado(data) ? OrigemNpc.Importado : OrigemNpc.Criado));
        }

        if (!string.IsNullOrWhiteSpace(sistemaFolderName))
        {
            List<EntryData> npcsSistema = CampanhaSistemaNpcLoader.CarregarNpcsDoSistema(sistemaFolderName);
            foreach (EntryData data in npcsSistema)
                _todosOsNpcs.Add(CriarItem(data, OrigemNpc.Sistema));
        }

        AplicarFiltrosEOrdenacao();
    }

    private static NpcListagemItem CriarItem(EntryData data, OrigemNpc origem) => new NpcListagemItem
    {
        Dados       = data,
        Origem      = origem,
        Nome        = NpcEntryFormatter.ObterNome(data) ?? "",
        Dificuldade = NpcEntryFormatter.ObterDificuldadeNumerica(data),
        Vida        = NpcEntryFormatter.ObterVidaNumerica(data),
    };

    // ── Ordenação (dropdown) ─────────────────────────────────────────────────

    private void ConfigurarDropdownOrdenacao()
    {
        if (dropdownOrdenacao == null) return;

        dropdownOrdenacao.onValueChanged.RemoveAllListeners();
        dropdownOrdenacao.ClearOptions();
        dropdownOrdenacao.AddOptions(new List<string>(OpcoesOrdenacao));
        dropdownOrdenacao.SetValueWithoutNotify(0);
        dropdownOrdenacao.onValueChanged.AddListener(_ => AplicarFiltrosEOrdenacao());
    }

    private static void OrdenarLista(List<NpcListagemItem> itens, int modoIndice)
    {
        int PorNome(NpcListagemItem a, NpcListagemItem b) => string.Compare(a.Nome, b.Nome, StringComparison.OrdinalIgnoreCase);

        switch (modoIndice)
        {
            case 1: // Nome (Z-A)
                itens.Sort((a, b) => PorNome(b, a));
                break;
            case 2: // Dificuldade crescente
                itens.Sort((a, b) => { int c = Nullable.Compare(a.Dificuldade, b.Dificuldade); return c != 0 ? c : PorNome(a, b); });
                break;
            case 3: // Dificuldade decrescente
                itens.Sort((a, b) => { int c = Nullable.Compare(b.Dificuldade, a.Dificuldade); return c != 0 ? c : PorNome(a, b); });
                break;
            case 4: // Vida crescente
                itens.Sort((a, b) => { int c = Nullable.Compare(a.Vida, b.Vida); return c != 0 ? c : PorNome(a, b); });
                break;
            case 5: // Vida decrescente
                itens.Sort((a, b) => { int c = Nullable.Compare(b.Vida, a.Vida); return c != 0 ? c : PorNome(a, b); });
                break;
            default: // Nome (A-Z)
                itens.Sort(PorNome);
                break;
        }
    }

    // ── Filtro de origem (chips + colapsar/expandir, mesmo padrão de
    // FichaGeralManager.ConfigurarExpansaoFiltro) ────────────────────────────

    private void ConfigurarChipsDeOrigem()
    {
        PopularChipsDeOrigem();

        if (conteudoFiltrosOrigem != null) conteudoFiltrosOrigem.gameObject.SetActive(false);
        if (btnColapsarFiltrosOrigem != null) btnColapsarFiltrosOrigem.gameObject.SetActive(false);
        if (btnExpandirFiltrosOrigem != null) btnExpandirFiltrosOrigem.gameObject.SetActive(true);

        if (btnExpandirFiltrosOrigem != null)
        {
            btnExpandirFiltrosOrigem.onClick.RemoveAllListeners();
            btnExpandirFiltrosOrigem.onClick.AddListener(() =>
            {
                if (conteudoFiltrosOrigem != null) conteudoFiltrosOrigem.gameObject.SetActive(true);
                btnExpandirFiltrosOrigem.gameObject.SetActive(false);
                if (btnColapsarFiltrosOrigem != null) btnColapsarFiltrosOrigem.gameObject.SetActive(true);
            });
        }

        if (btnColapsarFiltrosOrigem != null)
        {
            btnColapsarFiltrosOrigem.onClick.RemoveAllListeners();
            btnColapsarFiltrosOrigem.onClick.AddListener(() =>
            {
                if (conteudoFiltrosOrigem != null) conteudoFiltrosOrigem.gameObject.SetActive(false);
                btnColapsarFiltrosOrigem.gameObject.SetActive(false);
                if (btnExpandirFiltrosOrigem != null) btnExpandirFiltrosOrigem.gameObject.SetActive(true);
            });
        }
    }

    private void PopularChipsDeOrigem()
    {
        foreach (GameObject go in _chipsOrigemInstanciados) if (go != null) Destroy(go);
        _chipsOrigemInstanciados.Clear();
        _origensAtivas.Clear();

        if (conteudoFiltrosOrigem == null || filtroChipPrefab == null) return;

        foreach (string tag in TagsOrigem)
        {
            GameObject go = Instantiate(filtroChipPrefab, conteudoFiltrosOrigem, false);
            go.SetActive(true);
            _chipsOrigemInstanciados.Add(go);

            FiltroChipToggleUI chip = go.GetComponent<FiltroChipToggleUI>();
            if (chip == null) continue;

            chip.Configurar(tag, (tagAlterada, isOn) =>
            {
                if (isOn) _origensAtivas.Add(tagAlterada);
                else _origensAtivas.Remove(tagAlterada);
                AplicarFiltrosEOrdenacao();
            });

            // Todas as origens começam ativas — nada some da lista até o usuário desmarcar algo.
            chip.toggle?.SetIsOnWithoutNotify(true);
            _origensAtivas.Add(tag);
        }
    }

    private bool PassaNoFiltro(NpcListagemItem item)
    {
        // Chips não ligados no Inspector (Count == 0): não filtra nada, mostra tudo — mesma
        // tolerância de referência-opcional já usada no resto do arquivo.
        if (_chipsOrigemInstanciados.Count == 0) return true;

        string tag = item.Origem switch
        {
            OrigemNpc.Criado    => "Criados",
            OrigemNpc.Importado => "Importados",
            OrigemNpc.Sistema   => "Sistema",
            _                   => "",
        };
        return _origensAtivas.Contains(tag);
    }

    // ── Render ────────────────────────────────────────────────────────────────

    // Ponto de entrada de qualquer mudança de FILTRO/ORDENAÇÃO (chip, dropdown, Carregar novo) — o
    // conjunto de resultados muda, então a página atual pode nem existir mais nele. Sempre volta pra
    // primeira página nesse caso (mesmo raciocínio de EquipmentSelector.RecarregarVisualizacaoCategoriasEItens).
    private void AplicarFiltrosEOrdenacao()
    {
        _paginaAtual = 0;
        RenderizarPaginaAtual();
    }

    private void RenderizarPaginaAtual()
    {
        Limpar();
        if (content == null || prefabNpcCard == null) return;

        List<NpcListagemItem> visiveis = _todosOsNpcs.FindAll(PassaNoFiltro);
        OrdenarLista(visiveis, dropdownOrdenacao != null ? dropdownOrdenacao.value : 0);

        int totalPaginas = Mathf.Max(1, Mathf.CeilToInt(visiveis.Count / (float)ItensPorPagina));
        _paginaAtual = Mathf.Clamp(_paginaAtual, 0, totalPaginas - 1);

        int indiceInicial = _paginaAtual * ItensPorPagina;
        int quantidadeNestaPagina = Mathf.Clamp(visiveis.Count - indiceInicial, 0, ItensPorPagina);

        for (int i = 0; i < quantidadeNestaPagina; i++)
        {
            NpcListagemItem item = visiveis[indiceInicial + i];

            GameObject go = UIUtils.Spawn(prefabNpcCard, content);
            _instanciados.Add(go);

            CampanhaNpcCardUI card = go.GetComponent<CampanhaNpcCardUI>();
            card?.Configurar(item.Dados, _onEditar, _onDropNoGrid, _onAbrirLore);
        }

        AtualizarPaginacao(visiveis.Count, totalPaginas);
    }

    /// <summary>Recria os indicadores de página (mesmo padrão de EquipmentSelector.
    /// AtualizarPaginacaoCatalogo) — um por página, numerados a partir de 1, a atual em negrito/
    /// sublinhada. Se paginaPrefab tiver um Button, clicar nele chama IrParaPagina.</summary>
    private void AtualizarPaginacao(int totalItens, int totalPaginas)
    {
        foreach (GameObject go in _paginasInstanciadas) if (go != null) Destroy(go);
        _paginasInstanciadas.Clear();

        if (paginacaoContent == null || paginaPrefab == null) return;
        // Nada pra paginar (lista vazia pro filtro atual) — não mostra nem a página "1" vazia.
        if (totalItens == 0) return;

        for (int p = 0; p < totalPaginas; p++)
        {
            int paginaCapturada = p;
            GameObject go = Instantiate(paginaPrefab, paginacaoContent, false);
            go.SetActive(true);
            _paginasInstanciadas.Add(go);

            TMP_Text texto = go.GetComponentInChildren<TMP_Text>();
            if (texto != null)
                texto.text = (p == _paginaAtual) ? $"<b><u>{p + 1}</u></b>" : (p + 1).ToString();

            Button botao = go.GetComponentInChildren<Button>();
            if (botao != null)
            {
                botao.onClick.RemoveAllListeners();
                botao.onClick.AddListener(() => IrParaPagina(paginaCapturada));
            }
        }
    }

    /// <summary>Navega pra uma página específica (0-indexada) — chamado pelo clique nos indicadores
    /// instanciados por AtualizarPaginacao.</summary>
    public void IrParaPagina(int pagina)
    {
        if (pagina == _paginaAtual) return;
        _paginaAtual = pagina;
        RenderizarPaginaAtual();
    }

    private void Limpar()
    {
        foreach (GameObject go in _instanciados)
            if (go != null) Destroy(go);
        _instanciados.Clear();
    }
}
