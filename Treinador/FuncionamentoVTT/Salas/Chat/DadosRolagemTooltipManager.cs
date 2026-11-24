using UnityEngine;
using UnityEngine.InputSystem;

// Gerenciador central do tooltip de detalhe de rolagem — singleton igual a
// TokenStatBlockTooltipManager (Salas/Grid/Tooltips/), mas mais simples: só hover (sem catcher de
// clique-fora), some assim que o mouse sai do texto que abriu ele (ver RolagemTooltipTrigger).
public class DadosRolagemTooltipManager : MonoBehaviour
{
    public static DadosRolagemTooltipManager Instance { get; private set; }

    [Header("Prefab do painel (com DadosRolagemTooltipUI)")]
    [SerializeField] private DadosRolagemTooltipUI prefabTooltip;

    [Header("Container (Canvas raiz onde o tooltip é instanciado)")]
    [SerializeField] private Transform containerRaiz;

    // Mesmo algoritmo/campo de Sistemas/SistemasCustom/UITooltipTrigger.cs — pivot dinâmico conforme
    // o lado da tela (evita sair da tela) + conversão screen→local correta pro render mode do Canvas
    // (Overlay vs Camera). RectTransformCornerPositioner (usado antes) assume posição de tela ==
    // posição de mundo, o que só é verdade pra um Canvas Overlay na raiz — daí o tooltip aparecendo
    // fora do lugar quando o container real é outro.
    [Header("Posicionamento")]
    [SerializeField] private Vector2 offsetMouse = new Vector2(15f, -15f);

    [Header("Debug")]
    [Tooltip("Liga pra investigar no Editor: o tooltip NÃO some quando o mouse sai — fica parado na " +
        "tela (última posição) pra dar tempo de selecionar ele no Hierarchy e inspecionar. Continua " +
        "sendo substituído normalmente por um novo hover. Desliga pra voltar ao comportamento normal.")]
    [SerializeField] private bool manterAbertoAoSairDoHover = false;

    private DadosRolagemTooltipUI instanciaAtiva;
    private RectTransform rectAtivo;
    private RectTransform parentRectAtivo;
    private Canvas canvasAtivo;
    private bool seguindoMouse;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // Segue o mouse enquanto estiver de fato em hover — mesmo comportamento de UITooltipTrigger.
    // Só verificar instanciaAtiva não bastaria: com manterAbertoAoSairDoHover ligado, a instância
    // continua viva depois do mouse-exit, mas não deve mais perseguir o cursor (senão nunca fica
    // parado o suficiente pra selecionar no Hierarchy).
    private void Update()
    {
        if (instanciaAtiva == null || !seguindoMouse) return;
        AtualizarPosicao();
    }

    public void Mostrar(string nome, ResultadoRolagem resultado)
    {
        if (prefabTooltip == null || resultado == null) return;

        Esconder(forcar: true); // sempre troca pelo novo, mesmo com manterAbertoAoSairDoHover ligado

        Transform pai = containerRaiz != null ? containerRaiz : transform;
        instanciaAtiva = Instantiate(prefabTooltip, pai);
        instanciaAtiva.Configurar(nome, resultado);
        instanciaAtiva.transform.SetAsLastSibling();

        rectAtivo = instanciaAtiva.transform as RectTransform;
        parentRectAtivo = pai as RectTransform;
        canvasAtivo = instanciaAtiva.GetComponentInParent<Canvas>();
        seguindoMouse = true;

        AtualizarPosicao();
    }

    /// <summary>Chamado por RolagemTooltipTrigger no mouse-exit — com manterAbertoAoSairDoHover
    /// ligado, só para de seguir o mouse (fica parado na última posição pra inspecionar) em vez de
    /// destruir. Mostrar() sempre chama com forcar:true, então um novo hover substitui o tooltip
    /// preso mesmo com o debug ligado.</summary>
    public void Esconder(bool forcar = false)
    {
        if (!forcar && manterAbertoAoSairDoHover)
        {
            seguindoMouse = false;
            return;
        }

        if (instanciaAtiva != null)
            Destroy(instanciaAtiva.gameObject);
        instanciaAtiva = null;
        rectAtivo = null;
        parentRectAtivo = null;
        canvasAtivo = null;
        seguindoMouse = false;
    }

    private void AtualizarPosicao()
    {
        if (rectAtivo == null || canvasAtivo == null || Mouse.current == null) return;

        Vector2 posMouse = Mouse.current.position.ReadValue();

        // Pivot dinâmico: evita que o tooltip saia da tela.
        float pivotX = posMouse.x / Screen.width  > 0.5f ? 1f : 0f;
        float pivotY = posMouse.y / Screen.height > 0.5f ? 1f : 0f;
        rectAtivo.pivot = new Vector2(pivotX, pivotY);

        Camera cam = canvasAtivo.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvasAtivo.worldCamera;
        RectTransform parentRect = parentRectAtivo != null ? parentRectAtivo : (RectTransform)canvasAtivo.transform;

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, posMouse, cam, out Vector2 pontoLocal))
        {
            pontoLocal.x += pivotX == 0f ?  offsetMouse.x : -offsetMouse.x;
            pontoLocal.y += pivotY == 0f ?  Mathf.Abs(offsetMouse.y) : -Mathf.Abs(offsetMouse.y);
            rectAtivo.localPosition = pontoLocal;
        }
    }
}
