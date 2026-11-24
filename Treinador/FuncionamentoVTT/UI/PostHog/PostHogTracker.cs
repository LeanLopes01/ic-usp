using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class PostHogTracker : MonoBehaviour
{
    public static PostHogTracker Instancia;

    [Header("Configurações do PostHog")]
    [Tooltip("Cole sua Project API Key aqui")]
    public string apiKey = "phc_C8tynmtMaNpmUoRcC4jba95TzPosNDZR5HuhTxEKFsyR";

    [Tooltip("Geralmente https://us.i.posthog.com ou https://eu.i.posthog.com")]
    public string hostUrl = "https://us.i.posthog.com";

    [Tooltip("Limite de caracteres de log enviados no sumário de fechamento")]
    public int maxLogChars = 30000;

    [Header("Watchdog de Performance (picos de frame)")]
    [Tooltip("Duração de frame (ms) acima da qual um evento 'pico_de_frame' é enviado ao PostHog. " +
        "Ex: 50ms ≈ abaixo de 20 FPS naquele frame.")]
    public float limiarPicoFrameMs = 50f;
    [Tooltip("Intervalo mínimo (segundos) entre dois envios de 'pico_de_frame' — evita spammar o " +
        "PostHog numa sequência longa de frames ruins (ex: carregamento de cena inteira).")]
    public float cooldownPicoFrameSegundos = 5f;

    private float _ultimoEnvioPicoTempo = float.NegativeInfinity;

    // Chave de PlayerPrefs marcando que o hardware_info já foi enviado com SUCESSO nesta máquina —
    // só é gravada dentro do onSucesso de RotinaChamadaHttp (ver RotinaEnviarHardwareInfo), nunca só
    // por ter tentado: se a requisição falhar (erro de rede, como no log reportado), o flag continua
    // 0 e a próxima sessão tenta de novo — só para de mandar depois que realmente chegou no PostHog.
    // O sufixo "_v1" existe pra permitir invalidar e reenviar todo mundo no futuro (ex: se o formato
    // do evento mudar), só subindo pra "_v2".
    private const string ChaveHardwareInfoEnviado = "PostHog_HardwareInfoEnviado_v1";

    // "Última localização conhecida" do usuário dentro da cena única do jogo — cena_ativa do
    // Unity não serve pra nada aqui (é sempre a mesma), então qualquer manager de tela/painel
    // atualiza isso ao trocar de contexto (ver DefinirContextoAtual) e o pico_de_frame manda o
    // valor mais recente. Estático de propósito (mesmo padrão de GlobalFichaData) — não depende de
    // Instancia existir pra ser lido/escrito, útil se algum chamador rodar antes do Awake.
    private static string _contextoAtual = "desconhecido";

    // ── Identidade ────────────────────────────────────────────────
    private string usuarioID;

    // ── Sessão ────────────────────────────────────────────────────
    private DateTime      _inicioSessao;
    private StringBuilder _logsConsole = new StringBuilder();

    // ── Contadores acumuladores ───────────────────────────────────
    // Continuam existindo pro sumário de fechamento (app_fechado_sumario) — um retrato "melhor
    // esforço" da sessão inteira — mas cada criação TAMBÉM manda um evento imediato (ver
    // TrackFichaCriada/TrackSalaCriada/TrackCampanhaCriada/TrackSistemaCriado) que não depende de
    // OnApplicationQuit rodar até o fim, que é a causa mais provável de dado "sumido" (ver conversa).
    private int _fichasCriadasTotal;
    private int _salasCriadasTotal;
    private int _campanhasCriadasTotal;
    private int _sistemasCriadosTotal;
    private readonly Dictionary<string, int> _fichasPorSistema     = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _salasPorSistema      = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _campanhasPorSistema  = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    // ─────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (Instancia == null)
        {
            Instancia = this;
            DontDestroyOnLoad(gameObject);
            InicializarUsuario();
            Application.logMessageReceived += CapturarLog;
            TrackHardwareInfo();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= CapturarLog;
    }

    // ─────────────────────────────────────────────────────────────────
    // Watchdog de pico de frame — roda todo frame, então a checagem em si tem que ser barata
    // (só uma comparação de float). Não dá pra apontar o script/GameObject exato causador em
    // runtime sem o Profiler do Editor (fora de escopo aqui) — mas a CENA ativa + o timestamp já
    // ajudam a cruzar com os outros eventos (ex: "sempre trava logo depois de uma resolução de
    // ataque em área") pra achar o padrão na análise do PostHog.
    void Update()
    {
        float frameMs = Time.unscaledDeltaTime * 1000f;
        if (frameMs < limiarPicoFrameMs) return;
        if (Time.unscaledTime - _ultimoEnvioPicoTempo < cooldownPicoFrameSegundos) return;

        _ultimoEnvioPicoTempo = Time.unscaledTime;
        EnviarEventoEnriquecido("pico_de_frame",
            $@"""duracao_frame_ms"": {frameMs.ToString("F1", CultureInfo.InvariantCulture)}, ""contexto_atual"": ""{EscaparJson(_contextoAtual)}""");
    }

    /// <summary>
    /// Atualiza a "última localização conhecida" do usuário (tela de menu, painel da barra de
    /// ferramentas, aba de ficha, etc.) — chamado pelos managers de UI relevantes (ToggleObjects,
    /// BarraFerramentasManager, SalaAbasFichasManager) sempre que o contexto muda. O watchdog de
    /// pico_de_frame manda esse valor junto, já que a cena do Unity sozinha não diferencia nada aqui
    /// (o jogo inteiro roda numa única cena).
    /// </summary>
    public static void DefinirContextoAtual(string contexto)
    {
        if (!string.IsNullOrWhiteSpace(contexto))
            _contextoAtual = contexto;
    }

    // ─────────────────────────────────────────────────────────────────
    private void InicializarUsuario()
    {
        usuarioID = PlayerPrefs.HasKey("PostHog_UserID")
            ? PlayerPrefs.GetString("PostHog_UserID")
            : CriarNovoID();

        _inicioSessao = DateTime.UtcNow;
    }

    private string CriarNovoID()
    {
        string id = Guid.NewGuid().ToString();
        PlayerPrefs.SetString("PostHog_UserID", id);
        PlayerPrefs.Save();
        return id;
    }

    // ─────────────────────────────────────────────────────────────────
    private void CapturarLog(string mensagem, string stackTrace, LogType tipo)
    {
        if (tipo != LogType.Error && tipo != LogType.Exception) return;
        if (_logsConsole.Length >= maxLogChars) return;

        string ts = DateTime.Now.ToString("HH:mm:ss");
        _logsConsole.AppendLine($"[{ts}] [ERRO] {mensagem}\\n{stackTrace}");
    }

    // ─────────────────────────────────────────────────────────────────
    void OnApplicationQuit()
    {
        double minutos = Math.Round((DateTime.UtcNow - _inicioSessao).TotalMinutes, 2);
        string logs    = _logsConsole.Length > 0 ? _logsConsole.ToString() : "(sem logs)";

        // Monta as propriedades base
        var props = new StringBuilder();
        props.Append($@"""duracao_sessao_minutos"": {minutos.ToString(CultureInfo.InvariantCulture)}");
        props.Append($@", ""fichas_criadas_total"": {_fichasCriadasTotal}");
        props.Append($@", ""salas_criadas_total"": {_salasCriadasTotal}");
        props.Append($@", ""campanhas_criadas_total"": {_campanhasCriadasTotal}");
        props.Append($@", ""sistemas_criados_total"": {_sistemasCriadosTotal}");
        props.Append($@", ""$os"": ""{EscaparJson(SystemInfo.operatingSystem)}""");
        props.Append($@", ""versao_jogo"": ""{EscaparJson(Application.version)}""");
        props.Append($@", ""logs_console"": ""{EscaparJson(logs)}""");

        // Contagens planas por sistema (ex: "fichas_criadas_quinta_edicao": 3)
        foreach (var kvp in _fichasPorSistema)
            props.Append($@", ""fichas_criadas_{EscaparJson(NormalizarChaveSistema(kvp.Key))}"": {kvp.Value}");

        foreach (var kvp in _salasPorSistema)
            props.Append($@", ""salas_criadas_{EscaparJson(NormalizarChaveSistema(kvp.Key))}"": {kvp.Value}");

        foreach (var kvp in _campanhasPorSistema)
            props.Append($@", ""campanhas_criadas_{EscaparJson(NormalizarChaveSistema(kvp.Key))}"": {kvp.Value}");

        EnviarJsonSincrono($"{hostUrl}/capture/", MontarJson("app_fechado_sumario", props.ToString()));
    }

    // ─────────────────────────────────────────────────────────────────
    // Envio síncrono via HttpWebRequest — corrotinas não rodam em OnApplicationQuit
    private void EnviarJsonSincrono(string url, string json)
    {
        try
        {
            byte[]         data    = Encoding.UTF8.GetBytes(json);
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method        = "POST";
            request.ContentType   = "application/json";
            request.ContentLength = data.Length;
            request.Timeout       = 5000;

            using (var stream = request.GetRequestStream())
                stream.Write(data, 0, data.Length);
            using (request.GetResponse()) { }

            Debug.Log("[PostHog] app_fechado_sumario enviado.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PostHog] Falha ao enviar sumário: {ex.Message}");
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // Envio assíncrono (corrotina) — usado por eventos em tempo real
    public static void EnviarEvento(string nomeDoEvento)
    {
        if (Instancia != null)
            Instancia.StartCoroutine(Instancia.RotinaEnviarEvento(nomeDoEvento));
        else
            Debug.LogWarning("PostHogTracker não encontrado na cena!");
    }

    public static void EnviarEventoEnriquecido(string nomeDoEvento, string jsonPropriedadesCustomizadas)
    {
        if (Instancia != null)
            Instancia.StartCoroutine(Instancia.RotinaEnviarEventoEnriquecido(nomeDoEvento, jsonPropriedadesCustomizadas));
        else
            Debug.LogWarning("PostHogTracker não encontrado na cena!");
    }

    private IEnumerator RotinaEnviarEvento(string nomeDoEvento)
    {
        string props = $@"""$os"": ""{EscaparJson(SystemInfo.operatingSystem)}"", ""versao_jogo"": ""{EscaparJson(Application.version)}""";
        yield return RotinaChamadaHttp(MontarJson(nomeDoEvento, props), nomeDoEvento);
    }

    private IEnumerator RotinaEnviarEventoEnriquecido(string nomeDoEvento, string propriedadesCustomizadas)
    {
        string props =
            $@"""$os"": ""{EscaparJson(SystemInfo.operatingSystem)}"", " +
            $@"""versao_jogo"": ""{EscaparJson(Application.version)}"", " +
            propriedadesCustomizadas;
        yield return RotinaChamadaHttp(MontarJson(nomeDoEvento, props), nomeDoEvento);
    }

    private IEnumerator RotinaChamadaHttp(string json, string nomeDoEvento, Action onSucesso = null)
    {
        using (UnityWebRequest req = new UnityWebRequest($"{hostUrl}/capture/", "POST"))
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            req.uploadHandler   = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
                // Falha de envio (sem internet, PostHog fora do ar, bloqueio de firewall) é uma
                // condição esperada/recuperável pra telemetria, não um bug do jogo — LogWarning em
                // vez de LogError, mesmo raciocínio do AutoUpdater: um Error aciona o Sentry e pode
                // travar o Play Mode inteiro se "Error Pause" estiver ligado no Console do Editor.
                Debug.LogWarning($"[PostHog] Falha ao enviar '{nomeDoEvento}': {req.error}");
            else
            {
                Debug.Log($"[PostHog] Evento '{nomeDoEvento}' enviado.");
                onSucesso?.Invoke();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────
    private string MontarJson(string evento, string propriedades)
        => $@"{{""api_key"":""{apiKey}"",""event"":""{evento}"",""distinct_id"":""{usuarioID}"",""properties"":{{{propriedades}}}}}";

    // ─────────────────────────────────────────────────────────────────
    // Normaliza o nome do sistema para chave de propriedade segura
    // "Quinta Edição" → "quinta_edicao"
    private static string NormalizarChaveSistema(string nome)
    {
        if (string.IsNullOrWhiteSpace(nome)) return "desconhecido";

        string semAcento = nome.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (char c in semAcento)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
            else if (c == ' ' || c == '_' || c == '-') sb.Append('_');
        }
        return sb.Length > 0 ? sb.ToString().Trim('_') : "desconhecido";
    }

    // ─────────────────────────────────────────────────────────────────
    // API pública
    // ─────────────────────────────────────────────────────────────────

    /// <summary>Associa o usuarioID ao SteamID. Chamar logo após o login na Steam.</summary>
    public static void IdentificarUsuario(string steamId, string steamPersonaName)
    {
        if (Instancia == null) { Debug.LogWarning("PostHogTracker não encontrado na cena!"); return; }
        Instancia.usuarioID = steamId;
        PlayerPrefs.SetString("PostHog_UserID", steamId);
        PlayerPrefs.Save();
        EnviarEventoEnriquecido("$identify", $@"""name"": ""{EscaparJson(steamPersonaName)}""");
    }

    /// <summary>
    /// Registra criação de ficha: incrementa o acumulador da sessão (entra em fichas_criadas_total
    /// e fichas_criadas_{sistema} no sumário de fechamento) E manda um evento "ficha_criada" IMEDIATO
    /// — não depende de OnApplicationQuit rodar até o fim pra chegar ao PostHog.
    /// </summary>
    public static void TrackFichaCriada(string nomeSistema)
    {
        if (Instancia == null) return;
        Instancia._fichasCriadasTotal++;
        string chave = nomeSistema ?? "desconhecido";
        Instancia._fichasPorSistema.TryGetValue(chave, out int atual);
        Instancia._fichasPorSistema[chave] = atual + 1;

        EnviarEventoEnriquecido("ficha_criada", $@"""sistema"": ""{EscaparJson(chave)}""");
    }

    /// <summary>
    /// Registra criação de sala: acumulador da sessão (salas_criadas_total/salas_criadas_{sistema})
    /// + evento "sala_criada" imediato.
    /// </summary>
    public static void TrackSalaCriada(string nomeSistema, bool temSenha)
    {
        if (Instancia == null) return;
        Instancia._salasCriadasTotal++;
        string chave = nomeSistema ?? "desconhecido";
        Instancia._salasPorSistema.TryGetValue(chave, out int atual);
        Instancia._salasPorSistema[chave] = atual + 1;

        EnviarEventoEnriquecido("sala_criada",
            $@"""sistema"": ""{EscaparJson(chave)}"", ""tem_senha"": {(temSenha ? "true" : "false")}");
    }

    /// <summary>
    /// Registra criação de campanha: acumulador da sessão (campanhas_criadas_total/
    /// campanhas_criadas_{sistema}) + evento "campanha_criada" imediato. Mesmo padrão de
    /// TrackFichaCriada/TrackSalaCriada.
    /// </summary>
    public static void TrackCampanhaCriada(string nomeSistema)
    {
        if (Instancia == null) return;
        Instancia._campanhasCriadasTotal++;
        string chave = nomeSistema ?? "desconhecido";
        Instancia._campanhasPorSistema.TryGetValue(chave, out int atual);
        Instancia._campanhasPorSistema[chave] = atual + 1;

        EnviarEventoEnriquecido("campanha_criada", $@"""sistema"": ""{EscaparJson(chave)}""");
    }

    /// <summary>
    /// Registra criação de um sistema (homebrew, ver SystemManager.OnSistemaCriado): acumulador
    /// (sistemas_criados_total) + evento "sistema_criado" imediato. Centraliza aqui o que antes era
    /// montado na mão em SystemManager, mesmo padrão dos outros Track*.
    /// </summary>
    public static void TrackSistemaCriado(string nomeSistema)
    {
        if (Instancia == null) return;
        Instancia._sistemasCriadosTotal++;

        EnviarEventoEnriquecido("sistema_criado", $@"""nome_sistema"": ""{EscaparJson(nomeSistema)}""");
    }

    /// <summary>
    /// Registra quanto tempo o jogador ficou numa tela (ex: criação de ficha, salas, campanhas) —
    /// evento "tempo_na_tela" imediato. Chamado por ToggleObjects sempre que a tela ativa muda.
    /// </summary>
    public static void TrackTempoNaTela(string nomeTela, double segundos)
    {
        if (string.IsNullOrWhiteSpace(nomeTela) || segundos <= 0) return;

        EnviarEventoEnriquecido("tempo_na_tela",
            $@"""tela"": ""{EscaparJson(nomeTela)}"", ""duracao_segundos"": {segundos.ToString("F1", CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// Envia specs de hardware da máquina (CPU, RAM, GPU, SO) — chamado uma vez por sessão em Awake.
    /// Tipo de RAM (DDR3/4/5) é obtido no Windows e no Linux (ver ObterTipoRam, best-effort — pode
    /// vir "desconhecido" mesmo em plataforma suportada, ver comentário lá); no macOS vai sempre
    /// como "desconhecido" — o Unity (SystemInfo) não expõe isso em lugar nenhum.
    /// </summary>
    public static void TrackHardwareInfo()
    {
        if (Instancia == null) return;
        if (PlayerPrefs.GetInt(ChaveHardwareInfoEnviado, 0) == 1) return; // já enviado com sucesso antes — não repete
        Instancia.StartCoroutine(Instancia.RotinaEnviarHardwareInfo());
    }

    private IEnumerator RotinaEnviarHardwareInfo()
    {
        string tipoRam = ObterTipoRam();
        string props =
            $@"""cpu_modelo"": ""{EscaparJson(SystemInfo.processorType)}"", " +
            $@"""cpu_nucleos"": {SystemInfo.processorCount}, " +
            $@"""cpu_frequencia_mhz"": {SystemInfo.processorFrequency}, " +
            $@"""ram_mb"": {SystemInfo.systemMemorySize}, " +
            $@"""ram_tipo"": ""{EscaparJson(tipoRam)}"", " +
            $@"""gpu_modelo"": ""{EscaparJson(SystemInfo.graphicsDeviceName)}"", " +
            $@"""gpu_fabricante"": ""{EscaparJson(SystemInfo.graphicsDeviceVendor)}"", " +
            $@"""gpu_memoria_mb"": {SystemInfo.graphicsMemorySize}, " +
            $@"""gpu_api"": ""{EscaparJson(SystemInfo.graphicsDeviceType.ToString())}"", " +
            $@"""so"": ""{EscaparJson(SystemInfo.operatingSystem)}"", " +
            $@"""device_model"": ""{EscaparJson(SystemInfo.deviceModel)}""";

        yield return RotinaChamadaHttp(MontarJson("hardware_info", props), "hardware_info",
            onSucesso: () =>
            {
                PlayerPrefs.SetInt(ChaveHardwareInfoEnviado, 1);
                PlayerPrefs.Save();
            });
    }

    // Tipo de memória (DDR3/DDR4/DDR5) — best-effort, cross-platform. Depende de a placa-mãe/BIOS
    // ter preenchido corretamente o campo de tipo no SMBIOS: muita placa (principalmente notebooks/
    // OEM) simplesmente não relata isso direito, então "desconhecido" às vezes é o valor CORRETO,
    // não uma falha do nosso código — não tem jeito de adivinhar um dado que o firmware nunca gravou.
    //
    // Windows: PowerShell + Get-CimInstance (Win32_PhysicalMemory) — trocado do "wmic" antigo porque
    // a Microsoft já REMOVE o wmic.exe por padrão em builds recentes do Windows 11 (podia
    // simplesmente não existir na máquina do usuário).
    // Linux: "dmidecode --type 17" — só que ele lê /dev/mem e por isso EXIGE root; sem sudo o
    // processo falha (permissão negada) e cai no fallback "desconhecido". Não tem como contornar sem
    // pedir senha de administrador do usuário só pra telemetria, o que não faz sentido aqui.
    // macOS: sem utilitário de linha de comando padrão equivalente — fica "desconhecido" sempre.
    private static string ObterTipoRam()
    {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        string saida = ExecutarComandoEObterSaida("powershell",
            "-NoProfile -NonInteractive -Command \"(Get-CimInstance Win32_PhysicalMemory | Select-Object -First 1 -ExpandProperty SMBIOSMemoryType)\"");
        if (saida != null && int.TryParse(saida.Trim(), out int codigo))
            return CodigoSmbiosParaTipoRam(codigo);
        return "desconhecido";
#elif UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
        string saida = ExecutarComandoEObterSaida("dmidecode", "--type 17");
        if (saida == null) return "desconhecido";

        foreach (string linhaBruta in saida.Split('\n'))
        {
            string linha = linhaBruta.Trim();
            if (!linha.StartsWith("Type:", StringComparison.OrdinalIgnoreCase)) continue;

            // dmidecode já devolve o texto pronto (ex: "DDR4"), sem precisar traduzir código
            string tipo = linha.Substring("Type:".Length).Trim();
            if (!string.IsNullOrWhiteSpace(tipo) && !tipo.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
                return tipo;
        }
        return "desconhecido";
#else
        return "desconhecido";
#endif
    }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN || UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX
    // Roda um processo externo e devolve o stdout — null se falhar (comando ausente, sem permissão,
    // timeout). Compartilhado entre Windows (powershell) e Linux (dmidecode) pra não duplicar a
    // lógica de Process/try-catch em cada plataforma.
    private static string ExecutarComandoEObterSaida(string executavel, string argumentos)
    {
        try
        {
            var info = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = executavel,
                Arguments              = argumentos,
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using (var processo = System.Diagnostics.Process.Start(info))
            {
                string saida = processo.StandardOutput.ReadToEnd();
                processo.WaitForExit(5000);
                return saida;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PostHog] Falha ao obter tipo de RAM via '{executavel}': {ex.Message}");
            return null;
        }
    }
#endif

    // Códigos SMBIOSMemoryType (spec DMTF) mais comuns em PCs modernos — só usado no Windows
    // (Linux/dmidecode já devolve o nome do tipo pronto, sem código numérico).
    private static string CodigoSmbiosParaTipoRam(int codigo) => codigo switch
    {
        20 => "DDR",
        21 => "DDR2",
        24 => "DDR3",
        26 => "DDR4",
        34 => "DDR5",
        _  => "desconhecido",
    };

    /// <summary>Registra duração de uma sessão de sala (mestre/jogador). Evento imediato.</summary>
    public static void TrackSessaoEncerrada(float tempoEmSegundos, string papel)
    {
        float min = tempoEmSegundos / 60f;
        EnviarEventoEnriquecido("sessao_encerrada",
            $@"""duracao_minutos"": {min.ToString(CultureInfo.InvariantCulture)}, ""papel"": ""{EscaparJson(papel)}""");
    }

    public static string EscaparJson(string valor)
    {
        if (string.IsNullOrEmpty(valor)) return string.Empty;
        return valor
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "");
    }
}
