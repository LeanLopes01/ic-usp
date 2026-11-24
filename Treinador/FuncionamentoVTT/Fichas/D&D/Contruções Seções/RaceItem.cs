using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System;

public class RaceItem : MonoBehaviour, IPointerClickHandler
{
    public string raceKey;      // chave/string id do XML (ex: "human")
    public int raceDbId = 0;    // id numérico da tabela racas_dnd (se disponível)
    public string xmlContent;   // xml bruto (opcional)
    public string displayName;  // nome para exibir

    // eventos externos (sets by RaceListLoader)
    public Action<string, int, string> onSingleClick; // (raceKey, raceDbId, xml)
    public Action<string, int, string> onDoubleClick; // (raceKey, raceDbId, xml)

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
            onDoubleClick?.Invoke(raceKey, raceDbId, xmlContent);
        }
        else
        {
            // single click: agendar possível single (se não vier double)
            lastClickTime = t;
            // chamar single imediatamente (usamos assim para mostrar detalhes sem delay)
            onSingleClick?.Invoke(raceKey, raceDbId, xmlContent);
        }
    }
}
