using System;
using System.IO;
using UnityEngine;

public static class Pf2SrdLocalPath
{
    private const string SrdRoot      = "sistemas/pathfinder-2e-srd";
    private const string NewPrefix    = SrdRoot + "/";
    private const string SrdStreaming = "pathfinder-2e-srd";

    // Retorna o caminho completo para um arquivo/pasta do SRD PF2.
    // Prioridade: persistentDataPath (instalado pelo bootstrapper) → StreamingAssets (fallback).
    public static string Resolver(string relativePath)
    {
        string subPath = NormalizarSubPath(relativePath);

        string persistentPath = Path.Combine(
            Application.persistentDataPath,
            SrdRoot.Replace('/', Path.DirectorySeparatorChar),
            subPath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(persistentPath))
            return persistentPath;
        if (Directory.Exists(persistentPath) && Directory.GetFiles(persistentPath, "*", SearchOption.AllDirectories).Length > 0)
            return persistentPath;

        string streamingPath = Path.Combine(
            Application.streamingAssetsPath,
            SrdStreaming,
            subPath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(streamingPath) || Directory.Exists(streamingPath))
            return streamingPath;

        return persistentPath;
    }

    // Retorna a pasta raiz do SRD PF2.
    public static string ResolverRaiz()
    {
        string persistentRaiz = Path.Combine(
            Application.persistentDataPath,
            SrdRoot.Replace('/', Path.DirectorySeparatorChar));

        if (Directory.Exists(persistentRaiz))
            return persistentRaiz;

        return Path.Combine(Application.streamingAssetsPath, SrdStreaming);
    }

    private static string NormalizarSubPath(string relativePath)
    {
        string normalized = string.IsNullOrWhiteSpace(relativePath)
            ? string.Empty
            : relativePath.Replace('\\', '/').Trim().TrimStart('/');

        if (normalized.StartsWith(NewPrefix, StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(NewPrefix.Length).TrimStart('/');

        if (normalized.StartsWith(SrdRoot, StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(SrdRoot.Length).TrimStart('/');

        return normalized;
    }
}
