using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Mensagem de teste geral (perícia, resistência, etc.) — nome do teste + total, com o
/// mesmo destaque de crítico das rolagens de ataque/dano (ver ChatCriticoUI).</summary>
public class ChatMensagemTesteUI : MonoBehaviour
{
    [Header("Cabeçalho")]
    [SerializeField] private RawImage avatarImage;
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text horaText;

    [Header("Teste")]
    [SerializeField] private TMP_Text nomeTesteText;
    [SerializeField] private TMP_Text totalText;
    [SerializeField] private GameObject labelCriticoGO;
    [SerializeField] private TMP_Text labelCriticoText;
    [Tooltip("No mesmo GameObject de totalText — hover mostra o detalhe por dado.")]
    [SerializeField] private RolagemTooltipTrigger triggerTeste;

    public void Configurar(ChatEnvelope envelope, ChatTesteDTO dto)
    {
        if (envelope == null || dto == null) return;

        if (nomeText != null) nomeText.text = envelope.remetenteNome;
        if (horaText != null) horaText.text = envelope.horaEnvio;
        if (nomeTesteText != null) nomeTesteText.text = dto.nomeTeste;

        ChatCriticoUI.Aplicar(totalText, labelCriticoGO, labelCriticoText, dto.total, dto.critico);

        triggerTeste?.Configurar("Rolagem", new ResultadoRolagem { notacao = dto.notacaoTeste, dados = dto.dadosTeste, modificador = dto.modificadorTeste, total = dto.total });

        ChatAvatarLoader.CarregarAvatarEm(avatarImage, envelope.remetenteSteamId);
    }
}
