using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class SalaLocalSnapshot
{
    public string salaLocalId;
    public string salaSteamId;
    public string nomeSala;
    public string sistemaSala;
    public string descricaoSala;
    public string imagemSalaPath;
    public string mestreNome;
    public string mestreSteamId;
    public string participantesNomesCsv;
    public string participantesSteamIdsCsv;
    public int totalMembros;
    public int maxMembros;
    public string meuSteamId;
    public string meuPapel;
    public bool usuarioParticipa;
    public string ultimaAtualizacaoUtc;
    public string criadoEmUtc;
}

[Serializable]
public class SalaLocalSnapshotCollection
{
    public SalaLocalSnapshot[] salas = Array.Empty<SalaLocalSnapshot>();
}

public static class SalaLocalStorage
{
    private const string FolderName = "salas";
    private const string FileName = "salas_salvas.json";
    private const string ImagesFolderName = "imagens";

    public static string BasePath => Path.Combine(Application.persistentDataPath, FolderName);
    public static string FilePath => Path.Combine(BasePath, FileName);
    public static string ImagesPath => Path.Combine(BasePath, ImagesFolderName);

    public static void SaveOrUpdate(SalaLocalSnapshot snapshot)
    {
        if (snapshot == null)
            return;

        EnsureDirectory();

        if (string.IsNullOrWhiteSpace(snapshot.salaLocalId))
            snapshot.salaLocalId = Guid.NewGuid().ToString("N");

        snapshot.ultimaAtualizacaoUtc = DateTime.UtcNow.ToString("o");

        var lista = new List<SalaLocalSnapshot>(LoadAll());
        int index = FindIndex(lista, snapshot);

        if (index >= 0 && lista[index] != null && string.IsNullOrWhiteSpace(snapshot.imagemSalaPath))
            snapshot.imagemSalaPath = lista[index].imagemSalaPath;

        // Preserva a data de criação original da sala; só define agora se for realmente nova.
        if (index >= 0 && lista[index] != null && !string.IsNullOrWhiteSpace(lista[index].criadoEmUtc))
            snapshot.criadoEmUtc = lista[index].criadoEmUtc;
        else if (string.IsNullOrWhiteSpace(snapshot.criadoEmUtc))
            snapshot.criadoEmUtc = snapshot.ultimaAtualizacaoUtc;

        string imagemAntesPersistencia = snapshot.imagemSalaPath;

        // Usa salaLocalId (GUID estável) como nome de ficheiro da capa;
        // evita criar um novo ficheiro a cada sessão Steam quando salaSteamId muda.
        snapshot.imagemSalaPath = PersistImageIfPossible(
            snapshot.imagemSalaPath,
            !string.IsNullOrWhiteSpace(snapshot.salaLocalId) ? snapshot.salaLocalId : snapshot.salaSteamId);

        Debug.Log($"[SalaLocalStorage] SaveOrUpdate salaSteamId='{snapshot.salaSteamId}' nome='{snapshot.nomeSala}' imagemAntes='{imagemAntesPersistencia}' imagemFinal='{snapshot.imagemSalaPath}'");

        if (index >= 0)
            lista[index] = snapshot;
        else
            lista.Add(snapshot);

        SaveAll(lista.ToArray());
    }

    public static string EnsureImagePersisted(string imagePath, string salaIdHint = null)
    {
        EnsureDirectory();

        string hint = string.IsNullOrWhiteSpace(salaIdHint)
            ? Guid.NewGuid().ToString("N")
            : salaIdHint;

        string persisted = PersistImageIfPossible(imagePath, hint);
        Debug.Log($"[SalaLocalStorage] EnsureImagePersisted hint='{hint}' origem='{imagePath}' destino='{persisted}'");
        return persisted;
    }

    public static SalaLocalSnapshot[] LoadAll()
    {
        EnsureDirectory();

        if (!File.Exists(FilePath))
            return Array.Empty<SalaLocalSnapshot>();

        try
        {
            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<SalaLocalSnapshot>();

            var collection = JsonUtility.FromJson<SalaLocalSnapshotCollection>(json);
            if (collection == null || collection.salas == null)
                return Array.Empty<SalaLocalSnapshot>();

            return collection.salas;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SalaLocalStorage] Falha ao carregar salas locais: {e.Message}");
            return Array.Empty<SalaLocalSnapshot>();
        }
    }

    public static SalaLocalSnapshot[] LoadForUser(string steamId)
    {
        string normalizedSteamId = NormalizeSteamId(steamId);
        SalaLocalSnapshot[] all = LoadAll();
        var result = new List<SalaLocalSnapshot>();

        for (int i = 0; i < all.Length; i++)
        {
            SalaLocalSnapshot item = all[i];
            if (item == null)
                continue;

            bool mine = item.usuarioParticipa
                || EqualsOrdinal(item.meuSteamId, normalizedSteamId)
                || EqualsOrdinal(item.mestreSteamId, normalizedSteamId)
                || CsvContains(item.participantesSteamIdsCsv, normalizedSteamId);

            if (mine)
                result.Add(item);
        }

        return result.ToArray();
    }

    private static int FindIndex(List<SalaLocalSnapshot> lista, SalaLocalSnapshot alvo)
    {
        for (int i = 0; i < lista.Count; i++)
        {
            SalaLocalSnapshot atual = lista[i];
            if (atual == null)
                continue;

            bool sameSteamLobby = !string.IsNullOrWhiteSpace(alvo.salaSteamId)
                && EqualsOrdinal(atual.salaSteamId, alvo.salaSteamId);

            bool sameLocalId = !string.IsNullOrWhiteSpace(alvo.salaLocalId)
                && EqualsOrdinal(atual.salaLocalId, alvo.salaLocalId);

            if (sameSteamLobby || sameLocalId)
                return i;
        }

        return -1;
    }

    private static void SaveAll(SalaLocalSnapshot[] salas)
    {
        try
        {
            var collection = new SalaLocalSnapshotCollection
            {
                salas = salas ?? Array.Empty<SalaLocalSnapshot>()
            };

            string json = JsonUtility.ToJson(collection, true);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SalaLocalStorage] Falha ao salvar salas locais: {e.Message}");
        }
    }

    private static void EnsureDirectory()
    {
        if (!Directory.Exists(BasePath))
            Directory.CreateDirectory(BasePath);

        if (!Directory.Exists(ImagesPath))
            Directory.CreateDirectory(ImagesPath);
    }

    private static string PersistImageIfPossible(string originalImagePath, string salaId)
    {
        if (string.IsNullOrWhiteSpace(originalImagePath))
            return string.Empty;

        string source = originalImagePath.Trim();
        if (!File.Exists(source))
        {
            Debug.LogWarning($"[SalaLocalStorage] Arquivo de imagem nao encontrado para persistir: '{source}'");
            return source;
        }

        try
        {
            string sourceFullPath = Path.GetFullPath(source);

            string extensao = Path.GetExtension(sourceFullPath);
            if (string.IsNullOrWhiteSpace(extensao))
                extensao = ".png";

            string safeId = string.IsNullOrWhiteSpace(salaId) ? Guid.NewGuid().ToString("N") : SanitizeFileName(salaId);
            string destino = Path.Combine(ImagesPath, $"{safeId}{extensao}");
            string destinoFullPath = Path.GetFullPath(destino);

            if (string.Equals(sourceFullPath, destinoFullPath, StringComparison.OrdinalIgnoreCase))
            {
                Debug.Log($"[SalaLocalStorage] Imagem ja esta no destino final: '{destinoFullPath}'");
                return destinoFullPath;
            }

            File.Copy(sourceFullPath, destinoFullPath, true);
            Debug.Log($"[SalaLocalStorage] Imagem copiada origem='{sourceFullPath}' destino='{destinoFullPath}'");
            return destinoFullPath;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SalaLocalStorage] Falha ao persistir imagem da sala: {e.Message}");
            return source;
        }
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Guid.NewGuid().ToString("N");

        char[] invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Trim().ToCharArray();

        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalidChars, chars[i]) >= 0)
                chars[i] = '_';
        }

        string result = new string(chars);
        return string.IsNullOrWhiteSpace(result) ? Guid.NewGuid().ToString("N") : result;
    }

    private static string NormalizeSteamId(string steamId)
    {
        return string.IsNullOrWhiteSpace(steamId) ? "editor" : steamId.Trim();
    }

    private static bool CsvContains(string csv, string value)
    {
        if (string.IsNullOrWhiteSpace(csv) || string.IsNullOrWhiteSpace(value))
            return false;

        string[] parts = csv.Split(',');
        for (int i = 0; i < parts.Length; i++)
        {
            if (EqualsOrdinal(parts[i].Trim(), value))
                return true;
        }

        return false;
    }

    private static bool EqualsOrdinal(string a, string b)
    {
        return string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
    }
}
