using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Mensagem de descrição de magia — cabeçalho comum + campos de stat block de magia.</summary>
public class ChatMensagemMagiaUI : MonoBehaviour
{
    [Header("Cabeçalho")]
    [SerializeField] private RawImage avatarImage;
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text horaText;

    [Header("Magia")]
    [SerializeField] private TMP_Text nomeMagiaText;
    [SerializeField] private TMP_Text tempoConjuracaoText;
    [SerializeField] private TMP_Text alcanceText;
    [SerializeField] private TMP_Text alvoText;
    [SerializeField] private TMP_Text componentesText;
    [SerializeField] private TMP_Text duracaoText;
    [SerializeField] private TMP_Text descricaoText;

    public void Configurar(ChatEnvelope envelope, ChatMagiaDTO dto)
    {
        if (envelope == null || dto == null) return;

        if (nomeText != null) nomeText.text = envelope.remetenteNome;
        if (horaText != null) horaText.text = envelope.horaEnvio;

        if (nomeMagiaText != null) nomeMagiaText.text = dto.nomeMagia;
        if (tempoConjuracaoText != null) tempoConjuracaoText.text = dto.tempoConjuracao;
        if (alcanceText != null) alcanceText.text = dto.alcance;
        if (alvoText != null) alvoText.text = dto.alvo;
        if (componentesText != null) componentesText.text = dto.componentes;
        if (duracaoText != null) duracaoText.text = dto.duracao;
        if (descricaoText != null) descricaoText.text = dto.descricao;

        ChatAvatarLoader.CarregarAvatarEm(avatarImage, envelope.remetenteSteamId);
    }
}
