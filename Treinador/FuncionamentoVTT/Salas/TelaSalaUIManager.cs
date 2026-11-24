using System;
using System.Collections.Generic;
using System.IO;
using SimpleFileBrowser;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TelaSalaUIManager : MonoBehaviour
{
    [Header("Dependências")]
    [SerializeField] private SteamLobbyManager steamLobbyManager;
    [SerializeField] private BarraFerramentasManager barraFerramentasManager;
    [SerializeField] private ToggleObjects toggleObjects;
    [SerializeField] private EfeitoClimaManager efeitoClimaManager;
    [SerializeField] private AudioTabletopManager audioTabletopManager;
    [Tooltip("GameObject de nível superior alternado pelo ToggleObjects que representa esta tela — " +
        "usado pra detectar navegação pra fora da Sala sem clicar em \"Sair\" (ex: ir pra Campanha), " +
        "pausando grid/clima/áudio sem desconectar de verdade. Se deixado vazio, assume que este " +
        "próprio GameObject é a tela alternada.")]
    [SerializeField] private GameObject telaSalaReferencia;

    [Header("Barra Superior")]
    [SerializeField] private GameObject barraSuperior;
    [SerializeField] private Button botaoMostrarBarra;
    [Tooltip("Objeto extra que aparece/some junto com a barra superior ao clicar no botão. Desativado ao trocar de tela.")]
    [SerializeField] private GameObject objetoExtraAoMostrarBarra;

    [Header("Criar Sala — Painel")]
    [SerializeField] private Button botaoAbrirCriarSala;
    [SerializeField] private GameObject painelCriarSala;

    [Header("Criar Sala — Campos do Formulário")]
    [SerializeField] private TMP_InputField nomeSalaInput;
    [SerializeField] private TMP_Dropdown sistemaSalaDropdown;
    [SerializeField] private TMP_InputField descricaoSalaInput;
    [SerializeField] private TMP_InputField imagemSalaPathInput;
    [SerializeField] private bool ocultarInputCaminhoImagem = true;

    [Header("Criar Sala — Imagem")]
    [SerializeField] private Button botaoSelecionarImagemSala;
    [SerializeField] private GameObject previewImagemSalaGO;
    [SerializeField] private Image previewImagemSala;

    [Header("Criar Sala — Ação")]
    [SerializeField] private Button botaoConfirmarCriacao;

    [Header("Sala Aberta")]
    [SerializeField] private GameObject painelSalaAberta;
    [SerializeField] private TMP_Text tituloSalaText;
    [SerializeField] private Button botaoSairSala;
    [SerializeField] private TMP_Text feedbackText;

    [Header("Config")]
    [SerializeField, Min(2)] private int maxJogadores = 6;

    [Header("Modo de Exibição")]
    [Tooltip("Restringe esta tela a mostrar SÓ a sala atualmente aberta (se houver), escondendo o " +
        "formulário de criação inteiro — útil em contextos onde criar sala nova não faz sentido " +
        "(ex: uma tela que só deveria refletir o estado atual, sem oferecer criar outra sala por " +
        "cima). Arraste aqui o Toggle (componente padrão da Unity) do botão/checkbox correspondente " +
        "na cena — os eventos onValueChanged são ligados automaticamente em código (OnEnable), não " +
        "precisa configurar nada nele além de existir e ter um label/visual próprios.")]
    [SerializeField] private Toggle toggleSomenteSalaAtual;

    [Header("Background Padrão")]
    [SerializeField] private GameObject backgroundPadrao;

    private Texture2D texturaPreviewRuntime;
    private Sprite spritePreviewRuntime;
    private bool modoSomenteSalaAtual;
    private string imagemSalaSelecionadaPath;
    private MapManager mapManager;
    private readonly List<string> _sistemaFolderNames = new List<string>();

    private bool _deveMudarParaSala = false;
    private Steamworks.Data.Lobby _salaPendente;

    private void Update()
    {
        if (!_deveMudarParaSala) return;
        _deveMudarParaSala = false;
        ProcessarEntradaNaSala(_salaPendente);
        Canvas.ForceUpdateCanvases();
    }

    private void Awake()
    {
        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();

        if (mapManager == null)
            mapManager = FindFirstObjectByType<MapManager>();

        if (toggleObjects == null)
            toggleObjects = FindFirstObjectByType<ToggleObjects>();

        if (efeitoClimaManager == null)
            efeitoClimaManager = FindFirstObjectByType<EfeitoClimaManager>();

        if (audioTabletopManager == null)
            audioTabletopManager = FindFirstObjectByType<AudioTabletopManager>();

        // Assinado em Awake (não OnEnable/OnDisable) pra não depender de premissas sobre se este
        // GameObject fica sempre ativo ou é ele mesmo a tela alternada pelo ToggleObjects.
        if (toggleObjects != null)
            toggleObjects.OnTelaAlterada += OnTelaAlterada;
    }

    private void OnDestroy()
    {
        if (toggleObjects != null)
            toggleObjects.OnTelaAlterada -= OnTelaAlterada;

        LimparPreviewRuntime();
    }

    /// <summary>Pausa (não fecha) grid/clima/áudio ao navegar pra fora da tela da Sala sem clicar em
    /// "Sair" (ex: ir pra Campanha) — a sessão da sala continua carregada em memória, então voltar
    /// pra esta tela retoma tudo automaticamente. Ver plano "Grid/Clima/Áudio ao sair da Sala".</summary>
    private void OnTelaAlterada(GameObject novaTela)
    {
        GameObject minhaTela = telaSalaReferencia != null ? telaSalaReferencia : gameObject;
        bool salaEmSessao = mapManager != null && !string.IsNullOrWhiteSpace(mapManager.SalaIdAtual);
        if (!salaEmSessao)
            return;

        if (novaTela == minhaTela)
        {
            mapManager.DefinirSalaAtiva(true);
            efeitoClimaManager?.RetomarEfeitosLocal();
            audioTabletopManager?.RetomarAudioLocal();
        }
        else
        {
            mapManager.SalvarEstadoVTT();
            mapManager.DefinirSalaAtiva(false);
            efeitoClimaManager?.PausarEfeitosLocal();
            audioTabletopManager?.PausarAudioLocal();
        }
    }

    private void OnEnable()
    {
        // Fluxo simplificado: manter apenas um botão de criar sala.
        if (botaoAbrirCriarSala != null)
            botaoAbrirCriarSala.gameObject.SetActive(false);

        if (imagemSalaPathInput != null)
        {
            if (ocultarInputCaminhoImagem)
                imagemSalaPathInput.gameObject.SetActive(false);

            if (!string.IsNullOrWhiteSpace(imagemSalaPathInput.text) && string.IsNullOrWhiteSpace(imagemSalaSelecionadaPath))
                imagemSalaSelecionadaPath = imagemSalaPathInput.text.Trim();
        }

        PopularDropdownSistemas();

        if (botaoConfirmarCriacao != null)
        {
            botaoConfirmarCriacao.onClick.RemoveListener(OnClickConfirmarCriacao);
            botaoConfirmarCriacao.onClick.AddListener(OnClickConfirmarCriacao);
        }

        if (botaoSelecionarImagemSala != null)
        {
            botaoSelecionarImagemSala.onClick.RemoveListener(OnClickSelecionarImagemSala);
            botaoSelecionarImagemSala.onClick.AddListener(OnClickSelecionarImagemSala);
        }

        if (botaoSairSala != null)
        {
            botaoSairSala.onClick.RemoveListener(OnClickSairSala);
            botaoSairSala.onClick.AddListener(OnClickSairSala);
        }

        if (toggleSomenteSalaAtual != null)
        {
            toggleSomenteSalaAtual.onValueChanged.RemoveListener(OnToggleSomenteSalaAtualAlterado);
            toggleSomenteSalaAtual.onValueChanged.AddListener(OnToggleSomenteSalaAtualAlterado);
            modoSomenteSalaAtual = toggleSomenteSalaAtual.isOn;
        }

        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso += OnSalaAberta;
            steamLobbyManager.OnEntrouNaSalaComSucesso += OnSalaAberta;
            steamLobbyManager.OnEntrouEmSalaLocalComSucesso += OnSalaAbertaLocal;
            steamLobbyManager.OnSaiuDaSalaComSucesso += OnSaiuDaSala;
            steamLobbyManager.OnErroDeSala += OnErroDeSala;
        }

        SincronizarUIComEstadoAtualDaSala();
    }

    public void ToggleBarraSuperior()
    {
        if (barraSuperior == null) return;

        bool novoEstado = !barraSuperior.activeSelf;
        barraSuperior.SetActive(novoEstado);

        if (objetoExtraAoMostrarBarra != null)
            objetoExtraAoMostrarBarra.SetActive(novoEstado);
    }

    private void AtualizarBarraSuperior(bool emSala)
    {
        if (barraSuperior != null) barraSuperior.SetActive(!emSala);

        // Sempre reseta o objeto extra (troca de tela, entrada/saída da sala, etc.).
        if (objetoExtraAoMostrarBarra != null)
            objetoExtraAoMostrarBarra.SetActive(false);

        if (botaoMostrarBarra != null)
        {
            botaoMostrarBarra.gameObject.SetActive(emSala);
            botaoMostrarBarra.onClick.RemoveListener(ToggleBarraSuperior);
            if (emSala) botaoMostrarBarra.onClick.AddListener(ToggleBarraSuperior);
        }
    }

    private void OnDisable()
    {
        AtualizarBarraSuperior(false);

        if (botaoConfirmarCriacao != null)
            botaoConfirmarCriacao.onClick.RemoveListener(OnClickConfirmarCriacao);

        if (botaoSelecionarImagemSala != null)
            botaoSelecionarImagemSala.onClick.RemoveListener(OnClickSelecionarImagemSala);

        if (botaoSairSala != null)
            botaoSairSala.onClick.RemoveListener(OnClickSairSala);

        if (toggleSomenteSalaAtual != null)
            toggleSomenteSalaAtual.onValueChanged.RemoveListener(OnToggleSomenteSalaAtualAlterado);

        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso -= OnSalaAberta;
            steamLobbyManager.OnEntrouNaSalaComSucesso -= OnSalaAberta;
            steamLobbyManager.OnEntrouEmSalaLocalComSucesso -= OnSalaAbertaLocal;
            steamLobbyManager.OnSaiuDaSalaComSucesso -= OnSaiuDaSala;
            steamLobbyManager.OnErroDeSala -= OnErroDeSala;
        }
    }

    private void OnToggleSomenteSalaAtualAlterado(bool ativo)
    {
        modoSomenteSalaAtual = ativo;
        SincronizarUIComEstadoAtualDaSala();

        if (ativo)
            SetFeedback("Modo: apenas sala atual.");
        else
            SetFeedback("Modo: criação e listagem habilitadas.");
    }

    private void OnClickAbrirCriarSala()
    {
        if (modoSomenteSalaAtual)
            return;

        DefinirPainelAtivo(painelCriarSala, true);
        DefinirPainelAtivo(painelSalaAberta, false);
        AtualizarPreviewImagemPorCaminho(ObterImagemSalaPathAtual());

        if (nomeSalaInput != null)
        {
            if (string.IsNullOrWhiteSpace(nomeSalaInput.text))
                nomeSalaInput.text = string.Empty;

            nomeSalaInput.ActivateInputField();
            nomeSalaInput.Select();
        }

        SetFeedback(string.Empty);
    }

    private void OnClickConfirmarCriacao()
    {
        if (steamLobbyManager == null)
        {
            SetFeedback("SteamLobbyManager não encontrado na cena.", true);
            return;
        }

        string nomeSala = nomeSalaInput != null ? nomeSalaInput.text.Trim() : string.Empty;
        string sistemaSala = ObterSistemaSelecionado();
        string descricaoSala = descricaoSalaInput != null ? descricaoSalaInput.text.Trim() : string.Empty;
        string imagemSalaPath = ObterImagemSalaPathAtual();

        Debug.Log($"[TelaSalaUI] Confirmar criacao nome='{nomeSala}' sistema='{sistemaSala}' imagemAntes='{imagemSalaPath}'");

        if (!string.IsNullOrWhiteSpace(imagemSalaPath))
        {
            string salaIdHint = !string.IsNullOrWhiteSpace(nomeSala)
                ? nomeSala
                : Path.GetFileNameWithoutExtension(imagemSalaPath);

            if (string.IsNullOrWhiteSpace(salaIdHint))
                salaIdHint = $"sala_{DateTime.UtcNow.Ticks}";

            imagemSalaPath = SalaLocalStorage.EnsureImagePersisted(imagemSalaPath, salaIdHint);
            imagemSalaSelecionadaPath = imagemSalaPath;

            if (imagemSalaPathInput != null)
                imagemSalaPathInput.text = imagemSalaPath;

            Debug.Log($"[TelaSalaUI] Imagem persistida na confirmacao hint='{salaIdHint}' imagemDepois='{imagemSalaPath}'");
        }

        AbrirAbaSalaDuranteCriacao(nomeSala);

        steamLobbyManager.CriarSalaSegura(maxJogadores, nomeSala, sistemaSala, descricaoSala, imagemSalaPath);
        SetFeedback("Criando sala...");

        PostHogTracker.TrackSalaCriada(sistemaSala, false);
    }

    private void OnClickSelecionarImagemSala()
    {
        FileBrowser.SetFilters(true, new FileBrowser.Filter("Imagens", ".jpg", ".png", ".jpeg"));
        FileBrowser.ShowLoadDialog(
            (paths) =>
            {
                if (paths == null || paths.Length == 0)
                    return;

                string caminho = paths[0];
                Debug.Log($"[TelaSalaUI] Imagem selecionada original='{caminho}'");

                string nomeSalaDigitado = nomeSalaInput != null ? nomeSalaInput.text.Trim() : string.Empty;
                string salaIdHint = !string.IsNullOrWhiteSpace(nomeSalaDigitado)
                    ? nomeSalaDigitado
                    : Path.GetFileNameWithoutExtension(caminho);

                if (string.IsNullOrWhiteSpace(salaIdHint))
                    salaIdHint = steamLobbyManager != null && steamLobbyManager.SalaAtual.HasValue
                        ? steamLobbyManager.SalaAtual.Value.Id.ToString()
                        : $"sala_{DateTime.UtcNow.Ticks}";

                string caminhoPersistido = SalaLocalStorage.EnsureImagePersisted(caminho, salaIdHint);
                Debug.Log($"[TelaSalaUI] Imagem persistida na selecao hint='{salaIdHint}' destino='{caminhoPersistido}'");
                imagemSalaSelecionadaPath = caminhoPersistido;

                if (imagemSalaPathInput != null)
                    imagemSalaPathInput.text = caminhoPersistido;

                AtualizarPreviewImagemPorCaminho(ObterImagemSalaPathAtual());
            },
            null,
            FileBrowser.PickMode.Files, false, null, null, "Selecione uma Imagem", "Selecionar");
    }

    private void OnSalaAberta(Steamworks.Data.Lobby lobby)
    {
        _salaPendente = lobby;
        _deveMudarParaSala = true;
    }

    private void ProcessarEntradaNaSala(Steamworks.Data.Lobby lobby)
    {
        DefinirPainelAtivo(painelCriarSala, false);
        DefinirPainelAtivo(painelSalaAberta, true);

        string nomeSala = lobby.GetData(SteamLobbyManager.CHAVE_NOME_SALA);
        string nomeMestre = lobby.GetData(SteamLobbyManager.CHAVE_NOME_MESTRE);

        if (string.IsNullOrWhiteSpace(nomeSala))
            nomeSala = string.IsNullOrWhiteSpace(nomeMestre) ? "Sala" : $"Mesa de {nomeMestre}";

        if (tituloSalaText != null)
            tituloSalaText.text = nomeSala;

        AplicarModoSomenteSalaAtual();
        SetFeedback($"Sala aberta: {nomeSala}");

        string novoSteamId = lobby.Id.ToString();

        // ID estável: lê do lobby Steam (foi gravado por CriarSalaSegura).
        // Em reabertura, o snapshot tem o mesmo ID que foi gravado na criação original.
        string localId = lobby.GetData(SteamLobbyManager.CHAVE_SALA_LOCAL_ID);
        SalaLocalSnapshot snapshotReabertura = steamLobbyManager?.SnapshotReabertura;

        if (snapshotReabertura != null && !string.IsNullOrWhiteSpace(snapshotReabertura.salaLocalId))
            localId = snapshotReabertura.salaLocalId;

        // Fallback: se por alguma razão o ID ainda estiver vazio, gera um GUID
        if (string.IsNullOrWhiteSpace(localId))
            localId = Guid.NewGuid().ToString("N");

        if (!SalaDataStorage.HasSalaData(localId))
        {
            var snapshot = new SalaLocalSnapshot
            {
                salaLocalId = localId,
                salaSteamId = novoSteamId,
                nomeSala    = nomeSala
            };
            SalaDataStorage.CreateDefaultSalaData(snapshot);
            Debug.Log($"[TelaSalaUI] Dados padrão criados para sala: {localId}");
        }
        else
        {
            // Atualiza o Steam lobby ID (muda a cada sessão, não é a chave primária)
            SalaData dados = SalaDataStorage.LoadSalaData(localId);
            if (dados != null)
            {
                dados.salaSteamId = novoSteamId;
                SalaDataStorage.SaveSalaData(dados);
                Debug.Log($"[TelaSalaUI] Sala existente localId={localId} atualizada com novo steamId={novoSteamId}");
            }
        }

        steamLobbyManager?.LimparSnapshotReabertura();

        // Notifica o MapManager — arquivo já existe neste ponto
        mapManager ??= FindFirstObjectByType<MapManager>();
        mapManager?.IniciarSessaoComId(localId);

        // Notifica a barra de ferramentas sobre o sistema da sala
        string sistemaDaSala = snapshotReabertura?.sistemaSala ?? string.Empty;
        if (string.IsNullOrEmpty(sistemaDaSala))
        {
            SalaLocalSnapshot[] snapshots = SalaLocalStorage.LoadAll();
            foreach (SalaLocalSnapshot s in snapshots)
                if (s.salaLocalId == localId) { sistemaDaSala = s.sistemaSala ?? string.Empty; break; }
        }
        barraFerramentasManager ??= FindFirstObjectByType<BarraFerramentasManager>();
        barraFerramentasManager?.DefinirSistemaSala(sistemaDaSala);

        AtualizarBarraSuperior(true);
        DesativarBackgroundPadrao();
    }

    private void OnSalaAbertaLocal(SalaLocalSnapshot salaLocal)
    {
        DefinirPainelAtivo(painelCriarSala, false);
        DefinirPainelAtivo(painelSalaAberta, true);

        string nomeSala = string.IsNullOrWhiteSpace(salaLocal?.nomeSala) ? "Sala" : salaLocal.nomeSala;
        string nomeMestre = string.IsNullOrWhiteSpace(salaLocal?.mestreNome) ? "Mestre" : salaLocal.mestreNome;

        if (string.IsNullOrWhiteSpace(nomeSala))
            nomeSala = string.IsNullOrWhiteSpace(nomeMestre) ? "Sala" : $"Mesa de {nomeMestre}";

        if (tituloSalaText != null)
            tituloSalaText.text = nomeSala;

        AplicarModoSomenteSalaAtual();
        SetFeedback($"Sala aberta (offline): {nomeSala}");
        Debug.Log($"[TelaSalaUI] Sala local aberta: {nomeSala} (modo offline)");

        // Garantir que o salaLocalId existe antes de qualquer operação
        if (string.IsNullOrWhiteSpace(salaLocal.salaLocalId))
            salaLocal.salaLocalId = Guid.NewGuid().ToString("N");

        string salaId = salaLocal.salaLocalId;

        if (!SalaDataStorage.HasSalaData(salaId))
        {
            SalaDataStorage.CreateDefaultSalaData(salaLocal);
            Debug.Log($"[TelaSalaUI] Dados padrão criados para sala local: {salaId}");
        }
        else
        {
            Debug.Log($"[TelaSalaUI] Dados carregados para sala local: {salaId}");
        }

        // Notifica o MapManager — arquivo já existe neste ponto
        mapManager ??= FindFirstObjectByType<MapManager>();
        mapManager?.IniciarSessaoComId(salaId);

        // Notifica a barra de ferramentas sobre o sistema da sala
        barraFerramentasManager ??= FindFirstObjectByType<BarraFerramentasManager>();
        barraFerramentasManager?.DefinirSistemaSala(salaLocal.sistemaSala ?? string.Empty);

        AtualizarBarraSuperior(true);
        DesativarBackgroundPadrao();
    }

    private void OnClickSairSala()
    {
        if (steamLobbyManager == null)
        {
            SetFeedback("SteamLobbyManager não encontrado na cena.", true);
            return;
        }

        steamLobbyManager.SairDaSala();
        LimparDadosTemporariosDaSala();

        if (toggleSomenteSalaAtual != null && toggleSomenteSalaAtual.isOn)
        {
            toggleSomenteSalaAtual.isOn = false;
            modoSomenteSalaAtual = false;
        }

        DefinirPainelAtivo(painelSalaAberta, false);
        DefinirPainelAtivo(painelCriarSala, true);
        SetFeedback("Você saiu da sala.");
        AtualizarBarraSuperior(false);
        ReativarBackgroundPadrao();
    }

    private void OnSaiuDaSala()
    {
        DefinirPainelAtivo(painelCriarSala, true);
        DefinirPainelAtivo(painelSalaAberta, false);
        LimparDadosTemporariosDaSala();
        AplicarModoSomenteSalaAtual();
        SetFeedback("Você saiu da sala.");

        barraFerramentasManager ??= FindFirstObjectByType<BarraFerramentasManager>();
        barraFerramentasManager?.DefinirSistemaSala(string.Empty);

        AtualizarBarraSuperior(false);
        ReativarBackgroundPadrao();

        mapManager ??= FindFirstObjectByType<MapManager>();
        mapManager?.LimparElementosDaSala();
    }

    private void DesativarBackgroundPadrao()
    {
        if (backgroundPadrao != null)
            backgroundPadrao.SetActive(false);
    }

    private void ReativarBackgroundPadrao()
    {
        if (backgroundPadrao != null)
            backgroundPadrao.SetActive(true);
    }

    private void OnErroDeSala(string mensagem)
    {
        if (steamLobbyManager == null || !steamLobbyManager.SalaAtual.HasValue)
        {
            DefinirPainelAtivo(painelSalaAberta, false);
            DefinirPainelAtivo(painelCriarSala, true);
        }

        SetFeedback(mensagem, true);
    }

    private void AbrirAbaSalaDuranteCriacao(string nomeSala)
    {
        DefinirPainelAtivo(painelCriarSala, false);
        DefinirPainelAtivo(painelSalaAberta, true);

        if (tituloSalaText != null)
        {
            string titulo = string.IsNullOrWhiteSpace(nomeSala) ? "Criando sala..." : nomeSala;
            tituloSalaText.text = titulo;
        }
    }

    private void SincronizarUIComEstadoAtualDaSala()
    {
        AplicarModoSomenteSalaAtual();

        if (steamLobbyManager != null && steamLobbyManager.SalaAtual.HasValue)
        {
            OnSalaAberta(steamLobbyManager.SalaAtual.Value);
            return;
        }

        DefinirPainelAtivo(painelCriarSala, !modoSomenteSalaAtual);
        DefinirPainelAtivo(painelSalaAberta, false);

        AtualizarPreviewImagemPorCaminho(ObterImagemSalaPathAtual());

        if (feedbackText != null)
            feedbackText.text = string.Empty;
    }

    private void AplicarModoSomenteSalaAtual()
    {
        if (botaoAbrirCriarSala != null)
            botaoAbrirCriarSala.gameObject.SetActive(false);

        if (botaoSelecionarImagemSala != null)
            botaoSelecionarImagemSala.gameObject.SetActive(!modoSomenteSalaAtual);

        if (botaoConfirmarCriacao != null)
            botaoConfirmarCriacao.gameObject.SetActive(!modoSomenteSalaAtual);

        if (!modoSomenteSalaAtual)
            return;

        DefinirPainelAtivo(painelCriarSala, false);

        bool temSalaAtual = steamLobbyManager != null && steamLobbyManager.SalaAtual.HasValue;
        DefinirPainelAtivo(painelSalaAberta, temSalaAtual);
    }

    private void SetFeedback(string mensagem, bool erro = false)
    {
        if (feedbackText == null)
            return;

        feedbackText.text = mensagem;
        feedbackText.color = erro ? UnityEngine.Color.red : UnityEngine.Color.white;
    }

    private void AtualizarPreviewImagemPorCaminho(string caminho)
    {
        if (previewImagemSalaGO != null)
            previewImagemSalaGO.SetActive(false);

        if (previewImagemSala == null || string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho))
            return;

        byte[] bytes = File.ReadAllBytes(caminho);
        var novaTextura = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!novaTextura.LoadImage(bytes))
        {
            Destroy(novaTextura);
            SetFeedback("Falha ao carregar a imagem selecionada.", true);
            return;
        }

        LimparPreviewRuntime();

        texturaPreviewRuntime = novaTextura;
        spritePreviewRuntime = Sprite.Create(
            texturaPreviewRuntime,
            new Rect(0f, 0f, texturaPreviewRuntime.width, texturaPreviewRuntime.height),
            new Vector2(0.5f, 0.5f),
            100f);

        previewImagemSala.sprite = spritePreviewRuntime;

        if (previewImagemSalaGO != null)
            previewImagemSalaGO.SetActive(true);
    }


    private void LimparPreviewRuntime()
    {
        if (previewImagemSala != null)
            previewImagemSala.sprite = null;

        if (spritePreviewRuntime != null)
            Destroy(spritePreviewRuntime);

        if (texturaPreviewRuntime != null)
            Destroy(texturaPreviewRuntime);

        spritePreviewRuntime = null;
        texturaPreviewRuntime = null;
    }

    private void LimparDadosTemporariosDaSala()
    {
        imagemSalaSelecionadaPath = string.Empty;

        if (nomeSalaInput != null)
            nomeSalaInput.text = string.Empty;

        if (sistemaSalaDropdown != null)
        {
            sistemaSalaDropdown.value = 0;
            sistemaSalaDropdown.RefreshShownValue();
        }

        if (descricaoSalaInput != null)
            descricaoSalaInput.text = string.Empty;

        if (imagemSalaPathInput != null)
            imagemSalaPathInput.text = string.Empty;

        if (tituloSalaText != null)
            tituloSalaText.text = string.Empty;

        LimparPreviewRuntime();

        if (previewImagemSalaGO != null)
            previewImagemSalaGO.SetActive(false);
    }

    private static void DefinirPainelAtivo(GameObject alvo, bool ativo)
    {
        if (alvo == null)
            return;

        PainelToggleAnimado animado = alvo.GetComponent<PainelToggleAnimado>();
        if (animado != null)
        {
            if (ativo)
                animado.Abrir();
            else
                animado.Fechar();

            return;
        }

        alvo.SetActive(ativo);
    }

    private void PopularDropdownSistemas()
    {
        if (sistemaSalaDropdown == null) return;

        _sistemaFolderNames.Clear();
        sistemaSalaDropdown.ClearOptions();

        string baseDir = Path.Combine(Application.persistentDataPath, "sistemas");
        var opcoes = new List<string>();

        if (Directory.Exists(baseDir))
        {
            foreach (string dir in Directory.GetDirectories(baseDir))
            {
                string folderName = Path.GetFileName(dir);
                _sistemaFolderNames.Add(folderName);
                opcoes.Add(PrettyPrintFolderName(folderName));
            }
        }

        if (opcoes.Count == 0)
        {
            _sistemaFolderNames.Add("quinta-edicao-srd");
            opcoes.Add("Quinta Edição (D&D SRD)");
        }

        sistemaSalaDropdown.AddOptions(opcoes);
        sistemaSalaDropdown.value = 0;
        sistemaSalaDropdown.RefreshShownValue();
    }

    private string ObterSistemaSelecionado()
    {
        if (sistemaSalaDropdown == null) return string.Empty;
        int idx = sistemaSalaDropdown.value;
        if (idx < 0 || idx >= _sistemaFolderNames.Count) return string.Empty;
        return _sistemaFolderNames[idx];
    }

    private static string PrettyPrintFolderName(string folderName)
    {
        string[] parts = folderName.Split('-');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            parts[i] = char.ToUpperInvariant(parts[i][0]) + parts[i].Substring(1);
        }
        return string.Join(" ", parts);
    }

    private string ObterImagemSalaPathAtual()
    {
        if (!string.IsNullOrWhiteSpace(imagemSalaSelecionadaPath))
            return imagemSalaSelecionadaPath.Trim();

        if (imagemSalaPathInput != null && !string.IsNullOrWhiteSpace(imagemSalaPathInput.text))
            return imagemSalaPathInput.text.Trim();

        return string.Empty;
    }
}
