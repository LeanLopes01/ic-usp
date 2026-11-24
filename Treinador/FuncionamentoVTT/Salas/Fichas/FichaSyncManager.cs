using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Steamworks;
using UnityEngine;

/// <summary>
/// Sincroniza o CONTEÚDO COMPLETO de fichas entre jogadores via P2P (NetworkSyncManager), separado
/// do sync de metadados (nome/nível/raça/classe) que já viaja via Steam Lobby Data.
/// Cache é só em memória (perdido ao sair da sala) — nunca grava em LocalFichaStorage, que é
/// exclusivamente o storage local de CADA jogador.
/// Sync acontece em 2 checkpoints apenas (não em tempo real a cada edição): ao abrir/criar uma aba
/// (SalaAbasFichasManager.CriarOuSelecionarAbaDaFicha) e quando um jogador novo entra na sala.
/// </summary>
public class FichaSyncManager : MonoBehaviour
{
    [SerializeField] private SteamLobbyManager steamLobbyManager;

    private readonly Dictionary<string, LocalFichaData> cacheRemoto = new Dictionary<string, LocalFichaData>();

    /// <summary>Disparado quando o conteúdo de uma ficha remota chega/atualiza (ownerSteamId, fichasDndId).</summary>
    public event Action<string, int> OnFichaRemotaAtualizada;

    private void Awake()
    {
        if (steamLobbyManager == null)
            steamLobbyManager = FindFirstObjectByType<SteamLobbyManager>();
    }

    private static string ChaveCache(string ownerSteamId, int fichasDndId) => $"{ownerSteamId}_{fichasDndId}";

    /// <summary>Chave usada no protocolo P2P (MsgStartFileTransfer.fichaKey) — mesma convenção do cache.</summary>
    public static string ConstruirFichaKey(string ownerSteamId, int fichasDndId) => ChaveCache(ownerSteamId, fichasDndId);

    public bool TryObterFichaRemota(string ownerSteamId, int fichasDndId, out LocalFichaData ficha) =>
        cacheRemoto.TryGetValue(ChaveCache(ownerSteamId, fichasDndId), out ficha);

    /// <summary>Chamado por NetworkSyncManager quando o payload comprimido de uma ficha termina de chegar.</summary>
    public void ReceberFichaConcluida(string fichaKey, string ownerSteamId, byte[] dadosComprimidos)
    {
        LocalFichaData ficha;
        try
        {
            string json = Descomprimir(dadosComprimidos);
            ficha = JsonUtility.FromJson<LocalFichaData>(json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FichaSync] Falha ao decodificar ficha recebida ({fichaKey}): {e.Message}");
            return;
        }

        if (ficha == null || ficha.fichas_dnd_id <= 0) return;

        cacheRemoto[ChaveCache(ownerSteamId, ficha.fichas_dnd_id)] = ficha;
        Debug.Log($"[FichaSync] Ficha remota '{ficha.name_pc}' (dono {ownerSteamId}) armazenada em cache.");
        OnFichaRemotaAtualizada?.Invoke(ownerSteamId, ficha.fichas_dnd_id);
    }

    /// <summary>Serializa e comprime a MINHA ficha e transmite para TODOS na sala.</summary>
    public void TransmitirFichaPropria(int fichasGeneralId, int fichasDndId)
    {
        if (!TryResolverFichaPropria(fichasGeneralId, fichasDndId, out LocalFichaData snapshot, out string meuSteamId))
            return;

        byte[] comprimido = ComprimirFicha(snapshot);
        string fichaKey = ChaveCache(meuSteamId, fichasDndId);
        NetworkSyncManager.Instancia?.EnviarFichaParaTodos(fichaKey, meuSteamId, comprimido);
    }

    /// <summary>Serializa e comprime a MINHA ficha e transmite para um único jogador (ex: recém-entrou).</summary>
    public void TransmitirFichaParaJogador(int fichasGeneralId, int fichasDndId, SteamId alvo)
    {
        if (!TryResolverFichaPropria(fichasGeneralId, fichasDndId, out LocalFichaData snapshot, out string meuSteamId))
            return;

        byte[] comprimido = ComprimirFicha(snapshot);
        string fichaKey = ChaveCache(meuSteamId, fichasDndId);
        NetworkSyncManager.Instancia?.EnviarFichaParaJogador(fichaKey, meuSteamId, comprimido, alvo);
    }

    private static bool TryResolverFichaPropria(int fichasGeneralId, int fichasDndId, out LocalFichaData snapshot, out string meuSteamId)
    {
        snapshot = null;
        meuSteamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;

        if (!LocalFichaStorage.TryGetFichaSnapshot(meuSteamId, playerId, fichasGeneralId, fichasDndId, out snapshot))
        {
            Debug.LogWarning($"[FichaSync] Ficha {fichasDndId} não encontrada no storage local — não transmitida.");
            return false;
        }
        return true;
    }

    private static byte[] ComprimirFicha(LocalFichaData ficha)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonUtility.ToJson(ficha));
        using var saida = new MemoryStream();
        using (var gzip = new GZipStream(saida, CompressionMode.Compress, leaveOpen: true))
            gzip.Write(jsonBytes, 0, jsonBytes.Length);
        return saida.ToArray();
    }

    private static string Descomprimir(byte[] dadosComprimidos)
    {
        using var entrada = new MemoryStream(dadosComprimidos);
        using var gzip = new GZipStream(entrada, CompressionMode.Decompress);
        using var saida = new MemoryStream();
        gzip.CopyTo(saida);
        return Encoding.UTF8.GetString(saida.ToArray());
    }
}
