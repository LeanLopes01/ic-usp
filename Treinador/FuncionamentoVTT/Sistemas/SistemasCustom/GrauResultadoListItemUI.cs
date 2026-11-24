using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Item da lista de Graus de Resultado do tipo de rolagem ativo, em CustomSystemManager — mostra o
/// id do grau (ex: "critico") + a condição avaliada por AvaliadorDeExpressao (ex: "{d1} == 20"),
/// com um botão pra remover.
///
/// Prefab precisa ter:
///   - TMP_Text: textoId, textoCondicao
///   - Button: botaoRemover
/// </summary>
public class GrauResultadoListItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text textoId;
    [SerializeField] private TMP_Text textoCondicao;
    [SerializeField] private Button   botaoRemover;

    public void Configurar(DefinicaoGrauResultado grau, Action aoRemover)
    {
        if (grau == null) return;

        if (textoId != null) textoId.text = grau.id;
        if (textoCondicao != null) textoCondicao.text = grau.condicao;

        if (botaoRemover != null)
        {
            botaoRemover.onClick.RemoveAllListeners();
            botaoRemover.onClick.AddListener(() => aoRemover?.Invoke());
        }
    }
}
