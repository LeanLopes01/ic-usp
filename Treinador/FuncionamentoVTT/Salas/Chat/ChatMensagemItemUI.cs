using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Mensagem de descrição de item — cabeçalho comum + campos padrão de item D&D 5e.</summary>
public class ChatMensagemItemUI : MonoBehaviour
{
    [Header("Cabeçalho")]
    [SerializeField] private RawImage avatarImage;
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text horaText;

    [Header("Item")]
    [SerializeField] private TMP_Text nomeItemText;
    [SerializeField] private TMP_Text tipoItemText;
    [SerializeField] private TMP_Text raridadeItemText;
    [SerializeField] private TMP_Text pesoItemText;
    [SerializeField] private TMP_Text valorItemText;
    [SerializeField] private TMP_Text descricaoText;

    public void Configurar(ChatEnvelope envelope, ChatItemDTO dto)
    {
        if (envelope == null || dto == null) return;

        if (nomeText != null) nomeText.text = envelope.remetenteNome;
        if (horaText != null) horaText.text = envelope.horaEnvio;

        if (nomeItemText != null) nomeItemText.text = dto.nomeItem;
        if (tipoItemText != null) tipoItemText.text = dto.tipoItem;
        if (raridadeItemText != null) raridadeItemText.text = dto.raridadeItem;
        if (pesoItemText != null) pesoItemText.text = dto.pesoItem;
        if (valorItemText != null) valorItemText.text = dto.valorItem;
        if (descricaoText != null) descricaoText.text = dto.descricao;

        ChatAvatarLoader.CarregarAvatarEm(avatarImage, envelope.remetenteSteamId);
    }
}
