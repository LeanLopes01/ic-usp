using UnityEngine;
using UnityEngine.UI;

// Um botão dentro de um painelHover (ver BotaoHoverConfig.botoesSala), que ao ser clicado
// leva para um GameObject de tela específico. Sem telaDestino, o botão fica desativado.
[System.Serializable]
public class BotaoSalaHoverConfig
{
    [Tooltip("Apenas para organização no Inspector, não é usado em código.")]
    public string nomeAnotacao;
    [Tooltip("Botão dentro do painel de hover.")]
    public Button botao;
    [Tooltip("GameObject de tela para onde o clique navega. Vazio = botão fica desativado.")]
    public GameObject telaDestino;
}
