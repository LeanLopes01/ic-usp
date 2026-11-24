using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Lista paginada das campanhas salvas localmente, na tela inicial — mesmo padrão de
/// Fichas/Geral/CarregarFichas/FichaLoaderUI.cs e TelaInicial/Sistemas/SistemasListManager.cs (prefab
/// de item + IndicadorPaginasUI + botões anterior/próximo). Clicar num item navega direto pra tela de
/// campanha (mesmo padrão de SistemasListManager.OnClicarSistema — NavegarParaTela, não
/// GameObject.SetActive direto, ver comentário em ToggleObjects.NavegarParaTela). Só lê e exibe:
/// criação continua em CampanhasCreationUI, que também assina OnCampanhaClicada (pra popular
/// CampanhasManager com os dados da campanha) e chama RecarregarLista() depois de criar uma nova.
/// </summary>
public class CampanhaLoaderUI : MonoBehaviour
{
    [Header("UI / Prefab")]
    [SerializeField] private Transform contentParent;
    [SerializeField] private GameObject campanhaPrefab; // precisa ter CampanhaListaItemUI

    [Header("Paginação")]
    [SerializeField] private IndicadorPaginasUI indicadorPaginas;
    [SerializeField, Min(1)] private int itensPorPagina = 9;
    [SerializeField] private Button botaoAnterior;
    [SerializeField] private Button botaoProximo;

    [Header("Navegação")]
    [SerializeField] private ToggleObjects toggleObjects;
    [Tooltip("Tela onde a campanha selecionada é aberta (CampanhasManager vive nela).")]
    [SerializeField] private GameObject telaCampanha;

    /// <summary>Disparado quando o usuário clica pra abrir uma campanha da lista (depois da
    /// navegação já ter acontecido).</summary>
    public event Action<CampanhaLocalSnapshot> OnCampanhaClicada;

    private readonly List<GameObject> itensInstanciados = new List<GameObject>();
    private CampanhaLocalSnapshot[] todasCampanhas = Array.Empty<CampanhaLocalSnapshot>();
    private int paginaAtual;

    private void Awake()
    {
        if (toggleObjects == null)
            toggleObjects = FindFirstObjectByType<ToggleObjects>();
    }

    private void OnEnable()
    {
        botaoAnterior?.onClick.AddListener(PaginaAnterior);
        botaoProximo?.onClick.AddListener(ProximaPagina);

        RecarregarLista();
    }

    private void OnDisable()
    {
        botaoAnterior?.onClick.RemoveListener(PaginaAnterior);
        botaoProximo?.onClick.RemoveListener(ProximaPagina);
    }

    private void OnDestroy()
    {
        LimparListaVisual();
    }

    /// <summary>Relê o disco e recria os itens a partir da primeira página — chamado ao entrar na
    /// tela (OnEnable) e depois de criar uma campanha nova (ver CampanhasCreationUI).</summary>
    public void RecarregarLista()
    {
        CampanhaLocalSnapshot[] snapshots = CampanhasLocalStorage.LoadAll();
        OrdenarPorRecencia(snapshots);
        DefinirCampanhas(snapshots);
    }

    private void DefinirCampanhas(CampanhaLocalSnapshot[] snapshots)
    {
        todasCampanhas = snapshots ?? Array.Empty<CampanhaLocalSnapshot>();
        indicadorPaginas?.ConfigurarPaginas(todasCampanhas.Length, itensPorPagina);
        IrParaPagina(0);
    }

    // Ver mesmo raciocínio em FichaLoaderUI.cs: só a navegação explícita por botão anima com
    // slide — trocas de página vindas da carga inicial continuam instantâneas.
    private bool proximaNavegacaoDeveAnimar;
    private int  direcaoProximaNavegacao = 1;
    private Coroutine transicaoPaginaCoroutine;

    public void IrParaPagina(int pagina)
    {
        int totalPaginas = Mathf.Max(1, Mathf.CeilToInt((float)todasCampanhas.Length / itensPorPagina));
        int novaPagina = Mathf.Clamp(pagina, 0, totalPaginas - 1);
        bool animar = proximaNavegacaoDeveAnimar && novaPagina != paginaAtual;
        int  direcao = direcaoProximaNavegacao;
        proximaNavegacaoDeveAnimar = false;

        paginaAtual = novaPagina;

        int inicio = paginaAtual * itensPorPagina;
        int fim = Mathf.Min(inicio + itensPorPagina, todasCampanhas.Length);
        int quantidade = Mathf.Max(0, fim - inicio);

        var campanhasDaPagina = new CampanhaLocalSnapshot[quantidade];
        Array.Copy(todasCampanhas, inicio, campanhasDaPagina, 0, quantidade);

        indicadorPaginas?.AtualizarPaginaAtiva(paginaAtual);

        if (transicaoPaginaCoroutine != null) StopCoroutine(transicaoPaginaCoroutine);

        if (animar && contentParent is RectTransform rt)
            transicaoPaginaCoroutine = StartCoroutine(TransicaoDeslizantePagina.Executar(rt, direcao, () => RenderizarPagina(campanhasDaPagina)));
        else
            RenderizarPagina(campanhasDaPagina);
    }

    public void ProximaPagina()
    {
        proximaNavegacaoDeveAnimar = true;
        direcaoProximaNavegacao = 1;
        IrParaPagina(paginaAtual + 1);
    }

    public void PaginaAnterior()
    {
        proximaNavegacaoDeveAnimar = true;
        direcaoProximaNavegacao = -1;
        IrParaPagina(paginaAtual - 1);
    }

    private void RenderizarPagina(CampanhaLocalSnapshot[] snapshots)
    {
        LimparListaVisual();

        if (contentParent == null || campanhaPrefab == null) return;

        for (int i = 0; i < snapshots.Length; i++)
        {
            CampanhaLocalSnapshot snapshot = snapshots[i];
            GameObject go = Instantiate(campanhaPrefab, contentParent);
            itensInstanciados.Add(go);

            CampanhaListaItemUI item = go.GetComponent<CampanhaListaItemUI>();
            item?.Configurar(snapshot, () => AbrirCampanha(snapshot));
        }
    }

    private void AbrirCampanha(CampanhaLocalSnapshot snapshot)
    {
        if (snapshot == null) return;

        toggleObjects?.NavegarParaTela(telaCampanha);
        OnCampanhaClicada?.Invoke(snapshot);
    }

    private void LimparListaVisual()
    {
        for (int i = 0; i < itensInstanciados.Count; i++)
            if (itensInstanciados[i] != null) Destroy(itensInstanciados[i]);

        itensInstanciados.Clear();
    }

    private static void OrdenarPorRecencia(CampanhaLocalSnapshot[] snapshots)
    {
        Array.Sort(snapshots, (a, b) =>
        {
            DateTime aTime = ParseUtc(a != null ? a.atualizadoEmUtc : null);
            DateTime bTime = ParseUtc(b != null ? b.atualizadoEmUtc : null);
            return bTime.CompareTo(aTime);
        });
    }

    private static DateTime ParseUtc(string value)
        => DateTime.TryParse(value, out DateTime parsed) ? parsed : DateTime.MinValue;
}
