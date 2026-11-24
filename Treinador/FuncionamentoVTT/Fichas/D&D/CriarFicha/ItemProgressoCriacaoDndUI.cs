using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ItemProgressoCriacaoDndUI : MonoBehaviour
{
    [Header("Elementos do Prefab")]
    public Toggle toggleVisual;
    public Button botaoNavegacao;
    public TMP_Text textoEtapa;
    public TMP_Text textoDetalhes;
    public Image imagemConcluida;

    private void Awake()
    {
        DefinirConcluida(false);
    }

    public void Configurar(string titulo, string detalhes, bool concluida, UnityAction onClick = null)
    {
        if (textoEtapa != null)
        {
            textoEtapa.text = titulo ?? string.Empty;
        }

        if (textoDetalhes != null)
        {
            textoDetalhes.text = detalhes ?? string.Empty;
        }

        DefinirConcluida(concluida);

        if (botaoNavegacao != null && onClick != null)
        {
            botaoNavegacao.onClick.RemoveAllListeners();
            botaoNavegacao.onClick.AddListener(onClick);
        }
    }

    public void DefinirInteratividade(bool interactable)
    {
        if (botaoNavegacao != null)
        {
            botaoNavegacao.interactable = interactable;
        }

        if (toggleVisual != null)
        {
            toggleVisual.interactable = false;
        }
    }

    public void DefinirConcluida(bool concluida)
    {
        if (toggleVisual != null)
        {
            toggleVisual.isOn = concluida;
            toggleVisual.interactable = false;
        }

        if (imagemConcluida != null)
        {
            imagemConcluida.gameObject.SetActive(concluida);
        }
    }
}
