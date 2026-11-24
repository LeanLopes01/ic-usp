using TMPro;
using UnityEngine;
using UnityEngine.UI;
// FlexibleColorPicker é um tipo (MonoBehaviour), não um namespace —
// não requer 'using'. O tipo é acessível diretamente pelo nome.

/// <summary>
/// Gerencia os três painéis popup de formatação (Cor, Tamanho, Alinhamento).
///
/// POR QUE OS POPUPS NÃO DESTROEM A SELEÇÃO:
/// O RichTextEditorManager salva as posições de seleção no Update() enquanto o
/// InputField tem foco. Quando o usuário clica num botão para abrir um popup,
/// o OnDeselect do InputField é disparado, mas as posições JÁ foram salvas no
/// frame anterior. Portanto, abrirmos o popup e interagirmos com seus controles
/// internos (Slider, Dropdown, ColorPicker) não apaga os dados salvos no Manager.
/// Ao clicar "Aplicar", simplesmente lemos esses dados e chamamos o método do Manager.
/// </summary>
[DisallowMultipleComponent]
public class RichTextPopupsUI : MonoBehaviour
{
    [Header("Referência ao Manager Principal")]
    public RichTextEditorManager editorManager;

    // ─────────────────────────────────────────────────────────────────────────
    // Popup de Cor
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Popup – Cor")]
    [Tooltip("Painel raiz do popup de cor. Começa desativado.")]
    public GameObject colorPopupPanel;

    [Tooltip("Flexible Color Picker (asset de terceiros). Lemos fcp.color ao aplicar.")]
    public FlexibleColorPicker fcp;

    // ─────────────────────────────────────────────────────────────────────────
    // Popup de Tamanho
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Popup – Tamanho")]
    [Tooltip("Painel raiz do popup de tamanho. Começa desativado.")]
    public GameObject sizePopupPanel;

    [Tooltip("Slider de porcentagem de tamanho (min=50, max=200).")]
    public Slider sizeSlider;

    [Tooltip("Texto que exibe o valor atual do slider (ex: '100%').")]
    public TMP_Text sizeValueLabel;

    // ─────────────────────────────────────────────────────────────────────────
    // Popup de Alinhamento
    // ─────────────────────────────────────────────────────────────────────────

    [Header("Popup – Alinhamento")]
    [Tooltip("Painel raiz do popup de alinhamento. Começa desativado.")]
    public GameObject alignmentPopupPanel;

    [Tooltip("Dropdown com opções: Esquerda, Centro, Direita, Justificado.")]
    public TMP_Dropdown alignmentDropdown;

    // Mapeamento das opções do Dropdown para as strings aceitas pelo TMP.
    private static readonly string[] AlignmentValues = { "left", "center", "right", "justified" };

    // ─────────────────────────────────────────────────────────────────────────
    // Unity Lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void Start()
    {
        // Garante que todos os popups comecem fechados.
        SetPopupActive(colorPopupPanel,     false);
        SetPopupActive(sizePopupPanel,      false);
        SetPopupActive(alignmentPopupPanel, false);

        // Atualiza o label do slider ao mover (não aguarda o botão Aplicar).
        if (sizeSlider != null)
            sizeSlider.onValueChanged.AddListener(OnSizeSliderChanged);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Abertura / Fechamento de Popups
    // ─────────────────────────────────────────────────────────────────────────

    // Cada método Toggle fecha os outros dois antes de abrir o seu.
    // Isso impede sobreposição de popups e mantém a UI limpa.

    public void ToggleColorPopup()
    {
        bool opening = !colorPopupPanel.activeSelf;
        SetPopupActive(sizePopupPanel,      false);
        SetPopupActive(alignmentPopupPanel, false);
        SetPopupActive(colorPopupPanel,     opening);
    }

    public void ToggleSizePopup()
    {
        bool opening = !sizePopupPanel.activeSelf;
        SetPopupActive(colorPopupPanel,     false);
        SetPopupActive(alignmentPopupPanel, false);
        SetPopupActive(sizePopupPanel,      opening);

        // Inicializa o label ao abrir.
        if (opening) UpdateSizeLabel((int)sizeSlider.value);
    }

    public void ToggleAlignmentPopup()
    {
        bool opening = !alignmentPopupPanel.activeSelf;
        SetPopupActive(colorPopupPanel, false);
        SetPopupActive(sizePopupPanel,  false);
        SetPopupActive(alignmentPopupPanel, opening);
    }

    private static void SetPopupActive(GameObject panel, bool active)
    {
        if (panel != null) panel.SetActive(active);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Popup de Cor — Aplicar
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lê a cor escolhida no Flexible Color Picker, converte para hex e
    /// delega ao Manager. Chamado pelo botão "Aplicar" do popup de cor.
    /// </summary>
    public void OnApplyColorClicked()
    {
        if (fcp == null)
        {
            VTTLog.UIWarn("RichTextPopupsUI: FlexibleColorPicker não atribuído.");
            return;
        }

        // ColorUtility.ToHtmlStringRGB retorna "RRGGBB" sem o '#'.
        string hex = "#" + ColorUtility.ToHtmlStringRGB(fcp.color);
        editorManager.ApplyColor(hex);

        SetPopupActive(colorPopupPanel, false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Popup de Tamanho — Slider e Aplicar
    // ─────────────────────────────────────────────────────────────────────────

    private void OnSizeSliderChanged(float value)
    {
        UpdateSizeLabel((int)value);
    }

    private void UpdateSizeLabel(int value)
    {
        if (sizeValueLabel != null)
            sizeValueLabel.text = $"{value}%";
    }

    /// <summary>
    /// Lê o valor atual do slider e aplica a tag de tamanho.
    /// Chamado pelo botão "Aplicar" do popup de tamanho.
    /// </summary>
    public void OnApplySizeClicked()
    {
        int percentage = (int)sizeSlider.value;
        editorManager.ApplySize(percentage);

        SetPopupActive(sizePopupPanel, false);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Popup de Alinhamento — Aplicar
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Lê a opção selecionada no Dropdown e aplica a tag de alinhamento.
    /// Chamado pelo botão "Aplicar" do popup de alinhamento.
    /// </summary>
    public void OnApplyAlignmentClicked()
    {
        int idx = alignmentDropdown.value;

        if (idx < 0 || idx >= AlignmentValues.Length)
        {
            VTTLog.UIWarn("RichTextPopupsUI: índice de alinhamento inválido.");
            return;
        }

        editorManager.ApplyAlignment(AlignmentValues[idx]);
        SetPopupActive(alignmentPopupPanel, false);
    }
}
