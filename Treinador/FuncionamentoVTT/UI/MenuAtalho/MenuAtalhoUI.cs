using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// O painel do menu de atalho (prefab). Recebe a lista de opções e instancia um
// MenuAtalhoBotaoUI por opção dentro de containerOpcoes (normalmente um Vertical Layout Group).
[RequireComponent(typeof(RectTransform))]
public class MenuAtalhoUI : MonoBehaviour
{
    [SerializeField] private Transform containerOpcoes;
    [SerializeField] private GameObject prefabBotaoOpcao;

    private RectTransform rectTransform;

    private void Awake()
    {
        rectTransform = transform as RectTransform;
        if (containerOpcoes == null) containerOpcoes = transform;
    }

    public void Configurar(List<OpcaoMenuAtalho> opcoes)
    {
        for (int i = containerOpcoes.childCount - 1; i >= 0; i--)
            Destroy(containerOpcoes.GetChild(i).gameObject);

        if (opcoes == null || prefabBotaoOpcao == null)
            return;

        foreach (OpcaoMenuAtalho opcao in opcoes)
        {
            GameObject go = Instantiate(prefabBotaoOpcao, containerOpcoes);
            go.GetComponent<MenuAtalhoBotaoUI>()?.Configurar(opcao);
        }
    }

    // Posiciona o menu no ponto de tela (ex: posição do clique direito). O clique fica sempre num
    // canto do menu (nunca no centro) — ver RectTransformCornerPositioner (compartilhado com
    // TokenStatBlockTooltipUI).
    public void PosicionarNaTela(Vector2 posicaoTela)
    {
        RectTransformCornerPositioner.PosicionarNoCantoDoPonto(rectTransform, posicaoTela);
    }
}
