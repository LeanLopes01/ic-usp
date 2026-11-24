using UnityEngine;
using Steamworks;
using Steamworks.Data;
using System.Threading.Tasks;
using System;
using System.Reflection;
using Unity.Netcode;

public class SteamLobbyManager : MonoBehaviour
{
    // A SUA CHAVE DE SEGURANÇA (Mude isso quando for lançar atualizações incompatíveis)
    private const string CHAVE_SEGURANCA = "GameId";
    private const string VALOR_SEGURANCA = "VTT_Lean_USP_v1"; 

    public Lobby? SalaAtual;

    public SalaLocalSnapshot SnapshotReabertura { get; private set; }

    public event Action<Lobby> OnSalaCriadaComSucesso;
    public event Action<Lobby> OnEntrouNaSalaComSucesso;
    public event Action OnSaiuDaSalaComSucesso;
    public event Action<string> OnErroDeSala;
    public event Action<SalaLocalSnapshot> OnEntrouEmSalaLocalComSucesso;

    [Header("NGO")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private bool autoFindNetworkManager = true;

    private const string CHAVE_HOST_STEAM_ID = "HostSteamId";

    public const string CHAVE_NOME_SALA = "NomeDaSala";
    public const string CHAVE_NOME_MESTRE = "NomeDoMestre";
    public const string CHAVE_SISTEMA_SALA = "SistemaSala";
    public const string CHAVE_DESCRICAO_SALA = "DescricaoSala";
    public const string CHAVE_IMAGEM_SALA_PATH = "ImagemSalaPath";
    // ID estável da sala — persiste entre sessões mesmo com novos lobbies Steam
    public const string CHAVE_SALA_LOCAL_ID = "SalaLocalId";

    private void Awake()
    {
        ResolveNetworkManager();
    }

    private void Start()
    {
        // Se inscreve para escutar quando o jogador aceitar um convite de amigo pela overlay (Shift+Tab)
        SteamFriends.OnGameLobbyJoinRequested += AoAceitarConviteDaSteam;
    }

    private void OnDestroy()
    {
        // Limpa a inscrição para não dar erro de memória quando fechar o jogo
        SteamFriends.OnGameLobbyJoinRequested -= AoAceitarConviteDaSteam;
    }

    // ==========================================
    // 1. CRIAR A SALA (O MESTRE)
    // ==========================================
    public async void CriarSalaSegura(
        int maxJogadores = 6,
        string nomeDaSala = "",
        string sistemaDaSala = "",
        string descricaoDaSala = "",
        string imagemDaSalaPath = "",
        string salaLocalId = "")
    {
        Debug.Log("Pedindo para a Steam criar a sala...");

        // Pede para criar um lobby na Steam
        var lobbyCriado = await SteamMatchmaking.CreateLobbyAsync(maxJogadores);

        if (!lobbyCriado.HasValue)
        {
            Debug.LogError("A Steam falhou em criar a sala.");
            OnErroDeSala?.Invoke("A Steam falhou em criar a sala.");
            return;
        }

        SalaAtual = lobbyCriado.Value;

        // Gera o ID estável da sala caso não tenha sido passado
        if (string.IsNullOrWhiteSpace(salaLocalId))
            salaLocalId = Guid.NewGuid().ToString("N");

        // O SISTEMA DE SEGURANÇA AQUI:
        SalaAtual.Value.SetData(CHAVE_SEGURANCA, VALOR_SEGURANCA);
        SalaAtual.Value.SetData(CHAVE_SALA_LOCAL_ID, salaLocalId);
        
        // Outros dados úteis para mostrar na UI depois
        SalaAtual.Value.SetData(CHAVE_NOME_MESTRE, SteamClient.Name);
        SalaAtual.Value.SetData(CHAVE_NOME_SALA, string.IsNullOrWhiteSpace(nomeDaSala) ? $"Mesa de {SteamClient.Name}" : nomeDaSala.Trim());
        SalaAtual.Value.SetData(CHAVE_SISTEMA_SALA, string.IsNullOrWhiteSpace(sistemaDaSala) ? "Sistema não definido" : sistemaDaSala.Trim());
        SalaAtual.Value.SetData(CHAVE_DESCRICAO_SALA, string.IsNullOrWhiteSpace(descricaoDaSala) ? "Sem descrição." : descricaoDaSala.Trim());
        SalaAtual.Value.SetData(CHAVE_IMAGEM_SALA_PATH, string.IsNullOrWhiteSpace(imagemDaSalaPath) ? string.Empty : imagemDaSalaPath.Trim());
        SalaAtual.Value.SetData(CHAVE_HOST_STEAM_ID, SteamClient.SteamId.ToString());
        
        // Define quem pode entrar. 
        // Se colocar .SetFriendsOnly(), NINGUÉM acha na busca pública, só entra por convite.
        SalaAtual.Value.SetPublic(); 
        SalaAtual.Value.SetJoinable(true);

        bool hostIniciado = IniciarHostNetcode();
        if (!hostIniciado)
        {
            SalaAtual.Value.Leave();
            SalaAtual = null;
            OnErroDeSala?.Invoke("Falha ao iniciar Host da Unity Netcode.");
            return;
        }

        Debug.Log($"<color=green>Sala criada com sucesso!</color> Filtro de segurança ativado.");
        OnSalaCriadaComSucesso?.Invoke(SalaAtual.Value);
    }

    // ==========================================
    // 2. BUSCAR SALAS (O JOGADOR NO MENU)
    // ==========================================
    public async Task<Lobby[]> BuscarSalasDoMeuJogoAsync()
    {
        Lobby[] salasEncontradas = await SteamMatchmaking.LobbyList
            .WithKeyValue(CHAVE_SEGURANCA, VALOR_SEGURANCA) // Ignora o resto do Spacewar!
            .RequestAsync();

        if (salasEncontradas == null)
            return Array.Empty<Lobby>();

        return salasEncontradas;
    }

    public async void BuscarSalasDoMeuJogo()
    {
        Debug.Log("Buscando salas compatíveis na Steam...");

        Lobby[] salasEncontradas = await BuscarSalasDoMeuJogoAsync();

        if (salasEncontradas != null && salasEncontradas.Length > 0)
        {
            Debug.Log($"Encontramos {salasEncontradas.Length} salas do nosso VTT!");
            
            foreach (Lobby sala in salasEncontradas)
            {
                string nomeMestre = sala.GetData(CHAVE_NOME_MESTRE);
                Debug.Log($"Sala encontrada: Mesa de {nomeMestre} - {sala.MemberCount}/{sala.MaxMembers} jogadores.");
                
                // Aqui você instanciaria um botão na UI (no seu ScrollView) 
                // para o jogador clicar e chamar a função EntrarNaSala(sala);
            }
        }
        else
        {
            Debug.Log("Nenhuma sala do nosso VTT aberta no momento.");
        }
    }

    // ==========================================
    // 3. ENTRAR NA SALA (VIA BUSCA)
    // ==========================================
    public async void EntrarNaSala(Lobby salaAlvo)
    {
        Debug.Log($"Tentando entrar na sala...");
        RoomEnter entrar = await salaAlvo.Join();

        if (entrar == RoomEnter.Success)
        {
            SalaAtual = salaAlvo;

            ulong hostSteamId = ObterSteamIdDoHost(salaAlvo);
            bool clientIniciado = IniciarClientNetcode(hostSteamId);
            if (!clientIniciado)
            {
                OnErroDeSala?.Invoke("Entrou na sala Steam, mas falhou ao iniciar Client da Unity Netcode.");
                return;
            }

            Debug.Log("<color=blue>Entrou na sala com sucesso!</color>");
            PlayerStatsUI.RegistrarEntradaNaSala();
            OnEntrouNaSalaComSucesso?.Invoke(salaAlvo);
            // AQUI O NETCODE VAI ENTRAR EM AÇÃO (Próximo passo)
        }
        else
        {
            Debug.LogError("Falha ao entrar na sala.");
            OnErroDeSala?.Invoke($"Falha ao entrar na sala. Código: {entrar}");
        }
    }

    // ==========================================
    // 4. ENTRAR VIA CONVITE DE AMIGO (SHIFT + TAB)
    // ==========================================
    private void AoAceitarConviteDaSteam(Lobby sala, SteamId idDoAmigo)
    {
        Debug.Log("Você aceitou o convite de um amigo pela Steam! Entrando...");
        // A Steam já te passa a sala pronta, você só precisa mandar o Join
        EntrarNaSala(sala);
    }

    // ==========================================
    // 5. SAIR DA SALA
    // ==========================================
    public void SairDaSala()
    {
        FinalizarNetcodeSeAtivo();

        if (SalaAtual.HasValue)
        {
            SalaAtual.Value.Leave();
            SalaAtual = null;
            Debug.Log("Você saiu da sala.");
            OnSaiuDaSalaComSucesso?.Invoke();
        }
    }

    // ==========================================
    // 5B. REABRIR SALA SALVA (O MESTRE)
    // ==========================================
    public void ReabrirSalaSalva(SalaLocalSnapshot snapshot)
    {
        SnapshotReabertura = snapshot;
        int maxPlayers = snapshot.maxMembros > 0 ? snapshot.maxMembros : 6;
        // Passa o salaLocalId estável para que o novo lobby Steam guarde o mesmo ID
        CriarSalaSegura(maxPlayers, snapshot.nomeSala, snapshot.sistemaSala,
                        snapshot.descricaoSala, snapshot.imagemSalaPath, snapshot.salaLocalId);
    }

    public void LimparSnapshotReabertura()
    {
        SnapshotReabertura = null;
    }

    // ==========================================
    // 5C. ENTRAR EM SALA LOCAL (OFFLINE/FALLBACK)
    // ==========================================
    public void EntrarNaSalaLocal(SalaLocalSnapshot salaLocal)
    {
        if (salaLocal == null)
        {
            OnErroDeSala?.Invoke("Snapshot de sala local é nulo.");
            return;
        }

        Debug.Log($"[SteamLobby] Entrando em sala local (offline): {salaLocal.nomeSala}");

        // Simular um estado de sala local sem precisar do Steam
        // SalaAtual fica como null já que não há lobby Steam, mas disparamos o evento de sucesso
        OnEntrouEmSalaLocalComSucesso?.Invoke(salaLocal);
        Debug.Log($"<color=cyan>Entrou em sala local com sucesso! Modo offline.</color>");
    }

    public bool EstaEmSalaLocal => SalaAtual == null;

    public bool DefinirDadoDaSala(string chave, string valor)
    {
        if (!SalaAtual.HasValue)
        {
            Debug.LogWarning("[SteamLobby] Não há sala ativa para salvar dado.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(chave))
        {
            Debug.LogWarning("[SteamLobby] Chave vazia ao tentar salvar dado da sala.");
            return false;
        }

        string safeValue = valor ?? string.Empty;

        try
        {
            SalaAtual.Value.SetData(chave, safeValue);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SteamLobby] Falha ao salvar dado da sala ('{chave}'): {e.Message}");
            return false;
        }
    }

    public string ObterDadoDaSala(string chave)
    {
        if (!SalaAtual.HasValue || string.IsNullOrWhiteSpace(chave))
            return string.Empty;

        try
        {
            return SalaAtual.Value.GetData(chave);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SteamLobby] Falha ao ler dado da sala ('{chave}'): {e.Message}");
            return string.Empty;
        }
    }

    private bool IniciarHostNetcode()
    {
        NetworkManager nm = GetNetworkManager();
        if (nm == null)
        {
            Debug.LogError("[SteamLobby] NetworkManager não encontrado. Adicione um GameObject com NetworkManager na cena.");
            return false;
        }

        FinalizarNetcodeSeAtivo();

        if (!ConfigurarSteamIdNoTransport(SteamClient.SteamId, true))
        {
            Debug.LogError("[SteamLobby] Falha ao configurar Facepunch Transport para Host.");
            return false;
        }

        AtivarNetworkObjectsDeInscena();

        bool ok = nm.StartHost();
        Debug.Log(ok ? "[SteamLobby] Host NGO iniciado." : "[SteamLobby] StartHost retornou false.");
        return ok;
    }

    private bool IniciarClientNetcode(ulong hostSteamId)
    {
        NetworkManager nm = GetNetworkManager();
        if (nm == null)
        {
            Debug.LogError("[SteamLobby] NetworkManager não encontrado. Adicione um GameObject com NetworkManager na cena.");
            return false;
        }

        FinalizarNetcodeSeAtivo();

        if (hostSteamId == 0)
        {
            Debug.LogError("[SteamLobby] Host SteamId inválido para conexão cliente.");
            return false;
        }

        if (!ConfigurarSteamIdNoTransport((SteamId)hostSteamId, false))
        {
            Debug.LogError("[SteamLobby] Falha ao configurar Facepunch Transport com SteamId do host.");
            return false;
        }

        AtivarNetworkObjectsDeInscena();

        bool ok = nm.StartClient();
        Debug.Log(ok ? "[SteamLobby] Client NGO iniciado." : "[SteamLobby] StartClient retornou false.");
        return ok;
    }

    // Workaround para bug do NGO: PopulateScenePlacedObjects usa FindObjectsByType
    // com includeInactive=false, ignorando NetworkObjects em GameObjects inativos.
    // Ativamos todos antes de iniciar o NGO. Objetos com CanvasGroup.alpha=0
    // permanecem visualmente ocultos.
    private void AtivarNetworkObjectsDeInscena()
    {
        var networkObjects = UnityEngine.Object.FindObjectsByType<NetworkObject>(
            UnityEngine.FindObjectsInactive.Include,
            UnityEngine.FindObjectsSortMode.None);

        foreach (var no in networkObjects)
        {
            if (no.gameObject.scene.IsValid() && !no.gameObject.activeInHierarchy)
            {
                no.gameObject.SetActive(true);
                Debug.Log($"[SteamLobby] NetworkObject de inscena ativado para registro no NGO: {no.gameObject.name}");
            }
        }
    }

    private void FinalizarNetcodeSeAtivo()
    {
        NetworkManager nm = GetNetworkManager();
        if (nm == null) return;
        if (nm.IsListening)
            nm.Shutdown();
    }

    private ulong ObterSteamIdDoHost(Lobby lobby)
    {
        string salvo = lobby.GetData(CHAVE_HOST_STEAM_ID);
        if (ulong.TryParse(salvo, out ulong hostSteamId) && hostSteamId > 0)
            return hostSteamId;

        return lobby.Owner.Id;
    }

    private bool ConfigurarSteamIdNoTransport(SteamId steamId, bool hostMode)
    {
        NetworkManager nm = GetNetworkManager();
        if (nm == null) return false;

        object transport = nm.NetworkConfig != null
            ? nm.NetworkConfig.NetworkTransport
            : null;

        if (transport == null)
        {
            Debug.LogError("[SteamLobby] NetworkTransport não configurado no NetworkManager.");
            return false;
        }

        Type type = transport.GetType();
        bool aplicou = false;

        string[] candidateMembers =
        {
            "targetSteamId", "TargetSteamId",
            "steamId", "SteamId",
            "connectToSteamID", "ConnectToSteamID"
        };

        ulong targetValue = hostMode ? SteamClient.SteamId : steamId;

        for (int i = 0; i < candidateMembers.Length; i++)
        {
            string memberName = candidateMembers[i];

            FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (field != null)
            {
                if (TrySetMemberValue(field.FieldType, targetValue, value => field.SetValue(transport, value)))
                    aplicou = true;
                continue;
            }

            PropertyInfo prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (prop != null && prop.CanWrite)
            {
                if (TrySetMemberValue(prop.PropertyType, targetValue, value => prop.SetValue(transport, value)))
                    aplicou = true;
            }
        }

        if (!aplicou)
        {
            Debug.LogWarning($"[SteamLobby] Não encontrei campo/propriedade de SteamId no transport {type.Name}. Verifique se é FacepunchTransport.");
        }

        return aplicou;
    }

    private NetworkManager GetNetworkManager()
    {
        ResolveNetworkManager();
        return networkManager != null ? networkManager : NetworkManager.Singleton;
    }

    private void ResolveNetworkManager()
    {
        if (networkManager == null && NetworkManager.Singleton != null)
            networkManager = NetworkManager.Singleton;

        if (networkManager == null && autoFindNetworkManager)
            networkManager = FindObjectOfType<NetworkManager>(true);

        if (networkManager != null && !networkManager.gameObject.activeInHierarchy)
            networkManager.gameObject.SetActive(true);
    }

    private bool TrySetMemberValue(Type targetType, ulong value, Action<object> setter)
    {
        try
        {
            if (targetType == typeof(ulong))
            {
                setter(value);
                return true;
            }

            if (targetType == typeof(long))
            {
                setter((long)value);
                return true;
            }

            if (targetType == typeof(uint))
            {
                setter((uint)value);
                return true;
            }

            if (targetType == typeof(string))
            {
                setter(value.ToString());
                return true;
            }

            if (targetType == typeof(SteamId))
            {
                setter((SteamId)value);
                return true;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SteamLobby] Falha ao setar SteamId no transport: " + e.Message);
        }

        return false;
    }
}