using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

public class DebugClicks : MonoBehaviour
{
    void Update()
    {
        // Ao clicar com botão esquerdo
        if (Input.GetMouseButtonDown(0))
        {
            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };

            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            if (results.Count > 0)
            {
                Debug.Log("--- INÍCIO DO CLIQUE ---");
                foreach (RaycastResult result in results)
                {
                    Debug.Log($"Mouse clicou em: {result.gameObject.name} (Objeto Pai: {result.gameObject.transform.parent?.name})");
                }
                Debug.Log("--- FIM DO CLIQUE ---");
            }
            else
            {
                Debug.Log("Mouse clicou no vazio (sem UI)");
            }
        }
    }
}