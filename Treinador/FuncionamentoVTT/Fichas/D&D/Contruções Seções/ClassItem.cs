using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System;

public class ClassItem : MonoBehaviour, IPointerClickHandler
{
    public string classKey;      // chave/string id do XML (ex: "fighter")
    public int classDbId = 0;    // id numérico da tabela classes_dnd (se disponível)
    public string xmlContent;    // xml bruto (opcional)
    public string displayName;   // nome para exibir

    // eventos externos (sets by ClassListLoader)
    public Action<string, int, string> onSingleClick; // (classKey, classDbId, xml)
    public Action<string, int, string> onDoubleClick; // (classKey, classDbId, xml)

    // double click detection
    private float lastClickTime = 0f;
    private const float doubleClickThreshold = 0.35f; // ajustar se quiser

    public void OnPointerClick(PointerEventData eventData)
    {
        float t = Time.unscaledTime;
        if (t - lastClickTime <= doubleClickThreshold)
        {
            // double click
            lastClickTime = 0f;
            onDoubleClick?.Invoke(classKey, classDbId, xmlContent);
        }
        else
        {
            // single click: agendar possível single (se não vier double)
            lastClickTime = t;
            // chamar single imediatamente (consistente com RaceItem)
            onSingleClick?.Invoke(classKey, classDbId, xmlContent);
        }
    }
}
