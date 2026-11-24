using UnityEngine;
using UnityEngine.EventSystems;

public class TokenResizeHandle : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public System.Action<float> OnDeltaX;

    // 1f = handle direito (direita aumenta), -1f = handle esquerdo (esquerda aumenta)
    public float dragMultiplier = 1f;

    private float _xAnterior;

    public void OnPointerDown(PointerEventData eventData) => _xAnterior = eventData.position.x;

    public void OnDrag(PointerEventData eventData)
    {
        float delta = (eventData.position.x - _xAnterior) * dragMultiplier;
        _xAnterior = eventData.position.x;
        OnDeltaX?.Invoke(delta);
    }

    public void OnPointerUp(PointerEventData eventData) { }
}
