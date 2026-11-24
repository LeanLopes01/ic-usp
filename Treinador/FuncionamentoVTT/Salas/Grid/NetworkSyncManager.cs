using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Steamworks;
using Steamworks.Data;
using UnityEngine;

// ---------------------------------------------------------------------------
// Tipos de mensagem P2P
// ---------------------------------------------------------------------------

[Serializable]
public class NetMessage
{
    public int    tipo;    // 1=MoverToken  2=MudarMapa(legacy)  3=EstadoInicial  4=SpawnToken
                           // 5=StartFileTransfer  6=FileChunk  7=RemoverToken
                           // 8=TokenSegurado  9=TokenSolto  10=AtribuicaoDeDono  11=Clima
                           // 12=AudioTabletop  13=DanoToken  14=FlagsToken  15=RaioVisaoToken
    public string payload;
}

[Serializable] public class MsgMoverToken    { public string tokenId; public float x, y, z; }
[Serializable] public class MsgTokenSegurado { public string tokenId; public string nomeJogador; }
[Serializable] public class MsgRemoverToken  { public string tokenId; }
[Serializable] public class MsgMudarMapa     { public ulong workshopId; }
[Serializable] public class MsgAtribuicaoDeDono { public int fichasGeneralId; public int fichasDndId; public string donoSteamId; }
[Serializable] public class MsgClima { public int tipo; public ClimaConfig chuva; public ClimaConfig neve; }
[Serializable] public class MsgAudioTabletop { public int indice; public float volume; public bool loop = true; }
[Serializable] public class MsgDanoToken     { public string tokenId; public int novaVidaAtual; }
[Serializable] public class MsgFlagsToken    { public string tokenId; public bool bloqueado; public bool escondido; }
// Raio de visão (metros) de UM token — enviado pelo Mestre ao mexer no slider individual/global do
// painel "Content de Visão" (ver VisaoPainelUI/VisaoTokenSliderSlot).
[Serializable] public class MsgRaioVisaoToken { public string tokenId; public float raioMetros; }
[Serializable] public class MsgSpawnToken    { public string tokenId; public string nomeToken;
                                               public string imagemPath; public ulong workshopIdToken;
                                               public float x, y, z;
                                               // Link com a ficha de origem (0 = sem link)
                                               public int fichasGeneralId; public int fichasDndId;
                                               public string ownerSteamId; public int ownerPlayerId;
                                               // Link com o monstro de origem (vazio = sem link)
                                               public string monstroId; }

// EstadoInicial carrega mapa + todos os tokens actuais
[Serializable] public class EstadoTokenNet   { public string tokenId; public string nomeToken;
                                               public string imagemPath; public ulong workshopIdToken;
                                               public float x, y, z;
                                               public int fichasGeneralId; public int fichasDndId;
                                               public string ownerSteamId; public int ownerPlayerId;
                                               public string monstroId; }
[Serializable] public class EstadoTokenArray { public EstadoTokenNet[] itens; }
[Serializable] public class MsgEstadoInicial { public ulong workshopIdMapa; public string tokensJson; }

// Transferência P2P de ficheiros pesados (mapas, tokens e fichas)
[Serializable] public class MsgStartFileTransfer { public string fileId; public string fileName;
                                                   public int totalChunks; public long totalSize;
                                                   public string tokenId;     // vazio = mapa/ficha/paredes; preenchido = imagem de token
                                                   public long geracaoMapa;   // só relevante quando tokenId E fichaKey vazios (é mapa) — ver Bug C.2
                                                   public string fichaKey;    // preenchido = payload de ficha (ver FichaSyncManager)
                                                   public string ownerSteamId; // dono da ficha, só relevante se fichaKey preenchido
                                                   public bool   ehParedes; } // preenchido = payload de paredes (ver MsgParedesPayload)
[Serializable] public class MsgFileChunk         { public string fileId; public int chunkIndex;
                                                   public string chunkDataB64; }

// Payload comprimido (gzip) de paredes de UM mapa — ver MapManager.EnviarParedesDoMapaParaTodos/
// ReceberParedesConcluidas. mapaId identifica a qual MapConfig aplicar no lado receptor (o mapa pode
// não ser o ativo no momento em que a rede entrega isso).
[Serializable] public class MsgParedesPayload    { public string mapaId; public List<WallLine> paredes;
                                                   public bool vetorizado;
                                                   // Fog manual (ver Salas/Grid/FogRect.cs) — mesma
                                                   // categoria de "geometria estática do mapa
                                                   // autorada pelo host", reaproveita o mesmo payload
                                                   // em vez de um canal de rede novo.
                                                   public List<FogRect> fogRects;
                                                   // Configuração de iluminação dinâmica (ver
                                                   // MapManager.DefinirConfigIluminacao) — mesmo
                                                   // raciocínio de fogRects acima: é geometria/config
                                                   // estática autorada pelo Mestre, reaproveita o canal.
                                                   public float alphaMaximoVisaoDinamica;
                                                   public float transicaoGradienteEmCelulas;
                                                   public float alphaVisaoGeralTokens;
                                                   public float alphaFogParaHost;
                                                   public int   raiosBaseVisao;
                                                   // Color default (não Color.white) faria mapas
                                                   // salvos/enviados antes deste campo existir chegarem
                                                   // com filtro ligado sem querer — ver MapConfig.corLuzVisao.
                                                   public UnityEngine.Color corLuzVisao = UnityEngine.Color.white;
                                                   // Fontes de luz estática (tocha — ver Salas/Grid/
                                                   // LightSource.cs) — mesmo raciocínio de fogRects acima.
                                                   public List<LightSource> fontesDeLuz; }

// ---------------------------------------------------------------------------
// NetworkSyncManager
// ---------------------------------------------------------------------------

public class NetworkSyncManager : MonoBehaviour
{
    public static NetworkSyncManager Instancia { get; private set; }

    /// <summary>Disparado quando a posição final de um token chega pela rede (mesmo dado repassado
    /// a mapManager.ReceberMovimentoTokenRede) — usado por SalaMobileBridge pra replicar pro celular
    /// sem duplicar o parsing de NetMessage.</summary>
    public event Action<string, Vector3> OnTokenMovidoRecebido;

    /// <summary>Disparado quando alguém começa (segurando=true) ou termina (segurando=false) de
    /// arrastar um token — ver MapTokenDraggable.NotificarInicioArrasto/NotificarFimArrasto.</summary>
    public event Action<string, string, bool> OnTokenSeguradoRecebido;

    // Canal 10 — FacepunchTransport usa 0-1; ficamos longe deles
    private const int CANAL = 10;

    // 256 KB por chunk — Base64 eleva para ~341 KB; bem abaixo do limite de 1 MB do Steam P2P
    private const int TAMANHO_CHUNK = 256 * 1024;

    [SerializeField] private SteamLobbyManager      steamLobbyManager;
    [SerializeField] private MapManager             mapManager;
    [SerializeField] private DownloadMapUI          downloadMapUI;
    [SerializeField] private FichaSyncManager       fichaSyncManager;
    [SerializeField] private SalaAbasFichasManager  salaAbasFichasManager;
    [SerializeField] private EfeitoClimaManager     efeitoClimaManager;
    [SerializeField] private AudioTabletopManager   audioTabletopManager;

    // Maior geração de mapa já vista (aceita ou aplicada) — descarta transferências de mapa
    // desatualizadas que cheguem fora de ordem (chunks concorrentes de trocas rápidas). Ver Bug C.2.
    private long maiorGeracaoMapaVista = -1;

    // Limpeza de transferências "zumbi" — peer desconecta no meio de um envio e a entrada nunca
    // recebe o chunk final. Ver Bug C.4.
    private const float TIMEOUT_TRANSFERENCIA_SEGUNDOS = 30f;
    private const float INTERVALO_VARREDURA_ZUMBI      = 10f;

    // ── Estado de transferências activas (receptor) ─────────────────────────

    private class TransferenciaAtiva
    {
        public string   nomeArquivo;
        public string   tokenId;      // vazio = mapa/ficha; preenchido = token
        public string   fichaKey;     // vazio = mapa/token; preenchido = ficha (ver FichaSyncManager)
        public string   ownerSteamId; // dono da ficha, só relevante se fichaKey preenchido
        public bool     ehParedes;    // preenchido = payload de paredes (ver MsgParedesPayload)
        public long     geracaoMapa;  // só relevante para mapa
        public byte[][] chunks;
        public int      recebidos;
        public float    ultimoChunkRecebidoEm; // Time.unscaledTime — usado pela varredura de zumbis
    }

    private readonly Dictionary<string, TransferenciaAtiva> transferenciasAtivas
        = new Dictionary<string, TransferenciaAtiva>();

    // ── Ciclo de vida ────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instancia != null && Instancia != this) { Destroy(gameObject); return; }
        Instancia = this;
    }

    private void Start()
    {
        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();
        if (mapManager == null)
            mapManager = FindFirstObjectByType<MapManager>();
        if (fichaSyncManager == null)
            fichaSyncManager = FindFirstObjectByType<FichaSyncManager>();
        if (salaAbasFichasManager == null)
            salaAbasFichasManager = FindFirstObjectByType<SalaAbasFichasManager>();
        if (efeitoClimaManager == null)
            efeitoClimaManager = FindFirstObjectByType<EfeitoClimaManager>();
        if (audioTabletopManager == null)
            audioTabletopManager = FindFirstObjectByType<AudioTabletopManager>();

        SteamNetworking.OnP2PSessionRequest  += AceitarSessaoP2P;
        SteamMatchmaking.OnLobbyMemberJoined += AoNovoMembroEntrar;

        StartCoroutine(VarrerTransferenciasZumbi());
    }

    private void OnDestroy()
    {
        SteamNetworking.OnP2PSessionRequest  -= AceitarSessaoP2P;
        SteamMatchmaking.OnLobbyMemberJoined -= AoNovoMembroEntrar;
    }

    // ── Leitura de pacotes ──────────────────────────────────────────────────

    private void Update()
    {
        if (!SteamClient.IsValid) return;

        while (SteamNetworking.IsP2PPacketAvailable(CANAL))
        {
            P2Packet? pacote = SteamNetworking.ReadP2PPacket(CANAL);
            if (!pacote.HasValue) break;

            try
            {
                ProcessarMensagem(Encoding.UTF8.GetString(pacote.Value.Data));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NetworkSync] Pacote inválido: {e.Message}");
            }
        }
    }

    private void ProcessarMensagem(string json)
    {
        NetMessage msg = JsonUtility.FromJson<NetMessage>(json);
        switch (msg.tipo)
        {
            case 1: // MoverToken
                var mv = JsonUtility.FromJson<MsgMoverToken>(msg.payload);
                Vector3 posMovida = new Vector3(mv.x, mv.y, mv.z);
                mapManager?.ReceberMovimentoTokenRede(mv.tokenId, posMovida);
                OnTokenMovidoRecebido?.Invoke(mv.tokenId, posMovida);
                break;

            case 2: // MudarMapa via Workshop (mantido para compatibilidade)
                var mm = JsonUtility.FromJson<MsgMudarMapa>(msg.payload);
                if (mm.workshopId > 0)
                    mapManager?.BaixarEAplicarMapaWorkshop(mm.workshopId);
                break;

            case 3: // EstadoInicial — tokens (mapa chega via chunks P2P separados)
                var ei = JsonUtility.FromJson<MsgEstadoInicial>(msg.payload);
                if (ei.workshopIdMapa > 0)
                    mapManager?.BaixarEAplicarMapaWorkshop(ei.workshopIdMapa);

                if (!string.IsNullOrWhiteSpace(ei.tokensJson))
                {
                    EstadoTokenArray arr = JsonUtility.FromJson<EstadoTokenArray>(ei.tokensJson);
                    if (arr?.itens != null)
                    {
                        foreach (EstadoTokenNet t in arr.itens)
                            mapManager?.ReceberSpawnTokenRede(t.tokenId, t.nomeToken, t.imagemPath,
                                                              t.workshopIdToken, new Vector3(t.x, t.y, t.z),
                                                              t.fichasGeneralId, t.fichasDndId, t.ownerSteamId, t.ownerPlayerId,
                                                              t.monstroId);
                    }
                }
                break;

            case 4: // SpawnToken
                var sp = JsonUtility.FromJson<MsgSpawnToken>(msg.payload);
                mapManager?.ReceberSpawnTokenRede(sp.tokenId, sp.nomeToken, sp.imagemPath,
                                                  sp.workshopIdToken, new Vector3(sp.x, sp.y, sp.z),
                                                  sp.fichasGeneralId, sp.fichasDndId, sp.ownerSteamId, sp.ownerPlayerId,
                                                  sp.monstroId);
                break;

            case 5: // StartFileTransfer — prepara buffer para receber chunks (mapa, token, ficha ou paredes)
                var sft = JsonUtility.FromJson<MsgStartFileTransfer>(msg.payload);
                bool ehMapa = !sft.ehParedes && string.IsNullOrEmpty(sft.tokenId) && string.IsNullOrEmpty(sft.fichaKey);

                // Descarte cedo: se já vimos uma geração de mapa mais nova, nem aloca buffer pra
                // essa transferência — ela seria descartada de qualquer forma na conclusão (ver Bug C.2).
                if (ehMapa && sft.geracaoMapa < maiorGeracaoMapaVista)
                {
                    Debug.LogWarning($"[NetworkSync] Ignorando transferência de mapa desatualizada (geração {sft.geracaoMapa} < {maiorGeracaoMapaVista}).");
                    break;
                }
                if (ehMapa) maiorGeracaoMapaVista = sft.geracaoMapa;

                transferenciasAtivas[sft.fileId] = new TransferenciaAtiva
                {
                    nomeArquivo  = sft.fileName,
                    tokenId      = sft.tokenId ?? string.Empty,
                    fichaKey     = sft.fichaKey ?? string.Empty,
                    ownerSteamId = sft.ownerSteamId ?? string.Empty,
                    ehParedes    = sft.ehParedes,
                    geracaoMapa  = sft.geracaoMapa,
                    chunks       = new byte[sft.totalChunks][],
                    recebidos    = 0,
                    ultimoChunkRecebidoEm = Time.unscaledTime,
                };
                // UI de progresso somente para mapas (tokens/fichas/paredes chegam silenciosamente em background)
                if (ehMapa)
                {
                    downloadMapUI?.MostrarUI();
                    downloadMapUI?.AtualizarProgresso(0, sft.totalChunks);
                }
                Debug.Log($"[NetworkSync] Recebendo '{sft.fileName}' ({sft.totalChunks} chunks, {sft.totalSize / 1024f:0.#} KB)" +
                    $"{(string.IsNullOrEmpty(sft.tokenId) ? "" : $" [token:{sft.tokenId[..8]}]")}" +
                    $"{(string.IsNullOrEmpty(sft.fichaKey) ? "" : $" [ficha:{sft.fichaKey}]")}" +
                    $"{(sft.ehParedes ? " [paredes]" : "")}.");
                break;

            case 6: // FileChunk — armazena e verifica conclusão
                var fc = JsonUtility.FromJson<MsgFileChunk>(msg.payload);
                if (!transferenciasAtivas.TryGetValue(fc.fileId, out TransferenciaAtiva tr))
                {
                    Debug.LogWarning($"[NetworkSync] Chunk para fileId desconhecido: {fc.fileId}");
                    break;
                }

                tr.chunks[fc.chunkIndex] = Convert.FromBase64String(fc.chunkDataB64);
                tr.recebidos++;
                tr.ultimoChunkRecebidoEm = Time.unscaledTime;
                downloadMapUI?.AtualizarProgresso(tr.recebidos, tr.chunks.Length);

                if (tr.recebidos == tr.chunks.Length)
                    ReassemblarEAplicar(fc.fileId, tr);
                break;

            case 7: // RemoverToken — remove só a instância do mapa (catálogo/ficha não são afetados)
                var rm = JsonUtility.FromJson<MsgRemoverToken>(msg.payload);
                mapManager?.ReceberRemoverTokenRede(rm.tokenId);
                break;

            case 8: // TokenSegurado — alguém começou a arrastar
                var seg = JsonUtility.FromJson<MsgTokenSegurado>(msg.payload);
                OnTokenSeguradoRecebido?.Invoke(seg.tokenId, seg.nomeJogador, true);
                break;

            case 9: // TokenSolto — alguém terminou de arrastar (posição final vem separada, tipo 1)
                var solt = JsonUtility.FromJson<MsgTokenSegurado>(msg.payload);
                OnTokenSeguradoRecebido?.Invoke(solt.tokenId, solt.nomeJogador, false);
                break;

            case 10: // AtribuicaoDeDono — Mestre reatribuiu o dono de uma ficha/token
                var dono = JsonUtility.FromJson<MsgAtribuicaoDeDono>(msg.payload);
                mapManager?.AplicarDonoDeToken(dono.fichasGeneralId, dono.fichasDndId, dono.donoSteamId);
                salaAbasFichasManager?.AplicarDonoRecebido(dono.fichasGeneralId, dono.fichasDndId, dono.donoSteamId);
                break;

            case 11: // Clima — Mestre alterou tipo/valores do clima ativo
                var clima = JsonUtility.FromJson<MsgClima>(msg.payload);
                efeitoClimaManager?.AplicarClimaRecebido((TipoEfeitoClima)clima.tipo, clima.chuva, clima.neve);
                break;

            case 12: // AudioTabletop — Mestre alterou o volume e/ou o loop de um som ambiente
                var audioMsg = JsonUtility.FromJson<MsgAudioTabletop>(msg.payload);
                // Loop ANTES do volume — se isso cria a AudioSource agora (volume passando de 0),
                // ObterOuCriarFonte precisa ler o loop já atualizado na criação.
                audioTabletopManager?.AplicarLoopRecebido(audioMsg.indice, audioMsg.loop);
                audioTabletopManager?.AplicarVolumeRecebido(audioMsg.indice, audioMsg.volume);
                break;

            case 13: // DanoToken — dano/cura aplicado a um token (SelecaoAlvoManager)
                var dano = JsonUtility.FromJson<MsgDanoToken>(msg.payload);
                mapManager?.ReceberDanoTokenRede(dano.tokenId, dano.novaVidaAtual);
                break;

            case 14: // FlagsToken — Lock/Esconder alternado (content de ferramentas do token)
                var flags = JsonUtility.FromJson<MsgFlagsToken>(msg.payload);
                mapManager?.ReceberFlagsTokenRede(flags.tokenId, flags.bloqueado, flags.escondido);
                break;

            case 15: // RaioVisaoToken — Mestre mexeu no slider individual/global do painel "Content de Visão"
                var raioVisao = JsonUtility.FromJson<MsgRaioVisaoToken>(msg.payload);
                mapManager?.ReceberRaioVisaoTokenRede(raioVisao.tokenId, raioVisao.raioMetros);
                break;
        }
    }

    // Reconstrói o byte[] final e despacha para o destino certo (ficha, token ou mapa)
    private void ReassemblarEAplicar(string fileId, TransferenciaAtiva tr)
    {
        int tamanhoTotal = 0;
        for (int i = 0; i < tr.chunks.Length; i++)
            tamanhoTotal += tr.chunks[i].Length;

        byte[] dadosCompletos = new byte[tamanhoTotal];
        int pos = 0;
        for (int i = 0; i < tr.chunks.Length; i++)
        {
            Buffer.BlockCopy(tr.chunks[i], 0, dadosCompletos, pos, tr.chunks[i].Length);
            pos += tr.chunks[i].Length;
        }

        transferenciasAtivas.Remove(fileId);

        if (!string.IsNullOrEmpty(tr.fichaKey))
        {
            Debug.Log($"[NetworkSync] Ficha '{tr.fichaKey}' completa ({tamanhoTotal / 1024f:0.#} KB). Aplicando.");
            fichaSyncManager?.ReceberFichaConcluida(tr.fichaKey, tr.ownerSteamId, dadosCompletos);
        }
        else if (!string.IsNullOrEmpty(tr.tokenId))
        {
            Debug.Log($"[NetworkSync] Token '{tr.tokenId[..8]}' imagem completa ({tamanhoTotal / 1024f:0.#} KB). Aplicando sprite.");
            mapManager?.ReceberTokenImagemConcluida(tr.tokenId, dadosCompletos);
        }
        else if (tr.ehParedes)
        {
            Debug.Log($"[NetworkSync] Paredes completas ({tamanhoTotal / 1024f:0.#} KB). Aplicando.");
            mapManager?.ReceberParedesConcluidas(dadosCompletos);
        }
        else
        {
            // Guarda crítica: mesmo já tendo passado pela checagem no case 5, uma transferência
            // mais antiga pode ter COMEÇADO antes de uma mais nova e mesmo assim TERMINAR depois
            // (ordem de chegada de rede não é garantida). Sem essa segunda checagem, ela
            // sobrescreveria o mapa correto com um desatualizado. Ver Bug C.2.
            if (tr.geracaoMapa < maiorGeracaoMapaVista)
            {
                Debug.LogWarning($"[NetworkSync] Descartando mapa completo mas desatualizado (geração {tr.geracaoMapa} < {maiorGeracaoMapaVista}).");
                downloadMapUI?.EsconderUI();
                return;
            }

            downloadMapUI?.EsconderUI();
            Debug.Log($"[NetworkSync] '{tr.nomeArquivo}' completo ({tamanhoTotal / 1024f:0.#} KB). Aplicando mapa.");
            mapManager?.ReceberMapaConcluido(dadosCompletos);
        }
    }

    // Remove transferências abandonadas (peer desconectou no meio do envio) para evitar
    // vazamento de memória silencioso em sessões longas com várias trocas de mapa/ficha. Ver Bug C.4.
    private IEnumerator VarrerTransferenciasZumbi()
    {
        var esperaVarredura = new WaitForSeconds(INTERVALO_VARREDURA_ZUMBI);
        while (true)
        {
            yield return esperaVarredura;

            List<string> zumbis = null;
            foreach (KeyValuePair<string, TransferenciaAtiva> par in transferenciasAtivas)
            {
                if (Time.unscaledTime - par.Value.ultimoChunkRecebidoEm <= TIMEOUT_TRANSFERENCIA_SEGUNDOS)
                    continue;
                zumbis ??= new List<string>();
                zumbis.Add(par.Key);
            }
            if (zumbis == null) continue;

            foreach (string fileId in zumbis)
            {
                TransferenciaAtiva tr = transferenciasAtivas[fileId];
                transferenciasAtivas.Remove(fileId);
                Debug.LogWarning($"[NetworkSync] Transferência '{tr.nomeArquivo}' abandonada (sem chunks há mais de {TIMEOUT_TRANSFERENCIA_SEGUNDOS:0}s, {tr.recebidos}/{tr.chunks.Length} recebidos) — descartada.");
                if (!tr.ehParedes && string.IsNullOrEmpty(tr.tokenId) && string.IsNullOrEmpty(tr.fichaKey))
                    downloadMapUI?.EsconderUI();
            }
        }
    }

    // ── Envio ───────────────────────────────────────────────────────────────

    public void EnviarParaTodos(NetMessage msg)
    {
        if (!SteamClient.IsValid || steamLobbyManager?.SalaAtual == null) return;

        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        foreach (Friend membro in steamLobbyManager.SalaAtual.Value.Members)
        {
            if (membro.Id == SteamClient.SteamId) continue;
            SteamNetworking.SendP2PPacket(membro.Id, bytes, nChannel: CANAL, sendType: P2PSend.Reliable);
        }
    }

    public void EnviarParaJogador(SteamId alvo, NetMessage msg)
    {
        if (!SteamClient.IsValid) return;
        byte[] bytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(msg));
        SteamNetworking.SendP2PPacket(alvo, bytes, nChannel: CANAL, sendType: P2PSend.Reliable);
    }

    // ── API Pública — Transferência P2P de Ficheiros ────────────────────────

    /// <summary>Envia um ficheiro de mapa para TODOS os clientes em chunks de 256 KB.</summary>
    public void EnviarFicheiroPesado(byte[] dados, string nomeArquivo, long geracaoMapa) =>
        StartCoroutine(CorotinaEnvioFicheiro(dados, nomeArquivo, alvo: null, tokenId: string.Empty,
            geracaoMapa: geracaoMapa, fichaKey: string.Empty, ownerSteamId: string.Empty));

    /// <summary>Envia um ficheiro de mapa para um único jogador (ex: recém-entrou na sala).</summary>
    public void EnviarFicheiroPesadoParaJogador(byte[] dados, string nomeArquivo, SteamId alvo, long geracaoMapa) =>
        StartCoroutine(CorotinaEnvioFicheiro(dados, nomeArquivo, alvo, tokenId: string.Empty,
            geracaoMapa: geracaoMapa, fichaKey: string.Empty, ownerSteamId: string.Empty));

    /// <summary>Envia a imagem de um token para TODOS os clientes via P2P chunks.</summary>
    public void EnviarTokenImagemParaTodos(string tokenId, byte[] dados, string nomeArquivo) =>
        StartCoroutine(CorotinaEnvioFicheiro(dados, nomeArquivo, alvo: null, tokenId: tokenId,
            geracaoMapa: 0, fichaKey: string.Empty, ownerSteamId: string.Empty));

    /// <summary>Envia a imagem de um token para um único jogador (ex: recém-entrou na sala).</summary>
    public void EnviarTokenImagemParaJogador(string tokenId, byte[] dados, string nomeArquivo, SteamId alvo) =>
        StartCoroutine(CorotinaEnvioFicheiro(dados, nomeArquivo, alvo, tokenId: tokenId,
            geracaoMapa: 0, fichaKey: string.Empty, ownerSteamId: string.Empty));

    /// <summary>Envia o conteúdo completo (JSON comprimido) de uma ficha para TODOS. Ver FichaSyncManager.</summary>
    public void EnviarFichaParaTodos(string fichaKey, string ownerSteamId, byte[] dadosComprimidos) =>
        StartCoroutine(CorotinaEnvioFicheiro(dadosComprimidos, "ficha.json.gz", alvo: null, tokenId: string.Empty,
            geracaoMapa: 0, fichaKey: fichaKey, ownerSteamId: ownerSteamId));

    /// <summary>Envia o conteúdo completo (JSON comprimido) de uma ficha para um único jogador.</summary>
    public void EnviarFichaParaJogador(string fichaKey, string ownerSteamId, byte[] dadosComprimidos, SteamId alvo) =>
        StartCoroutine(CorotinaEnvioFicheiro(dadosComprimidos, "ficha.json.gz", alvo, tokenId: string.Empty,
            geracaoMapa: 0, fichaKey: fichaKey, ownerSteamId: ownerSteamId));

    /// <summary>Envia as paredes (JSON comprimido, ver MsgParedesPayload) de UM mapa para TODOS. Ver
    /// MapManager.EnviarParedesDoMapaParaTodos.</summary>
    public void EnviarParedesParaTodos(byte[] dadosComprimidos) =>
        StartCoroutine(CorotinaEnvioFicheiro(dadosComprimidos, "paredes.json.gz", alvo: null, tokenId: string.Empty,
            geracaoMapa: 0, fichaKey: string.Empty, ownerSteamId: string.Empty, ehParedes: true));

    /// <summary>Envia as paredes de UM mapa para um único jogador (ex: recém-entrou na sala).</summary>
    public void EnviarParedesParaJogador(byte[] dadosComprimidos, SteamId alvo) =>
        StartCoroutine(CorotinaEnvioFicheiro(dadosComprimidos, "paredes.json.gz", alvo, tokenId: string.Empty,
            geracaoMapa: 0, fichaKey: string.Empty, ownerSteamId: string.Empty, ehParedes: true));

    private IEnumerator CorotinaEnvioFicheiro(byte[] dados, string nomeArquivo, SteamId? alvo, string tokenId,
        long geracaoMapa, string fichaKey, string ownerSteamId, bool ehParedes = false)
    {
        if (dados == null || dados.Length == 0) yield break;

        string fileId      = Guid.NewGuid().ToString("N");
        int    totalChunks = Mathf.CeilToInt(dados.Length / (float)TAMANHO_CHUNK);

        // 1. Avisa os receptores para prepararem o buffer
        var msgInicio = new NetMessage
        {
            tipo    = 5,
            payload = JsonUtility.ToJson(new MsgStartFileTransfer
            {
                fileId       = fileId,
                fileName     = nomeArquivo,
                totalChunks  = totalChunks,
                totalSize    = dados.Length,
                tokenId      = tokenId,
                geracaoMapa  = geracaoMapa,
                fichaKey     = fichaKey,
                ownerSteamId = ownerSteamId,
                ehParedes    = ehParedes,
            })
        };
        if (alvo.HasValue) EnviarParaJogador(alvo.Value, msgInicio);
        else               EnviarParaTodos(msgInicio);

        Debug.Log($"[NetworkSync] Iniciando envio de '{nomeArquivo}' ({totalChunks} chunks).");
        yield return null;

        // 2. Envia cada chunk com uma pequena pausa para não congestionar o buffer Steam
        for (int i = 0; i < totalChunks; i++)
        {
            int    offset = i * TAMANHO_CHUNK;
            int    count  = Math.Min(TAMANHO_CHUNK, dados.Length - offset);
            byte[] chunk  = new byte[count];
            Buffer.BlockCopy(dados, offset, chunk, 0, count);

            var msgChunk = new NetMessage
            {
                tipo    = 6,
                payload = JsonUtility.ToJson(new MsgFileChunk
                {
                    fileId      = fileId,
                    chunkIndex  = i,
                    chunkDataB64 = Convert.ToBase64String(chunk)
                })
            };
            if (alvo.HasValue) EnviarParaJogador(alvo.Value, msgChunk);
            else               EnviarParaTodos(msgChunk);

            // Cede o frame a cada 10 chunks para não congelar o Mestre
            if (i % 10 == 9) yield return null;
            else              yield return new WaitForSeconds(0.02f);
        }

        Debug.Log($"[NetworkSync] Envio de '{nomeArquivo}' concluído.");
    }

    // ── API Pública — Mensagens de Jogo ─────────────────────────────────────

    /// <summary>Chamado por MapTokenDraggable ao soltar o token.</summary>
    public void NotificarMovimentoToken(string tokenId, Vector3 pos)
    {
        EnviarParaTodos(new NetMessage
        {
            tipo    = 1,
            payload = JsonUtility.ToJson(new MsgMoverToken { tokenId = tokenId, x = pos.x, y = pos.y, z = pos.z })
        });
    }

    /// <summary>Chamado por MapTokenDraggable ao começar a arrastar (mouse down no token). Também
    /// invoca localmente OnTokenSeguradoRecebido pra quem iniciou o arrasto ver o próprio label.</summary>
    public void NotificarInicioArrasto(string tokenId)
    {
        string meuNome = ObterMeuNomeExibicao();
        EnviarParaTodos(new NetMessage
        {
            tipo    = 8,
            payload = JsonUtility.ToJson(new MsgTokenSegurado { tokenId = tokenId, nomeJogador = meuNome })
        });
        OnTokenSeguradoRecebido?.Invoke(tokenId, meuNome, true);
    }

    /// <summary>Chamado por MapTokenDraggable ao soltar o token, ao lado de NotificarMovimentoToken.</summary>
    public void NotificarFimArrasto(string tokenId)
    {
        string meuNome = ObterMeuNomeExibicao();
        EnviarParaTodos(new NetMessage
        {
            tipo    = 9,
            payload = JsonUtility.ToJson(new MsgTokenSegurado { tokenId = tokenId, nomeJogador = meuNome })
        });
        OnTokenSeguradoRecebido?.Invoke(tokenId, meuNome, false);
    }

    private static string ObterMeuNomeExibicao()
    {
        try
        {
            if (SteamClient.IsValid) return SteamClient.Name;
        }
        catch { }
        return "Jogador";
    }

    /// <summary>Chamado por MapManager.RemoverTokenDoMapa — remove só a instância do mapa em todos
    /// os clientes; catálogo de tokens e a ficha/monstro linkado não são afetados.</summary>
    public void NotificarRemoverToken(string tokenId)
    {
        EnviarParaTodos(new NetMessage
        {
            tipo    = 7,
            payload = JsonUtility.ToJson(new MsgRemoverToken { tokenId = tokenId })
        });
    }

    /// <summary>Chamado por MapTokenDraggable.AplicarDano após aplicar o dano/cura localmente —
    /// propaga a vida atual (valor absoluto, não delta) pros demais peers.</summary>
    public void NotificarDanoToken(string tokenId, int novaVidaAtual)
    {
        EnviarParaTodos(new NetMessage
        {
            tipo    = 13,
            payload = JsonUtility.ToJson(new MsgDanoToken { tokenId = tokenId, novaVidaAtual = novaVidaAtual })
        });
    }

    /// <summary>Chamado por MapTokenDraggable.AlternarBloqueado/AlternarEscondido após aplicar o
    /// flag localmente — propaga os dois flags (estado completo, não só o que mudou) pros demais peers.</summary>
    public void NotificarFlagsToken(string tokenId, bool bloqueado, bool escondido)
    {
        EnviarParaTodos(new NetMessage
        {
            tipo    = 14,
            payload = JsonUtility.ToJson(new MsgFlagsToken { tokenId = tokenId, bloqueado = bloqueado, escondido = escondido })
        });
    }

    /// <summary>Chamado por MapManager.NotificarRaioVisaoAlterado (Mestre mexendo no slider
    /// individual/global de um token que pode não ser dele) — sem isso, a mudança de raio nunca
    /// chegaria na máquina do jogador dono real do token (cada cliente mantém sua própria instância
    /// local, sincronizadas só por mensagens de rede pontuais como esta).</summary>
    public void EnviarRaioVisaoTokenParaTodos(string tokenId, float raioMetros)
    {
        EnviarParaTodos(new NetMessage
        {
            tipo    = 15,
            payload = JsonUtility.ToJson(new MsgRaioVisaoToken { tokenId = tokenId, raioMetros = raioMetros })
        });
    }

    /// <summary>Chamado por SalaAbasFichasManager.AtribuirDono (Mestre-only) após aplicar a
    /// atribuição localmente — propaga pros demais peers.</summary>
    public void EnviarAtribuicaoDeDonoParaTodos(int fichasGeneralId, int fichasDndId, string donoSteamId)
    {
        EnviarParaTodos(new NetMessage
        {
            tipo    = 10,
            payload = JsonUtility.ToJson(new MsgAtribuicaoDeDono
            {
                fichasGeneralId = fichasGeneralId,
                fichasDndId     = fichasDndId,
                donoSteamId     = donoSteamId
            })
        });
    }

    /// <summary>Chamado por EfeitoClimaManager (Mestre-only) após aplicar localmente uma mudança de
    /// clima (tipo ou qualquer valor de slider) — propaga o estado completo (tipo + os dois presets)
    /// pros demais peers.</summary>
    public void EnviarClimaParaTodos(TipoEfeitoClima tipo, ClimaConfig chuva, ClimaConfig neve)
    {
        EnviarParaTodos(new NetMessage
        {
            tipo    = 11,
            payload = JsonUtility.ToJson(new MsgClima { tipo = (int)tipo, chuva = chuva, neve = neve })
        });
    }

    /// <summary>Chamado por AudioTabletopManager (Mestre-only) após aplicar localmente o volume e/ou
    /// o loop de um som ambiente — propaga o estado combinado (os dois campos, não só o que mudou)
    /// pros demais peers, mesmo motivo de EnviarClimaParaTodos mandar tipo+chuva+neve juntos.</summary>
    public void EnviarAudioParaTodos(int indice, float volume, bool loop)
    {
        EnviarParaTodos(new NetMessage
        {
            tipo    = 12,
            payload = JsonUtility.ToJson(new MsgAudioTabletop { indice = indice, volume = volume, loop = loop })
        });
    }

    /// <summary>Chamado por MapManager após upload de token via Workshop.</summary>
    public void NotificarSpawnToken(string tokenId, string nomeToken, string imagemPath, ulong workshopIdToken, Vector3 pos,
                                     int fichasGeneralId = 0, int fichasDndId = 0, string ownerSteamId = "", int ownerPlayerId = 0,
                                     string monstroId = "")
    {
        EnviarParaTodos(new NetMessage
        {
            tipo = 4,
            payload = JsonUtility.ToJson(new MsgSpawnToken
            {
                tokenId         = tokenId,
                nomeToken       = nomeToken,
                imagemPath      = imagemPath,
                workshopIdToken = workshopIdToken,
                x = pos.x, y = pos.y, z = pos.z,
                fichasGeneralId = fichasGeneralId,
                fichasDndId     = fichasDndId,
                ownerSteamId    = ownerSteamId,
                ownerPlayerId   = ownerPlayerId,
                monstroId       = monstroId
            })
        });
    }

    /// <summary>Envia tokens ao jogador recém-chegado; o mapa é enviado via P2P chunks.</summary>
    public void EnviarEstadoInicialPara(SteamId novoJogador)
    {
        if (mapManager == null) return;

        EstadoTokenNet[] tokens   = mapManager.ObterEstadoTokensParaRede();
        string           tokensJson = JsonUtility.ToJson(new EstadoTokenArray { itens = tokens });

        EnviarParaJogador(novoJogador, new NetMessage
        {
            tipo    = 3,
            payload = JsonUtility.ToJson(new MsgEstadoInicial
            {
                workshopIdMapa = 0,     // mapa chega via P2P chunks abaixo
                tokensJson     = tokensJson
            })
        });
    }

    // ── Callbacks ───────────────────────────────────────────────────────────

    private void AceitarSessaoP2P(SteamId remetente)
    {
        if (steamLobbyManager?.SalaAtual == null) return;
        foreach (Friend membro in steamLobbyManager.SalaAtual.Value.Members)
        {
            if (membro.Id == remetente)
            {
                SteamNetworking.AcceptP2PSessionWithUser(remetente);
                return;
            }
        }
    }

    private void AoNovoMembroEntrar(Lobby lobby, Friend novoMembro)
    {
        if (!SouHost()) return;
        Debug.Log($"[NetworkSync] Novo membro: {novoMembro.Name} — enviando estado completo.");
        EnviarEstadoInicialPara(novoMembro.Id);                        // estado JSON dos tokens
        mapManager?.EnviarMapaParaJogador(novoMembro.Id);              // mapa via P2P chunks
        mapManager?.EnviarImagensTokensParaJogador(novoMembro.Id);     // imagens dos tokens via P2P chunks
        mapManager?.EnviarParedesDoMapaAtivoParaJogador(novoMembro.Id); // paredes do mapa ativo via P2P chunks
    }

    private bool SouHost() =>
        steamLobbyManager?.SalaAtual?.IsOwnedBy(SteamClient.SteamId) == true;
}
