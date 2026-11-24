using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CarrosselNoticias : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Transform containerNoticias;

    [Header("Navegação")]
    [SerializeField] private Button botaoAnterior;
    [SerializeField] private Button botaoProximo;
    [SerializeField] private IndicadorPaginasUI indicadorPaginas; // Assumo que você tenha esse script

    [Header("Prefabs")]
    [Tooltip("Cada prefab é uma notícia/banner completo já pronto. Arraste na ordem de exibição.")]
    [SerializeField] private List<GameObject> noticiaPrefabs;

    [Header("Configurações do Carrossel Espacial")]
    [Tooltip("Espaço em pixels entre o centro de uma notícia e outra.")]
    [SerializeField] private float espacamento = 800f;
    [Tooltip("Tamanho das notícias que não estão no centro.")]
    [Range(0f, 1f)] [SerializeField] private float escalaInativos = 0.8f;
    [Tooltip("Transparência das notícias que não estão no centro.")]
    [Range(0f, 1f)] [SerializeField] private float alphaInativos = 0.5f;

    [Header("Configurações de Tempo")]
    [SerializeField] private float tempoExibicao = 5f;
    [SerializeField] private float tempoTransicao = 0.5f;
    [Tooltip("Deixa a animação de deslizar mais suave no final.")]
    [SerializeField] private AnimationCurve curvaAnimacao = AnimationCurve.EaseInOut(0, 0, 1, 1);

    private readonly List<GameObject> instanciasAtivas = new List<GameObject>();
    private readonly List<RectTransform> rectTransforms = new List<RectTransform>();
    private readonly List<CanvasGroup> canvasGroups = new List<CanvasGroup>();
    
    private int indexAtual;
    private Coroutine rotinaAtiva;
    private Coroutine transicaoAtiva;

    private void OnEnable()
    {
        InstanciarNoticias();

        if (botaoAnterior != null) botaoAnterior.onClick.AddListener(NoticiaAnterior);
        if (botaoProximo != null) botaoProximo.onClick.AddListener(ProximaNoticia);

        ReiniciarRotinaAutomatica();
    }

    private void OnDisable()
    {
        if (botaoAnterior != null) botaoAnterior.onClick.RemoveListener(NoticiaAnterior);
        if (botaoProximo != null) botaoProximo.onClick.RemoveListener(ProximaNoticia);

        PararRotina();
        LimparInstancias();
    }

    public void ProximaNoticia()
    {
        if (transicaoAtiva != null || instanciasAtivas.Count <= 1) return;

        int proximoIndex = (indexAtual + 1) % instanciasAtivas.Count;
        TrocarNoticia(proximoIndex, 1); // Direção 1 (Avançar)
    }

    public void NoticiaAnterior()
    {
        if (transicaoAtiva != null || instanciasAtivas.Count <= 1) return;

        int anteriorIndex = (indexAtual - 1 + instanciasAtivas.Count) % instanciasAtivas.Count;
        TrocarNoticia(anteriorIndex, -1); // Direção -1 (Voltar)
    }

    private void TrocarNoticia(int proximoIndex, int direcao)
    {
        PararRotina();
        transicaoAtiva = StartCoroutine(AnimarDeslizamento(proximoIndex, direcao));
    }

    private void ReiniciarRotinaAutomatica()
    {
        PararRotina();
        if (instanciasAtivas.Count > 1)
            rotinaAtiva = StartCoroutine(RotinaCarrossel());
    }

    private void InstanciarNoticias()
    {
        if (containerNoticias == null || noticiaPrefabs == null || noticiaPrefabs.Count == 0) return;

        for (int i = 0; i < noticiaPrefabs.Count; i++)
        {
            if (noticiaPrefabs[i] == null) continue;

            GameObject go = Instantiate(noticiaPrefabs[i], containerNoticias);
            go.SetActive(true); // Todos ficam ativos para aparecerem nas laterais

            RectTransform rt = go.GetComponent<RectTransform>();
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();

            // Configura a âncora para o centro do container
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            instanciasAtivas.Add(go);
            rectTransforms.Add(rt);
            canvasGroups.Add(cg);
        }

        indexAtual = 0;
        
        // Posiciona os itens inicialmente sem animação
        for (int i = 0; i < instanciasAtivas.Count; i++)
        {
            int offset = ObterOffsetRelativo(i, indexAtual, instanciasAtivas.Count);
            AtualizarVisualItem(i, offset);
        }

        if (indicadorPaginas != null)
        {
            indicadorPaginas.ConfigurarPaginas(instanciasAtivas.Count, 1);
            indicadorPaginas.AtualizarPaginaAtiva(indexAtual);
        }
    }

    private IEnumerator RotinaCarrossel()
    {
        while (true)
        {
            yield return new WaitForSeconds(tempoExibicao);
            int proximoIndex = (indexAtual + 1) % instanciasAtivas.Count;
            yield return StartCoroutine(AnimarDeslizamento(proximoIndex, 1));
        }
    }

    private IEnumerator AnimarDeslizamento(int proximoIndex, int direcaoMovimento)
    {
        float tempoDecorrido = 0f;
        int total = instanciasAtivas.Count;

        int[] offsetAlvo = new int[total];
        int[] offsetInicial = new int[total];

        // Prepara os locais de onde cada item vai sair e para onde vai
        for (int i = 0; i < total; i++)
        {
            offsetAlvo[i] = ObterOffsetRelativo(i, proximoIndex, total);
            // O item sai de uma posição baseada na direção do clique para manter o fluxo
            offsetInicial[i] = offsetAlvo[i] + direcaoMovimento; 
        }

        while (tempoDecorrido < tempoTransicao)
        {
            tempoDecorrido += Time.deltaTime;
            float t = tempoDecorrido / tempoTransicao;
            float progresso = curvaAnimacao.Evaluate(t);

            for (int i = 0; i < total; i++)
            {
                // Interpola a posição "virtual" (-1, 0, 1...) e atualiza o visual
                float posicaoVirtual = Mathf.Lerp(offsetInicial[i], offsetAlvo[i], progresso);
                AtualizarVisualItem(i, posicaoVirtual);
            }

            yield return null;
        }

        // Garante que terminou no lugar exato
        for (int i = 0; i < total; i++)
        {
            AtualizarVisualItem(i, offsetAlvo[i]);
        }

        indexAtual = proximoIndex;
        if (indicadorPaginas != null) indicadorPaginas.AtualizarPaginaAtiva(indexAtual);
        
        transicaoAtiva = null;
        ReiniciarRotinaAutomatica();
    }

    // Calcula se um item está no centro (0), na esquerda (-1) ou na direita (+1)
    private int ObterOffsetRelativo(int indexElemento, int indexCentro, int totalItens)
    {
        int delta = indexElemento - indexCentro;
        int metade = totalItens / 2;

        // Lógica matemática para dar o efeito de loop infinito
        if (delta > metade)
            delta -= totalItens;
        else if (delta < -metade)
            delta += totalItens;

        return delta;
    }

    // Aplica posição física, tamanho e opacidade baseado na distância do centro
    private void AtualizarVisualItem(int index, float offsetVirtual)
    {
        RectTransform rt = rectTransforms[index];
        CanvasGroup cg = canvasGroups[index];

        // 1. Move no Eixo X (espaçamento)
        rt.anchoredPosition = new Vector2(offsetVirtual * espacamento, 0f);

        // 2. Calcula a distância do centro (0) para aplicar os efeitos
        float distanciaDoCentro = Mathf.Abs(offsetVirtual);
        
        // Fator varia de 1 (no centro) até 0 (longe do centro)
        float fatorDestaque = 1f - Mathf.Clamp01(distanciaDoCentro);

        // 3. Aplica Escala e Opacidade
        rt.localScale = Vector3.one * Mathf.Lerp(escalaInativos, 1f, fatorDestaque);
        cg.alpha = Mathf.Lerp(alphaInativos, 1f, fatorDestaque);

        // 4. Só o item CENTRALIZADO pode receber clique — os das laterais ficam com os botões
        // desativados e sem bloquear raycast, pra não roubar clique de quem tenta acertar o item
        // central (as notícias inativas ficam parcialmente sobrepostas durante a transição).
        bool estaCentralizado = distanciaDoCentro < 0.5f;
        cg.interactable = estaCentralizado;
        cg.blocksRaycasts = estaCentralizado;

        // 5. Joga o item que está mais próximo do centro para frente (Ordem de Renderização)
        if (estaCentralizado)
        {
            rt.SetAsLastSibling();
        }
    }

    private void PararRotina()
    {
        if (rotinaAtiva != null)
        {
            StopCoroutine(rotinaAtiva);
            rotinaAtiva = null;
        }
    }

    private void LimparInstancias()
    {
        for (int i = 0; i < instanciasAtivas.Count; i++)
        {
            if (instanciasAtivas[i] != null) Destroy(instanciasAtivas[i]);
        }
        instanciasAtivas.Clear();
        rectTransforms.Clear();
        canvasGroups.Clear();
        indexAtual = 0;
    }
}