using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gerenciador central do menu de atalho (clique direito, ou qualquer outro gatilho). Genérico:
/// qualquer script do jogo chama Instance.Mostrar(posicaoTela, opcoes) passando os textos e as
/// ações de cada botão — este script só interpreta a lista e instancia os prefabs necessários
/// (o painel + um botão por opção). Não sabe nada sobre fichas, tokens, combate, etc.
///
/// USO:
///   MenuAtalhoManager.Instance.Mostrar(Mouse.current.position.ReadValue(), new List&lt;OpcaoMenuAtalho&gt;
///   {
///       new OpcaoMenuAtalho("Atacar", () => IniciarAtaque(token)),
///       new OpcaoMenuAtalho("Curar",  () => IniciarCura(token)),
///       new OpcaoMenuAtalho("Arremessar", () => IniciarArremesso(token)),
///       new OpcaoMenuAtalho("Editar", () => AbrirEdicao(token)),
///   });
/// </summary>
public class MenuAtalhoManager : MonoBehaviour
{
    public static MenuAtalhoManager Instance { get; private set; }

    [Header("Prefab do painel do menu (com MenuAtalhoUI)")]
    [SerializeField] private MenuAtalhoUI prefabMenu;

    [Header("Container (Canvas raiz onde o menu é instanciado)")]
    [SerializeField] private Transform containerRaiz;

    private MenuAtalhoUI menuAtivo;
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

    /// <summary>Fecha qualquer menu aberto e instancia um novo na posição de tela informada.</summary>
    public void Mostrar(Vector2 posicaoTela, List<OpcaoMenuAtalho> opcoes)
    {
        Fechar();

        if (prefabMenu == null || opcoes == null || opcoes.Count == 0)
        {
            Debug.LogWarning("[MenuAtalhoManager] prefabMenu não atribuído no Inspector ou lista de opções vazia.");
            return;
        }

        Transform pai = containerRaiz != null ? containerRaiz : transform;

        // Catcher invisível cobrindo a tela inteira: fecha o menu ao clicar fora dele.
        // Instanciado ANTES do menu para ficar atrás dele na ordem de desenho/raycast.
        catcherFechar = new GameObject("MenuAtalho_Catcher", typeof(RectTransform), typeof(Image), typeof(Button));
        catcherFechar.transform.SetParent(pai, false);
        RectTransform catcherRect = (RectTransform)catcherFechar.transform;
        catcherRect.anchorMin = Vector2.zero;
        catcherRect.anchorMax = Vector2.one;
        catcherRect.offsetMin = Vector2.zero;
        catcherRect.offsetMax = Vector2.zero;
        catcherFechar.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        catcherFechar.GetComponent<Button>().onClick.AddListener(Fechar);

        menuAtivo = Instantiate(prefabMenu, pai);
        menuAtivo.Configurar(opcoes);
        menuAtivo.PosicionarNaTela(posicaoTela);
    }

    /// <summary>Fecha o menu ativo, se houver.</summary>
    public void Fechar()
    {
        if (menuAtivo != null) Destroy(menuAtivo.gameObject);
        menuAtivo = null;

        if (catcherFechar != null) Destroy(catcherFechar);
        catcherFechar = null;
    }

    public bool EstaAberto => menuAtivo != null;
}
