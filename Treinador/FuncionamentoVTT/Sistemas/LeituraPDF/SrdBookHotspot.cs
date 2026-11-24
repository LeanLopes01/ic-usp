using UnityEngine;
using UnityEngine.EventSystems;

public class SrdBookHotspot : MonoBehaviour, IPointerDownHandler, IPointerEnterHandler
{
    [Header("Referência")]
    public SrdBookNavigator navigator;
    public string termo;

    [Header("Comportamento")]
    public bool triggerOnPointerDown = true;
    public bool triggerOnPointerEnter;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (!triggerOnPointerDown) return;
        AbrirTermo();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!triggerOnPointerEnter) return;
        AbrirTermo();
    }

    public void AbrirTermo()
    {
        if (navigator == null || string.IsNullOrWhiteSpace(termo)) return;
        navigator.MostrarPorTermo(termo);
    }
}