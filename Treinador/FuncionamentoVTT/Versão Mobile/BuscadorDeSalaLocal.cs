using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Escuta o broadcast UDP disparado por AnunciadorDeSalaLocal (no PC do Mestre) para
/// descobrir o IP da mesa automaticamente — sem o jogador precisar digitar o IP.
/// </summary>
public class BuscadorDeSalaLocal : MonoBehaviour
{
    [Header("Configuração")]
    [SerializeField] private int portaBroadcast = 9090;
    [SerializeField] private float timeoutSegundos = 8f;

    /// <summary>Disparado quando uma sala é encontrada: (ip do PC, nome da sala anunciada).</summary>
    public event Action<string, string> OnSalaEncontrada;

    /// <summary>Disparado se o tempo de busca esgotar sem encontrar nenhuma sala.</summary>
    public event Action OnBuscaExpirou;

    private UdpClient udpClient;
    private bool buscando;

    public bool Buscando => buscando;

    public async void ProcurarSalaLocal()
    {
        if (buscando) return;
        buscando = true;

        try
        {
            udpClient = new UdpClient(portaBroadcast);
            Debug.Log("[BuscadorDeSalaLocal] Procurando sala na rede local...");

            Task<UdpReceiveResult> tarefaRecebimento = udpClient.ReceiveAsync();
            Task tarefaTimeout = Task.Delay(TimeSpan.FromSeconds(timeoutSegundos));

            Task concluida = await Task.WhenAny(tarefaRecebimento, tarefaTimeout);

            if (concluida == tarefaRecebimento && tarefaRecebimento.IsCompletedSuccessfully)
            {
                string mensagem = Encoding.UTF8.GetString(tarefaRecebimento.Result.Buffer);
                if (mensagem.StartsWith("DICEVTT_SALA"))
                {
                    string ipDoMestre = tarefaRecebimento.Result.RemoteEndPoint.Address.ToString();
                    string[] partes = mensagem.Split('|');
                    string nomeSala = partes.Length > 1 ? partes[1] : "Sala DiceVTT";

                    Debug.Log($"[BuscadorDeSalaLocal] Sala '{nomeSala}' encontrada em {ipDoMestre}.");
                    OnSalaEncontrada?.Invoke(ipDoMestre, nomeSala);
                }
                else
                {
                    Debug.LogWarning("[BuscadorDeSalaLocal] Pacote recebido não reconhecido, ignorando.");
                    OnBuscaExpirou?.Invoke();
                }
            }
            else
            {
                Debug.Log("[BuscadorDeSalaLocal] Busca expirou sem encontrar nenhuma sala.");
                OnBuscaExpirou?.Invoke();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[BuscadorDeSalaLocal] Falha na busca: {e.Message}");
            OnBuscaExpirou?.Invoke();
        }
        finally
        {
            try { udpClient?.Close(); } catch { }
            udpClient = null;
            buscando = false;
        }
    }

    public void CancelarBusca()
    {
        try { udpClient?.Close(); } catch { }
        udpClient = null;
        buscando = false;
    }

    private void OnDisable() => CancelarBusca();
}
