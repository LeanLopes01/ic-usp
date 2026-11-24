using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[RequireComponent(typeof(Button))]
public class MenuTabButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Elementos Visuais")]
    [Tooltip("Deixe vazio para pegar o Button neste mesmo objeto.")]
    public Button button;
    [Tooltip("Texto do botão (Text ou TMP_Text). Só a cor do texto muda, não o fundo.")]
    public Graphic buttonText;
    public GameObject underlineBar; // O objeto da barra embaixo do texto

    [Header("Cores do Texto")]
    public Color normalTextColor = new Color(1f, 1f, 1f, 1f);
    public Color hoverTextColor = new Color(0.7608f, 0.1255f, 0.9804f, 1f); // #C220FA
    public Color activeTextColor = new Color(0.7608f, 0.1255f, 0.9804f, 1f); // #C220FA

    // Controla se este é o botão da tela atual
    private bool isActiveScreen = false;
    private Image buttonImage;
    private Selectable.Transition transitionOriginal;

    void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        buttonImage = button != null ? button.image : null;
    }

    void Start()
    {
        if (button != null) transitionOriginal = button.transition;
        if (!isActiveScreen) DeselectTab();
    }

    // Chamado quando o mouse ENTRA no botão
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!isActiveScreen)
        {
            if (buttonText != null) buttonText.color = hoverTextColor;
            underlineBar.SetActive(true);
        }
    }

    // Chamado quando o mouse SAI do botão
    public void OnPointerExit(PointerEventData eventData)
    {
        if (!isActiveScreen)
        {
            if (buttonText != null) buttonText.color = normalTextColor;
            underlineBar.SetActive(false);
        }
    }

    // Chame esta função quando a tela deste botão for aberta
    public void SelectTab()
    {
        isActiveScreen = true;
        if (buttonText != null) buttonText.color = activeTextColor;
        underlineBar.SetActive(true);

        // Trava o fundo do botão na cor "Pressed" configurada no próprio Button,
        // desligando a transição automática (senão o hover/exit sobrescreveria a cor).
        if (button != null && buttonImage != null)
        {
            button.transition = Selectable.Transition.None;
            buttonImage.color = button.colors.pressedColor;
        }
    }

    // Chame esta função quando trocar para outra tela
    public void DeselectTab()
    {
        isActiveScreen = false;
        if (buttonText != null) buttonText.color = normalTextColor;
        underlineBar.SetActive(false);

        // Devolve o controle do fundo para a transição normal do Button.
        if (button != null && buttonImage != null)
        {
            button.transition = transitionOriginal;
            buttonImage.color = button.colors.normalColor;
        }
    }
}
