using System;
using System.IO;
using UnityEngine;

public static class SteamAppIdGenerator
{
    private const string APP_ID   = "480";
    private const string FILENAME = "steam_appid.txt";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnsureSteamAppIdFile()
    {
#if !UNITY_EDITOR
        try
        {
            // Em builds standalone, dataPath aponta para <exe_folder>/<GameName>_Data
            string exeFolder = Path.GetDirectoryName(Application.dataPath);
            if (string.IsNullOrWhiteSpace(exeFolder))
                exeFolder = AppDomain.CurrentDomain.BaseDirectory;

            string filePath = Path.Combine(exeFolder, FILENAME);
            if (!File.Exists(filePath))
            {
                File.WriteAllText(filePath, APP_ID);
                Debug.Log($"[SteamAppId] {FILENAME} criado em: {filePath}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[SteamAppId] Falha ao criar {FILENAME}: {e.Message}");
        }
#endif
    }
}
