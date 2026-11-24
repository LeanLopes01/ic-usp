using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class CampanhaLocalSnapshot
{
    public string campanhaId;
    public string nome;
    public string sistema;
    public string descricao;
    public string imagemPath;
    public string criadoEmUtc;
    public string atualizadoEmUtc;
}

[Serializable]
public class CampanhaLocalSnapshotCollection
{
    public CampanhaLocalSnapshot[] campanhas = Array.Empty<CampanhaLocalSnapshot>();
}

/// <summary>
/// Persistência local das campanhas — mesmo padrão de Salas/SalaLocalStorage.cs,
/// mas sem nenhuma integração de rede/Steam.
/// </summary>
public static class CampanhasLocalStorage
{
    private const string FolderName = "campanhas";
    private const string FileName = "campanhas_salvas.json";
    private const string ImagesFolderName = "imagens";
    private const string NpcsFolderName = "npcs";

    public static string BasePath => Path.Combine(Application.persistentDataPath, FolderName);
    public static string FilePath => Path.Combine(BasePath, FileName);
    public static string ImagesPath => Path.Combine(BasePath, ImagesFolderName);

    public static string CampaignFolderPath(string campanhaId) => Path.Combine(BasePath, campanhaId);
    public static string NpcsFolderPath(string campanhaId) => Path.Combine(CampaignFolderPath(campanhaId), NpcsFolderName);
    public static string NpcsImagesPath(string campanhaId) => Path.Combine(NpcsFolderPath(campanhaId), ImagesFolderName);

    public static void SaveOrUpdate(CampanhaLocalSnapshot snapshot)
    {
        if (snapshot == null) return;

        EnsureDirectory();

        if (string.IsNullOrWhiteSpace(snapshot.campanhaId))
            snapshot.campanhaId = Guid.NewGuid().ToString("N");

        snapshot.atualizadoEmUtc = DateTime.UtcNow.ToString("o");

        var lista = new List<CampanhaLocalSnapshot>(LoadAll());
        int index = lista.FindIndex(c => c != null && EqualsOrdinal(c.campanhaId, snapshot.campanhaId));

        if (index >= 0 && !string.IsNullOrWhiteSpace(lista[index].criadoEmUtc))
            snapshot.criadoEmUtc = lista[index].criadoEmUtc;
        else if (string.IsNullOrWhiteSpace(snapshot.criadoEmUtc))
            snapshot.criadoEmUtc = snapshot.atualizadoEmUtc;

        if (index >= 0)
            lista[index] = snapshot;
        else
            lista.Add(snapshot);

        SaveAll(lista.ToArray());
    }

    public static string EnsureImagePersisted(string imagePath, string idHint = null)
    {
        EnsureDirectory();
        return PersistImageIfPossible(imagePath, ImagesPath, string.IsNullOrWhiteSpace(idHint) ? Guid.NewGuid().ToString("N") : idHint);
    }

    public static CampanhaLocalSnapshot[] LoadAll()
    {
        EnsureDirectory();

        if (!File.Exists(FilePath))
            return Array.Empty<CampanhaLocalSnapshot>();

        try
        {
            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<CampanhaLocalSnapshot>();

            var collection = JsonUtility.FromJson<CampanhaLocalSnapshotCollection>(json);
            return collection?.campanhas ?? Array.Empty<CampanhaLocalSnapshot>();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CampanhasLocalStorage] Falha ao carregar campanhas locais: {e.Message}");
            return Array.Empty<CampanhaLocalSnapshot>();
        }
    }

    /// <summary>Copia um arquivo de imagem para dentro de uma pasta de destino, nomeado pelo hint.</summary>
    public static string PersistImageIfPossible(string originalImagePath, string destinoFolder, string idHint)
    {
        if (string.IsNullOrWhiteSpace(originalImagePath))
            return string.Empty;

        string source = originalImagePath.Trim();
        if (!File.Exists(source))
        {
            Debug.LogWarning($"[CampanhasLocalStorage] Arquivo de imagem não encontrado para persistir: '{source}'");
            return source;
        }

        try
        {
            Directory.CreateDirectory(destinoFolder);

            string sourceFullPath = Path.GetFullPath(source);
            string extensao = Path.GetExtension(sourceFullPath);
            if (string.IsNullOrWhiteSpace(extensao))
                extensao = ".png";

            string safeId = SanitizeFileName(idHint);
            string destino = Path.Combine(destinoFolder, $"{safeId}{extensao}");
            string destinoFullPath = Path.GetFullPath(destino);

            if (string.Equals(sourceFullPath, destinoFullPath, StringComparison.OrdinalIgnoreCase))
                return destinoFullPath;

            File.Copy(sourceFullPath, destinoFullPath, true);
            return destinoFullPath;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CampanhasLocalStorage] Falha ao persistir imagem: {e.Message}");
            return source;
        }
    }

    private static void SaveAll(CampanhaLocalSnapshot[] campanhas)
    {
        try
        {
            var collection = new CampanhaLocalSnapshotCollection { campanhas = campanhas ?? Array.Empty<CampanhaLocalSnapshot>() };
            File.WriteAllText(FilePath, JsonUtility.ToJson(collection, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CampanhasLocalStorage] Falha ao salvar campanhas locais: {e.Message}");
        }
    }

    private static void EnsureDirectory()
    {
        if (!Directory.Exists(BasePath)) Directory.CreateDirectory(BasePath);
        if (!Directory.Exists(ImagesPath)) Directory.CreateDirectory(ImagesPath);
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Guid.NewGuid().ToString("N");

        char[] invalidChars = Path.GetInvalidFileNameChars();
        var chars = value.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (Array.IndexOf(invalidChars, chars[i]) >= 0)
                chars[i] = '_';

        string result = new string(chars);
        return string.IsNullOrWhiteSpace(result) ? Guid.NewGuid().ToString("N") : result;
    }

    private static bool EqualsOrdinal(string a, string b)
        => string.Equals(a ?? string.Empty, b ?? string.Empty, StringComparison.Ordinal);
}
