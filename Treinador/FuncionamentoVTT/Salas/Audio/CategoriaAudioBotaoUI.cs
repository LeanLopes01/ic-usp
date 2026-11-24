using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Botão de categoria (aba) do painel Tabletop Audio — instanciado dinamicamente em
/// AudioTabletopPainelUI.PopularBotoesDeCategoria, um por pasta descoberta em disco (ver
/// AudioTabletopManager.ObterCategorias). Categorias deixaram de ser um conjunto fixo (enum) que dava
/// pra autorar manualmente no Inspector — qualquer pasta em Application.persistentDataPath/audios
/// vira uma categoria — então o botão precisa ser um prefab compartilhado, mesmo padrão já usado por
/// SomTabletopItemUI pros itens de som dentro de cada categoria.</summary>
public class CategoriaAudioBotaoUI : MonoBehaviour
{
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private Button botao;
    [Tooltip("Opcional — indicador visual de \"esta é a categoria aberta agora\" (ex: um contorno ou " +
        "fundo destacado). Deixe vazio se o próprio Button já mostrar o estado selecionado sozinho.")]
    [SerializeField] private GameObject destaque;

    /// <summary>Nome da pasta-categoria que este botão representa, verbatim (ex: "Trilha Sonoras") —
    /// usado por AudioTabletopPainelUI pra saber qual botão destacar/qual categoria popular.</summary>
    public string Categoria { get; private set; }

    public void Configurar(string categoria, Action<string> aoClicar)
    {
        Categoria = categoria;
        if (nomeText != null) nomeText.text = categoria;

        if (botao != null)
        {
            botao.onClick.RemoveAllListeners();
            botao.onClick.AddListener(() => aoClicar?.Invoke(categoria));
        }

        DefinirSelecionado(false);
    }

    public void DefinirSelecionado(bool selecionado)
    {
        if (destaque != null) destaque.SetActive(selecionado);
    }
}
