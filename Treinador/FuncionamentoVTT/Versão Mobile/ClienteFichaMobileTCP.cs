using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Cliente TCP do celular: conecta no PC do Mestre (IP descoberto por BuscadorDeSalaLocal
/// ou digitado manualmente) e fica esperando no Stream. Sem polling — o PC só envia
/// dados quando a ficha realmente muda, então o celular não gasta CPU/bateria à toa.
/// </summary>
public class ClienteFichaMobileTCP : MonoBehaviour
{
    [SerializeField] private int porta = 8080;
    [SerializeField] private int tamanhoBuffer = 8192;

    /// <summary>Disparado a cada JSON de ficha recebido do PC.</summary>
    public event Action<string> OnFichaRecebida;

    /// <summary>Disparado quando a conexão com o PC é perdida ou falha.</summary>
    public event Action<string> OnDesconectado;

    private TcpClient tcpClient;
    private bool conectado;

    public bool Conectado => conectado;

    public async void ConectarNaMesa(string ipDoMestre)
    {
        if (conectado) return;

        try
        {
            tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(ipDoMestre, porta);
            conectado = true;

            Debug.Log($"[ClienteFichaMobileTCP] Conectado em {ipDoMestre}:{porta}. Aguardando dados da ficha...");
            _ = LerFichaContinuamente();
        }
        catch (Exception e)
        {
            conectado = false;
            Debug.LogWarning($"[ClienteFichaMobileTCP] Falha ao conectar: {e.Message}");
            OnDesconectado?.Invoke(e.Message);
        }
    }

    private async Task LerFichaContinuamente()
    {
        NetworkStream stream = tcpClient.GetStream();
        byte[] buffer = new byte[tamanhoBuffer];

        try
        {
            while (conectado && tcpClient.Connected)
            {
                // Fica parado aqui sem gastar CPU até o PC enviar algo pelo socket.
                int bytesLidos = await stream.ReadAsync(buffer, 0, buffer.Length);

                if (bytesLidos == 0)
                {
                    // Conexão encerrada pelo PC.
                    break;
                }

                string jsonRecebido = Encoding.UTF8.GetString(buffer, 0, bytesLidos);
                Debug.Log("[ClienteFichaMobileTCP] Ficha atualizada recebida do Mestre.");
                OnFichaRecebida?.Invoke(jsonRecebido);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ClienteFichaMobileTCP] Conexão perdida: {e.Message}");
        }
        finally
        {
            conectado = false;
            OnDesconectado?.Invoke("Conexão com o Mestre encerrada.");
        }
    }

    public void Desconectar()
    {
        conectado = false;
        try { tcpClient?.Close(); } catch { }
        tcpClient = null;
    }

    private void OnDisable() => Desconectar();
    private void OnApplicationQuit() => Desconectar();
}
