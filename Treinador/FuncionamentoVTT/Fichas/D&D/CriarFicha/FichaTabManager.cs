using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Gerencia a navegação entre as abas (Raça, Classe, Antecedentes, etc)
/// Sem alterar cores das imagens.
/// </summary>
public class FichaTabManager : MonoBehaviour
{
    [Header("Configuração das Abas")]
    public List<TabConfig> abas; // Lista configurável no Inspector

    [Serializable]
    public class TabConfig
    {
        public string nome;          // Apenas para organização no Inspector
        public Button botao;         // O botão que fica no topo
        public GameObject painel;    // O GameObject (Empty ou Panel) que contém o conteúdo
    }

    private void Start()
    {
        // Configura os cliques dos botões automaticamente
        for (int i = 0; i < abas.Count; i++)
        {
            int index = i; // Captura o índice localmente para o lambda
            if (abas[i].botao != null)
            {
                abas[i].botao.onClick.AddListener(() => AbrirAba(index));
            }
        }

        // Abre a primeira aba por padrão (se houver)
        if (abas.Count > 0)
        {
            AbrirAba(0);
        }
    }

    public void AbrirAba(int indexSelecionado)
    {
        for (int i = 0; i < abas.Count; i++)
        {
            TabConfig aba = abas[i];
            bool ehOAbaSelecionada = (i == indexSelecionado);

            // 1. Ativa/Desativa o Painel de Conteúdo
            if (aba.painel != null)
            {
                aba.painel.SetActive(ehOAbaSelecionada);
            }

            // 2. Apenas gerencia a interação (não muda cor via script)
            if (aba.botao != null)
            {
                // Desativa o botão selecionado para ele não ser clicável novamente
                // Se isso deixar o botão cinza feio, mude o "Transition" do componente Button para "None" no Inspector
                aba.botao.interactable = !ehOAbaSelecionada;
            }
        }
    }
}