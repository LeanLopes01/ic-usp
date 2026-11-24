using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System;

public class StepOptionUI : MonoBehaviour, IPointerClickHandler
{
    public TMP_Text labelText;
    public string OptionId { get; private set; }
    
    private Action<StepOptionUI> onSingleClick;
    private Action<StepOptionUI> onDoubleClick;

    // No StepOptionUI.cs
    public void Setup(string id, string text, Action<StepOptionUI> onSingle, Action<StepOptionUI> onDouble)
    {
        OptionId = id;
        if (labelText == null) labelText = GetComponent<TMP_Text>();
        Debug.Log($"[StepOptionUI.Setup] id={id} cor={labelText?.color} fonte={labelText?.font?.name} fontSize={labelText?.fontSize}");
        labelText.text = text;
        onSingleClick = onSingle;
        onDoubleClick = onDouble;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left) return;

        if (eventData.clickCount == 1)
        {
            Debug.Log($"[StepOptionUI.OnPointerClick] single click id={OptionId} cor={labelText?.color}");
            onSingleClick?.Invoke(this);
        }
        else if (eventData.clickCount == 2)
        {
            Debug.Log($"[StepOptionUI.OnPointerClick] double click id={OptionId} cor={labelText?.color}");
            onDoubleClick?.Invoke(this);
        }
    }

    public void SetVisualSelecionado(bool selecionado)
    {
        Debug.Log($"[StepOptionUI.SetVisualSelecionado] id={OptionId} selecionado={selecionado} cor={labelText?.color} fonte={labelText?.font?.name}");
    }
}