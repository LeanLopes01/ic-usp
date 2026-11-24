using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Cliente TCP do celular pro protocolo de sala (ver SalaLanProtocolo/SalaLanServidor) — substitui o
/// ClienteFichaMobileTCP antigo (unidirecional, só ficha). Conecta no PC do Mestre (IP achado por
/// BuscadorDeSalaLocal), manda ENTRAR com o nome do jogador, e depois fala nos dois sentidos:
/// recebe estado do tabuleiro/chat/rolagens/stat block, e manda chat/rolagem/pedido de stat block.
/// </summary>
public class SalaLanCliente : MonoBehaviour
{
    [SerializeField] private int porta = 8080;

    private TcpClient tcpClient;
    private NetworkStream stream;
    private bool conectado;

    // Serializa as escritas (ver mesmo raciocínio em SalaLanServidor.travasEscrita) — aqui é menos
    // crítico (as chamadas vêm de cliques de UI, tipicamente sequenciais), mas dois toques rápidos
    // (ex: pedir stat block de dois tokens seguidos) já bastam pra correr o risco.
    private readonly SemaphoreSlim travaEscrita = new SemaphoreSlim(1, 1);

    public bool Conectado => conectado;

    public event Action OnConectado;
    public event Action<string> OnDesconectado; // motivo

    public event Action<byte[]> OnMapaInicial;
    public event Action<EstadoTokenArray> OnEstadoInicial;
    public event Action<MsgMoverToken> OnTokenMovido;
    public event Action<MsgTokenSegurado> OnTokenSegurado;
    public event Action<MsgTokenSegurado> OnTokenSolto;
    public event Action<LanChatRecebidoPayload> OnChatRecebido;
    public event Action<LanRolagemResultadoPayload> OnRolagemResultado;
    public event Action<TokenStatBlockData> OnStatBlockRecebido;
    public event Action<string> OnErro;

    public async void ConectarNaMesa(string ipDoMestre, string nomeJogador)
    {
        if (conectado) return;

        try
        {
            tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(ipDoMestre, porta);
            stream = tcpClient.GetStream();
            conectado = true;

            Debug.Log($"[SalaLanCliente] Conectado em {ipDoMestre}:{porta}. Entrando como '{nomeJogador}'...");

            await SalaLanFraming.EscreverAsync(stream, new LanMsg
            {
                tipo = LanMsgTipo.Entrar,
                payloadJson = JsonUtility.ToJson(new LanEntrarPayload { nome = nomeJogador })
            });

            OnConectado?.Invoke();
            _ = LerContinuamente();
        }
        catch (Exception e)
        {
            conectado = false;
            Debug.LogWarning($"[SalaLanCliente] Falha ao conectar: {e.Message}");
            OnDesconectado?.Invoke(e.Message);
        }
    }

    private async Task LerContinuamente()
    {
        try
        {
            while (conectado && tcpClient.Connected)
            {
                LanMsg msg = await SalaLanFraming.LerAsync(stream);
                if (msg == null) break; // PC encerrou a conexão

                DespacharMensagem(msg);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SalaLanCliente] Conexão perdida: {e.Message}");
        }
        finally
        {
            conectado = false;
            OnDesconectado?.Invoke("Conexão com o Mestre encerrada.");
        }
    }

    private void DespacharMensagem(LanMsg msg)
    {
        switch (msg.tipo)
        {
            case LanMsgTipo.MapaInicial:
                var mapa = JsonUtility.FromJson<LanMapaInicialPayload>(msg.payloadJson);
                if (mapa?.imagemBytes != null && mapa.imagemBytes.Length > 0) OnMapaInicial?.Invoke(mapa.imagemBytes);
                break;
            case LanMsgTipo.EstadoInicial:
                OnEstadoInicial?.Invoke(JsonUtility.FromJson<EstadoTokenArray>(msg.payloadJson));
                break;
            case LanMsgTipo.TokenMovido:
                OnTokenMovido?.Invoke(JsonUtility.FromJson<MsgMoverToken>(msg.payloadJson));
                break;
            case LanMsgTipo.TokenSegurado:
                OnTokenSegurado?.Invoke(JsonUtility.FromJson<MsgTokenSegurado>(msg.payloadJson));
                break;
            case LanMsgTipo.TokenSolto:
                OnTokenSolto?.Invoke(JsonUtility.FromJson<MsgTokenSegurado>(msg.payloadJson));
                break;
            case LanMsgTipo.ChatRecebido:
                OnChatRecebido?.Invoke(JsonUtility.FromJson<LanChatRecebidoPayload>(msg.payloadJson));
                break;
            case LanMsgTipo.RolagemResultado:
                OnRolagemResultado?.Invoke(JsonUtility.FromJson<LanRolagemResultadoPayload>(msg.payloadJson));
                break;
            case LanMsgTipo.StatBlockDados:
                OnStatBlockRecebido?.Invoke(JsonUtility.FromJson<TokenStatBlockData>(msg.payloadJson));
                break;
            case LanMsgTipo.Erro:
                OnErro?.Invoke(JsonUtility.FromJson<LanErroPayload>(msg.payloadJson)?.mensagem ?? "Erro desconhecido.");
                break;
            default:
                Debug.LogWarning($"[SalaLanCliente] Tipo de mensagem não tratado do PC: {msg.tipo}");
                break;
        }
    }

    public void EnviarChat(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return;
        _ = EnviarAsync(LanMsgTipo.ChatEnviar, new LanChatEnviarPayload { texto = texto.Trim() });
    }

    public void PedirRolagem(string notacao, string contexto = null)
    {
        if (string.IsNullOrWhiteSpace(notacao)) return;
        _ = EnviarAsync(LanMsgTipo.RolarDados, new LanRolarPayload { notacao = notacao.Trim(), contexto = contexto });
    }

    public void PedirStatBlock(string tokenId)
    {
        if (string.IsNullOrWhiteSpace(tokenId)) return;
        _ = EnviarAsync(LanMsgTipo.PedirStatBlock, new LanPedirStatBlockPayload { tokenId = tokenId });
    }

    private async Task EnviarAsync(string tipo, object payload)
    {
        if (!conectado || stream == null) return;
        try
        {
            await travaEscrita.WaitAsync();
            try
            {
                await SalaLanFraming.EscreverAsync(stream, new LanMsg { tipo = tipo, payloadJson = JsonUtility.ToJson(payload) });
            }
            finally
            {
                travaEscrita.Release();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SalaLanCliente] Falha ao enviar '{tipo}': {e.Message}");
        }
    }

    public void Desconectar()
    {
        conectado = false;
        try { tcpClient?.Close(); } catch { }
        tcpClient = null;
        stream = null;
    }

    private void OnDisable() => Desconectar();
    private void OnApplicationQuit() => Desconectar();
}
