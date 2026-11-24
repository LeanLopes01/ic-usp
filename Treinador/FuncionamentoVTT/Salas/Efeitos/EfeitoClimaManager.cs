using System;
using UnityEngine;

public enum TipoEfeitoClima { Nenhum, Chuva, Neve }

[Serializable]
public class ClimaConfig
{
    public float tamanho = 1f;            // -> main.startSize
    public float angulo = 25f;            // -> shape.angle
    public float quantidade = 220f;       // -> emission.rateOverTime
    public float velocidadeQueda = 17.5f; // magnitude positiva; aplicado como -Y em velocityOverLifetime
    public float vento = -7.5f;           // -> velocityOverLifetime.x
    public float duracaoVida = 1f;        // -> main.startLifetime (segundos até a partícula sumir)
    public float ruidoX = 0f;             // -> noise.strengthX (turbulência no eixo X)
    public float ruidoY = 0f;             // -> noise.strengthY (turbulência no eixo Y)
    // Z do ruído NÃO é configurável — fica sempre travado em 0 (ver AplicarConfig/noise.strengthZ).
}

/// <summary>
/// Controla os efeitos climáticos (Chuva/Neve) da Sala — Mestre-only, sincronizado por rede e
/// persistido em lobby-data Steam (mesmo padrão de SalaAbasFichasManager.AtribuirDono: aplica
/// localmente -> transmite -> persiste). Cada efeito é um ParticleSystem próprio, com sua config
/// (tamanho/ângulo/quantidade/velocidade de queda/vento) lembrada separadamente.
/// </summary>
public class EfeitoClimaManager : MonoBehaviour
{
    [Header("Dependências")]
    [SerializeField] private SteamLobbyManager steamLobbyManager;

    [Header("Particle Systems (um GameObject dedicado por efeito)")]
    [SerializeField] private ParticleSystem chuvaParticleSystem;
    [SerializeField] private ParticleSystem neveParticleSystem;

    [Header("Limites dos controles (compartilhados entre Chuva e Neve)")]
    [SerializeField] private float tamanhoMin = 0.1f, tamanhoMax = 3f;
    [SerializeField] private float anguloMin = 0f, anguloMax = 89f;
    [SerializeField] private float quantidadeMin = 0f, quantidadeMax = 500f;
    [SerializeField] private float velocidadeQuedaMin = 0f, velocidadeQuedaMax = 40f;
    [SerializeField] private float ventoMin = -20f, ventoMax = 20f;
    [SerializeField] private float duracaoVidaMin = 0.5f, duracaoVidaMax = 15f;
    [SerializeField] private float ruidoMin = 0f, ruidoMax = 5f;

    [Header("Persistência Steam Lobby")]
    [SerializeField] private string chaveLobbyClima = "SalaClimaAtual"; // só o Mestre escreve

    private TipoEfeitoClima tipoAtual = TipoEfeitoClima.Nenhum;
    private ClimaConfig configChuva = new ClimaConfig();
    // Usado só se neveParticleSystem não estiver atribuído no Inspector — com a referência presente,
    // Awake() sobrescreve isto lendo os valores reais já autorados no asset (mesmo padrão da Chuva).
    private ClimaConfig configNeve = new ClimaConfig
        { tamanho = 0.5f, angulo = 10f, quantidade = 150f, velocidadeQueda = 5f, vento = 1f };

    /// <summary>Disparado sempre que o tipo ou qualquer valor muda (local ou recebido da rede) —
    /// ClimaPainelUI assina isso pra repintar, mesmo padrão de
    /// SalaAbasFichasManager.OnFichasImportadasAlterado.</summary>
    public event Action OnClimaAlterado;

    private bool SouMestre() =>
        Steamworks.SteamClient.IsValid && steamLobbyManager?.SalaAtual?.IsOwnedBy(Steamworks.SteamClient.SteamId) == true;

    private void Awake()
    {
        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();

        // Semeia os presets a partir dos valores JÁ AUTORADOS em cada ParticleSystem — evita
        // hardcodar números mágicos em C# que podem divergir do asset se reajustado no Editor.
        if (chuvaParticleSystem != null)
            configChuva = LerConfigDoParticleSystem(chuvaParticleSystem);
        if (neveParticleSystem != null)
            configNeve = LerConfigDoParticleSystem(neveParticleSystem);

        DesativarAmbos();
    }

    private void OnEnable()
    {
        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso  += OnSalaDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso += OnSalaDisponivel;
            steamLobbyManager.OnSaiuDaSalaComSucesso   += OnSaiuDaSala;
        }
        RestaurarClimaDoLobby();
    }

    private void OnDisable()
    {
        if (steamLobbyManager != null)
        {
            steamLobbyManager.OnSalaCriadaComSucesso  -= OnSalaDisponivel;
            steamLobbyManager.OnEntrouNaSalaComSucesso -= OnSalaDisponivel;
            steamLobbyManager.OnSaiuDaSalaComSucesso   -= OnSaiuDaSala;
        }
    }

    private void OnSalaDisponivel(Steamworks.Data.Lobby lobby) => RestaurarClimaDoLobby();

    // Só local — os demais peers continuam na sala com o próprio estado, sem broadcast. Evita o
    // clima "vazar" pra fora da Sala, já que o ParticleSystem fica parentado na Main Camera (raiz de
    // cena) e não é escopado por tela — ver AjusteClimaCamera.
    private void OnSaiuDaSala()
    {
        DesativarAmbos();
        tipoAtual = TipoEfeitoClima.Nenhum;
        OnClimaAlterado?.Invoke();
    }

    /// <summary>Pausa localmente os efeitos SEM alterar tipoAtual/configs (retomável via
    /// RetomarEfeitosLocal) — usado quando o usuário sai da tela da Sala sem clicar em "Sair" (ex:
    /// navega pra Campanha), mantendo a sala tecnicamente aberta em segundo plano. Diferente de
    /// OnSaiuDaSala, que é um fechamento de verdade e zera tipoAtual.</summary>
    public void PausarEfeitosLocal() => DesativarAmbos();

    /// <summary>Reativa o efeito que já estava tocando (tipoAtual/configs preservados por
    /// PausarEfeitosLocal) — usado ao voltar pra tela da Sala com a sessão ainda ativa.</summary>
    public void RetomarEfeitosLocal() => AplicarTipoRecebido(tipoAtual);

    // ── Leitura (UI) ─────────────────────────────────────────────────────────

    public TipoEfeitoClima ObterTipoAtual() => tipoAtual;

    /// <summary>Config do efeito ATUALMENTE ativo, ou null se Nenhum. Nunca mutar diretamente —
    /// sempre passar pelos Definir*.</summary>
    public ClimaConfig ObterConfigAtual() => tipoAtual switch
    {
        TipoEfeitoClima.Chuva => configChuva,
        TipoEfeitoClima.Neve  => configNeve,
        _ => null
    };

    public float ObterTamanhoMin() => tamanhoMin;
    public float ObterTamanhoMax() => tamanhoMax;
    public float ObterAnguloMin() => anguloMin;
    public float ObterAnguloMax() => anguloMax;
    public float ObterQuantidadeMin() => quantidadeMin;
    public float ObterQuantidadeMax() => quantidadeMax;
    public float ObterVelocidadeQuedaMin() => velocidadeQuedaMin;
    public float ObterVelocidadeQuedaMax() => velocidadeQuedaMax;
    public float ObterVentoMin() => ventoMin;
    public float ObterVentoMax() => ventoMax;
    public float ObterDuracaoVidaMin() => duracaoVidaMin;
    public float ObterDuracaoVidaMax() => duracaoVidaMax;
    // Mesma faixa min/max compartilhada pelos sliders de ruído X e Y.
    public float ObterRuidoMin() => ruidoMin;
    public float ObterRuidoMax() => ruidoMax;

    // ── Escrita (Mestre-only) ────────────────────────────────────────────────

    public void DefinirTipoClima(TipoEfeitoClima tipo)
    {
        if (!SouMestre()) return;
        AplicarTipoRecebido(tipo);
        NetworkSyncManager.Instancia?.EnviarClimaParaTodos(tipoAtual, configChuva, configNeve);
        SalvarClimaNoLobby();
    }

    public void DefinirTamanho(float v) => AtualizarCampo(cfg => cfg.tamanho = Mathf.Clamp(v, tamanhoMin, tamanhoMax));
    public void DefinirAngulo(float v) => AtualizarCampo(cfg => cfg.angulo = Mathf.Clamp(v, anguloMin, anguloMax));
    public void DefinirQuantidade(float v) => AtualizarCampo(cfg => cfg.quantidade = Mathf.Clamp(v, quantidadeMin, quantidadeMax));
    public void DefinirVelocidadeQueda(float v) => AtualizarCampo(cfg => cfg.velocidadeQueda = Mathf.Clamp(v, velocidadeQuedaMin, velocidadeQuedaMax));
    public void DefinirVento(float v) => AtualizarCampo(cfg => cfg.vento = Mathf.Clamp(v, ventoMin, ventoMax));
    public void DefinirDuracaoVida(float v) => AtualizarCampo(cfg => cfg.duracaoVida = Mathf.Clamp(v, duracaoVidaMin, duracaoVidaMax));
    public void DefinirRuidoX(float v) => AtualizarCampo(cfg => cfg.ruidoX = Mathf.Clamp(v, ruidoMin, ruidoMax));
    public void DefinirRuidoY(float v) => AtualizarCampo(cfg => cfg.ruidoY = Mathf.Clamp(v, ruidoMin, ruidoMax));

    private void AtualizarCampo(Action<ClimaConfig> mutador)
    {
        if (!SouMestre() || tipoAtual == TipoEfeitoClima.Nenhum) return;

        ClimaConfig cfg = tipoAtual == TipoEfeitoClima.Chuva ? configChuva : configNeve;
        mutador(cfg);
        AplicarConfig(ParticleSystemAtivo(), cfg);
        NetworkSyncManager.Instancia?.EnviarClimaParaTodos(tipoAtual, configChuva, configNeve);
        SalvarClimaNoLobby();
        OnClimaAlterado?.Invoke();
    }

    private ParticleSystem ParticleSystemAtivo() => tipoAtual switch
    {
        TipoEfeitoClima.Chuva => chuvaParticleSystem,
        TipoEfeitoClima.Neve  => neveParticleSystem,
        _ => null
    };

    /// <summary>Chamado pelo NetworkSyncManager quando o clima chega de outro peer, e também na
    /// restauração ao entrar/reconectar (RestaurarClimaDoLobby) — SEM rebroadcast. Mesmo padrão de
    /// SalaAbasFichasManager.AplicarDonoRecebido.</summary>
    public void AplicarClimaRecebido(TipoEfeitoClima tipo, ClimaConfig chuva, ClimaConfig neve)
    {
        if (chuva != null) configChuva = chuva;
        if (neve != null) configNeve = neve;
        AplicarTipoRecebido(tipo);
    }

    private void AplicarTipoRecebido(TipoEfeitoClima tipo)
    {
        tipoAtual = tipo;
        DesativarAmbos();
        switch (tipo)
        {
            case TipoEfeitoClima.Chuva: AtivarEAplicar(chuvaParticleSystem, configChuva); break;
            case TipoEfeitoClima.Neve:  AtivarEAplicar(neveParticleSystem, configNeve); break;
        }
        OnClimaAlterado?.Invoke();
    }

    // ── Aplicação nos módulos do ParticleSystem ─────────────────────────────

    private static void AplicarConfig(ParticleSystem ps, ClimaConfig cfg)
    {
        if (ps == null || cfg == null) return;

        var main = ps.main;
        main.startSize = cfg.tamanho;
        main.startLifetime = cfg.duracaoVida;

        var shape = ps.shape;
        shape.angle = cfg.angulo;

        var emissao = ps.emission;
        emissao.rateOverTime = cfg.quantidade;

        var velocidade = ps.velocityOverLifetime;
        velocidade.enabled = true;
        // x/y/z precisam estar no MESMO ParticleSystemCurveMode (Unity valida isso em conjunto) —
        // atribuir um float vira Constant implicitamente; setar os 3 aqui evita ficar um eixo em
        // Constant (os que a gente escreve) e outro no modo antigo (z, nunca tocado antes disso).
        velocidade.x = cfg.vento;
        velocidade.y = -cfg.velocidadeQueda; // queda é sempre pra baixo; UI só mostra magnitude positiva
        velocidade.z = 0f;

        var ruido = ps.noise;
        ruido.enabled = true;
        ruido.separateAxes = true; // necessário pra strengthX/Y/Z valerem — sem isso só "strength" conta
        ruido.strengthX = cfg.ruidoX;
        ruido.strengthY = cfg.ruidoY;
        ruido.strengthZ = 0f; // fixo sempre — não é configurável pelo slider
    }

    private static ClimaConfig LerConfigDoParticleSystem(ParticleSystem ps)
    {
        var main = ps.main;
        var shape = ps.shape;
        var emissao = ps.emission;
        var velocidade = ps.velocityOverLifetime;
        var ruido = ps.noise;
        return new ClimaConfig
        {
            tamanho = main.startSize.constant,
            angulo = shape.angle,
            quantidade = emissao.rateOverTime.constant,
            velocidadeQueda = -PontoMedio(velocidade.y),
            vento = PontoMedio(velocidade.x),
            duracaoVida = main.startLifetime.constant,
            ruidoX = PontoMedio(ruido.strengthX),
            ruidoY = PontoMedio(ruido.strengthY)
        };
    }

    private static float PontoMedio(ParticleSystem.MinMaxCurve curva) =>
        curva.mode == ParticleSystemCurveMode.TwoConstants ? (curva.constantMin + curva.constantMax) / 2f : curva.constant;

    private void DesativarAmbos()
    {
        DesativarParticleSystem(chuvaParticleSystem);
        DesativarParticleSystem(neveParticleSystem);
    }

    private static void DesativarParticleSystem(ParticleSystem ps)
    {
        if (ps == null || !ps.gameObject.activeSelf) return;
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.gameObject.SetActive(false);
    }

    private static void AtivarEAplicar(ParticleSystem ps, ClimaConfig cfg)
    {
        if (ps == null || cfg == null) return;
        AplicarConfig(ps, cfg);
        ps.gameObject.SetActive(true);
        ps.Clear(true);
        ps.Play(true);
    }

    // ── Persistência Steam Lobby ─────────────────────────────────────────────

    [Serializable]
    private class ClimaLobbyPayload
    {
        public int tipo;
        public ClimaConfig chuva = new ClimaConfig();
        public ClimaConfig neve = new ClimaConfig();
    }

    private void SalvarClimaNoLobby()
    {
        if (!SouMestre() || steamLobbyManager == null) return;

        var payload = new ClimaLobbyPayload { tipo = (int)tipoAtual, chuva = configChuva, neve = configNeve };
        steamLobbyManager.DefinirDadoDaSala(chaveLobbyClima, JsonUtility.ToJson(payload));
    }

    private void RestaurarClimaDoLobby()
    {
        if (steamLobbyManager == null || !steamLobbyManager.SalaAtual.HasValue) return;

        string json = steamLobbyManager.ObterDadoDaSala(chaveLobbyClima);
        if (string.IsNullOrWhiteSpace(json)) return;

        ClimaLobbyPayload payload;
        try { payload = JsonUtility.FromJson<ClimaLobbyPayload>(json); }
        catch (Exception e) { Debug.LogWarning("[EfeitoClima] JSON de clima inválido no lobby: " + e.Message); return; }

        if (payload == null) return;
        AplicarClimaRecebido((TipoEfeitoClima)payload.tipo, payload.chuva, payload.neve);
    }
}
