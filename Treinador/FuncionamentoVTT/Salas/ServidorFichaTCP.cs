using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Servidor TCP persistente no PC do Mestre: aceita a conexão do celular e empurra o
/// JSON da ficha só quando ela muda (chamando EnviarFichaAtualizada). Sem polling —
/// o celular fica esperando no Stream sem gastar CPU/bateria até o PC mandar algo.
/// Suporta um único celular conectado por vez nesta versão de teste.
/// </summary>
public class ServidorFichaTCP : MonoBehaviour
{
    [SerializeField] private int porta = 8080;

    private TcpListener servidor;
    private TcpClient clienteAtual;
    private bool rodando;

    public bool Rodando => rodando;
    public bool TemClienteConectado => clienteAtual != null && clienteAtual.Connected;

    public void IniciarServidor()
    {
        if (rodando) return;

        try
        {
            servidor = new TcpListener(IPAddress.Any, porta);
            servidor.Start();
            rodando = true;
            Debug.Log($"[ServidorFichaTCP] Escutando na porta {porta}. IP local: {ObterIPLocal()}");
            _ = AceitarConexoesLoop();
        }
        catch (Exception e)
        {
            Debug.LogError($"[ServidorFichaTCP] Falha ao iniciar servidor: {e.Message}");
            rodando = false;
        }
    }

    public void PararServidor()
    {
        if (!rodando) return;
        rodando = false;

        try { clienteAtual?.Close(); } catch { }
        clienteAtual = null;

        try { servidor?.Stop(); } catch { }
        servidor = null;

        Debug.Log("[ServidorFichaTCP] Servidor parado.");
    }

    private async Task AceitarConexoesLoop()
    {
        while (rodando)
        {
            try
            {
                TcpClient novoCliente = await servidor.AcceptTcpClientAsync();

                // Só um celular por vez nesta versão de teste: substitui a conexão anterior.
                try { clienteAtual?.Close(); } catch { }
                clienteAtual = novoCliente;

                Debug.Log("[ServidorFichaTCP] Celular conectado na mesa.");
            }
            catch (ObjectDisposedException)
            {
                break; // servidor foi parado por PararServidor()
            }
            catch (Exception e)
            {
                if (rodando) Debug.LogWarning($"[ServidorFichaTCP] Erro ao aceitar conexão: {e.Message}");
            }
        }
    }

    /// <summary>Chame sempre que a ficha for salva/modificada no PC para empurrar o novo estado.</summary>
    public void EnviarFichaAtualizada(string novoJson)
    {
        if (!TemClienteConectado) return;

        try
        {
            byte[] dados = Encoding.UTF8.GetBytes(novoJson);
            NetworkStream stream = clienteAtual.GetStream();
            stream.Write(dados, 0, dados.Length);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ServidorFichaTCP] Falha ao enviar ficha atualizada: {e.Message}");
        }
    }

    public static string ObterIPLocal()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                    return ip.ToString();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ServidorFichaTCP] Falha ao obter IP local: {e.Message}");
        }
        return "IP não encontrado";
    }

    private void OnDisable() => PararServidor();
    private void OnApplicationQuit() => PararServidor();
}
