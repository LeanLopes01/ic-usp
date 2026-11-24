using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gerenciador central do tooltip de stat block. 3 formas de abrir (ver ModoStatBlockTooltip):
///   Hover  — MapTokenDraggable chama Mostrar(token, Hover) após 2s parado sobre o token.
///   Atacar/Curar — chamado pelas opções do menu de atalho (clique direito no token).
/// Singleton igual a PopupManager/MenuAtalhoManager.
/// </summary>
public class TokenStatBlockTooltipManager : MonoBehaviour
{
    public static TokenStatBlockTooltipManager Instance { get; private set; }

    [Header("Prefab do painel (com TokenStatBlockTooltipUI)")]
    [SerializeField] private TokenStatBlockTooltipUI prefabTooltip;

    [Header("Container (Canvas raiz onde o tooltip é instanciado)")]
    [SerializeField] private Transform containerRaiz;

    private TokenStatBlockTooltipUI instanciaAtiva;
    private bool mouseSobrePainel;
    private GameObject catcherFechar;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Mostrar(MapTokenDraggable token, ModoStatBlockTooltip modo)
    {
        if (token == null || prefabTooltip == null) return;

        TokenStatBlockData dados = ConstruirDados(token, modo);
        if (dados == null) return;

        Esconder();

        Transform pai = containerRaiz != null ? containerRaiz : transform;

        // Atacar/Curar (aberto via menu, não por hover) ganham um catcher invisível full-screen
        // que fecha o painel ao clicar fora — mesmo padrão do MenuAtalhoManager. O modo Hover não
        // precisa disso: ele já some sozinho quando o mouse se move (ver TokenStatBlockTooltipUI).
        if (modo != ModoStatBlockTooltip.Hover)
        {
            catcherFechar = new GameObject("StatBlockTooltip_Catcher", typeof(RectTransform), typeof(Image), typeof(Button));
            catcherFechar.transform.SetParent(pai, false);
            RectTransform catcherRect = (RectTransform)catcherFechar.transform;
            catcherRect.anchorMin = Vector2.zero;
            catcherRect.anchorMax = Vector2.one;
            catcherRect.offsetMin = Vector2.zero;
            catcherRect.offsetMax = Vector2.zero;
            catcherFechar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
            catcherFechar.GetComponent<Button>().onClick.AddListener(Esconder);
        }

        instanciaAtiva = Instantiate(prefabTooltip, pai);
        mouseSobrePainel = false;
        instanciaAtiva.Configurar(dados, token, this, modo);
    }

    // Público — reaproveitado por SelecaoAlvoManager pra montar TokenStatBlockData de QUALQUER
    // token (não só o mirado) em modo Testes, pra buscar o modificador de salvaguarda de cada alvo
    // numa resolução em área, sem duplicar o branching IsLinked/IsMonstro.
    public static TokenStatBlockData ConstruirDados(MapTokenDraggable token, ModoStatBlockTooltip modo)
    {
        if (token.IsLinked)
        {
            LocalFichaData ficha = token.ObterDadosDaFicha();
            return ficha != null ? TokenStatBlockBuilder.ConstruirDeFicha(ficha, modo) : null;
        }

        if (token.IsMonstro)
        {
            MonstroDado monstro = token.ObterDadosDoMonstro();
            return monstro != null ? TokenStatBlockBuilder.ConstruirDeMonstro(monstro, modo) : null;
        }

        return null; // token sem link — nada pra mostrar no stat block
    }

    /// <summary>
    /// Chamado por MapTokenDraggable quando o hover no token termina. Só esconde de fato se o
    /// mouse não estiver dentro do próprio painel (permite mover até os botões sem ele sumir).
    /// </summary>
    public void SolicitarEsconder()
    {
        if (mouseSobrePainel) return;
        Esconder();
    }

    public void Esconder()
    {
        if (instanciaAtiva != null)
            Destroy(instanciaAtiva.gameObject);
        instanciaAtiva = null;
        mouseSobrePainel = false;

        if (catcherFechar != null)
            Destroy(catcherFechar);
        catcherFechar = null;
    }

    internal void NotificarMouseEntrouNoPainel() => mouseSobrePainel = true;

    internal void NotificarMouseSaiuDoPainel()
    {
        mouseSobrePainel = false;
        Esconder();
    }
}
