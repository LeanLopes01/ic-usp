using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;

// Prefab 4: opção de sub-passo com Toggle + TMP_Text + Button.
// Clique único no texto/Button mostra os detalhes (mesmo padrão de MainOptionButtonUI); duplo
// clique no texto/Button OU marcar o Toggle confirma a escolha diretamente — o Toggle deixou de
// ser puramente visual e agora é outro jeito de selecionar a opção, sem precisar do duplo clique.
public class SubChoiceOptionUI : MonoBehaviour, IPointerClickHandler
{
    public TMP_Text labelText;
    public Button   button;
    public Toggle   selectionToggle;

    public string OptionId { get; private set; }

    private Action<SubChoiceOptionUI> onSingleClick;
    private Action<SubChoiceOptionUI> onDoubleClick;

    private float _lastClickTime = -1f;
    private const float DoubleClickInterval = 0.3f;

    // Evita que SetVisualSelected (chamado pelo próprio StepContainerUI pra refletir estado —
    // highlight de single click, ou a confirmação já processada) dispare de novo o listener do
    // Toggle como se fosse um clique real do jogador, o que causaria confirmar→SetVisualSelected→
    // onValueChanged→confirmar de novo (ou desfazer a seleção que acabou de ser confirmada).
    private bool _atualizandoToggleInternamente;

    public void Setup(string id, string label, Action<SubChoiceOptionUI> onSingle, Action<SubChoiceOptionUI> onDouble)
    {
        OptionId = id;
        if (labelText) labelText.text = label;
        onSingleClick = onSingle;
        onDoubleClick = onDouble;
        _lastClickTime = -1f;

        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(OnButtonClick);
        }

        if (selectionToggle != null)
        {
            selectionToggle.interactable = true;
            selectionToggle.onValueChanged.RemoveListener(OnToggleValueChanged);
            selectionToggle.onValueChanged.AddListener(OnToggleValueChanged);
        }

        SetVisualSelected(false);
    }

    // Detecta duplo-clique por intervalo de tempo.
    // Usado tanto quando Button é filho (único caminho) quanto quando está no mesmo objeto
    // (nesse caso OnPointerClick também dispara, mas é ignorado via guard abaixo).
    private void OnButtonClick()
    {
        float now = Time.unscaledTime;
        if (_lastClickTime > 0f && (now - _lastClickTime) <= DoubleClickInterval)
        {
            _lastClickTime = -1f;
            onDoubleClick?.Invoke(this);
        }
        else
        {
            _lastClickTime = now;
            onSingleClick?.Invoke(this);
        }
    }

    // Marcar o Toggle (clique real do jogador nele) confirma a escolha direto, sem precisar de
    // duplo clique no texto/Button.
    private void OnToggleValueChanged(bool isOn)
    {
        if (_atualizandoToggleInternamente) return;
        if (isOn) onDoubleClick?.Invoke(this);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;
        // Se Button está no mesmo objeto, OnButtonClick já tratou tudo — ignora aqui.
        if (button != null) return;
        if (eventData.clickCount == 1) onSingleClick?.Invoke(this);
        else if (eventData.clickCount == 2) onDoubleClick?.Invoke(this);
    }

    public void SetVisualSelected(bool selected)
    {
        if (selectionToggle == null) return;
        _atualizandoToggleInternamente = true;
        selectionToggle.isOn = selected;
        _atualizandoToggleInternamente = false;
    }
}
