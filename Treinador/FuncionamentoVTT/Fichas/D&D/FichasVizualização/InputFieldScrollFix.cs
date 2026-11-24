using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class InputFieldScrollFix : MonoBehaviour, IScrollHandler
{
    private ScrollRect parentScrollRect;

    void Start()
    {
        // Procura o ScrollRect subindo na hierarquia de objetos
        parentScrollRect = GetComponentInParent<ScrollRect>();

        if (parentScrollRect == null)
        {
            Debug.LogWarning("InputFieldScrollFix: Nenhum ScrollRect foi encontrado nos pais deste objeto!");
        }
    }

    // Esse método é chamado automaticamente quando o mouse scrolla em cima do InputField
    public void OnScroll(PointerEventData eventData)
    {
        if (parentScrollRect != null)
        {
            // Repassa o evento de scroll para o ScrollRect pai resolver
            parentScrollRect.OnScroll(eventData);
        }
    }
}