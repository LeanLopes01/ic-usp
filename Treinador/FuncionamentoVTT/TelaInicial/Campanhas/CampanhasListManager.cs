using System.Collections.Generic;
using UnityEngine;

public class CampanhasListManager : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Transform content;
    [SerializeField] private GameObject campanhaCardPrefab;
    [SerializeField] private IndicadorPaginasUI indicadorPaginas;

    [Header("Paginação")]
    [SerializeField, Min(1)] private int itensPorPagina = 9;

    private readonly List<CampanhaCardUI> pool = new List<CampanhaCardUI>();
    private List<CampanhaData> campanhas = new List<CampanhaData>();
    private int paginaAtual;

    public void ExibirCampanhas(List<CampanhaData> novasCampanhas)
    {
        campanhas = novasCampanhas ?? new List<CampanhaData>();
        indicadorPaginas?.ConfigurarPaginas(campanhas.Count, itensPorPagina);
        IrParaPagina(0);
    }

    public void IrParaPagina(int pagina)
    {
        int totalPaginas = Mathf.Max(1, Mathf.CeilToInt((float)campanhas.Count / itensPorPagina));
        paginaAtual = Mathf.Clamp(pagina, 0, totalPaginas - 1);

        int inicio = paginaAtual * itensPorPagina;
        int fim = Mathf.Min(inicio + itensPorPagina, campanhas.Count);
        int quantidadeNestaPagina = fim - inicio;

        GarantirPool(quantidadeNestaPagina);

        for (int i = 0; i < pool.Count; i++)
        {
            bool visivel = i < quantidadeNestaPagina;
            pool[i].gameObject.SetActive(visivel);

            if (visivel)
            {
                CampanhaData campanha = campanhas[inicio + i];
                pool[i].Configurar(campanha, () => OnClicarAcaoCampanha(campanha));
            }
        }

        indicadorPaginas?.AtualizarPaginaAtiva(paginaAtual);
    }

    public void ProximaPagina() => IrParaPagina(paginaAtual + 1);

    public void PaginaAnterior() => IrParaPagina(paginaAtual - 1);

    // Reaproveita cards já instanciados (SetActive) em vez de Destroy/Instantiate a cada
    // troca de página — evita picos de Garbage Collector em listas grandes.
    private void GarantirPool(int quantidadeMinima)
    {
        if (content == null || campanhaCardPrefab == null)
        {
            Debug.LogWarning("[CampanhasListManager] Configure content e campanhaCardPrefab no Inspector.");
            return;
        }

        while (pool.Count < quantidadeMinima)
        {
            GameObject go = Instantiate(campanhaCardPrefab, content);
            CampanhaCardUI card = go.GetComponent<CampanhaCardUI>();

            if (card == null)
            {
                Debug.LogError("[CampanhasListManager] campanhaCardPrefab não possui o componente CampanhaCardUI.");
                Destroy(go);
                return;
            }

            pool.Add(card);
        }
    }

    private void OnClicarAcaoCampanha(CampanhaData campanha)
    {
        Debug.Log($"[CampanhasListManager] Ação clicada para a campanha '{campanha.titulo}'.");
    }
}
