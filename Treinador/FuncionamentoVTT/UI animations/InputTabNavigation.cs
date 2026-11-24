using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class InputTabNavigation : MonoBehaviour
{
    [Tooltip("Próximo campo que deve receber o foco ao pressionar Tab ou Enter.")]
    public Selectable nextInput;

    void Update()
    {
        // Verifica se o objeto atual está selecionado
        if (EventSystem.current.currentSelectedGameObject == gameObject)
        {
            // Ao pressionar Tab ou Enter
            if (Input.GetKeyDown(KeyCode.Tab) || Input.GetKeyDown(KeyCode.Return))
            {
                // Remove o foco do atual
                if (TryGetComponent<TMP_InputField>(out var tmp))
                    tmp.DeactivateInputField();
                else if (TryGetComponent<InputField>(out var input))
                    input.DeactivateInputField();

                // Move o foco para o próximo input
                if (nextInput != null)
                    nextInput.Select();
            }
        }
    }
}
