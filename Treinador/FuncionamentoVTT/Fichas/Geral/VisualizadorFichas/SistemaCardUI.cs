using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Dados passados pelo VisualizadorFichasManager para cada card de ficha
public struct FichaCardData
{
    public string nome;
    public string classe;
    public int    generalId;
    public int    dndId;
    public int    level;
    public string imagemPath;
}

/// <summary>
/// Um bloco visual por sistema de RPG.
/// Estrutura esperada no prefab:
///   SistemaCard (este script)
///   ├── TitleText  (TMP_Text)
///   ├── SetaEsquerda (Button)
///   ├── Viewport (RectTransform + Mask)
///   │   └── PagesContent (RectTransform)  ← atribuir em pagesContent
///   └── SetaDireita (Button)
/// </summary>
public class SistemaCardUI : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text      titleText;
    public RectTransform viewport;       // área com Mask, define a largura visível
    public RectTransform pagesContent;   // desliza dentro do viewport
    public Button        setaEsquerda;
    public Button        setaDireita;

    [Header("Prefab")]
    public GameObject fichaCardPrefab;

    [Header("Config")]
    public int   fichasPorPagina = 9;
    [Tooltip("Duração da animação de slide (segundos)")]
    public float duracaoSlide    = 0.3f;
    [Tooltip("Espaçamento entre células no grid da página")]
    public Vector2 espacamentoCelula = new Vector2(8f, 8f);
    [Tooltip("Padding interno de cada página")]
    public RectOffset paddingPagina;

    private int   _paginaAtual;
    private int   _totalPaginas;
    private float _larguraPagina;
    private bool  _animando;

    private Action<int, int, string, int, bool> _onCliqueFicha;

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (paddingPagina == null) paddingPagina = new RectOffset(8, 8, 8, 8);
    }

    void Start()
    {
        if (setaEsquerda != null) setaEsquerda.onClick.AddListener(PaginaAnterior);
        if (setaDireita  != null) setaDireita.onClick.AddListener(ProximaPagina);
        AtualizarBotoes();
    }

    // ─────────────────────────────────────────────────────────────────
    public void Setup(string sistemaNome,
                      List<FichaCardData> fichas,
                      Action<int, int, string, int, bool> onCliqueFicha)
    {
        _onCliqueFicha = onCliqueFicha;

        if (titleText != null) titleText.text = sistemaNome;

        // Limpa páginas antigas
        foreach (Transform c in pagesContent) Destroy(c.gameObject);

        int count = fichas?.Count ?? 0;
        _totalPaginas = Mathf.Max(1, Mathf.CeilToInt((float)count / fichasPorPagina));
        _paginaAtual  = 0;

        // Cria uma page por bloco de 9 fichas
        for (int p = 0; p < _totalPaginas; p++)
        {
            RectTransform pageRect = CriarPagina(p);

            int inicio = p * fichasPorPagina;
            int fim    = Mathf.Min(inicio + fichasPorPagina, count);
            for (int i = inicio; i < fim; i++)
            {
                FichaCardData fd = fichas[i];
                GameObject go = Instantiate(fichaCardPrefab, pageRect);
                var ui = go.GetComponent<FichaCardMiniUI>();
                ui?.SetFicha(fd.nome, fd.classe, fd.generalId, fd.dndId,
                             fd.level, fd.imagemPath, _onCliqueFicha);
            }
        }

        StartCoroutine(AjustarLayoutAposFrame());
    }

    // ─────────────────────────────────────────────────────────────────
    private RectTransform CriarPagina(int indice)
    {
        var go   = new GameObject($"Pagina_{indice}", typeof(RectTransform));
        go.transform.SetParent(pagesContent, false);

        var rect = go.GetComponent<RectTransform>();
        // âncora esquerda, empurrado para direita pelo indice depois do layout
        rect.anchorMin = new Vector2(0, 0);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot     = new Vector2(0, 0.5f);

        var grid                = go.AddComponent<GridLayoutGroup>();
        grid.constraint         = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount    = fichasPorPagina;
        grid.childAlignment     = TextAnchor.UpperLeft;
        grid.spacing            = espacamentoCelula;
        grid.padding            = paddingPagina;

        return rect;
    }

    // Aguarda um frame para que o viewport tenha seu rect calculado
    private IEnumerator AjustarLayoutAposFrame()
    {
        yield return new WaitForEndOfFrame();

        _larguraPagina = (viewport != null) ? viewport.rect.width : Screen.width;
        if (_larguraPagina <= 0f) _larguraPagina = Screen.width;

        // Tamanho natural da célula vem do prefab; só define se o prefab tiver RectTransform
        Vector2 cellSize = Vector2.zero;
        if (fichaCardPrefab != null)
        {
            var prefabRect = fichaCardPrefab.GetComponent<RectTransform>();
            if (prefabRect != null) cellSize = prefabRect.sizeDelta;
        }

        // Redimensiona cada página posicionando-a horizontalmente
        int idx = 0;
        foreach (RectTransform pageRect in pagesContent)
        {
            pageRect.sizeDelta        = new Vector2(_larguraPagina, 0f);
            pageRect.anchoredPosition = new Vector2(idx * _larguraPagina, 0f);

            if (cellSize != Vector2.zero)
            {
                var grid = pageRect.GetComponent<GridLayoutGroup>();
                if (grid != null) grid.cellSize = cellSize;
            }
            idx++;
        }

        // Expande o pagesContent para conter todas as páginas
        pagesContent.sizeDelta        = new Vector2(_larguraPagina * _totalPaginas, 0f);
        pagesContent.anchoredPosition = Vector2.zero;

        AtualizarBotoes();
    }

    // ─────────────────────────────────────────────────────────────────
    private void PaginaAnterior()
    {
        if (_animando || _paginaAtual <= 0) return;
        _paginaAtual--;
        StartCoroutine(AnimarSlide());
    }

    private void ProximaPagina()
    {
        if (_animando || _paginaAtual >= _totalPaginas - 1) return;
        _paginaAtual++;
        StartCoroutine(AnimarSlide());
    }

    private IEnumerator AnimarSlide()
    {
        _animando = true;
        AtualizarBotoes();

        Vector2 origem = pagesContent.anchoredPosition;
        Vector2 alvo   = new Vector2(-_paginaAtual * _larguraPagina, 0f);
        float   t      = 0f;

        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duracaoSlide;
            pagesContent.anchoredPosition = Vector2.Lerp(origem, alvo, EaseInOut(Mathf.Clamp01(t)));
            yield return null;
        }

        pagesContent.anchoredPosition = alvo;
        _animando = false;
        AtualizarBotoes();
    }

    private void AtualizarBotoes()
    {
        if (setaEsquerda != null)
            setaEsquerda.interactable = !_animando && _paginaAtual > 0;
        if (setaDireita != null)
            setaDireita.interactable  = !_animando && _paginaAtual < _totalPaginas - 1;
    }

    private static float EaseInOut(float t) => t * t * (3f - 2f * t);
}
