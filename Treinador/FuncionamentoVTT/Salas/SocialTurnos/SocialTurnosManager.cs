using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Duas listas independentes da sala:
///
/// 1) Roster de jogadores — um SocialTurnoJogadorItemUI por membro do lobby Steam, com avatar real
///    da Steam e nome (aparece no hover, ver SocialTurnoJogadorItemUI). Clicar no token de um
///    jogador abre a ficha vinculada a ele em FichaCatalogoPanelUI — só o próprio dono da ficha ou
///    o Mestre da sala podem abrir a ficha de alguém assim (ver OnClickJogador); qualquer outro
///    clique é ignorado silenciosamente, mesmo padrão de ações mestre-only já usado em
///    SalaAbasFichasManager/BarraFerramentasManager.
///
/// 2) Lista de turnos (iniciativa) — um item por TOKEN DO GRID (não por jogador; inclui monstros e
///    NPCs), ordenado por valor rolado (ver RolarTurnoPara, chamado pelo menu de atalho "Rolar
///    Turno" de MapTokenDraggable). Autoritativa no Mestre e sincronizada em rede pra todos os
///    jogadores via ServerRpc/ClientRpc — mesmo par de RPCs + envelope JSON que ChatManager já usa
///    (EnviarMensagemServerRpc/DistribuirMensagemClientRpc), então este script precisa ser um
///    NetworkBehaviour com um NetworkObject já presente na cena (mesmo padrão de ChatManager/
///    NetworkSyncManager — ver comentário em Awake).
/// </summary>
public class SocialTurnosManager : NetworkBehaviour
{
    /// <summary>Acesso estático — MapTokenDraggable chama Instance?.RolarTurnoPara(this) direto do
    /// menu de atalho, sem precisar de uma referência arrastada no Inspector (mesmo padrão de
    /// BarraFerramentasManager.Instancia/MenuAtalhoManager.Instance).</summary>
    public static SocialTurnosManager Instance { get; private set; }

    [Header("Dependências")]
    [SerializeField] private SteamLobbyManager steamLobbyManager;
    [SerializeField] private SalaAbasFichasManager salaAbasFichasManager;
    [SerializeField] private FichaCatalogoPanelUI fichaCatalogoPanel;
    [SerializeField] private MapManager mapManager;
    [SerializeField] private ChatManager chatManager;

    [Header("UI — Roster de Jogadores")]
    [SerializeField] private Transform content;
    [SerializeField] private GameObject prefabItemJogador;

    [Header("Expandir/Colapsar — Roster de Jogadores")]
    [Tooltip("Container da lista inteira (ex: o ScrollView) que o botão único abaixo mostra/esconde " +
        "— diferente de 'content' acima, que é só onde os itens (tokens de jogador) são instanciados. " +
        "Um botão só (não dois como em BarraFerramentasManager) — mesmo sprite, alterna direto.")]
    [SerializeField] private GameObject containerLista;
    [SerializeField] private Button botaoExpandirColapsar;

    [Header("UI — Lista de Turnos (Iniciativa)")]
    [Tooltip("Content separado do roster acima — um item por TOKEN DO GRID (não por jogador), " +
        "populado/reordenado em AtualizarListaDeTurnosUI. Reaproveita o mesmo componente " +
        "SocialTurnoJogadorItemUI, só que via ConfigurarTurno em vez de Setup.")]
    [SerializeField] private Transform contentTurnos;
    [SerializeField] private GameObject prefabItemTurno;
    [SerializeField] private GameObject containerListaTurnos;
    [SerializeField] private Button botaoExpandirColapsarTurnos;

    [Header("Debug")]
    [Tooltip("Força o nome (com o sufixo Mestre/ficha) a ficar sempre visível em todos os itens do " +
        "roster, sem precisar passar o mouse por cima — só pra depuração, não é um controle do " +
        "jogador. Reaplica na hora se mudado em Play Mode pelo Inspector (ver OnValidate).")]
    [SerializeField] private bool debugSempreExibirNome;

    // Resetado pra expandido a cada OnEnable — mesmo padrão de "estado inicial limpo" já usado em
    // BarraFerramentasManager.
    private bool _listaColapsada;
    private bool _listaTurnosColapsada;

    // Estado autoritativo dos turnos — atualizado só pelo servidor (Mestre) em AplicarAtualizacao*,
    // espelhado em todo cliente via DistribuirTurnosClientRpc. Nunca mutado direto fora dessas duas
    // vias, pra garantir que servidor e clientes nunca divirjam.
    private readonly List<TurnoEntrada> _turnos = new List<TurnoEntrada>();
    private int _indiceTurnoAtual = -1;

    private void Awake()
    {
        // NetworkBehaviour: este componente precisa estar num GameObject com NetworkObject já
        // presente/spawned na cena de Salas (mesmo padrão de ChatManager/NetworkSyncManager, achados
        // via FindFirstObjectByType em vez de instanciados dinamicamente) — sem isso, as RPCs abaixo
        // nunca disparam (ver guarda PodeEnviarRpc, chamada em RolarTurnoPara/OnAlternarInimigo/
        // OnClickTurno).
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();

        if (salaAbasFichasManager == null)
            salaAbasFichasManager = FindFirstObjectByType<SalaAbasFichasManager>();

        if (fichaCatalogoPanel == null)
            fichaCatalogoPanel = FindFirstObjectByType<FichaCatalogoPanelUI>();

        if (mapManager == null)
            mapManager = FindFirstObjectByType<MapManager>();

        if (chatManager == null)
            chatManager = FindFirstObjectByType<ChatManager>();

        if (botaoExpandirColapsar != null)
            botaoExpandirColapsar.onClick.AddListener(AlternarExpansaoLista);

        if (botaoExpandirColapsarTurnos != null)
            botaoExpandirColapsarTurnos.onClick.AddListener(AlternarExpansaoListaTurnos);
    }

    private void OnEnable()
    {
        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso   += OnLobbyDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso  += OnLobbyDisponivel;
        }

        // Mantém o roster sincronizado com quem realmente está na sala — sem isso, um jogador que
        // sai ficaria com o token preso na lista até a próxima reabertura desta tela.
        Steamworks.SteamMatchmaking.OnLobbyMemberJoined      += OnMembroMudou;
        Steamworks.SteamMatchmaking.OnLobbyMemberLeave       += OnMembroMudou;
        Steamworks.SteamMatchmaking.OnLobbyMemberDisconnected += OnMembroMudou;

        _listaColapsada = false;
        if (containerLista != null) containerLista.SetActive(true);

        _listaTurnosColapsada = false;
        if (containerListaTurnos != null) containerListaTurnos.SetActive(true);

        PopularLista();
        AtualizarListaDeTurnosUI();
    }

    private void OnDisable()
    {
        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso   -= OnLobbyDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso  -= OnLobbyDisponivel;
        }

        Steamworks.SteamMatchmaking.OnLobbyMemberJoined      -= OnMembroMudou;
        Steamworks.SteamMatchmaking.OnLobbyMemberLeave       -= OnMembroMudou;
        Steamworks.SteamMatchmaking.OnLobbyMemberDisconnected -= OnMembroMudou;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (botaoExpandirColapsar != null)
            botaoExpandirColapsar.onClick.RemoveListener(AlternarExpansaoLista);

        if (botaoExpandirColapsarTurnos != null)
            botaoExpandirColapsarTurnos.onClick.RemoveListener(AlternarExpansaoListaTurnos);
    }

    private void OnLobbyDisponivel(Steamworks.Data.Lobby lobby) => PopularLista();
    private void OnMembroMudou(Steamworks.Data.Lobby lobby, Steamworks.Friend membro) => PopularLista();

#if UNITY_EDITOR
    // Reaplica na hora ao marcar/desmarcar debugSempreExibirNome pelo Inspector em Play Mode — só
    // conveniência de depuração, não roda em build (guardado por UNITY_EDITOR).
    private void OnValidate()
    {
        if (Application.isPlaying) PopularLista();
    }
#endif

    // ── Expandir/Colapsar ─────────────────────────────────────────────────────

    private void AlternarExpansaoLista()
    {
        _listaColapsada = !_listaColapsada;
        if (containerLista != null) containerLista.SetActive(!_listaColapsada);
    }

    private void AlternarExpansaoListaTurnos()
    {
        _listaTurnosColapsada = !_listaTurnosColapsada;
        if (containerListaTurnos != null) containerListaTurnos.SetActive(!_listaTurnosColapsada);
    }

    // ── Roster de Jogadores ───────────────────────────────────────────────────

    private void PopularLista()
    {
        if (content == null || prefabItemJogador == null) return;
        if (steamLobbyManager == null || !steamLobbyManager.SalaAtual.HasValue) return;

        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        foreach (Steamworks.Friend membro in steamLobbyManager.SalaAtual.Value.Members)
        {
            GameObject go = UIUtils.Spawn(prefabItemJogador, content, false);
            SocialTurnoJogadorItemUI item = go.GetComponent<SocialTurnoJogadorItemUI>();
            if (item == null)
            {
                Debug.LogWarning("[SocialTurnosManager] Prefab de item não possui SocialTurnoJogadorItemUI.");
                continue;
            }

            string steamId = membro.Id.ToString();
            item.Setup(steamId, ConstruirNomeExibicao(membro, steamId), OnClickJogador, debugSempreExibirNome);
        }
    }

    // "(Mestre)" pro dono da sala; "(NomeDaFicha)" pros demais jogadores, só se já tiverem uma
    // ficha aberta na sala (ver ObterFichaDoJogador) — sem ficha aberta ainda, fica só o nome da Steam.
    private string ConstruirNomeExibicao(Steamworks.Friend membro, string steamId)
    {
        bool ehMestre = steamLobbyManager != null && steamLobbyManager.SalaAtual.HasValue
            && steamLobbyManager.SalaAtual.Value.IsOwnedBy(membro.Id);
        if (ehMestre)
            return $"{membro.Name} (Mestre)";

        FichaData ficha = salaAbasFichasManager?.ObterFichaDoJogador(steamId);
        return ficha != null && !string.IsNullOrWhiteSpace(ficha.name_pc)
            ? $"{membro.Name} ({ficha.name_pc})"
            : membro.Name;
    }

    // Só o dono da ficha ou o Mestre da sala podem abrir a ficha de alguém pelo token da lista —
    // qualquer outro jogador que clique não recebe feedback nenhum (nem erro, nem popup), mesmo
    // comportamento silencioso de outras ações mestre-only já existentes (ex: AtribuirDono).
    private void OnClickJogador(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId) || salaAbasFichasManager == null || fichaCatalogoPanel == null) return;

        string meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        bool souDono = string.Equals(steamId, meuSteamId, StringComparison.Ordinal);
        if (!souDono && !SouMestre()) return;

        FichaData ficha = salaAbasFichasManager.ObterFichaDoJogador(steamId);
        if (ficha == null) return; // jogador ainda não abriu/importou nenhuma ficha nesta sala

        fichaCatalogoPanel.ToggleParaFicha(ficha, steamId);
    }

    // Mesmo padrão já duplicado em AudioTabletopManager/ClimaPainelUI/EfeitoClimaManager/
    // SalaAbasFichasManager/MapManager/NetworkSyncManager/BarraFerramentasManager.
    private bool SouMestre() =>
        Steamworks.SteamClient.IsValid && steamLobbyManager?.SalaAtual?.IsOwnedBy(Steamworks.SteamClient.SteamId) == true;

    // ── Lista de Turnos (Iniciativa) ──────────────────────────────────────────
    //
    // Chamado pelo menu de atalho "Rolar Turno" de qualquer token do grid (ver
    // MapTokenDraggable.AbrirMenuDeAtalho). Rola 1d20 (+ modificador de Destreza, se o token tiver
    // ficha vinculada), SEMPRE anuncia no chat da sala (única opção — não existe versão silenciosa,
    // por decisão de produto) e propaga o novo valor pra todos via ServerRpc — mesmo padrão
    // ChatManager.EnviarMensagem->EnviarMensagemServerRpc: o cliente que rolou é só quem INICIA a
    // chamada, o valor final que todo mundo vê é o que o SERVIDOR (Mestre) redistribui.

    public void RolarTurnoPara(MapTokenDraggable token)
    {
        if (token == null || !PodeEnviarRpc()) return;

        int bonus = 0;
        string nomeExibicao = token.NomeToken;
        bool temFicha = token.IsLinked;

        if (temFicha)
        {
            LocalFichaData ficha = token.ObterDadosDaFicha();
            if (ficha != null)
            {
                bonus = CalcularModificador(ficha.atributos?.destreza ?? 10);
                if (!string.IsNullOrWhiteSpace(ficha.name_pc)) nomeExibicao = ficha.name_pc;
            }
        }

        ResultadoRolagem resultado = RolagemDados.RolarNotacaoDetalhada(RolagemDados.FormatarNotacaoD20(bonus));

        ResultadoCritico critico = resultado.dados.Count > 0
            ? RolagemDados.ClassificarD20(resultado.dados[0].valor)
            : ResultadoCritico.Normal;
        chatManager?.EnviarTesteGeral($"Turno de {nomeExibicao}", resultado, critico);

        AtualizarTurnoServerRpc(token.TokenId, nomeExibicao, resultado.total, temFicha);
    }

    // Mesma fórmula (privada em AttributeManager, sem um helper compartilhado ainda — ver
    // AttributeManager.CalcularModificador) — cópia local pra não criar uma dependência cruzada só
    // por uma conta de uma linha.
    private static int CalcularModificador(int valorAtributo) => Mathf.FloorToInt((valorAtributo - 10) / 2f);

    // Guarda antes de QUALQUER chamada de ServerRpc daqui — sem isso,
    // NetworkBehaviour.__endSendServerRpc lança NullReferenceException se o NetworkObject deste
    // componente ainda não foi spawnado na rede (GameObject sem NetworkObject, ou presente mas o
    // spawn automático de cena do NetworkManager ainda não rodou/não alcançou este objeto — mesmo
    // requisito de ChatManager/NetworkSyncManager, ver comentário em Awake). IsSpawned já cobre
    // sozinho o caso de NetworkManager.Singleton nulo/não-listening (não dá pra estar spawnado sem
    // uma sessão de rede ativa), então substitui o check antigo por completo.
    private bool PodeEnviarRpc()
    {
        if (IsSpawned) return true;

        VTTLog.NetworkWarn("[SocialTurnos] NetworkObject deste SocialTurnosManager ainda não foi " +
            "spawnado na rede — RPC ignorada. Confira se o GameObject tem um componente NetworkObject " +
            "e se ele está presente na cena de Salas (mesmo requisito de ChatManager/NetworkSyncManager).");
        return false;
    }

    // Clicar na imagem de aliado/inimigo de um item da lista de turnos — só faz sentido pra tokens
    // SEM ficha (ver TurnoEntrada.temFicha); tokens de ficha são sempre aliados. Mestre-only: o
    // próprio SocialTurnoJogadorItemUI já esconde o clique pra quem não é Mestre (ver
    // AtualizarListaDeTurnosUI), isto aqui é só a segunda camada de defesa.
    private void OnAlternarInimigo(string tokenId)
    {
        if (string.IsNullOrWhiteSpace(tokenId) || !SouMestre() || !PodeEnviarRpc()) return;
        AlternarInimigoServerRpc(tokenId);
    }

    // Duplo clique num item da lista de turnos — centraliza a câmera no token correspondente. Não é
    // mestre-only (qualquer jogador pode olhar pra onde um turno está).
    private void OnDuploCliqueTurno(string tokenId)
    {
        if (string.IsNullOrWhiteSpace(tokenId) || mapManager == null) return;

        MapTokenDraggable token = mapManager.EncontrarTokenPorId(tokenId);
        if (token == null) return;

        mapManager.cameraVTT?.CentralizarEm(token.transform.position);
    }

    // Clique simples num item da lista de turnos marca ele como "o turno atual" (indicador azul) —
    // Mestre-only, mesma segunda camada de defesa de OnAlternarInimigo.
    private void OnClickTurno(string tokenId)
    {
        if (string.IsNullOrWhiteSpace(tokenId) || !SouMestre() || !PodeEnviarRpc()) return;
        DefinirTurnoAtualServerRpc(tokenId);
    }

    // ── RPCs (autoridade no Mestre = servidor) ───────────────────────────────

    [ServerRpc(RequireOwnership = false)]
    private void AtualizarTurnoServerRpc(string tokenId, string nomeExibicao, int valor, bool temFicha)
    {
        TurnoEntrada entrada = _turnos.FirstOrDefault(t => t.tokenId == tokenId);
        if (entrada == null)
        {
            entrada = new TurnoEntrada { tokenId = tokenId, ehInimigo = false };
            _turnos.Add(entrada);
        }

        entrada.nomeExibicao = nomeExibicao;
        entrada.valor = valor;
        entrada.temFicha = temFicha;
        if (temFicha) entrada.ehInimigo = false; // ficha nunca é inimigo, mesmo se já tivesse marcado antes

        RedistribuirTurnos();
    }

    [ServerRpc(RequireOwnership = false)]
    private void AlternarInimigoServerRpc(string tokenId)
    {
        TurnoEntrada entrada = _turnos.FirstOrDefault(t => t.tokenId == tokenId);
        if (entrada == null || entrada.temFicha) return; // ficha nunca alterna — ver comentário em TurnoEntrada

        entrada.ehInimigo = !entrada.ehInimigo;
        RedistribuirTurnos();
    }

    [ServerRpc(RequireOwnership = false)]
    private void DefinirTurnoAtualServerRpc(string tokenId)
    {
        int indice = _turnos.FindIndex(t => t.tokenId == tokenId);
        if (indice < 0) return;

        // Clicar de novo no que já é o atual desmarca (nenhum turno atual) — mesmo padrão de toggle
        // já usado em ToggleSlot/ToggleChat.
        _indiceTurnoAtual = _indiceTurnoAtual == indice ? -1 : indice;
        RedistribuirTurnos();
    }

    private void RedistribuirTurnos()
    {
        var payload = new TurnoListaPayload { turnos = _turnos, indiceAtual = _indiceTurnoAtual };
        DistribuirTurnosClientRpc(JsonUtility.ToJson(payload));
    }

    [ClientRpc]
    private void DistribuirTurnosClientRpc(string payloadJson)
    {
        TurnoListaPayload payload = JsonUtility.FromJson<TurnoListaPayload>(payloadJson);
        if (payload == null) return;

        _turnos.Clear();
        if (payload.turnos != null) _turnos.AddRange(payload.turnos);
        _indiceTurnoAtual = payload.indiceAtual;

        AtualizarListaDeTurnosUI();
    }

    // Reconstrói a lista visual inteira a partir de _turnos, ordenada por valor decrescente (quem
    // ainda não rolou fica com valor 0, então afunda naturalmente pro fim da lista). Chamado toda
    // vez que _turnos muda (ver DistribuirTurnosClientRpc) e ao reabrir a tela (OnEnable).
    private void AtualizarListaDeTurnosUI()
    {
        if (contentTurnos == null || prefabItemTurno == null) return;

        for (int i = contentTurnos.childCount - 1; i >= 0; i--)
            Destroy(contentTurnos.GetChild(i).gameObject);

        bool souMestre = SouMestre();
        List<TurnoEntrada> ordenados = _turnos.OrderByDescending(t => t.valor).ToList();

        for (int i = 0; i < ordenados.Count; i++)
        {
            TurnoEntrada entrada = ordenados[i];
            GameObject go = UIUtils.Spawn(prefabItemTurno, contentTurnos, false);
            SocialTurnoJogadorItemUI item = go.GetComponent<SocialTurnoJogadorItemUI>();
            if (item == null)
            {
                Debug.LogWarning("[SocialTurnosManager] Prefab de item de turno não possui SocialTurnoJogadorItemUI.");
                continue;
            }

            bool ehAtual = _turnos.IndexOf(entrada) == _indiceTurnoAtual;

            // A imagem não viaja pela rede (caminhos de arquivo são locais a cada máquina) — cada
            // cliente resolve o MapTokenDraggable ainda vivo no seu próprio grid (já sincronizado
            // por MapManager/NetworkSyncManager) e usa a mesma imagem que o token mostra no mapa.
            // Token removido do grid depois de rolar = imagem em branco (ImagemPath vira null).
            string imagemPath = mapManager?.EncontrarTokenPorId(entrada.tokenId)?.ImagemPath;

            // Só Mestre pode alternar aliado/inimigo e marcar o turno atual — jogadores comuns só
            // veem o estado (ver OnAlternarInimigo/OnClickTurno, segunda camada de defesa server-side).
            item.ConfigurarTurno(
                entrada.tokenId,
                entrada.nomeExibicao,
                imagemPath,
                entrada.valor,
                entrada.ehInimigo,
                ehAtual,
                podeAlternarInimigo: souMestre && !entrada.temFicha,
                podeMarcarAtual: souMestre,
                aoClicar: OnClickTurno,
                aoDuploClicar: OnDuploCliqueTurno,
                aoAlternarInimigo: OnAlternarInimigo);
        }
    }
}
