using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Um botão individual dentro do menu de atalho. Prefab: Button + TMP_Text filho.
public class MenuAtalhoBotaoUI : MonoBehaviour
{
    [SerializeField] private TMP_Text texto;
    [SerializeField] private Button botao;

    private Action onClicar;

    private void Awake()
    {
        if (botao == null) botao = GetComponent<Button>();
        if (texto == null) texto = GetComponentInChildren<TMP_Text>();
    }

    public void Configurar(OpcaoMenuAtalho opcao)
    {
        if (opcao == null) return;

        onClicar = opcao.onClicar;

        if (texto != null)
            texto.text = opcao.texto;

        if (botao != null)
        {
            botao.interactable = opcao.interativel;
            botao.onClick.RemoveAllListeners();
            // Menu de atalho sempre fecha ao clicar em QUALQUER opção — mesmo comportamento de um
            // menu de contexto normal. Centralizado aqui (em vez de cada chamador ter que lembrar de
            // fechar) pra valer pra toda opção, de qualquer script, sem exceção.
            botao.onClick.AddListener(() =>
            {
                onClicar?.Invoke();
                MenuAtalhoManager.Instance?.Fechar();
            });
        }

        // Botão de excluir usa o visual especial de hover (sprite/cor próprios) em vez do degradê
        // padrão — ver HoverGradientFix.ConfigurarComoExcluir.
        GetComponent<HoverGradientFix>()?.ConfigurarComoExcluir(opcao.ehExclusao);
    }
}
