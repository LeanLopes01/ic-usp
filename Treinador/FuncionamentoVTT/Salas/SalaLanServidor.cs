using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Servidor TCP no PC do Mestre para o(s) celular(es) companheiro(s) — substitui o ServidorFichaTCP
/// antigo (1 cliente, unidirecional, sem framing). Aceita MÚLTIPLOS celulares simultâneos, cada um
/// com o próprio nome de jogador (handshake ENTRAR), e fala nos dois sentidos usando o protocolo
/// framed de SalaLanProtocolo. Quem interpreta cada tipo de mensagem e decide o que fazer é o
/// SalaMobileBridge — este script só cuida da conexão/framing/roteamento por cliente.
/// </summary>
public class SalaLanServidor : MonoBehaviour
{
    [SerializeField] private int porta = 8080;

    private TcpListener servidor;
    private bool rodando;

    // ConcurrentDictionary porque a leitura de cada cliente roda em Task própria (thread pool),
    // enquanto Update()/scripts do jogo (main thread) podem estar iterando/mandando pra todos.
    private readonly ConcurrentDictionary<TcpClient, string> jogadoresConectados = new ConcurrentDictionary<TcpClient, string>();

    // Serializa as escritas POR CLIENTE — sem isso, dois EnviarPara() concorrentes no mesmo socket
    // (ex: ESTADO_INICIAL saindo ao mesmo tempo que um TOKEN_MOVIDO de broadcast) intercalam bytes
    // no meio de um framing [tamanho][corpo] e corrompem a mensagem pro celular, derrubando a conexão.
    private readonly ConcurrentDictionary<TcpClient, SemaphoreSlim> travasEscrita = new ConcurrentDictionary<TcpClient, SemaphoreSlim>();

    // Fila de eventos recebidos nas Tasks de leitura (threads do pool) — drenada em Update() (main
    // thread) antes de disparar os eventos C#, porque quase tudo que os listeners vão fazer
    // (MapManager, ChatManager/NGO, etc.) só é seguro chamar na main thread do Unity.
    private readonly ConcurrentQueue<Action> eventosPendentes = new ConcurrentQueue<Action>();

    public bool Rodando => rodando;
    public int TotalConectados => jogadoresConectados.Count;

    /// <summary>Novo celular entrou (após handshake ENTRAR) — (cliente, nomeEscolhido).</summary>
    public event Action<TcpClient, string> OnJogadorEntrou;
    public event Action<TcpClient> OnJogadorSaiu;
    public event Action<TcpClient, string> OnChatRecebido; // (cliente, texto)
    public event Action<TcpClient, string, string> OnPedidoRolagem; // (cliente, notacao, contexto)
    public event Action<TcpClient, string> OnPedidoStatBlock; // (cliente, tokenId)

    public string NomeDoJogador(TcpClient cliente) =>
        jogadoresConectados.TryGetValue(cliente, out string nome) ? nome : "Jogador";

    public void IniciarServidor()
    {
        if (rodando) return;

        try
        {
            servidor = new TcpListener(IPAddress.Any, porta);
            servidor.Start();
            rodando = true;
            Debug.Log($"[SalaLanServidor] Escutando na porta {porta}. IP local: {ServidorFichaTCP.ObterIPLocal()}");
            _ = AceitarConexoesLoop();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SalaLanServidor] Falha ao iniciar: {e.Message}");
            rodando = false;
        }
    }

    public void PararServidor()
    {
        if (!rodando) return;
        rodando = false;

        foreach (TcpClient cliente in jogadoresConectados.Keys)
            try { cliente.Close(); } catch { }
        jogadoresConectados.Clear();

        try { servidor?.Stop(); } catch { }
        servidor = null;

        Debug.Log("[SalaLanServidor] Servidor parado.");
    }

    private async Task AceitarConexoesLoop()
    {
        while (rodando)
        {
            TcpClient cliente;
            try
            {
                cliente = await servidor.AcceptTcpClientAsync();
            }
            catch (ObjectDisposedException) { break; }
            catch (Exception e)
            {
                if (rodando) Debug.LogWarning($"[SalaLanServidor] Erro ao aceitar conexão: {e.Message}");
                continue;
            }

            _ = AtenderClienteLoop(cliente);
        }
    }

    private async Task AtenderClienteLoop(TcpClient cliente)
    {
        NetworkStream stream = cliente.GetStream();
        string nomeJogador = null;
        travasEscrita[cliente] = new SemaphoreSlim(1, 1);

        try
        {
            while (rodando && cliente.Connected)
            {
                LanMsg msg = await SalaLanFraming.LerAsync(stream);
                if (msg == null) break; // conexão encerrada pelo celular

                switch (msg.tipo)
                {
                    case LanMsgTipo.Entrar:
                        var entrar = JsonUtility.FromJson<LanEntrarPayload>(msg.payloadJson);
                        nomeJogador = string.IsNullOrWhiteSpace(entrar?.nome) ? "Jogador (mobile)" : entrar.nome.Trim();
                        jogadoresConectados[cliente] = nomeJogador;
                        string nomeCapturado = nomeJogador;
                        eventosPendentes.Enqueue(() => OnJogadorEntrou?.Invoke(cliente, nomeCapturado));
                        break;

                    case LanMsgTipo.ChatEnviar:
                        var chat = JsonUtility.FromJson<LanChatEnviarPayload>(msg.payloadJson);
                        if (!string.IsNullOrWhiteSpace(chat?.texto))
                            eventosPendentes.Enqueue(() => OnChatRecebido?.Invoke(cliente, chat.texto));
                        break;

                    case LanMsgTipo.RolarDados:
                        var rolar = JsonUtility.FromJson<LanRolarPayload>(msg.payloadJson);
                        if (!string.IsNullOrWhiteSpace(rolar?.notacao))
                            eventosPendentes.Enqueue(() => OnPedidoRolagem?.Invoke(cliente, rolar.notacao, rolar.contexto));
                        break;

                    case LanMsgTipo.PedirStatBlock:
                        var pedido = JsonUtility.FromJson<LanPedirStatBlockPayload>(msg.payloadJson);
                        if (!string.IsNullOrWhiteSpace(pedido?.tokenId))
                            eventosPendentes.Enqueue(() => OnPedidoStatBlock?.Invoke(cliente, pedido.tokenId));
                        break;

                    default:
                        Debug.LogWarning($"[SalaLanServidor] Tipo de mensagem não tratado do celular: {msg.tipo}");
                        break;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SalaLanServidor] Conexão com '{nomeJogador ?? "celular"}' encerrada: {e.Message}");
        }
        finally
        {
            jogadoresConectados.TryRemove(cliente, out _);
            if (travasEscrita.TryRemove(cliente, out SemaphoreSlim trava)) trava.Dispose();
            try { cliente.Close(); } catch { }
            eventosPendentes.Enqueue(() => OnJogadorSaiu?.Invoke(cliente));
        }
    }

    private void Update()
    {
        // Drena os eventos coletados pelas Tasks de leitura (outras threads) na main thread, onde é
        // seguro mexer em MapManager/ChatManager/etc.
        while (eventosPendentes.TryDequeue(out Action acao))
        {
            try { acao(); }
            catch (Exception e) { Debug.LogError($"[SalaLanServidor] Erro processando evento: {e.Message}"); }
        }
    }

    /// <summary>Manda uma mensagem framed pra UM celular específico. Serializado por cliente (ver
    /// travasEscrita) — nunca deixa duas mensagens saírem intercaladas no mesmo socket.</summary>
    public async void EnviarPara(TcpClient cliente, string tipo, object payload)
    {
        if (cliente == null || !cliente.Connected) return;
        if (!travasEscrita.TryGetValue(cliente, out SemaphoreSlim trava)) return; // já desconectou

        var msg = new LanMsg { tipo = tipo, payloadJson = payload != null ? JsonUtility.ToJson(payload) : string.Empty };

        try
        {
            await trava.WaitAsync();
            try
            {
                await SalaLanFraming.EscreverAsync(cliente.GetStream(), msg);
            }
            finally
            {
                trava.Release();
            }
        }
        catch (ObjectDisposedException)
        {
            // Cliente desconectou entre o TryGetValue e o WaitAsync/Release — sem problema, a
            // mensagem só não chega a tempo de qualquer forma.
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SalaLanServidor] Falha ao enviar '{tipo}' pro celular: {e.Message}");
        }
    }

    /// <summary>Manda a mesma mensagem pra TODOS os celulares conectados.</summary>
    public void EnviarParaTodos(string tipo, object payload)
    {
        foreach (TcpClient cliente in jogadoresConectados.Keys)
            EnviarPara(cliente, tipo, payload);
    }

    private void OnDisable() => PararServidor();
    private void OnApplicationQuit() => PararServidor();
}
