using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Steamworks;
using Steamworks.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SalasSalvasUIManager : MonoBehaviour
{
    [Header("Dependências")]
    [SerializeField] private SteamLobbyManager steamLobbyManager;

    [Header("UI")]
    [SerializeField] private Button botaoCarregarSalas;
    [SerializeField] private Transform listaSalasContent;
    [SerializeField] private GameObject salaItemPrefab;
    [SerializeField] private TMP_Text feedbackText;

    [Header("Comportamento")]
    [SerializeField] private bool carregarAoHabilitar = false;
    [SerializeField] private bool buscarSalasSteamAoCarregar = true;
    [SerializeField] private bool mostrarSomenteSalasDoUsuario = true;

    [Header("Paginação")]
    [SerializeField] private IndicadorPaginasUI indicadorPaginas;
    [SerializeField, Min(1)] private int itensPorPagina = 9;
    [SerializeField] private Button botaoAnterior;
    [SerializeField] private Button botaoProximo;

    [Header("Filtro por Sistema (opcional)")]
    [Tooltip("Mesma base de FichaLoaderUI.cs — liga/desliga a seção inteira (painelFiltroSistema) " +
        "sem precisar de outra versão do prefab. Chips (toggle+texto) reaproveitando FiltroChipToggleUI, " +
        "descobertos a partir dos valores de sistemaSala realmente presentes nas salas carregadas.")]
    [SerializeField] private bool habilitarFiltroSistema = false;
    [SerializeField] private GameObject painelFiltroSistema;
    [SerializeField] private Transform  conteudoFiltroSistema; // Content onde os chips são instanciados
    [SerializeField] private GameObject filtroChipPrefab;      // prefab com FiltroChipToggleUI
    [SerializeField] private Button     btnExpandirFiltroSistema;
    [SerializeField] private Button     btnColapsarFiltroSistema;

    [Header("Busca")]
    [Tooltip("Filtra por nome da sala, nome do mestre ou sistema — busca em texto livre, " +
        "case-insensitive, combinada com o filtro de sistema (se ligado).")]
    [SerializeField] private TMP_InputField inputBusca;

    [Header("Navegação")]
    [SerializeField] private ToggleObjects toggleObjects;
    [Tooltip("Tela onde a sala aberta é exibida (TelaSalaUIManager vive nela).")]
    [SerializeField] private GameObject telaSalaCriada;

    private readonly List<GameObject> itensInstanciados = new List<GameObject>();
    private SalaLocalSnapshot[] todasSalas = new SalaLocalSnapshot[0];
    private SalaLocalSnapshot[] salasFiltradas = new SalaLocalSnapshot[0];
    private int paginaAtual;

    // ── Filtro por sistema ───────────────────────────────────────────────────
    private readonly HashSet<string> sistemasAtivos = new HashSet<string>();
    private readonly List<GameObject> chipsFiltroSistemaInstanciados = new List<GameObject>();

    // ── Busca ────────────────────────────────────────────────────────────────
    private string textoBuscaAtual = "";

    private void Awake()
    {
        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();

        if (toggleObjects == null)
            toggleObjects = FindFirstObjectByType<ToggleObjects>();

        if (painelFiltroSistema != null)
            painelFiltroSistema.SetActive(habilitarFiltroSistema);
    }

    private void OnEnable()
    {
        if (botaoCarregarSalas != null)
        {
            botaoCarregarSalas.onClick.RemoveListener(OnClickCarregarSalas);
            botaoCarregarSalas.onClick.AddListener(OnClickCarregarSalas);
        }

        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso += OnSalaDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso += OnSalaDisponivel;
        }

        botaoAnterior?.onClick.AddListener(PaginaAnterior);
        botaoProximo?.onClick.AddListener(ProximaPagina);

        inputBusca?.onValueChanged.AddListener(OnBuscaAlterada);

        if (habilitarFiltroSistema)
        {
            btnExpandirFiltroSistema?.onClick.AddListener(ExpandirFiltroSistema);
            btnColapsarFiltroSistema?.onClick.AddListener(ColapsarFiltroSistema);
            ColapsarFiltroSistema(); // começa recolhido, mesmo padrão de FichaLoaderUI
        }

        if (carregarAoHabilitar)
            _ = CarregarSalasSalvasAsync();
    }

    private void OnDisable()
    {
        if (botaoCarregarSalas != null)
            botaoCarregarSalas.onClick.RemoveListener(OnClickCarregarSalas);

        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso -= OnSalaDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso -= OnSalaDisponivel;
        }

        botaoAnterior?.onClick.RemoveListener(PaginaAnterior);
        botaoProximo?.onClick.RemoveListener(ProximaPagina);

        inputBusca?.onValueChanged.RemoveListener(OnBuscaAlterada);

        btnExpandirFiltroSistema?.onClick.RemoveListener(ExpandirFiltroSistema);
        btnColapsarFiltroSistema?.onClick.RemoveListener(ColapsarFiltroSistema);
    }

    private void OnSalaDisponivel(Lobby lobby)
    {
        string localIdLegado = steamLobbyManager?.SnapshotReabertura?.salaLocalId;
        SalvarSnapshotDaLobby(lobby, true, localIdLegado);

        // A sala foi criada/reaberta com sucesso — navega para a tela onde ela é exibida.
        // Sem isso, o GameObject da tela nunca é ativado via ToggleObjects, então o
        // LateUpdate dele desfaz qualquer SetActive manual feito por outro lugar.
        toggleObjects?.NavegarParaTela(telaSalaCriada);
    }

    private void OnClickCarregarSalas()
    {
        _ = CarregarSalasSalvasAsync();
    }

    public async Task CarregarSalasSalvasAsync()
    {
        SetFeedback("Carregando salas...");

        if (steamLobbyManager != null && buscarSalasSteamAoCarregar && SteamClient.IsValid)
        {
            Lobby[] salasSteam = await steamLobbyManager.BuscarSalasDoMeuJogoAsync();
            for (int i = 0; i < salasSteam.Length; i++)
                SalvarSnapshotDaLobby(salasSteam[i], false);
        }

        string meuSteamId = ObterMeuSteamId();
        SalaLocalSnapshot[] snapshots = mostrarSomenteSalasDoUsuario
            ? SalaLocalStorage.LoadForUser(meuSteamId)
            : SalaLocalStorage.LoadAll();

        OrdenarPorRecencia(snapshots);
        DefinirSalas(snapshots);

        if (snapshots.Length == 0)
        {
            SetFeedback("Nenhuma sala salva encontrada.");
            return;
        }

        SetFeedback($"{snapshots.Length} sala(s) carregada(s).");
    }

    // Novo conjunto de dados (fetch inicial ou atualização) — reseta a paginação para a primeira página.
    private void DefinirSalas(SalaLocalSnapshot[] snapshots)
    {
        todasSalas = snapshots ?? new SalaLocalSnapshot[0];

        if (habilitarFiltroSistema)
            PopularChipsFiltroSistema();

        AplicarFiltroEIrParaPagina(0);
    }

    // ── Filtro por sistema ───────────────────────────────────────────────────
    //
    // Mesmo modelo de FichaLoaderUI: nenhuma tag ativa = mostra tudo (sem filtro), qualquer tag
    // ativa restringe à lista. Aqui é mais simples que em FichaLoaderUI — SalaLocalSnapshot já
    // carrega sistemaSala direto no objeto, sem precisar resolver via outro storage.

    private void PopularChipsFiltroSistema()
    {
        foreach (GameObject go in chipsFiltroSistemaInstanciados) if (go != null) Destroy(go);
        chipsFiltroSistemaInstanciados.Clear();
        sistemasAtivos.Clear();

        if (conteudoFiltroSistema == null || filtroChipPrefab == null) return;

        var sistemasPresentes = new HashSet<string>();
        foreach (SalaLocalSnapshot s in todasSalas)
            if (s != null) sistemasPresentes.Add(string.IsNullOrWhiteSpace(s.sistemaSala) ? "Desconhecido" : s.sistemaSala);

        var sistemasDisponiveis = new List<string>(sistemasPresentes);
        sistemasDisponiveis.Sort(StringComparer.OrdinalIgnoreCase);

        foreach (string sistema in sistemasDisponiveis)
        {
            GameObject go = Instantiate(filtroChipPrefab, conteudoFiltroSistema, false);
            go.SetActive(true);
            chipsFiltroSistemaInstanciados.Add(go);

            FiltroChipToggleUI chip = go.GetComponent<FiltroChipToggleUI>();
            if (chip == null) continue;

            chip.Configurar(sistema, (tagAlterada, isOn) =>
            {
                if (isOn) sistemasAtivos.Add(tagAlterada);
                else sistemasAtivos.Remove(tagAlterada);
                AplicarFiltroEIrParaPagina(0);
            });
        }

        if (conteudoFiltroSistema is RectTransform rt)
        {
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    private void ExpandirFiltroSistema()
    {
        if (conteudoFiltroSistema != null) conteudoFiltroSistema.gameObject.SetActive(true);
        if (btnExpandirFiltroSistema != null) btnExpandirFiltroSistema.gameObject.SetActive(false);
        if (btnColapsarFiltroSistema != null) btnColapsarFiltroSistema.gameObject.SetActive(true);
    }

    private void ColapsarFiltroSistema()
    {
        if (conteudoFiltroSistema != null) conteudoFiltroSistema.gameObject.SetActive(false);
        if (btnColapsarFiltroSistema != null) btnColapsarFiltroSistema.gameObject.SetActive(false);
        if (btnExpandirFiltroSistema != null) btnExpandirFiltroSistema.gameObject.SetActive(true);
    }

    private void AplicarFiltroEIrParaPagina(int pagina)
    {
        salasFiltradas = FiltrarPorBusca(FiltrarPorSistema(todasSalas));
        indicadorPaginas?.ConfigurarPaginas(salasFiltradas.Length, itensPorPagina);
        IrParaPagina(pagina);
    }

    private SalaLocalSnapshot[] FiltrarPorSistema(SalaLocalSnapshot[] origem)
    {
        if (!habilitarFiltroSistema || sistemasAtivos.Count == 0) return origem;

        var resultado = new List<SalaLocalSnapshot>();
        foreach (SalaLocalSnapshot s in origem)
        {
            if (s == null) continue;
            string sistema = string.IsNullOrWhiteSpace(s.sistemaSala) ? "Desconhecido" : s.sistemaSala;
            if (sistemasAtivos.Contains(sistema)) resultado.Add(s);
        }
        return resultado.ToArray();
    }

    // ── Busca (nome da sala / nome do mestre / sistema) ─────────────────────

    private void OnBuscaAlterada(string texto)
    {
        textoBuscaAtual = texto?.Trim() ?? "";
        AplicarFiltroEIrParaPagina(0);
    }

    private SalaLocalSnapshot[] FiltrarPorBusca(SalaLocalSnapshot[] origem)
    {
        if (string.IsNullOrWhiteSpace(textoBuscaAtual)) return origem;

        string termo = textoBuscaAtual.ToLowerInvariant();
        var resultado = new List<SalaLocalSnapshot>();
        foreach (SalaLocalSnapshot s in origem)
        {
            if (s == null) continue;
            if (ContemTermo(s.nomeSala, termo) || ContemTermo(s.mestreNome, termo) || ContemTermo(s.sistemaSala, termo))
                resultado.Add(s);
        }
        return resultado.ToArray();
    }

    private static bool ContemTermo(string valor, string termoEmMinusculas)
        => !string.IsNullOrEmpty(valor) && valor.ToLowerInvariant().Contains(termoEmMinusculas);

    // Ver mesmo raciocínio em FichaLoaderUI.cs: só a navegação explícita por botão anima com
    // slide — trocas de página vindas de filtro/busca ou da carga inicial continuam instantâneas.
    private bool proximaNavegacaoDeveAnimar;
    private int  direcaoProximaNavegacao = 1;
    private Coroutine transicaoPaginaCoroutine;

    public void IrParaPagina(int pagina)
    {
        int totalPaginas = Mathf.Max(1, Mathf.CeilToInt((float)salasFiltradas.Length / itensPorPagina));
        int novaPagina = Mathf.Clamp(pagina, 0, totalPaginas - 1);
        bool animar = proximaNavegacaoDeveAnimar && novaPagina != paginaAtual;
        int  direcao = direcaoProximaNavegacao;
        proximaNavegacaoDeveAnimar = false;

        paginaAtual = novaPagina;

        int inicio = paginaAtual * itensPorPagina;
        int fim = Mathf.Min(inicio + itensPorPagina, salasFiltradas.Length);
        int quantidade = Mathf.Max(0, fim - inicio);

        SalaLocalSnapshot[] salasDaPagina = new SalaLocalSnapshot[quantidade];
        Array.Copy(salasFiltradas, inicio, salasDaPagina, 0, quantidade);

        indicadorPaginas?.AtualizarPaginaAtiva(paginaAtual);

        if (transicaoPaginaCoroutine != null) StopCoroutine(transicaoPaginaCoroutine);

        if (animar && listaSalasContent is RectTransform rt)
            transicaoPaginaCoroutine = StartCoroutine(TransicaoDeslizantePagina.Executar(rt, direcao, () => RenderizarLista(salasDaPagina)));
        else
            RenderizarLista(salasDaPagina);
    }

    public void ProximaPagina()
    {
        proximaNavegacaoDeveAnimar = true;
        direcaoProximaNavegacao = 1;
        IrParaPagina(paginaAtual + 1);
    }

    public void PaginaAnterior()
    {
        proximaNavegacaoDeveAnimar = true;
        direcaoProximaNavegacao = -1;
        IrParaPagina(paginaAtual - 1);
    }

    private void RenderizarLista(SalaLocalSnapshot[] snapshots)
    {
        LimparListaVisual();

        if (listaSalasContent == null || salaItemPrefab == null)
        {
            SetFeedback("Configure listaSalasContent e salaItemPrefab no Inspector.");
            return;
        }

        for (int i = 0; i < snapshots.Length; i++)
        {
            SalaLocalSnapshot snapshot = snapshots[i];
            GameObject go = Instantiate(salaItemPrefab, listaSalasContent);
            itensInstanciados.Add(go);

            var item = go.GetComponent<SalaListaItemUI>();
            if (item != null)
            {
                item.Configurar(snapshot, () => EntrarNaSalaSnapshot(snapshot));
            }
        }
    }

    private void EntrarNaSalaSnapshot(SalaLocalSnapshot snapshot)
    {
        if (snapshot == null)
        {
            SetFeedback("Sala inválida.", true);
            return;
        }

        bool ehMestre = string.Equals(snapshot.meuPapel, "Mestre", StringComparison.OrdinalIgnoreCase);
        if (!ehMestre)
        {
            SetFeedback("Apenas o mestre pode reabrir esta sala.", true);
            return;
        }

        if (!SteamClient.IsValid)
        {
            SetFeedback("Steam não disponível. Conecte-se à Steam para abrir a sala.", true);
            return;
        }

        if (steamLobbyManager == null)
        {
            SetFeedback("SteamLobbyManager não encontrado.", true);
            return;
        }

        Debug.Log($"[SalasSalvasUI] Mestre reabrindo sala='{snapshot.nomeSala}' localId='{snapshot.salaLocalId}'");
        SetFeedback($"Reabrindo sala '{snapshot.nomeSala}' na Steam...");
        steamLobbyManager.ReabrirSalaSalva(snapshot);
    }

    private void SalvarSnapshotDaLobby(Lobby lobby, bool usuarioParticipaForcado, string salaLocalIdLegado = null)
    {
        string meuSteamId = ObterMeuSteamId();

        string nomeSala = SafeLobbyData(lobby, SteamLobbyManager.CHAVE_NOME_SALA, "Sala");
        string nomeMestre = SafeLobbyData(lobby, SteamLobbyManager.CHAVE_NOME_MESTRE, "Mestre");
        string sistemaSala = SafeLobbyData(lobby, SteamLobbyManager.CHAVE_SISTEMA_SALA, "Sistema não definido");
        string descricaoSala = SafeLobbyData(lobby, SteamLobbyManager.CHAVE_DESCRICAO_SALA, "Sem descrição.");
        string imagemSalaPath = SafeLobbyData(lobby, SteamLobbyManager.CHAVE_IMAGEM_SALA_PATH, string.Empty);
        string mestreSteamId = SafeLobbyData(lobby, "HostSteamId", lobby.Owner.Id.ToString());

        Debug.Log($"[SalasSalvasUI] Snapshot lobbyId='{lobby.Id}' nome='{nomeSala}' localIdLegado='{salaLocalIdLegado}'");

        List<string> membrosNomes = new List<string>();
        List<string> membrosIds = new List<string>();

        foreach (Friend membro in lobby.Members)
        {
            membrosNomes.Add(membro.Name);
            membrosIds.Add(membro.Id.ToString());
        }

        string nomesCsv = string.Join(", ", membrosNomes.ToArray());
        string idsCsv = string.Join(",", membrosIds.ToArray());

        bool usuarioParticipa = usuarioParticipaForcado || CsvContains(idsCsv, meuSteamId);
        bool souMestre = string.Equals(mestreSteamId, meuSteamId, StringComparison.Ordinal);

        // Lê o ID estável dos metadados da lobby; evita gerar um novo GUID a cada sessão Steam
        string localIdDaLobby = SafeLobbyData(lobby, SteamLobbyManager.CHAVE_SALA_LOCAL_ID, string.Empty);
        var snapshot = new SalaLocalSnapshot
        {
            salaLocalId = !string.IsNullOrWhiteSpace(salaLocalIdLegado) ? salaLocalIdLegado
                        : !string.IsNullOrWhiteSpace(localIdDaLobby)    ? localIdDaLobby
                        : null,
            salaSteamId = lobby.Id.ToString(),
            nomeSala = nomeSala,
            sistemaSala = sistemaSala,
            descricaoSala = descricaoSala,
            imagemSalaPath = imagemSalaPath,
            mestreNome = nomeMestre,
            mestreSteamId = mestreSteamId,
            participantesNomesCsv = nomesCsv,
            participantesSteamIdsCsv = idsCsv,
            totalMembros = Mathf.Max(0, lobby.MemberCount),
            maxMembros = Mathf.Max(0, lobby.MaxMembers),
            meuSteamId = meuSteamId,
            meuPapel = souMestre ? "Mestre" : "Jogador",
            usuarioParticipa = usuarioParticipa
        };

        SalaLocalStorage.SaveOrUpdate(snapshot);
        Debug.Log($"[SalasSalvasUI] Snapshot salvo lobbyId='{lobby.Id}' salaLocalId='{snapshot.salaLocalId}'");
    }

    private void LimparListaVisual()
    {
        for (int i = 0; i < itensInstanciados.Count; i++)
        {
            if (itensInstanciados[i] != null)
                Destroy(itensInstanciados[i]);
        }

        itensInstanciados.Clear();
    }

    private void SetFeedback(string mensagem, bool erro = false)
    {
        if (feedbackText == null)
            return;

        feedbackText.text = mensagem;
        feedbackText.color = erro ? UnityEngine.Color.red : UnityEngine.Color.white;
    }

    private static void OrdenarPorRecencia(SalaLocalSnapshot[] snapshots)
    {
        Array.Sort(snapshots, (a, b) =>
        {
            DateTime aTime = ParseUtc(a != null ? a.ultimaAtualizacaoUtc : null);
            DateTime bTime = ParseUtc(b != null ? b.ultimaAtualizacaoUtc : null);
            return bTime.CompareTo(aTime);
        });
    }

    private static DateTime ParseUtc(string value)
    {
        if (DateTime.TryParse(value, out DateTime parsed))
            return parsed;

        return DateTime.MinValue;
    }

    private static string SafeLobbyData(Lobby lobby, string key, string fallback)
    {
        string value = lobby.GetData(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static bool CsvContains(string csv, string value)
    {
        if (string.IsNullOrWhiteSpace(csv) || string.IsNullOrWhiteSpace(value))
            return false;

        string[] parts = csv.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            if (string.Equals(parts[i].Trim(), value, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static string ObterMeuSteamId()
    {
        return SteamClient.IsValid ? SteamClient.SteamId.ToString() : "editor";
    }
}
