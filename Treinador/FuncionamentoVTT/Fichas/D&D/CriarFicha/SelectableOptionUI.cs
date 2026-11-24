using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System;

// Este script vai no PREFAB da opção (ex: o texto do Traço de Personalidade)
public class SelectableOptionUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI")]
    public TMP_Text textComponent; // Arraste o componente de texto aqui no inspector do Prefab
    
    // Evento que avisa o Manager que este item foi escolhido
    public Action<SelectableOptionUI> OnOptionConfirmed;

    public string OptionText => textComponent.text;

    // Detecta cliques do mouse/toque
    public void OnPointerClick(PointerEventData eventData)
    {
        // Verifica se foi um clique duplo (intervalo rápido)
        if (eventData.clickCount == 2)
        {
            // Dispara o evento avisando "Fui escolhido!"
            OnOptionConfirmed?.Invoke(this);
        }
    }

    public void SetText(string text)
    {
        if (textComponent != null) textComponent.text = text;
    }
}