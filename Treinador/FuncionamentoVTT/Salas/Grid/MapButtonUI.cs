using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Botão compartilhado de miniatura clicável/arrastável — dois modos, definidos por qual Setup foi
/// chamado:
///
/// - Modo MAPA (galeria de cenas de uma sala): só a miniatura. Passar o mouse por cima mostra um
///   ícone de overlay (hoverIcone) E o nome da cena (ver BarraFerramentasManager.MostrarNomeHover —
///   tooltip central e reaproveitável, não um Text fixo neste prefab); clicar abre uma confirmação
///   (PopupManager) perguntando se quer trocar pra aquela cena — só troca se o jogador confirmar
///   (ver MapManager.TrocarMapaComProgresso).
/// - Modo TOKEN (catálogo de tokens de uma sala): miniatura, mesmo ícone/tooltip de hover, mas SEM
///   clique — só arrasta pro grid (mesma mecânica de Salas/Grid/TokenButtonUI.cs, que continua sendo
///   o script usado pro token JÁ no mapa/grid, com badge de redimensionar; este aqui é só a entrada
///   do catálogo, sem redimensionar).
///
/// Um único script pros dois porque a funcionalidade É a mesma (miniatura + hover + interação), só a
/// interação final (clique vs. arrastar) muda. O nome não é mais um TMP_Text fixo no prefab — vem do
/// tooltip central de BarraFerramentasManager, pra poder ser reaproveitado em qualquer item futuro
/// (mapas/adereços/montarias/anexos/notas) sem duplicar um campo de texto em cada prefab.
/// </summary>
public class MapButtonUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private enum Modo { Nenhum, Mapa, Token }

    [Header("UI")]
    [SerializeField] private Image miniaturaImage;
    [Tooltip("GameObject (ícone/overlay) mostrado só enquanto o mouse estiver sobre a miniatura — " +
        "sinaliza que dá pra clicar (modo mapa) ou arrastar (modo token). Começa desativado.")]
    [SerializeField] private GameObject hoverIcone;

    [Header("Arrastar (só modo Token)")]
    [SerializeField] private Canvas canvasRaiz;
    [SerializeField] private CanvasGroup canvasGroup;

    private Modo modo = Modo.Nenhum;
    private MapManager manager;
    private string nomeItem; // mostrado só no tooltip de hover (ver BarraFerramentasManager)

    // ── Modo Mapa ──────────────────────────────────────────────────────────
    private MapConfig mapConfigAtual;

    // ── Modo Token ─────────────────────────────────────────────────────────
    private MapManager.TokenCatalogItem itemCatalogo;
    private RectTransform rectTransform;
    private Vector2 posicaoOriginal;

    private void Awake()
    {
        rectTransform = transform as RectTransform;
        canvasGroup ??= GetComponent<CanvasGroup>();
        canvasGroup ??= gameObject.AddComponent<CanvasGroup>();
        canvasRaiz  ??= GetComponentInParent<Canvas>();

        if (hoverIcone != null) hoverIcone.SetActive(false);
    }

    // ── Setup ──────────────────────────────────────────────────────────────

    /// <summary>Configura como botão de MAPA (galeria de cenas da sala).</summary>
    public void Setup(MapConfig config, MapManager mapManager = null)
    {
        modo = Modo.Mapa;
        mapConfigAtual = config;
        manager = mapManager != null ? mapManager : FindFirstObjectByType<MapManager>();
        nomeItem = config != null ? config.nomeDoMapa : string.Empty;

        if (miniaturaImage != null)
            miniaturaImage.sprite = config != null ? config.imagemMapa : null;
    }

    /// <summary>Configura como botão de TOKEN (catálogo de tokens da sala) — arrastável.</summary>
    public void Setup(MapManager mapManager, MapManager.TokenCatalogItem item, string nome)
    {
        modo = Modo.Token;
        manager = mapManager != null ? mapManager : FindFirstObjectByType<MapManager>();
        itemCatalogo = item;
        nomeItem = string.IsNullOrWhiteSpace(nome) ? "Token" : nome;

        if (miniaturaImage != null)
            miniaturaImage.sprite = item != null ? item.sprite : null;
    }

    // ── Hover (ambos os modos) ────────────────────────────────────────────

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (hoverIcone != null) hoverIcone.SetActive(true);
        BarraFerramentasManager.Instancia?.MostrarNomeHover(nomeItem, rectTransform);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (hoverIcone != null) hoverIcone.SetActive(false);
        BarraFerramentasManager.Instancia?.EsconderNomeHover();
    }

    // ── Clique — só modo Mapa (token só reage a arrastar, ver OnEndDrag) ─────

    public void OnPointerClick(PointerEventData eventData)
    {
        if (modo != Modo.Mapa) return;
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (mapConfigAtual == null)
        {
            Debug.LogWarning("[MapButtonUI] Nenhum MapConfig foi definido neste botão.");
            return;
        }

        if (manager == null) manager = FindFirstObjectByType<MapManager>();
        if (manager == null)
        {
            Debug.LogWarning("[MapButtonUI] MapManager não encontrado na cena.");
            return;
        }

        if (mapConfigAtual == manager.mapaAtivo) return; // já é a cena ativa — nada a confirmar

        string nomeCena = string.IsNullOrWhiteSpace(mapConfigAtual.nomeDoMapa) ? "esta cena" : mapConfigAtual.nomeDoMapa;
        MapConfig alvo = mapConfigAtual;
        MapManager gerenciador = manager;

        if (PopupManager.Instance != null)
        {
            PopupManager.Instance.MostrarConfirmacao(
                "Abrir Cena", $"Deseja abrir a cena '{nomeCena}'?",
                onSim: () => gerenciador.TrocarMapaComProgresso(alvo),
                labelSim: "Abrir", labelNao: "Cancelar");
        }
        else
        {
            gerenciador.TrocarMapaComProgresso(alvo);
        }
    }

    // ── Arrastar — só modo Token (mesma mecânica de TokenButtonUI.OnBeginDrag/OnDrag/OnEndDrag) ────

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (modo != Modo.Token) return;
        if (itemCatalogo == null || rectTransform == null) return;

        posicaoOriginal = rectTransform.anchoredPosition;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.alpha = 0.7f;
        if (hoverIcone != null) hoverIcone.SetActive(false);
        BarraFerramentasManager.Instancia?.EsconderNomeHover();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (modo != Modo.Token) return;
        if (itemCatalogo == null || rectTransform == null || canvasRaiz == null) return;

        float scale = Mathf.Max(0.0001f, canvasRaiz.scaleFactor);
        rectTransform.anchoredPosition += eventData.delta / scale;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (modo != Modo.Token) return;

        if (rectTransform != null)
            rectTransform.anchoredPosition = posicaoOriginal;

        canvasGroup.blocksRaycasts = true;
        canvasGroup.alpha = 1f;

        if (manager != null && itemCatalogo != null)
            manager.TentarSoltarTokenNoMapa(itemCatalogo, eventData.position);
    }
}
