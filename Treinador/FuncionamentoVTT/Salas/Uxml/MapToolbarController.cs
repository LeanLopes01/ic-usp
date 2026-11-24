using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Controlador da barra de ferramentas do mapa (UI Toolkit — BarraFerramentas.uxml/
/// BarraFerramentasCSS.uss). Dispara eventos por botão (pra qualquer listener externo) e, pros
/// botões que já têm funcionalidade correspondente pronta em BarraFerramentasManager (Chat,
/// Efeitos), chama direto o toggle já existente. Ferramenta/Dado/Régua/Pincel/Texto/Fog ainda não
/// têm sistema correspondente implementado — os eventos disparam normalmente, só não acionam nada
/// (ver comentários em cada handler).
/// </summary>
public class MapToolbarController : MonoBehaviour
{
    [SerializeField] private UIDocument uiDocument;

    [Header("Conexão com a barra de ferramentas existente (uGUI)")]
    [Tooltip("Usado pra Chat (ToggleChat) e Efeitos (ToggleClima) — os únicos botões com " +
        "funcionalidade correspondente já implementada hoje.")]
    [SerializeField] private BarraFerramentasManager barraFerramentas;

    private VisualElement toolbar;
    private Button expandCollapseBtn;
    private Button chatBtn;
    private Button toolBtn;
    private Button diceBtn;
    private Button rulerBtn;
    private Button brushBtn;
    private Button textBtn;
    private Button fogBtn;
    private Button effectsBtn;

    private Button selectedButton;
    private bool isCollapsed = false;

    // Eventos
    public event Action OnChatClicked;
    public event Action OnToolClicked;
    public event Action OnDiceClicked;
    public event Action OnRulerClicked;
    public event Action OnBrushClicked;
    public event Action OnTextClicked;
    public event Action OnFogClicked;
    public event Action OnEffectsClicked;
    public event Action<bool> OnExpandCollapseToggled;

    private void OnEnable()
    {
        if (uiDocument == null)
            uiDocument = GetComponent<UIDocument>();

        if (barraFerramentas == null)
            barraFerramentas = FindFirstObjectByType<BarraFerramentasManager>();

        InitializeUI();
        RegisterCallbacks();
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
    }

    private void InitializeUI()
    {
        var root = uiDocument.rootVisualElement;

        toolbar = root.Q<VisualElement>(className: "map-toolbar");

        if (toolbar == null)
        {
            Debug.LogError("Toolbar não encontrada! Certifique-se de que o UXML foi carregado corretamente.");
            return;
        }

        // Busca por classe específica de cada botão (não por índice — a ordem/quantidade de
        // elementos no UXML, incluindo separadores, pode mudar sem quebrar isso).
        expandCollapseBtn = toolbar.Q<Button>(className: "expand-collapse-btn");
        chatBtn           = toolbar.Q<Button>(className: "chat-btn");
        toolBtn           = toolbar.Q<Button>(className: "tool-btn");
        diceBtn           = toolbar.Q<Button>(className: "dice-btn");
        rulerBtn          = toolbar.Q<Button>(className: "ruler-btn");
        brushBtn          = toolbar.Q<Button>(className: "brush-btn");
        textBtn           = toolbar.Q<Button>(className: "text-btn");
        fogBtn            = toolbar.Q<Button>(className: "fog-btn");
        effectsBtn        = toolbar.Q<Button>(className: "effects-btn");
    }

    private void RegisterCallbacks()
    {
        if (expandCollapseBtn != null) expandCollapseBtn.clicked += OnExpandCollapseClicked;
        if (chatBtn != null)     chatBtn.clicked     += OnChatBtnClicked;
        if (toolBtn != null)     toolBtn.clicked     += OnToolBtnClicked;
        if (diceBtn != null)     diceBtn.clicked     += OnDiceBtnClicked;
        if (rulerBtn != null)    rulerBtn.clicked    += OnRulerBtnClicked;
        if (brushBtn != null)    brushBtn.clicked    += OnBrushBtnClicked;
        if (textBtn != null)     textBtn.clicked     += OnTextBtnClicked;
        if (fogBtn != null)      fogBtn.clicked      += OnFogBtnClicked;
        if (effectsBtn != null)  effectsBtn.clicked  += OnEffectsBtnClicked;
    }

    private void UnregisterCallbacks()
    {
        if (expandCollapseBtn != null) expandCollapseBtn.clicked -= OnExpandCollapseClicked;
        if (chatBtn != null)     chatBtn.clicked     -= OnChatBtnClicked;
        if (toolBtn != null)     toolBtn.clicked     -= OnToolBtnClicked;
        if (diceBtn != null)     diceBtn.clicked     -= OnDiceBtnClicked;
        if (rulerBtn != null)    rulerBtn.clicked    -= OnRulerBtnClicked;
        if (brushBtn != null)    brushBtn.clicked    -= OnBrushBtnClicked;
        if (textBtn != null)     textBtn.clicked     -= OnTextBtnClicked;
        if (fogBtn != null)      fogBtn.clicked      -= OnFogBtnClicked;
        if (effectsBtn != null)  effectsBtn.clicked  -= OnEffectsBtnClicked;
    }

    // ── Handlers por botão ───────────────────────────────────────────────────
    // Nomeados (em vez de lambda inline) pra que RegisterCallbacks/UnregisterCallbacks removam
    // exatamente o mesmo delegate — lambdas novas a cada chamada nunca são removidas de fato.

    private void OnChatBtnClicked()
    {
        SelectButton(chatBtn);
        barraFerramentas?.ToggleChat();
        OnChatClicked?.Invoke();
    }

    private void OnEffectsBtnClicked()
    {
        SelectButton(effectsBtn);
        barraFerramentas?.ToggleClima();
        OnEffectsClicked?.Invoke();
    }

    // Ferramenta/Dado/Régua/Pincel/Texto/Fog: nenhum sistema correspondente existe ainda no
    // projeto — só o evento dispara, sem ação. Conectar aqui quando a funcionalidade existir.
    private void OnToolBtnClicked()   { SelectButton(toolBtn);   OnToolClicked?.Invoke(); }
    private void OnDiceBtnClicked()   { SelectButton(diceBtn);   OnDiceClicked?.Invoke(); }
    private void OnRulerBtnClicked()  { SelectButton(rulerBtn);  OnRulerClicked?.Invoke(); }
    private void OnBrushBtnClicked()  { SelectButton(brushBtn);  OnBrushClicked?.Invoke(); }
    private void OnTextBtnClicked()   { SelectButton(textBtn);   OnTextClicked?.Invoke(); }
    private void OnFogBtnClicked()    { SelectButton(fogBtn);    OnFogClicked?.Invoke(); }

    private void OnExpandCollapseClicked()
    {
        isCollapsed = !isCollapsed;

        if (toolbar != null)
        {
            if (isCollapsed)
            {
                toolbar.AddToClassList("collapsed");
                UpdateExpandButtonLabel("⟩");
            }
            else
            {
                toolbar.RemoveFromClassList("collapsed");
                UpdateExpandButtonLabel("⟨");
            }
        }

        OnExpandCollapseToggled?.Invoke(isCollapsed);
    }

    private void UpdateExpandButtonLabel(string label)
    {
        if (expandCollapseBtn != null)
        {
            var labelElement = expandCollapseBtn.Q<Label>(className: "toolbar-icon");
            if (labelElement != null)
                labelElement.text = label;
        }
    }

    private void SelectButton(Button newButton)
    {
        if (newButton == null) return;

        // Remove seleção anterior
        if (selectedButton != null && selectedButton != newButton)
        {
            selectedButton.RemoveFromClassList("selected");
        }

        // Alterna a seleção do botão clicado
        if (newButton.ClassListContains("selected"))
        {
            newButton.RemoveFromClassList("selected");
            selectedButton = null;
        }
        else
        {
            newButton.AddToClassList("selected");
            selectedButton = newButton;
        }
    }

    /// <summary>
    /// Define qual ferramenta está selecionada
    /// </summary>
    public void SelectTool(ToolType tool)
    {
        Button buttonToSelect = tool switch
        {
            ToolType.Chat => chatBtn,
            ToolType.Tool => toolBtn,
            ToolType.Dice => diceBtn,
            ToolType.Ruler => rulerBtn,
            ToolType.Brush => brushBtn,
            ToolType.Text => textBtn,
            ToolType.Fog => fogBtn,
            ToolType.Effects => effectsBtn,
            _ => null
        };

        if (buttonToSelect != null)
            SelectButton(buttonToSelect);
    }

    /// <summary>
    /// Alterna entre modo horizontal e vertical
    /// </summary>
    public void SetVerticalMode(bool vertical)
    {
        if (toolbar != null)
        {
            if (vertical)
                toolbar.AddToClassList("vertical");
            else
                toolbar.RemoveFromClassList("vertical");
        }
    }
}

public enum ToolType
{
    Chat,
    Tool,
    Dice,
    Ruler,
    Brush,
    Text,
    Fog,
    Effects
}
