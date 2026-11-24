using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Prefab de um filtro individual (toggle + texto) usado nos painéis de filtro expansíveis
// de Magias, Ações, Inventário e Habilidades. É instanciado por código — nome/tipo do
// filtro vem de FichaGeralManager (TagsFiltroX), não é montado manualmente na cena.
public class FiltroChipToggleUI : MonoBehaviour
{
    [Header("Referências de UI")]
    public Toggle toggle;
    public TMP_Text label;

    public string Tag { get; private set; }

    public void Configurar(string tag, Action<string, bool> onToggleChanged)
    {
        Tag = tag;
        if (label != null) label.text = tag;
        RegistrarCallback(onToggleChanged);
    }

    private void RegistrarCallback(Action<string, bool> onToggleChanged)
    {
        if (toggle == null) return;

        toggle.onValueChanged.RemoveAllListeners();
        toggle.SetIsOnWithoutNotify(false);
        toggle.onValueChanged.AddListener((isOn) => onToggleChanged?.Invoke(Tag, isOn));
    }
}
