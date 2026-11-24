using TMPro;
using UnityEngine;

/// <summary>
/// Manager singleton do editor Rich Text. Não precisa de referência direta a nenhum
/// TMP_InputField — qualquer campo da cena que tenha um RichTextFocusTracker registra
/// automaticamente o foco aqui. Isso permite que N prefabs instanciados (tópicos,
/// caixas de texto, etc.) compartilhem a mesma toolbar de formatação sem nenhuma
/// configuração extra por instância.
/// </summary>
[DisallowMultipleComponent]
public class RichTextEditorManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // Singleton
    // ─────────────────────────────────────────────────────────────────────────

    public static RichTextEditorManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Estado do campo ativo
    // ─────────────────────────────────────────────────────────────────────────

    // Campo que recebeu foco por último. Permanece válido após o deselect para
    // que os botões da toolbar saibam onde aplicar a formatação.
    private TMP_InputField _activeField;

    // Posições de seleção salvas do _activeField (string index, não char index).
    private int _savedAnchor;
    private int _savedFocus;

    // True apenas enquanto _activeField está fisicamente focado neste frame.
    private bool _isFocused;

    // ─────────────────────────────────────────────────────────────────────────
    // Update — rastreia seleção em tempo real
    // ─────────────────────────────────────────────────────────────────────────

    private void Update()
    {
        // Salva as posições a cada frame enquanto o campo está focado.
        // Quando o usuário clica num botão da toolbar, o OnDeselect do
        // InputField é disparado ANTES do onClick do botão. Como salvamos
        // no frame anterior, as posições ainda são as corretas.
        if (_isFocused && _activeField != null)
        {
            _savedAnchor = _activeField.selectionStringAnchorPosition;
            _savedFocus  = _activeField.selectionStringFocusPosition;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // API pública chamada pelo RichTextFocusTracker (em cada prefab)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Chamado pelo RichTextFocusTracker quando UM campo específico recebe foco.
    /// Atualiza qual campo está ativo para os próximos comandos da toolbar.
    /// </summary>
    public void NotifyFocus(TMP_InputField field)
    {
        _activeField = field;
        _isFocused   = true;
    }

    /// <summary>
    /// Chamado pelo RichTextFocusTracker quando o campo perde foco.
    /// Mantém _activeField para que a toolbar saiba onde escrever,
    /// mas para de rastrear a seleção (ela já foi salva).
    /// </summary>
    public void NotifyDeselect(TMP_InputField field)
    {
        // Ignora se outro campo já tomou o foco antes deste deselect chegar
        // (pode ocorrer quando o usuário clica diretamente de um campo para outro).
        if (_activeField == field)
            _isFocused = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Método Core de Formatação
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Envolve o texto selecionado do campo ativo com as tags fornecidas.
    /// Funciona com qualquer campo que tenha sido focado por último.
    /// </summary>
    public void WrapSelectedText(string tagOpen, string tagClose)
    {
        if (_activeField == null) return;

        string fullText = _activeField.text;

        // Normaliza para start <= end independente da direção da seleção.
        int start = Mathf.Min(_savedAnchor, _savedFocus);
        int end   = Mathf.Max(_savedAnchor, _savedFocus);

        // Clamp defensivo: o texto pode ter mudado entre o save e o clique.
        start = Mathf.Clamp(start, 0, fullText.Length);
        end   = Mathf.Clamp(end,   0, fullText.Length);

        string before   = fullText.Substring(0, start);
        string selected = fullText.Substring(start, end - start);
        string after    = fullText.Substring(end);

        _activeField.text = before + tagOpen + selected + tagClose + after;

        int newCursorPos = start + tagOpen.Length + selected.Length + tagClose.Length;
        _activeField.stringPosition = newCursorPos;

        // Reativa o campo para o usuário continuar digitando sem clicar de novo.
        _activeField.ActivateInputField();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Métodos de Formatação Diretos (botões B / I / U)
    // ─────────────────────────────────────────────────────────────────────────

    public void ApplyBold()      => WrapSelectedText("<b>",  "</b>");
    public void ApplyItalic()    => WrapSelectedText("<i>",  "</i>");
    public void ApplyUnderline() => WrapSelectedText("<u>",  "</u>");

    // ─────────────────────────────────────────────────────────────────────────
    // Métodos de Formatação com Parâmetros (chamados pelos Popups)
    // ─────────────────────────────────────────────────────────────────────────

    /// <param name="hexCode">Cor hex, ex: "#FF0000"</param>
    public void ApplyColor(string hexCode)    => WrapSelectedText($"<color={hexCode}>", "</color>");

    /// <param name="percentage">Tamanho relativo, ex: 150 → size=150%</param>
    public void ApplySize(int percentage)     => WrapSelectedText($"<size={percentage}%>", "</size>");

    /// <param name="alignType">left | center | right | justified</param>
    public void ApplyAlignment(string alignType) => WrapSelectedText($"<align={alignType}>", "</align>");
}
