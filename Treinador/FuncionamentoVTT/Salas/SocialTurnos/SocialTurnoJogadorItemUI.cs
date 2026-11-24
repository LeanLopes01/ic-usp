using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Item reaproveitado nas DUAS listas de SocialTurnosManager, cada uma via um método de setup
/// próprio:
///
/// - Roster de jogadores (Setup) — token com o avatar REAL da Steam do jogador (não o token do
///   personagem — ver ChatAvatarLoader, que já corrige a inversão de linhas que a API da Steam às
///   vezes devolve). Clicar abre a ficha vinculada (ver SocialTurnosManager.OnClickJogador).
///
/// - Lista de turnos/iniciativa (ConfigurarTurno) — um token do GRID (pode ser monstro, sem avatar
///   Steam nenhum), com o valor rolado + indicadores de aliado/inimigo/turno-atual. avatarImage
///   mostra a MESMA imagem que o token tem no mapa (MapManager/MapTokenDraggable.ImagemPath — ver
///   TokenImagemLoader), não um avatar Steam, já que reaproveita o mesmo campo do modo roster.
///   Clique simples marca como turno atual (Mestre-only); clique na imagem de aliado/inimigo
///   alterna a classificação (Mestre-only, só pra tokens sem ficha); DUPLO clique centraliza a
///   câmera no token (qualquer jogador).
///
/// Em ambos os modos, o nome só aparece ao passar o mouse por cima (ou sempre, se
/// SocialTurnosManager.debugSempreExibirNome estiver ligado — só afeta o modo roster).
/// </summary>
public class SocialTurnoJogadorItemUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
{
    [Header("UI — Comum aos dois modos")]
    [SerializeField] private Button botao;
    [SerializeField] private TMP_Text nomeText;

    [Header("UI — Compartilhado (avatar Steam no roster, imagem do token nos turnos)")]
    [SerializeField] private RawImage avatarImage;

    [Header("UI — Só Lista de Turnos")]
    [SerializeField] private TMP_Text turnoValorText;
    [Tooltip("Mostrada quando o token NÃO está marcado como inimigo (padrão pra qualquer token " +
        "novo, inclusive monstros — ver TurnoEntrada.ehInimigo).")]
    [SerializeField] private Image imagemAliado;
    [Tooltip("Mostrada só quando o Mestre marca explicitamente um token sem ficha como inimigo.")]
    [SerializeField] private Image imagemInimigo;
    [Tooltip("Mostrada quando esta entrada é o turno atual (ver SocialTurnosManager.DefinirTurnoAtualServerRpc).")]
    [SerializeField] private Image imagemAtual;
    [Tooltip("Clicar alterna aliado/inimigo — só interactable quando podeAlternarInimigo (Mestre e " +
        "token sem ficha, ver ConfigurarTurno).")]
    [SerializeField] private Button botaoAlternarStatus;

    // "steamId" no modo roster, "tokenId" no modo turno — um único campo genérico de id, já que os
    // dois modos nunca coexistem na mesma instância (prefabs separados, ver SocialTurnosManager).
    private string _id;
    private Action<string> _aoClicar;
    private Action<string> _aoDuploClicar;
    private Action<string> _aoAlternarInimigo;
    private bool _sempreExibirNome;

    private void Awake()
    {
        if (nomeText != null) nomeText.gameObject.SetActive(false);
        if (botao != null) botao.onClick.AddListener(OnClick);
        if (botaoAlternarStatus != null) botaoAlternarStatus.onClick.AddListener(OnClickAlternarStatus);
    }

    private void OnDestroy()
    {
        if (botao != null) botao.onClick.RemoveListener(OnClick);
        if (botaoAlternarStatus != null) botaoAlternarStatus.onClick.RemoveListener(OnClickAlternarStatus);
    }

    // ── Modo Roster de Jogadores ──────────────────────────────────────────────

    /// <summary>steamId como string (mesmo formato usado em toda a base — ver DataManager.GetCurrentSteamId/
    /// FichaSyncManager) — nomeExibicao já vem pronto de SocialTurnosManager.ConstruirNomeExibicao
    /// (com o sufixo "(Mestre)"/"(NomeDaFicha)" quando aplicável), aoClicar recebe de volta este
    /// mesmo steamId quando o botão é clicado. sempreExibirNome (debug) faz o nome ficar sempre
    /// visível em vez de só aparecer no hover — ver SocialTurnosManager.debugSempreExibirNome.</summary>
    public void Setup(string steamId, string nomeExibicao, Action<string> aoClicar, bool sempreExibirNome = false)
    {
        _id = steamId;
        _aoClicar = aoClicar;
        _aoDuploClicar = null;
        _aoAlternarInimigo = null;
        _sempreExibirNome = sempreExibirNome;

        if (nomeText != null)
        {
            nomeText.text = nomeExibicao;
            nomeText.gameObject.SetActive(sempreExibirNome);
        }
        if (botaoAlternarStatus != null) botaoAlternarStatus.gameObject.SetActive(false);
        if (imagemAliado != null) imagemAliado.gameObject.SetActive(false);
        if (imagemInimigo != null) imagemInimigo.gameObject.SetActive(false);
        if (imagemAtual != null) imagemAtual.gameObject.SetActive(false);

        ChatAvatarLoader.CarregarAvatarEm(avatarImage, steamId);
    }

    // ── Modo Lista de Turnos ──────────────────────────────────────────────────

    /// <summary>tokenId identifica o token do grid (ver MapTokenDraggable.TokenId). imagemPath é a
    /// MESMA imagem que o token usa no mapa (MapTokenDraggable.ImagemPath, resolvido por
    /// SocialTurnosManager via MapManager.EncontrarTokenPorId — não viaja pela rede, cada cliente
    /// resolve localmente a partir do seu próprio MapTokenDraggable já sincronizado); vazio/token
    /// removido do mapa = avatarImage fica em branco. podeAlternarInimigo controla se
    /// botaoAlternarStatus fica clicável (Mestre + token sem ficha — tokens de ficha são sempre
    /// aliados); podeMarcarAtual controla se o clique simples (aoClicar) faz algo (Mestre-only) —
    /// jogadores comuns só enxergam o estado, sem poder alterá-lo. aoDuploClicar (câmera) funciona
    /// pra qualquer jogador, independente de podeMarcarAtual.</summary>
    public void ConfigurarTurno(string tokenId, string nomeExibicao, string imagemPath, int valor, bool ehInimigo,
        bool ehAtual, bool podeAlternarInimigo, bool podeMarcarAtual, Action<string> aoClicar,
        Action<string> aoDuploClicar, Action<string> aoAlternarInimigo)
    {
        _id = tokenId;
        _aoClicar = podeMarcarAtual ? aoClicar : null;
        _aoDuploClicar = aoDuploClicar;
        _aoAlternarInimigo = podeAlternarInimigo ? aoAlternarInimigo : null;
        _sempreExibirNome = false;

        if (nomeText != null)
        {
            nomeText.text = nomeExibicao;
            nomeText.gameObject.SetActive(false); // sempre hover-only no modo turno
        }

        if (turnoValorText != null) turnoValorText.text = valor.ToString();

        TokenImagemLoader.CarregarImagemEm(avatarImage, imagemPath);

        if (imagemAliado != null) imagemAliado.gameObject.SetActive(!ehInimigo);
        if (imagemInimigo != null) imagemInimigo.gameObject.SetActive(ehInimigo);
        if (imagemAtual != null) imagemAtual.gameObject.SetActive(ehAtual);

        if (botaoAlternarStatus != null)
        {
            botaoAlternarStatus.gameObject.SetActive(true);
            botaoAlternarStatus.interactable = podeAlternarInimigo;
        }
    }

    // ── Interações ────────────────────────────────────────────────────────────

    private void OnClick() => _aoClicar?.Invoke(_id);
    private void OnClickAlternarStatus() => _aoAlternarInimigo?.Invoke(_id);

    // Duplo clique — só dispara no modo turno (aoDuploClicar fica null no modo roster, ver Setup).
    // Button.onClick já cobre o clique simples; isto aqui é só a detecção do SEGUNDO clique.
    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.clickCount == 2) _aoDuploClicar?.Invoke(_id);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (nomeText != null) nomeText.gameObject.SetActive(true);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (nomeText != null && !_sempreExibirNome) nomeText.gameObject.SetActive(false);
    }
}
