using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Componente do prefab de botão do sumário de regras.
/// Requer um Button e um TMP_Text filho na hierarquia.
/// </summary>
public class SumarioButtonUI : MonoBehaviour
{
    [SerializeField] private Button   botao;
    [SerializeField] private TMP_Text labelTexto;

    public void Setup(string titulo, Action aoClicar)
    {
        if (labelTexto != null)
            labelTexto.text = titulo;

        if (botao != null)
        {
            botao.onClick.RemoveAllListeners();
            botao.onClick.AddListener(() => aoClicar?.Invoke());
        }
    }

    public void DefinirSelecionado(bool selecionado)
    {
        // opcional: use para destacar o botão ativo visualmente
    }
}
