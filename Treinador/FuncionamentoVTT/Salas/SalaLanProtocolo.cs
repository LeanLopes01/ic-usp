using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

/// <summary>
/// Protocolo de sala pela rede local (celular↔PC do Mestre, sem Steam) — usado por
/// SalaLanServidor (PC) e SalaLanCliente (celular). Substitui o ServidorFichaTCP/
/// ClienteFichaMobileTCP antigos (que só mandavam um JSON cru, sem framing, PC→celular).
///
/// Framing: cada mensagem no socket é [4 bytes big-endian = tamanho][N bytes UTF-8 do JSON de
/// LanMsg] — sem isso, várias mensagens saindo/chegando perto uma da outra colariam ou cortariam
/// no meio (TCP é stream, não é "uma escrita = uma leitura").
/// </summary>
[Serializable]
public class LanMsg
{
    public string tipo;
    public string payloadJson;
}

// Tipos de LanMsg.tipo (string, não int — mais fácil de ler no log de rede durante o teste):
public static class LanMsgTipo
{
    public const string Entrar           = "ENTRAR";            // celular -> PC
    public const string EntrouOk         = "ENTROU_OK";         // PC -> celular
    public const string MapaInicial      = "MAPA_INICIAL";      // PC -> celular (payload = LanMapaInicialPayload)
    public const string EstadoInicial    = "ESTADO_INICIAL";    // PC -> celular (payload = EstadoTokenArray)
    public const string TokenMovido      = "TOKEN_MOVIDO";      // PC -> celular (payload = MsgMoverToken)
    public const string TokenSegurado    = "TOKEN_SEGURADO";    // PC -> celular (payload = MsgTokenSegurado)
    public const string TokenSolto       = "TOKEN_SOLTO";       // PC -> celular (payload = MsgTokenSegurado)
    public const string ChatEnviar       = "CHAT_ENVIAR";       // celular -> PC (payload = LanChatEnviarPayload)
    public const string ChatRecebido     = "CHAT_RECEBIDO";     // PC -> celular (payload = LanChatRecebidoPayload)
    public const string RolarDados       = "ROLAR_DADOS";       // celular -> PC (payload = LanRolarPayload)
    public const string RolagemResultado = "ROLAGEM_RESULTADO"; // PC -> celular (payload = LanRolagemResultadoPayload)
    public const string PedirStatBlock   = "PEDIR_STATBLOCK";   // celular -> PC (payload = LanPedirStatBlockPayload)
    public const string StatBlockDados   = "STATBLOCK_DADOS";   // PC -> celular (payload = TokenStatBlockData)
    public const string Erro             = "ERRO";              // PC -> celular (payload = LanErroPayload)
}

[Serializable] public class LanEntrarPayload         { public string nome; }
// JsonUtility serializa byte[] automaticamente como string base64 — sem conversão manual.
[Serializable] public class LanMapaInicialPayload    { public byte[] imagemBytes; }
[Serializable] public class LanChatEnviarPayload      { public string texto; }
[Serializable] public class LanChatRecebidoPayload    { public string remetente; public string texto; public string hora; }
[Serializable] public class LanRolarPayload           { public string notacao; public string contexto; }
[Serializable] public class LanRolagemResultadoPayload{ public string notacao; public string contexto; public int total; public string detalhe; }
[Serializable] public class LanPedirStatBlockPayload  { public string tokenId; }
[Serializable] public class LanErroPayload            { public string mensagem; }

/// <summary>Leitura/escrita framed compartilhada pelo servidor (PC) e cliente (celular).</summary>
public static class SalaLanFraming
{
    public static async Task EscreverAsync(NetworkStream stream, LanMsg msg)
    {
        string json = UnityEngine.JsonUtility.ToJson(msg);
        byte[] corpo = Encoding.UTF8.GetBytes(json);
        byte[] tamanho = BitConverter.GetBytes(corpo.Length);
        if (BitConverter.IsLittleEndian) Array.Reverse(tamanho);

        await stream.WriteAsync(tamanho, 0, 4);
        await stream.WriteAsync(corpo, 0, corpo.Length);
    }

    /// <summary>Lê UMA mensagem completa do stream (bloqueia até chegar). Retorna null se a conexão
    /// foi encerrada (0 bytes lidos no cabeçalho).</summary>
    public static async Task<LanMsg> LerAsync(NetworkStream stream)
    {
        byte[] cabecalho = await LerExatamenteAsync(stream, 4);
        if (cabecalho == null) return null;

        if (BitConverter.IsLittleEndian) Array.Reverse(cabecalho);
        int tamanho = BitConverter.ToInt32(cabecalho, 0);
        // Teto generoso o bastante pra caber a imagem do mapa em base64 (~1.37x do tamanho original;
        // MapManager já limita o mapa em si a 25 MB pro canal P2P, ver EnviarFicheiroPesado) — serve
        // só de guarda contra corpo absurdo por stream corrompido, não como limite de uso normal.
        if (tamanho <= 0 || tamanho > 48 * 1024 * 1024)
            throw new IOException($"Tamanho de mensagem inválido: {tamanho}");

        byte[] corpo = await LerExatamenteAsync(stream, tamanho);
        if (corpo == null) return null;

        string json = Encoding.UTF8.GetString(corpo);
        return UnityEngine.JsonUtility.FromJson<LanMsg>(json);
    }

    private static async Task<byte[]> LerExatamenteAsync(NetworkStream stream, int quantidade)
    {
        byte[] buffer = new byte[quantidade];
        int lidos = 0;
        while (lidos < quantidade)
        {
            int n = await stream.ReadAsync(buffer, lidos, quantidade - lidos);
            if (n == 0) return null; // conexão encerrada pelo outro lado
            lidos += n;
        }
        return buffer;
    }
}
