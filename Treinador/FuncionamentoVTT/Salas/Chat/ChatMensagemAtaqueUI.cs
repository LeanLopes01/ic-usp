using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Mensagem de rolagem de ataque — nome do ataque (ou item/magia), total do ataque e total do dano,
/// cada um com destaque de crítico (ver ChatCriticoUI). botaoVerDetalhes abre o prefab de detalhe de
/// ataque/dano — ainda não implementado (fica como stub até esse prefab existir).
/// </summary>
public class ChatMensagemAtaqueUI : MonoBehaviour
{
    [Header("Cabeçalho")]
    [SerializeField] private RawImage avatarImage;
    [SerializeField] private TMP_Text nomeText;
    [SerializeField] private TMP_Text horaText;

    [Header("Ataque")]
    [SerializeField] private TMP_Text nomeAtaqueText;
    [SerializeField] private TMP_Text totalAtaqueText;
    [SerializeField] private GameObject labelCriticoAtaqueGO;
    [SerializeField] private TMP_Text labelCriticoAtaqueText;
    [Tooltip("No mesmo GameObject de totalAtaqueText — hover mostra o detalhe por dado.")]
    [SerializeField] private RolagemTooltipTrigger triggerAtaque;

    [Header("Dano")]
    [SerializeField] private TMP_Text totalDanoText;
    [SerializeField] private GameObject labelCriticoDanoGO;
    [SerializeField] private TMP_Text labelCriticoDanoText;
    [Tooltip("No mesmo GameObject de totalDanoText — hover mostra o detalhe por dado.")]
    [SerializeField] private RolagemTooltipTrigger triggerDano;

    [Header("Detalhes")]
    [Tooltip("Abre o prefab de detalhe de ataque/dano — ainda não implementado.")]
    [SerializeField] private Button botaoVerDetalhes;

    private void Awake()
    {
        if (botaoVerDetalhes != null)
        {
            botaoVerDetalhes.onClick.RemoveListener(AbrirDetalhesDeAtaqueEDano);
            botaoVerDetalhes.onClick.AddListener(AbrirDetalhesDeAtaqueEDano);
        }
    }

    public void Configurar(ChatEnvelope envelope, ChatAtaqueDTO dto)
    {
        if (envelope == null || dto == null) return;

        if (nomeText != null) nomeText.text = envelope.remetenteNome;
        if (horaText != null) horaText.text = envelope.horaEnvio;
        if (nomeAtaqueText != null) nomeAtaqueText.text = dto.nomeAtaque;

        ChatCriticoUI.Aplicar(totalAtaqueText, labelCriticoAtaqueGO, labelCriticoAtaqueText, dto.totalAtaque, dto.critAtaque);
        ChatCriticoUI.Aplicar(totalDanoText, labelCriticoDanoGO, labelCriticoDanoText, dto.totalDano, dto.critDano);

        triggerAtaque?.Configurar("Ataque", new ResultadoRolagem { notacao = dto.notacaoAtaque, dados = dto.dadosAtaque, modificador = dto.modificadorAtaque, total = dto.totalAtaque });
        triggerDano?.Configurar("Dano", new ResultadoRolagem { notacao = dto.notacaoDano, dados = dto.dadosDano, modificador = dto.modificadorDano, total = dto.totalDano });

        ChatAvatarLoader.CarregarAvatarEm(avatarImage, envelope.remetenteSteamId);
    }

    // TODO: substituir pelo prefab de detalhe de ataque/dano quando ele existir.
    private void AbrirDetalhesDeAtaqueEDano()
    {
        Debug.Log("[ChatMensagemAtaqueUI] Detalhes de ataque/dano ainda não implementados.");
    }
}
