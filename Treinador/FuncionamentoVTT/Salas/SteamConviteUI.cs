using System.Collections.Generic;
using System.Linq;
using Steamworks;
using Steamworks.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Painel de convidar amigos Steam para a sala atual.
///
/// === FLUXO ===
/// 1. Jogador clica em "Convidar Amigos" (botaoAbrir) → abre o painel
/// 2. Lista de amigos Steam é carregada com status e avatar
/// 3. Clicar em "Convidar" num amigo envia o convite via Steam
/// 4. O amigo recebe notificação no Steam Overlay → aceita → entra automaticamente
///    (o SteamLobbyManager já trata AoAceitarConviteDaSteam)
///
/// === INSPECTOR SETUP ===
/// steamLobbyManager  -> SteamLobbyManager da cena
/// painelConvite      -> GameObject raiz do painel (começa desativado)
/// listContent        -> Transform/Content do ScrollView onde os itens são instanciados
/// amigoItemPrefab    -> Prefab com SteamAmigoItemUI
/// botaoToggle        -> Button que alterna abrir/fechar o painel (pode estar em outro GameObject)
/// feedbackText       -> TMP_Text para mensagens ("Convite enviado!", erros, etc.)
/// toggleApenasOnline -> Toggle para filtrar apenas amigos online (opcional)
/// </summary>
public class SteamConviteUI : MonoBehaviour
{
    [Header("Dependências")]
    [SerializeField] private SteamLobbyManager steamLobbyManager;

    [Header("UI - Painel")]
    [SerializeField] private GameObject painelConvite;
    [SerializeField] private Transform listContent;
    [SerializeField] private GameObject amigoItemPrefab;
    [SerializeField] private TMP_Text feedbackText;

    [Header("Filtros")]
    [SerializeField] private Toggle toggleApenasOnline;

    private readonly List<GameObject> itensInstanciados = new();

    // ==========================================
    // UNITY LIFECYCLE
    // ==========================================
    private void Awake()
    {
        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();
    }

    private void OnEnable()
    {
        if (toggleApenasOnline != null)
        {
            toggleApenasOnline.onValueChanged.RemoveListener(OnFiltroAlterado);
            toggleApenasOnline.onValueChanged.AddListener(OnFiltroAlterado);
        }

        SetActiveSafe(painelConvite, false);
    }

    private void OnDisable()
    {
        if (toggleApenasOnline != null) toggleApenasOnline.onValueChanged.RemoveListener(OnFiltroAlterado);
    }

    // ==========================================
    // ABRIR / FECHAR / TOGGLE
    // ==========================================
    public bool EstaAbertoPainel() => painelConvite != null && painelConvite.activeSelf;

    public void TogglePainel()
    {
        if (EstaAbertoPainel()) FecharPainel();
        else AbrirPainel();
    }

    public void AbrirPainel()
    {
        Debug.Log($"[SteamConvite] AbrirPainel — SteamValid={SteamClient.IsValid}, lobbyManager={steamLobbyManager != null}, salaAtual={steamLobbyManager?.SalaAtual.HasValue}, painelConvite={painelConvite != null}");

        if (!SteamClient.IsValid)
        {
            Debug.LogWarning("[SteamConvite] Steam não está disponível.");
            SetFeedback("Steam não está disponível.");
            return;
        }

        if (steamLobbyManager == null || !steamLobbyManager.SalaAtual.HasValue)
        {
            Debug.LogWarning("[SteamConvite] Fora de sala — bloqueando abertura.");
            SetFeedback("Você precisa estar em uma sala para convidar amigos.");
            return;
        }

        SetActiveSafe(painelConvite, true);
        CarregarAmigos();
    }

    public void FecharPainel()
    {
        SetActiveSafe(painelConvite, false);
        LimparLista();
        SetFeedback("");
    }

    // ==========================================
    // CARREGAR LISTA DE AMIGOS
    // ==========================================
    private void OnFiltroAlterado(bool _) => CarregarAmigos();

    private void CarregarAmigos()
    {
        LimparLista();
        SetFeedback("");

        bool apenasOnline = toggleApenasOnline != null && toggleApenasOnline.isOn;
        Lobby? salaAtual = steamLobbyManager.SalaAtual;

        IEnumerable<Friend> amigos = SteamFriends.GetFriends();

        if (apenasOnline)
            amigos = amigos.Where(a => a.IsOnline);

        // Ordena: na sala > jogando este jogo > online > offline
        amigos = amigos.OrderByDescending(a =>
        {
            if (salaAtual.HasValue)
            {
                foreach (var m in salaAtual.Value.Members)
                    if (m.Id == a.Id) return 3;
            }
            if (a.IsPlayingThisGame) return 2;
            if (a.IsOnline) return 1;
            return 0;
        });

        int total = 0;
        foreach (var amigo in amigos)
        {
            if (amigoItemPrefab == null || listContent == null) break;

            var go = Instantiate(amigoItemPrefab, listContent);
            var item = go.GetComponent<SteamAmigoItemUI>();
            if (item != null)
                item.Configurar(amigo, salaAtual, ConvidarAmigo);

            itensInstanciados.Add(go);
            total++;
        }

        if (total == 0)
            SetFeedback(apenasOnline ? "Nenhum amigo online no momento." : "Nenhum amigo na lista da Steam.");
    }

    // ==========================================
    // ENVIAR CONVITE
    // ==========================================
    private void ConvidarAmigo(Friend amigo)
    {
        if (steamLobbyManager == null || !steamLobbyManager.SalaAtual.HasValue)
        {
            SetFeedback("Nenhuma sala ativa para enviar convite.");
            return;
        }

        try
        {
            bool ok = steamLobbyManager.SalaAtual.Value.InviteFriend(amigo.Id);

            if (ok)
            {
                SetFeedback($"Convite enviado para {amigo.Name}!");
                Debug.Log($"[SteamConvite] Convite enviado para {amigo.Name} ({amigo.Id}).");
            }
            else
            {
                SetFeedback($"Não foi possível convidar {amigo.Name}.");
                Debug.LogWarning($"[SteamConvite] InviteUserToLobby retornou false para {amigo.Name}.");
            }
        }
        catch (System.Exception e)
        {
            SetFeedback($"Erro ao convidar {amigo.Name}.");
            Debug.LogWarning($"[SteamConvite] Exceção ao convidar {amigo.Name}: {e.Message}");
        }
    }

    // ==========================================
    // HELPERS
    // ==========================================
    private void LimparLista()
    {
        foreach (var go in itensInstanciados)
        {
            if (go != null)
                Destroy(go);
        }
        itensInstanciados.Clear();
    }

    private void SetFeedback(string msg)
    {
        if (feedbackText != null)
            feedbackText.text = msg;
    }

    private static void SetActiveSafe(GameObject go, bool ativo)
    {
        if (go != null) go.SetActive(ativo);
    }
}
