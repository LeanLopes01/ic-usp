using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Item da lista de tópicos em CustomSystemManager — substitui o antigo TopicNavButtonUI (nav de
/// wizard linear, um tópico de cada vez) por um item de lista com DOIS botões: Selecionar (ver
/// CustomSystemManager.SelecionarTopico) e Editar (abre a lista de entradas/arquivos XML do
/// tópico, ver CustomSystemManager.AbrirTopico). Sem destaque visual de seleção — mesmo formato
/// simples de EntradaListItemUI (texto + botão), já que os dois passaram a compartilhar o mesmo
/// prefab.
///
/// Prefab precisa ter:
///   - TMP_Text: textoTopico
///   - Button: botaoSelecionar, botaoEditar
/// </summary>
public class TopicoListItemUI : MonoBehaviour
{
    [SerializeField] private TMP_Text textoTopico;
    [SerializeField] private Button   botaoSelecionar;
    [SerializeField] private Button   botaoEditar;

    public void Configurar(string topicoId, Action aoSelecionar, Action aoEditar)
    {
        if (textoTopico != null) textoTopico.text = topicoId.Replace("-", " ");

        if (botaoSelecionar != null)
        {
            botaoSelecionar.onClick.RemoveAllListeners();
            botaoSelecionar.onClick.AddListener(() => aoSelecionar?.Invoke());
        }

        if (botaoEditar != null)
        {
            botaoEditar.onClick.RemoveAllListeners();
            botaoEditar.onClick.AddListener(() => aoEditar?.Invoke());
        }
    }
}
