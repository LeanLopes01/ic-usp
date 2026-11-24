using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Item da lista de campos já definidos na Estrutura (Schema) de um tópico, em
/// CustomSystemManager — mostra o Label + o Tipo escolhido pelo criador do sistema, com um botão
/// pra remover o campo da estrutura.
///
/// Prefab precisa ter:
///   - TMP_Text: textoLabel, textoTipo
///   - Button: botaoRemover
/// </summary>
public class SchemaCampoItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text textoLabel;
    [SerializeField] private TMP_Text textoTipo;
    [SerializeField] private Button   botaoRemover;

    public void Configurar(string label, string tipoExibicao, Action aoRemover)
    {
        if (textoLabel != null) textoLabel.text = label;
        if (textoTipo  != null) textoTipo.text  = tipoExibicao;

        if (botaoRemover != null)
        {
            botaoRemover.onClick.RemoveAllListeners();
            botaoRemover.onClick.AddListener(() => aoRemover?.Invoke());
        }
    }
}
