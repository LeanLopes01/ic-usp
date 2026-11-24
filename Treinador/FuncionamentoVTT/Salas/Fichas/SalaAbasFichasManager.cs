using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SalaAbasFichasManager : MonoBehaviour
{
    [Serializable]
    private class SalaTabsPayload
    {
        public SalaFichaTabState[] tabs = Array.Empty<SalaFichaTabState>();
    }

    [Serializable]
    private class FichaDonoEntry
    {
        public int fichas_general_id;
        public int fichas_dnd_id;
        public string dono_steam_id;
    }

    [Serializable]
    private class FichaDonosPayload
    {
        public FichaDonoEntry[] itens = Array.Empty<FichaDonoEntry>();
    }

    [Serializable]
    private class SalaFichaTabState
    {
        public int fichas_general_id;
        public int fichas_dnd_id;
        public string name_pc;
        public int level_pc;
        public string race_pc;
        public string class_pc;
        public string owner_steam_id; // vazio = ficha própria (saves antigos, tratado como "minha")

        // Separado de owner_steam_id (que é chave de partição no lobby-data — ver SalvarAbasNoLobby).
        // Controla só visibilidade da aba: quem pode VER, não quem é dono do conteúdo. Seedado com
        // owner_steam_id na criação; muda via AtribuirDono/AplicarDonoRecebido.
        public string dono_atual_steam_id;
    }

    private class SalaFichaTabRuntime
    {
        public SalaFichaTabState state;
        public GameObject tabButtonObject;
        public Button tabButton;
        public TMP_Text tabTitle;
    }

    [Header("Dependências")]
    [SerializeField] private SteamLobbyManager steamLobbyManager;
    [SerializeField] private MapManager mapManager;
    [SerializeField] private FichaSyncManager fichaSyncManager;

    [Header("Mapa")]
    [SerializeField] private Button botaoAbaMapa;
    [SerializeField] private GameObject painelMapa;

    [Header("Abas de Ficha")]
    [SerializeField] private Transform abasContent;
    [SerializeField] private GameObject abaFichaPrefab;

    [Header("Painel de Fichas Importadas (toggle com o Chat)")]
    [Tooltip("GameObject que envolve abasContent — vira condicional pra poder ocupar a mesma área " +
        "de tela do Chat, alternado pelo slot FichasImportadas em BarraFerramentasManager.")]
    [SerializeField] private GameObject painelAbasFichas;

    [Header("Adicionar Ficha")]
    [Tooltip("Abre o painel Conteudo (BarraFerramentasManager) já na sub-aba Ficha — ver " +
        "AbrirConteudoFicha. Substitui o antigo popup SalaFichaLoaderUI.")]
    [SerializeField] private Button botaoAdicionarAba;
    [SerializeField] private Button botaoFecharAbaAtual;

    [Header("Painel de visualização de ficha")]
    [SerializeField] private FichaCatalogoPanelUI fichaCatalogoPanel;

    [Header("UI - Tooltip de Habilidades/Ações (Prefab)")]
    [SerializeField] private GameObject tooltipPrefab;
    private TooltipUI tooltipInstancia;

    [Header("Persistência Steam Lobby")]
    [SerializeField] private bool salvarAbasNoLobby = true;
    [SerializeField] private bool restaurarAbasAoEntrar = true;
    [SerializeField] private string chaveLobbyTabs = "SalaTabsFichasJson";
    [SerializeField] private string chaveLobbyDonos = "FichaDonosAtuais"; // só o Mestre escreve
    [SerializeField, Min(1)] private int maxAbasFicha = 8;

    private readonly List<SalaFichaTabRuntime> tabsAbertas = new List<SalaFichaTabRuntime>();
    private SalaFichaTabRuntime abaSelecionada;

    /// <summary>Disparado sempre que uma aba é criada/fechada ou a visibilidade dela muda (atribuição
    /// de dono) — BarraFerramentasManager assina isso pra repopular a galeria do slot FichasImportadas
    /// (ver PopularGaleriaFichasImportadas).</summary>
    public event Action OnFichasImportadasAlterado;

    private void Awake()
    {
        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();

        if (mapManager == null)
            mapManager = FindFirstObjectByType<MapManager>();

        if (fichaSyncManager == null)
            fichaSyncManager = FindFirstObjectByType<FichaSyncManager>();
    }

    private void OnEnable()
    {
        InstanciarTooltip();
        if (botaoAbaMapa != null)
        {
            botaoAbaMapa.onClick.RemoveListener(SelecionarMapa);
            botaoAbaMapa.onClick.AddListener(SelecionarMapa);
            ConfigurarTooltipNomeAba(botaoAbaMapa.gameObject, "Mapa");
        }

        if (botaoAdicionarAba != null)
        {
            botaoAdicionarAba.onClick.RemoveListener(AbrirConteudoFicha);
            botaoAdicionarAba.onClick.AddListener(AbrirConteudoFicha);
        }

        if (botaoFecharAbaAtual != null)
        {
            botaoFecharAbaAtual.onClick.RemoveListener(FecharAbaAtual);
            botaoFecharAbaAtual.onClick.AddListener(FecharAbaAtual);
        }

        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso += OnSalaDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso += OnSalaDisponivel;
            steamLobbyManager.OnSaiuDaSalaComSucesso += OnSaiuDaSala;
        }

        // Checkpoint 2 do sync de fichas (Bug D): quando um jogador novo entra, cada peer reenvia
        // o conteúdo completo das SUAS PRÓPRIAS abas abertas pra ele. Sem gate de host — fichas são
        // autoritativas por-jogador (ao contrário do mapa, que só o host reenvia).
        Steamworks.SteamMatchmaking.OnLobbyMemberJoined += AoNovoMembroEntrarReenviarFichas;

        SelecionarMapa();

        if (mapManager != null)
            mapManager.DefinirSalaAtiva(steamLobbyManager != null && steamLobbyManager.SalaAtual.HasValue);

        if (restaurarAbasAoEntrar)
        {
            TentarRestaurarAbasDoLobby();
            RestaurarDonosDoLobby();
        }
    }

    private void OnDisable()
    {
        if (mapManager != null)
            mapManager.DefinirAbaMapaAtiva(false);

        if (botaoAbaMapa != null)
            botaoAbaMapa.onClick.RemoveListener(SelecionarMapa);

        if (botaoAdicionarAba != null)
            botaoAdicionarAba.onClick.RemoveListener(AbrirConteudoFicha);

        if (botaoFecharAbaAtual != null)
            botaoFecharAbaAtual.onClick.RemoveListener(FecharAbaAtual);

        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso -= OnSalaDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso -= OnSalaDisponivel;
            steamLobbyManager.OnSaiuDaSalaComSucesso -= OnSaiuDaSala;
        }

        Steamworks.SteamMatchmaking.OnLobbyMemberJoined -= AoNovoMembroEntrarReenviarFichas;
    }

    private void AoNovoMembroEntrarReenviarFichas(Steamworks.Data.Lobby lobby, Steamworks.Friend novoMembro)
    {
        if (fichaSyncManager == null) return;

        string meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        for (int i = 0; i < tabsAbertas.Count; i++)
        {
            SalaFichaTabState state = tabsAbertas[i]?.state;
            if (state == null || state.fichas_dnd_id <= 0) continue;

            // Só reenvia as MINHAS abas — cada peer é responsável só pela própria ficha.
            bool ehMinha = string.IsNullOrEmpty(state.owner_steam_id) ||
                           string.Equals(state.owner_steam_id, meuSteamId, StringComparison.Ordinal);
            if (!ehMinha) continue;

            fichaSyncManager.TransmitirFichaParaJogador(state.fichas_general_id, state.fichas_dnd_id, novoMembro.Id);
        }
    }

    // ── Tooltip ──────────────────────────────────────────────────────────────

    private void InstanciarTooltip()
    {
        if (tooltipPrefab == null || tooltipInstancia != null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        canvas ??= FindFirstObjectByType<Canvas>();
        if (canvas == null) return;

        GameObject go = Instantiate(tooltipPrefab, canvas.transform);
        tooltipInstancia = go.GetComponent<TooltipUI>();
        tooltipInstancia?.Esconder();
        go.transform.SetAsLastSibling();
    }

    public void MostrarTooltip(string titulo, string subtitulo, string descricao)
    {
        tooltipInstancia?.Mostrar(titulo, subtitulo, descricao);
    }

    public void EsconderTooltip()
    {
        tooltipInstancia?.Esconder();
    }

    /// <summary>
    /// Configura PointerEnter/Exit num GameObject para exibir o tooltip com os textos fornecidos.
    /// </summary>
    public void ConfigurarHoverTooltip(GameObject go, string titulo, string subtitulo, string descricao)
    {
        if (go == null) return;

        EventTrigger trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();

        EventTrigger.Entry enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => MostrarTooltip(titulo, subtitulo, descricao));
        trigger.triggers.Add(enter);

        EventTrigger.Entry exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => EsconderTooltip());
        trigger.triggers.Add(exit);
    }

    // Hover PointerEnter/Exit num botão de aba (ficha ou "Mapa") mostrando seu nome no tooltip
    // compartilhado de BarraFerramentasManager — mesmo mecanismo já usado nos botões da barra de
    // ferramentas e nos itens de galeria, reaproveitado aqui em vez de duplicar a lógica de
    // posicionamento/texto. Cada tabButtonObject é instanciado uma única vez por aba (não passa por
    // um ciclo de re-registro tipo OnEnable), então não precisa de guarda contra duplicação — só o
    // botão fixo da aba "Mapa" (chamado a cada OnEnable) tem esse risco, evitado checando o EventTrigger.
    private static void ConfigurarTooltipNomeAba(GameObject go, string nome)
    {
        if (go == null || string.IsNullOrWhiteSpace(nome)) return;

        RectTransform rect = go.transform as RectTransform;
        if (rect == null) return;

        EventTrigger trigger = go.GetComponent<EventTrigger>();
        if (trigger != null && trigger.triggers.Exists(e => e.eventID == EventTriggerType.PointerEnter))
            return;

        if (trigger == null) trigger = go.AddComponent<EventTrigger>();

        EventTrigger.Entry entrar = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        entrar.callback.AddListener(_ => BarraFerramentasManager.Instancia?.MostrarNomeHover(nome, rect));
        trigger.triggers.Add(entrar);

        EventTrigger.Entry sair = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        sair.callback.AddListener(_ => BarraFerramentasManager.Instancia?.EsconderNomeHover());
        trigger.triggers.Add(sair);
    }

    private void OnSalaDisponivel(Steamworks.Data.Lobby lobby)
    {
        if (mapManager != null)
            mapManager.DefinirSalaAtiva(true);

        if (restaurarAbasAoEntrar)
        {
            TentarRestaurarAbasDoLobby();
            RestaurarDonosDoLobby();
        }
    }

    private void OnSaiuDaSala()
    {
        if (mapManager != null)
            mapManager.DefinirSalaAtiva(false);

        LimparAbasInstanciadas();
        SelecionarMapa();
    }

    /// <summary>Chamado pelo botaoAdicionarAba — abre o painel Conteudo (BarraFerramentasManager) já
    /// na sub-aba Ficha, que lista as fichas locais filtradas pelo sistema da sala e importa com um
    /// clique (ver BarraFerramentasManager.PopularConteudoFicha/ImportarFichaDoConteudo). Substitui o
    /// antigo popup SalaFichaLoaderUI, removido.</summary>
    private void AbrirConteudoFicha()
    {
        BarraFerramentasManager.Instancia?.AbrirConteudoESelecionarFicha();
    }

    // ── Toggle do painel de fichas importadas (mesma área do Chat) ─────────────

    public bool EstaAbertoPainelAbas() => painelAbasFichas != null && painelAbasFichas.activeSelf;
    public void AbrirPainelAbas()  { if (painelAbasFichas != null) painelAbasFichas.SetActive(true); }
    public void FecharPainelAbas() { if (painelAbasFichas != null) painelAbasFichas.SetActive(false); }

    private FichaData[] ObterFichasDoUsuario()
    {
        if (DataManager.Instance == null)
            return Array.Empty<FichaData>();

        if (DataManager.Instance.HasData)
            return DataManager.Instance.GetFichasCopy();

        int pid = DataManager.Instance.GetCurrentId();
        if (pid > 0)
            DataManager.Instance.StartFetch(pid, false);

        return DataManager.Instance.GetFichasCopy();
    }

    public int ObterSistemaIdDaSalaAtual()
    {
        if (steamLobbyManager == null) return -1;
        string sistema = steamLobbyManager.ObterDadoDaSala(SteamLobbyManager.CHAVE_SISTEMA_SALA);
        return MapearNomeSistemaParaId(sistema);
    }

    private static int MapearNomeSistemaParaId(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return -1;
        string m = nome.ToLowerInvariant().Replace("-", "").Replace(" ", "").Replace("&", "");
        if (m.Contains("quinta") || m.Contains("dnd") || m.Contains("dragon") || m.Contains("5e")) return 1;
        if (m.Contains("desbravador") || m.Contains("pathfinder") || m.Contains("pf2")) return 5;
        if (m.Contains("cyber")) return 4;
        return -1;
    }

    public void CriarOuSelecionarAbaDaFicha(FichaData ficha, string ownerSteamId = null)
    {
        if (ficha == null || ficha.fichas_dnd_id <= 0)
        {
            Debug.LogWarning("[SalaAbas] CriarOuSelecionarAbaDaFicha: ficha nula ou sem fichas_dnd_id válido.");
            return;
        }

        string meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        ownerSteamId ??= meuSteamId;
        bool ehMinha = string.Equals(ownerSteamId, meuSteamId, StringComparison.Ordinal);

        if (EncontrarAba(ownerSteamId, ficha.fichas_dnd_id) != null)
        {
            Debug.Log($"[SalaAbas] Aba da ficha '{ficha.name_pc}' (dndId={ficha.fichas_dnd_id}) já está aberta — ignorando.");
            return;
        }

        if (tabsAbertas.Count >= maxAbasFicha)
        {
            Debug.LogWarning($"[SalaAbas] Limite de abas de ficha atingido ({maxAbasFicha}).");
            return;
        }

        if (abaFichaPrefab == null || abasContent == null)
        {
            Debug.LogWarning("[SalaAbas] abaFichaPrefab/abasContent não configurados.");
            return;
        }

        var state = new SalaFichaTabState
        {
            fichas_general_id  = ficha.fichas_general_id,
            fichas_dnd_id      = ficha.fichas_dnd_id,
            name_pc            = string.IsNullOrWhiteSpace(ficha.name_pc) ? "Ficha" : ficha.name_pc,
            level_pc           = ficha.level_pc,
            race_pc            = ficha.race_pc,
            class_pc           = ficha.class_pc,
            owner_steam_id     = ownerSteamId,
            dono_atual_steam_id = ownerSteamId
        };

        SalaFichaTabRuntime runtime = new SalaFichaTabRuntime { state = state };

        runtime.tabButtonObject = Instantiate(abaFichaPrefab, abasContent);
        runtime.tabButton       = runtime.tabButtonObject.GetComponent<Button>();
        runtime.tabTitle        = runtime.tabButtonObject.GetComponentInChildren<TMP_Text>(true);
        if (runtime.tabTitle != null)
            runtime.tabTitle.text = state.name_pc;
        ConfigurarTooltipNomeAba(runtime.tabButtonObject, state.name_pc);

        FichaData fichaCapturada = ficha;
        string ownerCapturado = ownerSteamId;
        if (runtime.tabButton != null)
            runtime.tabButton.onClick.AddListener(() =>
            {
                SelecionarAba(runtime);
                fichaCatalogoPanel?.ToggleParaFicha(fichaCapturada, ownerCapturado);
            });

        int generalIdCapturado = state.fichas_general_id;
        int dndIdCapturado     = state.fichas_dnd_id;
        SalaFichaAbaContextMenuUI contextMenu = runtime.tabButtonObject.GetComponent<SalaFichaAbaContextMenuUI>()
            ?? runtime.tabButtonObject.AddComponent<SalaFichaAbaContextMenuUI>();
        contextMenu.Configurar(posicaoTela => AbrirMenuDono(generalIdCapturado, dndIdCapturado, posicaoTela));

        AplicarVisibilidade(runtime);

        tabsAbertas.Add(runtime);
        SelecionarAba(runtime);
        SalvarAbasNoLobby();
        OnFichasImportadasAlterado?.Invoke();

        if (ehMinha)
        {
            // Token e transmissão de conteúdo só fazem sentido pra MINHA própria ficha — só eu
            // tenho a imagem/o conteúdo completo no meu storage local.
            AdicionarTokenDaFichaAoMapa(ficha);
            fichaSyncManager?.TransmitirFichaPropria(ficha.fichas_general_id, ficha.fichas_dnd_id);
        }
    }

    private void AdicionarTokenDaFichaAoMapa(FichaData ficha)
    {
        if (mapManager == null || ficha == null) return;

        string steamId  = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        int    playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId()      : 0;

        if (!LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId,
                ficha.fichas_general_id, ficha.fichas_dnd_id, out LocalFichaData snap))
        {
            Debug.LogWarning($"[SalaAbas] Token não adicionado: snapshot local não encontrado pra ficha " +
                $"'{ficha.name_pc}' (steamId={steamId}, playerId={playerId}, generalId={ficha.fichas_general_id}, dndId={ficha.fichas_dnd_id}).");
            return;
        }

        if (string.IsNullOrWhiteSpace(snap.token_image_path))
        {
            Debug.LogWarning($"[SalaAbas] Token não adicionado: ficha '{ficha.name_pc}' não tem imagem de token definida.");
            return;
        }

        string nome = string.IsNullOrWhiteSpace(snap.name_pc) ? "Ficha" : snap.name_pc;
        mapManager.AdicionarTokenAoCatalogo(nome, snap.token_image_path,
            ficha.fichas_general_id, ficha.fichas_dnd_id, steamId, playerId);
    }

    /// <summary>Chamado pelo botaoFecharAbaAtual — fecha e destrói a aba de ficha selecionada.</summary>
    public void FecharAbaAtual()
    {
        FecharAba(abaSelecionada);
    }

    private void FecharAbaSelecionada() => FecharAbaAtual();

    private void FecharAba(SalaFichaTabRuntime runtime)
    {
        if (runtime == null) return;

        if (runtime.tabButtonObject != null)
            Destroy(runtime.tabButtonObject);

        tabsAbertas.Remove(runtime);

        // Fecha o painel de visualização se estiver mostrando esta ficha
        if (fichaCatalogoPanel != null && fichaCatalogoPanel.EstaAberto)
            fichaCatalogoPanel.FecharPainel();

        if (abaSelecionada == runtime)
            SelecionarMapa();

        SalvarAbasNoLobby();
        OnFichasImportadasAlterado?.Invoke();
    }

    private SalaFichaTabRuntime EncontrarAba(string ownerSteamId, int fichaDndId)
    {
        for (int i = 0; i < tabsAbertas.Count; i++)
        {
            SalaFichaTabRuntime tab = tabsAbertas[i];
            if (tab?.state != null && tab.state.fichas_dnd_id == fichaDndId &&
                string.Equals(tab.state.owner_steam_id ?? string.Empty, ownerSteamId ?? string.Empty, StringComparison.Ordinal))
                return tab;
        }
        return null;
    }

    private SalaFichaTabRuntime EncontrarAbaPorFicha(int fichasGeneralId, int fichasDndId)
    {
        for (int i = 0; i < tabsAbertas.Count; i++)
        {
            SalaFichaTabRuntime tab = tabsAbertas[i];
            if (tab?.state != null && tab.state.fichas_general_id == fichasGeneralId &&
                tab.state.fichas_dnd_id == fichasDndId)
                return tab;
        }
        return null;
    }

    // ── Dono / visibilidade da aba ──────────────────────────────────────────────

    private bool SouMestre() =>
        Steamworks.SteamClient.IsValid && steamLobbyManager?.SalaAtual?.IsOwnedBy(Steamworks.SteamClient.SteamId) == true;

    // Só o dono atual e o Mestre veem a aba na faixa — token no mapa/galeria não é afetado (ver
    // MapManager.AplicarDonoDeToken, que só mexe em quem pode MOVER, não em visibilidade).
    private bool PodeVerFicha(SalaFichaTabState state)
    {
        if (state == null) return false;
        if (SouMestre()) return true;

        string meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        // Fallback pra owner_steam_id cobre abas restauradas de lobby-data/save salvos antes deste
        // campo existir (dono_atual_steam_id viria vazio do JSON antigo).
        string dono = string.IsNullOrEmpty(state.dono_atual_steam_id) ? state.owner_steam_id : state.dono_atual_steam_id;
        return string.IsNullOrEmpty(dono) || string.Equals(dono, meuSteamId, StringComparison.Ordinal);
    }

    private void AplicarVisibilidade(SalaFichaTabRuntime runtime)
    {
        if (runtime?.tabButtonObject == null || runtime.state == null) return;
        runtime.tabButtonObject.SetActive(PodeVerFicha(runtime.state));
    }

    /// <summary>Popula uma galeria de "fichas importadas" com cards FichaUI — um por aba atualmente
    /// visível pra MIM (mesma regra de PodeVerFicha já usada na faixa de abas: Mestre vê todas, os
    /// demais só a própria/as que forem atribuídas a eles). Usado pelo slot FichasImportadas de
    /// BarraFerramentasManager (ver PopularFichasImportadas lá). Clique no card seleciona a mesma aba
    /// da faixa (SelecionarAba + fichaCatalogoPanel); clique-direito abre o mesmo menu de atribuição
    /// de dono já usado nos botões da faixa (AbrirMenuDono) — Mestre-only, no-op pra quem não é.</summary>
    public void PopularGaleriaFichasImportadas(Transform content, GameObject prefabItem)
    {
        if (content == null || prefabItem == null) return;

        for (int i = content.childCount - 1; i >= 0; i--)
            Destroy(content.GetChild(i).gameObject);

        for (int i = 0; i < tabsAbertas.Count; i++)
        {
            SalaFichaTabRuntime runtime = tabsAbertas[i];
            SalaFichaTabState state = runtime?.state;
            if (state == null || !PodeVerFicha(state)) continue;

            GameObject go = Instantiate(prefabItem, content);
            FichaUI fichaUI = go.GetComponent<FichaUI>();
            if (fichaUI == null)
            {
                Debug.LogWarning("[SalaAbas] Prefab da galeria de fichas importadas não possui FichaUI.");
                Destroy(go);
                continue;
            }

            SalaFichaTabRuntime runtimeCapturado = runtime;
            fichaUI.SetCustomClickHandler(_ =>
            {
                SelecionarAba(runtimeCapturado);
                fichaCatalogoPanel?.ToggleParaFicha(EstadoParaFichaData(runtimeCapturado.state), runtimeCapturado.state.owner_steam_id);
            });
            fichaUI.SetFicha(state.name_pc, state.level_pc, state.race_pc, state.class_pc,
                state.fichas_general_id, state.fichas_dnd_id, ObterTokenImagePath(state));

            int generalIdCapturado = state.fichas_general_id;
            int dndIdCapturado     = state.fichas_dnd_id;
            SalaFichaAbaContextMenuUI contextMenu = go.GetComponent<SalaFichaAbaContextMenuUI>()
                ?? go.AddComponent<SalaFichaAbaContextMenuUI>();
            contextMenu.Configurar(posicaoTela => AbrirMenuDono(generalIdCapturado, dndIdCapturado, posicaoTela));
        }
    }

    private static FichaData EstadoParaFichaData(SalaFichaTabState state) => new FichaData
    {
        name_pc           = state.name_pc,
        level_pc          = state.level_pc,
        race_pc           = state.race_pc,
        class_pc          = state.class_pc,
        fichas_general_id = state.fichas_general_id,
        fichas_dnd_id     = state.fichas_dnd_id
    };

    /// <summary>Ficha atualmente aberta (aba) pertencente a um jogador da sala, ou null se ele não
    /// tiver nenhuma — usado por Salas/SocialTurnos (lista de jogadores) pra saber qual ficha abrir
    /// ao clicar no token de alguém. Primeira aba encontrada com esse dono, mesma resolução simples
    /// de EncontrarAba/EncontrarAbaPorFicha (um jogador só costuma ter uma aba própria aberta).</summary>
    public FichaData ObterFichaDoJogador(string steamId)
    {
        for (int i = 0; i < tabsAbertas.Count; i++)
        {
            SalaFichaTabState state = tabsAbertas[i]?.state;
            if (state != null && string.Equals(state.owner_steam_id ?? string.Empty, steamId ?? string.Empty, StringComparison.Ordinal))
                return EstadoParaFichaData(state);
        }
        return null;
    }

    // Mesma resolução em 3 níveis já usada por FichaCatalogoPanelUI.AbrirParaFicha: (1) meu próprio
    // storage local, (2) cache P2P de FichaSyncManager pra ficha de outro jogador, (3) sem imagem.
    private string ObterTokenImagePath(SalaFichaTabState state)
    {
        string meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;

        if (LocalFichaStorage.TryGetFichaSnapshot(meuSteamId, playerId,
                state.fichas_general_id, state.fichas_dnd_id, out LocalFichaData snapshotLocal))
            return snapshotLocal.token_image_path ?? string.Empty;

        if (fichaSyncManager != null &&
            fichaSyncManager.TryObterFichaRemota(state.owner_steam_id, state.fichas_dnd_id, out LocalFichaData snapshotRemoto))
            return snapshotRemoto.token_image_path ?? string.Empty;

        return string.Empty;
    }

    // Mestre-only: abre o menu de atalho com os jogadores da sala pra escolher o novo dono da ficha.
    private void AbrirMenuDono(int fichasGeneralId, int fichasDndId, Vector2 posicaoTela)
    {
        if (!SouMestre() || steamLobbyManager == null || !steamLobbyManager.SalaAtual.HasValue) return;
        if (MenuAtalhoManager.Instance == null) return;

        var opcoes = new List<OpcaoMenuAtalho>();
        foreach (Steamworks.Friend membro in steamLobbyManager.SalaAtual.Value.Members)
        {
            string steamIdDoMembro = membro.Id.ToString();
            opcoes.Add(new OpcaoMenuAtalho(membro.Name, () => AtribuirDono(fichasGeneralId, fichasDndId, steamIdDoMembro)));
        }

        MenuAtalhoManager.Instance.Mostrar(posicaoTela, opcoes);
    }

    // Mestre-only: aplica localmente (aba + token no mapa), propaga pra rede e persiste no lobby.
    public void AtribuirDono(int fichasGeneralId, int fichasDndId, string novoDono)
    {
        if (!SouMestre()) return;

        AplicarDonoRecebido(fichasGeneralId, fichasDndId, novoDono);
        mapManager?.AplicarDonoDeToken(fichasGeneralId, fichasDndId, novoDono);
        NetworkSyncManager.Instancia?.EnviarAtribuicaoDeDonoParaTodos(fichasGeneralId, fichasDndId, novoDono);
        SalvarDonosNoLobby();
    }

    /// <summary>Chamado pelo NetworkSyncManager quando a atribuição de dono chega de outro peer, e
    /// também na restauração ao entrar/reconectar (RestaurarDonosDoLobby) — SEM rebroadcast.</summary>
    public void AplicarDonoRecebido(int fichasGeneralId, int fichasDndId, string novoDono)
    {
        SalaFichaTabRuntime runtime = EncontrarAbaPorFicha(fichasGeneralId, fichasDndId);
        if (runtime?.state == null) return;

        runtime.state.dono_atual_steam_id = novoDono;
        AplicarVisibilidade(runtime);
        OnFichasImportadasAlterado?.Invoke();
    }

    private void SalvarDonosNoLobby()
    {
        if (!SouMestre() || steamLobbyManager == null) return;

        var payload = new FichaDonosPayload();
        var lista   = new List<FichaDonoEntry>();
        for (int i = 0; i < tabsAbertas.Count; i++)
        {
            SalaFichaTabState state = tabsAbertas[i]?.state;
            if (state == null || string.IsNullOrEmpty(state.dono_atual_steam_id)) continue;

            lista.Add(new FichaDonoEntry
            {
                fichas_general_id = state.fichas_general_id,
                fichas_dnd_id     = state.fichas_dnd_id,
                dono_steam_id     = state.dono_atual_steam_id
            });
        }
        payload.itens = lista.ToArray();
        steamLobbyManager.DefinirDadoDaSala(chaveLobbyDonos, JsonUtility.ToJson(payload));
    }

    private void RestaurarDonosDoLobby()
    {
        if (steamLobbyManager == null || !steamLobbyManager.SalaAtual.HasValue) return;

        string json = steamLobbyManager.ObterDadoDaSala(chaveLobbyDonos);
        if (string.IsNullOrWhiteSpace(json)) return;

        FichaDonosPayload payload;
        try { payload = JsonUtility.FromJson<FichaDonosPayload>(json); }
        catch (Exception e) { Debug.LogWarning("[SalaAbas] JSON de donos inválido no lobby: " + e.Message); return; }

        if (payload?.itens == null) return;

        for (int i = 0; i < payload.itens.Length; i++)
        {
            FichaDonoEntry entry = payload.itens[i];
            if (entry == null) continue;
            AplicarDonoRecebido(entry.fichas_general_id, entry.fichas_dnd_id, entry.dono_steam_id);
        }
    }

    private void SelecionarAba(SalaFichaTabRuntime runtime)
    {
        if (runtime == null || runtime.state == null)
            return;

        abaSelecionada = runtime;

        // GlobalFichaData representa MEU personagem ativo (usado em rolagens/ações) — só atualiza
        // quando a aba selecionada é a MINHA própria ficha; abrir a aba de outro jogador é só
        // visualização (ver Bug D).
        string meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        bool ehMinha = string.IsNullOrEmpty(runtime.state.owner_steam_id) ||
                       string.Equals(runtime.state.owner_steam_id, meuSteamId, StringComparison.Ordinal);
        if (ehMinha)
            AtualizarGlobalFichaData(runtime.state);

        PostHogTracker.DefinirContextoAtual($"Sala > Ficha: {runtime.state.name_pc}");
    }

    private void SelecionarMapa()
    {
        abaSelecionada = null;

        if (painelMapa != null)
            painelMapa.SetActive(true);

        if (mapManager != null)
            mapManager.DefinirAbaMapaAtiva(true);

        PostHogTracker.DefinirContextoAtual("Sala > Mapa");
    }

    private static void AtualizarGlobalFichaData(SalaFichaTabState state)
    {
        GlobalFichaData.fichasGeneralId = state.fichas_general_id;
        GlobalFichaData.fichasDndId = state.fichas_dnd_id;
        GlobalFichaData.nomePersonagem = state.name_pc ?? string.Empty;
        GlobalFichaData.levelPersonagem = state.level_pc;
        GlobalFichaData.racaPersonagem = state.race_pc ?? string.Empty;
        GlobalFichaData.classePersonagem = state.class_pc ?? string.Empty;
    }

    private string ChaveLobbyTabsPorJogador(string ownerSteamId) => $"{chaveLobbyTabs}_{ownerSteamId}";

    private void TentarRestaurarAbasDoLobby()
    {
        if (!restaurarAbasAoEntrar || steamLobbyManager == null) return;
        if (tabsAbertas.Count > 0) return;

        string meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        // Tenta primeiro restaurar as MINHAS próprias abas do storage local (tem tokenPath e
        // sobrevive a uma reabertura mesmo sem rede).
        bool restaurouLocal = false;
        string salaId = ObterSalaLocalId();
        if (!string.IsNullOrWhiteSpace(salaId))
        {
            SalaData dados = SalaDataStorage.LoadSalaData(salaId);
            if (dados?.fichasImportadas != null && dados.fichasImportadas.Length > 0)
            {
                foreach (FichaImportadaSaveData f in dados.fichasImportadas)
                {
                    if (f == null || f.fichas_dnd_id <= 0) continue;
                    // Saves antigos (sem owner_steam_id gravado) eram sempre da minha própria ficha.
                    string owner = string.IsNullOrWhiteSpace(f.owner_steam_id) ? meuSteamId : f.owner_steam_id;
                    CriarOuSelecionarAbaDaFicha(new FichaData
                    {
                        fichas_general_id = f.fichas_general_id,
                        fichas_dnd_id     = f.fichas_dnd_id,
                        name_pc           = f.name_pc,
                        level_pc          = f.level_pc,
                        race_pc           = f.race_pc,
                        class_pc          = f.class_pc
                    }, owner);
                }
                restaurouLocal = true;
            }
        }

        // Restaura as abas de TODOS os jogadores da sala — cada um grava numa chave de lobby data
        // PRÓPRIA (ver SalvarAbasNoLobby), pra não sobrescrever a lista dos demais.
        bool restaurouRemoto = false;
        if (steamLobbyManager.SalaAtual.HasValue)
        {
            FichaData[] fichasDoUsuario = ObterFichasDoUsuario();
            foreach (Steamworks.Friend membro in steamLobbyManager.SalaAtual.Value.Members)
            {
                string ownerSteamId = membro.Id.ToString();
                if (ownerSteamId == meuSteamId && restaurouLocal) continue; // já restaurado acima

                string json = steamLobbyManager.ObterDadoDaSala(ChaveLobbyTabsPorJogador(ownerSteamId));
                if (string.IsNullOrWhiteSpace(json)) continue;

                SalaTabsPayload payload;
                try { payload = JsonUtility.FromJson<SalaTabsPayload>(json); }
                catch (Exception e) { Debug.LogWarning("[SalaAbas] JSON de abas inválido no lobby: " + e.Message); continue; }

                if (payload?.tabs == null) continue;

                for (int i = 0; i < payload.tabs.Length; i++)
                {
                    SalaFichaTabState state = payload.tabs[i];
                    if (state == null || state.fichas_dnd_id <= 0) continue;

                    FichaData ficha = (ownerSteamId == meuSteamId
                        ? EncontrarFichaPorId(fichasDoUsuario, state.fichas_dnd_id)
                        : null) ?? new FichaData
                    {
                        fichas_general_id = state.fichas_general_id,
                        fichas_dnd_id     = state.fichas_dnd_id,
                        name_pc           = state.name_pc,
                        level_pc          = state.level_pc,
                        race_pc           = state.race_pc,
                        class_pc          = state.class_pc
                    };

                    CriarOuSelecionarAbaDaFicha(ficha, ownerSteamId);
                    restaurouRemoto = true;
                }
            }
        }

        if (restaurouLocal || restaurouRemoto)
            SelecionarMapa();
    }

    private static FichaData EncontrarFichaPorId(FichaData[] fichas, int fichaDndId)
    {
        if (fichas == null) return null;
        for (int i = 0; i < fichas.Length; i++)
        {
            FichaData f = fichas[i];
            if (f != null && f.fichas_dnd_id == fichaDndId)
                return f;
        }
        return null;
    }

    private void SalvarAbasNoLobby()
    {
        SalvarAbasLocal();

        if (!salvarAbasNoLobby || steamLobbyManager == null)
            return;

        string meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        var payload = new SalaTabsPayload();
        var lista   = new List<SalaFichaTabState>();
        for (int i = 0; i < tabsAbertas.Count; i++)
        {
            SalaFichaTabRuntime r = tabsAbertas[i];
            // Só grava NESTA chave (por-jogador) as abas que EU abri — abas de outros jogadores
            // vivem na chave deles, restauradas separadamente em TentarRestaurarAbasDoLobby. Se
            // todos escrevessem na mesma chave, o último a salvar sobrescreveria os demais.
            bool ehMinha = r?.state != null && (string.IsNullOrEmpty(r.state.owner_steam_id) ||
                string.Equals(r.state.owner_steam_id, meuSteamId, StringComparison.Ordinal));
            if (ehMinha) lista.Add(r.state);
        }
        payload.tabs = lista.ToArray();
        steamLobbyManager.DefinirDadoDaSala(ChaveLobbyTabsPorJogador(meuSteamId), JsonUtility.ToJson(payload));
    }

    private void SalvarAbasLocal()
    {
        string salaId = ObterSalaLocalId();
        if (string.IsNullOrWhiteSpace(salaId)) return;

        SalaData dados = SalaDataStorage.LoadSalaData(salaId) ?? new SalaData
        {
            salaLocalId = salaId,
            salaSteamId = steamLobbyManager != null
                ? steamLobbyManager.ObterDadoDaSala("SteamLobbyId") ?? string.Empty
                : string.Empty
        };

        string steamId  = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        int    playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId()      : 0;

        var fichas = new System.Collections.Generic.List<FichaImportadaSaveData>();
        for (int i = 0; i < tabsAbertas.Count; i++)
        {
            SalaFichaTabRuntime r = tabsAbertas[i];
            if (r?.state == null) continue;

            bool ehMinha = string.IsNullOrEmpty(r.state.owner_steam_id) ||
                           string.Equals(r.state.owner_steam_id, steamId, StringComparison.Ordinal);

            // tokenPath só existe no MEU storage local — buscar pra uma ficha de outro jogador
            // nunca encontraria nada (ela não existe em disco nesta máquina).
            string tokenPath = string.Empty;
            if (ehMinha && LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId,
                    r.state.fichas_general_id, r.state.fichas_dnd_id, out LocalFichaData snap))
                tokenPath = snap.token_image_path ?? string.Empty;

            fichas.Add(new FichaImportadaSaveData
            {
                fichas_general_id = r.state.fichas_general_id,
                fichas_dnd_id     = r.state.fichas_dnd_id,
                name_pc           = r.state.name_pc,
                level_pc          = r.state.level_pc,
                race_pc           = r.state.race_pc,
                class_pc          = r.state.class_pc,
                tokenPath         = tokenPath,
                owner_steam_id    = r.state.owner_steam_id
            });
        }

        dados.fichasImportadas = fichas.ToArray();
        SalaDataStorage.SaveSalaData(dados);
    }

    private string ObterSalaLocalId()
    {
        if (steamLobbyManager == null) return string.Empty;
        string steamId = steamLobbyManager.SalaAtual.HasValue
            ? steamLobbyManager.SalaAtual.Value.Id.ToString()
            : string.Empty;
        if (string.IsNullOrWhiteSpace(steamId)) return string.Empty;

        SalaLocalSnapshot[] salas = SalaLocalStorage.LoadAll();
        if (salas == null) return string.Empty;
        foreach (SalaLocalSnapshot s in salas)
            if (s != null && s.salaSteamId == steamId)
                return s.salaLocalId;
        return string.Empty;
    }

    private void LimparAbasInstanciadas()
    {
        for (int i = 0; i < tabsAbertas.Count; i++)
        {
            SalaFichaTabRuntime tab = tabsAbertas[i];
            if (tab?.tabButtonObject != null)
                Destroy(tab.tabButtonObject);
        }

        tabsAbertas.Clear();
        abaSelecionada = null;
    }
}
