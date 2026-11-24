using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Item da lista de entradas (arquivos XML) de um tópico aberto em CustomSystemManager — clicar
/// seleciona a entrada e abre o editor completo dela (toggles pra entrada nova / campos já salvos
/// pra uma existente, ver CustomSystemManager.AbrirEntradaExistente/ConfirmarCamposDaNovaEntrada).
///
/// Prefab precisa ter:
///   - TMP_Text: textoNome
///   - Button: botaoSelecionar
/// </summary>
public class EntradaListItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text textoNome;
    [SerializeField] private Button   botaoSelecionar;

    public void Configurar(string nomeExibicao, Action aoSelecionar)
    {
        if (textoNome != null) textoNome.text = nomeExibicao;

        if (botaoSelecionar != null)
        {
            botaoSelecionar.onClick.RemoveAllListeners();
            botaoSelecionar.onClick.AddListener(() => aoSelecionar?.Invoke());
        }
    }
}
