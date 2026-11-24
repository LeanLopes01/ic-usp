using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Mensagem de chat comum — avatar (Steam), nome do remetente, hora e o texto digitado.</summary>
public class ChatMensagemComumUI : MonoBehaviour
{
    [Header("Cabeçalho")]
    [SerializeField] private RawImage avatarImage;
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text horaText;

    [Header("Corpo")]
    [SerializeField] private TMP_Text mensagemText;

    public void Configurar(ChatEnvelope envelope, ChatComumDTO dto)
    {
        if (envelope == null || dto == null) return;

        if (nomeText != null) nomeText.text = envelope.remetenteNome;
        if (horaText != null) horaText.text = envelope.horaEnvio;
        if (mensagemText != null) mensagemText.text = dto.texto;

        ChatAvatarLoader.CarregarAvatarEm(avatarImage, envelope.remetenteSteamId);
    }
}
