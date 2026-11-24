using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// Anuncia a existência da sala na rede local via UDP broadcast, repetindo o pacote em
/// intervalos regulares. Permite que o app mobile descubra o IP do PC automaticamente
/// (sem digitar IP, sem depender da Steam) através de BuscadorDeSalaLocal no celular.
/// </summary>
public class AnunciadorDeSalaLocal : MonoBehaviour
{
    [Header("Configuração")]
    [SerializeField] private int portaBroadcast = 9090;
    [SerializeField] private float intervaloSegundos = 2f;
    [SerializeField] private string nomeSalaPadrao = "Campanha DiceVTT";

    private UdpClient udpClient;
    private float timer;
    private bool anunciando;
    private string nomeSalaAtual;

    public bool Anunciando => anunciando;

    /// <summary>Inicia o broadcast periódico. Pode receber o nome real da sala aberta.</summary>
    public void IniciarAnuncio(string nomeSala = null)
    {
        if (anunciando) return;

        nomeSalaAtual = string.IsNullOrWhiteSpace(nomeSala) ? nomeSalaPadrao : nomeSala;

        try
        {
            udpClient = new UdpClient();
            udpClient.EnableBroadcast = true;
            anunciando = true;
            timer = intervaloSegundos; // dispara o primeiro pacote já no próximo Update

            Debug.Log($"[AnunciadorDeSalaLocal] Anunciando '{nomeSalaAtual}' na porta {portaBroadcast} a cada {intervaloSegundos}s.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[AnunciadorDeSalaLocal] Falha ao iniciar broadcast: {e.Message}");
            anunciando = false;
        }
    }

    public void PararAnuncio()
    {
        if (!anunciando) return;

        anunciando = false;
        try { udpClient?.Close(); } catch { }
        udpClient = null;

        Debug.Log("[AnunciadorDeSalaLocal] Anúncio parado.");
    }

    private void Update()
    {
        if (!anunciando) return;

        timer += Time.deltaTime;
        if (timer < intervaloSegundos) return;
        timer = 0f;

        EnviarPacote();
    }

    private void EnviarPacote()
    {
        try
        {
            string mensagem = $"DICEVTT_SALA|{nomeSalaAtual}";
            byte[] bytes = Encoding.UTF8.GetBytes(mensagem);
            var endPoint = new IPEndPoint(IPAddress.Broadcast, portaBroadcast);
            udpClient.Send(bytes, bytes.Length, endPoint);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AnunciadorDeSalaLocal] Falha ao enviar pacote de broadcast: {e.Message}");
        }
    }

    private void OnDisable() => PararAnuncio();
    private void OnApplicationQuit() => PararAnuncio();
}
