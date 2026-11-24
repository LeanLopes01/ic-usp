using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class HoverGradientFix : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Componentes")]
    public Image buttonImage;
    public TextMeshProUGUI buttonText;

    [Header("Configurações do Background")]
    [Tooltip("Pode deixar vazio (None) se for apenas a cor sólida")]
    public Sprite normalSprite; 
    [Tooltip("Coloque o seu Sprite de degradê aqui")]
    public Sprite gradientHoverSprite; 
    
    // Cores do Background
    public Color normalImageColor = new Color(0.278f, 0.278f, 0.278f, 1f); // O cinza #474747
    public Color hoverImageColor = Color.white; // Branco para não tingir o sprite de degradê

    [Header("Configurações do Texto")]
    public Color normalTextColor = Color.white;
    public Color hoverTextColor = new Color(0.278f, 0.278f, 0.278f, 1f); // #474747

    [Header("Configuração Especial - Botão Excluir")]
    [Tooltip("Quando marcado, o hover usa excluirHoverSprite/excluirHoverImageColor no lugar do gradiente padrão. Setado em runtime por quem monta o botão (ex: MenuAtalhoBotaoUI) — não precisa marcar manualmente no prefab genérico.")]
    public bool ehBotaoExcluir = false;
    [Tooltip("Sprite de fundo usado no hover só quando ehBotaoExcluir estiver marcado (ex: um degradê vermelho).")]
    public Sprite excluirHoverSprite;
    public Color excluirHoverImageColor = Color.white;

    void Awake()
    {
        // Garante que o botão comece no estado normal
        ApplyNormalState();
    }

    /// <summary>
    /// Alterna este botão para o visual especial de "excluir" (ou de volta ao padrão). Chamado por
    /// quem monta o botão dinamicamente (ex: MenuAtalhoBotaoUI.Configurar) — o prefab genérico não
    /// precisa saber de antemão se vai ser usado como opção de exclusão.
    /// </summary>
    public void ConfigurarComoExcluir(bool ehExcluir)
    {
        ehBotaoExcluir = ehExcluir;
        ApplyNormalState();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        ApplyHoverState();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ApplyNormalState();
    }

    private void ApplyNormalState()
    {
        if (buttonImage != null)
        {
            buttonImage.sprite = normalSprite;
            buttonImage.color = normalImageColor;
        }
        
        if (buttonText != null)
        {
            buttonText.color = normalTextColor;
        }
    }

    private void ApplyHoverState()
    {
        if (buttonImage != null)
        {
            buttonImage.sprite = ehBotaoExcluir ? excluirHoverSprite : gradientHoverSprite;
            buttonImage.color = ehBotaoExcluir ? excluirHoverImageColor : hoverImageColor;
        }

        if (buttonText != null)
        {
            buttonText.color = hoverTextColor;
        }
    }
}