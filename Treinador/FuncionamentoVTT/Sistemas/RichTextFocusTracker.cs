using TMPro;
using UnityEngine;

/// <summary>
/// Componente leve que vai em CADA TMP_InputField que deve participar do editor
/// Rich Text — tanto no prefab de tópico (título e conteúdo) quanto em qualquer
/// outro campo que você queira formatar no futuro.
///
/// Funcionamento:
///   onSelect   → notifica o RichTextEditorManager que ESTE campo está ativo.
///   onDeselect → notifica que perdeu foco (o Manager mantém o campo salvo para
///                que a toolbar saiba onde escrever mesmo após o deselect).
///
/// Não precisa de configuração manual: ao entrar em cena, busca o Manager pelo
/// singleton. Adicione este script ao GameObject do TMP_InputField na prefab.
/// </summary>
[RequireComponent(typeof(TMP_InputField))]
[DisallowMultipleComponent]
public class RichTextFocusTracker : MonoBehaviour
{
    private TMP_InputField _field;

    private void Awake()
    {
        _field = GetComponent<TMP_InputField>();
    }

    private void OnEnable()
    {
        _field.onSelect.AddListener(OnSelected);
        _field.onDeselect.AddListener(OnDeselected);
    }

    private void OnDisable()
    {
        _field.onSelect.RemoveListener(OnSelected);
        _field.onDeselect.RemoveListener(OnDeselected);
    }

    private void OnSelected(string _)
    {
        RichTextEditorManager.Instance?.NotifyFocus(_field);
    }

    private void OnDeselected(string _)
    {
        RichTextEditorManager.Instance?.NotifyDeselect(_field);
    }
}
