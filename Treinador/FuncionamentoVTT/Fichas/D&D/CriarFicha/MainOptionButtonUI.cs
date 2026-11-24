using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;

public class MainOptionButtonUI : MonoBehaviour, IPointerClickHandler
{
    public TMP_Text labelText;
    public Button   button;
    public Toggle   selectionToggle;

    public string OptionId { get; private set; }

    private Action<MainOptionButtonUI> onSingleClick;
    private Action<MainOptionButtonUI> onDoubleClick;

    private float _lastClickTime = -1f;
    private const float DoubleClickInterval = 0.3f;

    // Evita que SetVisualSelected (chamado de fora pra refletir estado — highlight de single click,
    // ou a confirmação já processada) dispare de novo o listener do Toggle como se fosse um clique
    // real do jogador, o que causaria confirmar→SetVisualSelected→onValueChanged→confirmar de novo
    // (ou desfazer a seleção que acabou de ser confirmada). Mesmo padrão de SubChoiceOptionUI.
    private bool _atualizandoToggleInternamente;

    public void Setup(string id, string label, Action<MainOptionButtonUI> onSingle, Action<MainOptionButtonUI> onDouble)
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
        Debug.Log($"[MainOptionButtonUI.OnButtonClick] id={OptionId} delta={now - _lastClickTime:F3}");
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
        Debug.Log($"[MainOptionButtonUI.OnPointerClick] id={OptionId} clickCount={eventData.clickCount} (sem Button)");
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
