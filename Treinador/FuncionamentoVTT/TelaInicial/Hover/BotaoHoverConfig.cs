using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Configuração de um botão fixo que abre um painel em hover (ex: barra superior com
// "Salas", "Fichas" etc.). Adicione quantos quiser na lista ToggleObjects.botoesHover,
// igual ao padrão já usado em objectsToToggle/toggleButtons.
[System.Serializable]
public class BotaoHoverConfig
{
    [Tooltip("Apenas para organização no Inspector, não é usado em código.")]
    public string nomeAnotacao;
    [Tooltip("Botão fixo da barra que abre o painel ao passar o mouse.")]
    public Button botaoFixo;
    [Tooltip("Painel que cresce de cima para baixo com as opções (ex: lista de salas). O pivot Y é forçado para 1 em runtime.")]
    public RectTransform painelHover;
    [Tooltip("Índice em objectsToToggle para onde o clique no botão fixo navega. -1 = o botão fixo fica desativado (só abre o painel via hover, não navega sozinho).")]
    public int telaDestinoIndex = -1;
    [Tooltip("Botões dentro de painelHover (ex: lista de salas). Adicione quantos quiser.")]
    public List<BotaoSalaHoverConfig> botoesSala = new List<BotaoSalaHoverConfig>();
}
