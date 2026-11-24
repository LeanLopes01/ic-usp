using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class IndicadorPaginasUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Transform containerDots;
    [SerializeField] private GameObject dotPrefab;

    [Header("Cores")]
    [SerializeField] private Color corAtivo = Color.white;
    [SerializeField] private Color corInativo = new Color(0.4f, 0.4f, 1f, 1f);

    private readonly List<Image> dotsInstanciados = new List<Image>();
    private int paginaAtual;

    public int TotalPaginas => dotsInstanciados.Count;
    public int PaginaAtual => paginaAtual;

    public void ConfigurarPaginas(int totalItens, int itensPorPagina)
    {
        if (containerDots == null || dotPrefab == null)
        {
            Debug.LogWarning("[IndicadorPaginasUI] Configure containerDots e dotPrefab no Inspector.");
            return;
        }

        LimparDots();

        if (itensPorPagina <= 0 || totalItens <= 0)
            return;

        int totalPaginas = Mathf.CeilToInt((float)totalItens / itensPorPagina);

        for (int i = 0; i < totalPaginas; i++)
        {
            GameObject go = Instantiate(dotPrefab, containerDots);
            go.SetActive(true);
            dotsInstanciados.Add(go.GetComponent<Image>());
        }

        AtualizarPaginaAtiva(0);
    }

    public void AtualizarPaginaAtiva(int indexPaginaAtual)
    {
        if (dotsInstanciados.Count == 0)
            return;

        paginaAtual = Mathf.Clamp(indexPaginaAtual, 0, dotsInstanciados.Count - 1);

        for (int i = 0; i < dotsInstanciados.Count; i++)
        {
            if (dotsInstanciados[i] != null)
                dotsInstanciados[i].color = (i == paginaAtual) ? corAtivo : corInativo;
        }
    }

    public void ProximaPagina()
    {
        if (dotsInstanciados.Count == 0)
            return;

        AtualizarPaginaAtiva((paginaAtual + 1) % dotsInstanciados.Count);
    }

    public void PaginaAnterior()
    {
        if (dotsInstanciados.Count == 0)
            return;

        AtualizarPaginaAtiva((paginaAtual - 1 + dotsInstanciados.Count) % dotsInstanciados.Count);
    }

    private void LimparDots()
    {
        foreach (Transform child in containerDots)
            Destroy(child.gameObject);

        dotsInstanciados.Clear();
        paginaAtual = 0;
    }
}
