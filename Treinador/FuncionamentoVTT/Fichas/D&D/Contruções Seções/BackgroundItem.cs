using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System;

public class BackgroundItem : MonoBehaviour, IPointerClickHandler
{
    public string backgroundKey;      // chave/string id do XML (ex: "heroi-do-povo")
    public int backgroundDbId = 0;    // id numérico da tabela antecedentes_dnd (se disponível)
    public string xmlContent;         // xml bruto (opcional)
    public string displayName;        // nome para exibir

    // eventos externos (sets by BackgroundListLoader)
    public Action<string, int, string> onSingleClick; // (backgroundKey, backgroundDbId, xml)
    public Action<string, int, string> onDoubleClick; // (backgroundKey, backgroundDbId, xml)

    // double click detection
    private float lastClickTime = 0f;
    private const float doubleClickThreshold = 0.35f;

    public void OnPointerClick(PointerEventData eventData)
    {
        float t = Time.unscaledTime;
        if (t - lastClickTime <= doubleClickThreshold)
        {
            // double click
            lastClickTime = 0f;
            onDoubleClick?.Invoke(backgroundKey, backgroundDbId, xmlContent);
        }
        else
        {
            // single click: agendar possível single (se não vier double)
            lastClickTime = t;
            // chamar single imediatamente (usamos assim para mostrar detalhes sem delay)
            onSingleClick?.Invoke(backgroundKey, backgroundDbId, xmlContent);
        }
    }
}