using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Prefab de toggle — representa um campo XML disponível para seleção.
/// Attach: Toggle + TMP_Text (label) + Image opcional (cor de categoria).
/// </summary>
public class FieldToggleUI : MonoBehaviour
{
    [SerializeField] private Toggle    toggle;
    [SerializeField] private TMP_Text  labelText;
    [SerializeField] private Image     categoryBadge; // cor de categoria (opcional)

    public XmlFieldDef FieldDef { get; private set; }

    /// <summary>Disparado quando o toggle muda de estado.</summary>
    public event Action<XmlFieldDef, bool> OnToggled;

    private static readonly Color[] CategoryColors =
    {
        new Color(0.27f, 0.51f, 0.71f), // Identificação — azul
        new Color(0.18f, 0.63f, 0.44f), // Metadados     — verde
        new Color(0.60f, 0.29f, 0.71f), // Aparência     — roxo
        new Color(0.83f, 0.33f, 0.00f), // Listas        — laranja
        new Color(0.75f, 0.22f, 0.17f), // Habilidades   — vermelho
        new Color(0.17f, 0.63f, 0.73f), // Magia         — ciano
        new Color(0.51f, 0.38f, 0.18f), // Progressão    — marrom
        new Color(0.24f, 0.35f, 0.67f), // Atributos     — azul escuro
    };

    private static readonly string[] CategoryNames =
    {
        "Identificação", "Metadados", "Aparência", "Listas",
        "Habilidades",   "Magia",     "Progressão", "Atributos",
    };

    public void Setup(XmlFieldDef def)
    {
        FieldDef = def;

        if (labelText != null)
            labelText.text = def.Label;

        if (categoryBadge != null)
        {
            int idx = Array.IndexOf(CategoryNames, def.Category);
            categoryBadge.color = idx >= 0 ? CategoryColors[idx] : Color.gray;
        }

        if (toggle != null)
        {
            toggle.isOn = false;
            toggle.onValueChanged.RemoveAllListeners();
            toggle.onValueChanged.AddListener(v => OnToggled?.Invoke(FieldDef, v));
        }
    }

    public bool IsOn => toggle != null && toggle.isOn;

    public void SetOn(bool value)
    {
        if (toggle != null) toggle.isOn = value;
    }

    public void ForceOff()
    {
        if (toggle != null) toggle.isOn = false;
    }
}
