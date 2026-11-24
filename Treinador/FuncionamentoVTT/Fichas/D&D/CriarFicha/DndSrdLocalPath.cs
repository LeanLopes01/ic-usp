using System;
using System.IO;
using UnityEngine;

public static class DndSrdLocalPath
{
    private const string LegacyPrefix  = "Scripts/Fichas/D&D/Dados/";
    private const string SrdRoot       = "sistemas/quinta-edicao-srd";
    private const string NewPrefix     = SrdRoot + "/";
    private const string SrdStreaming  = "quinta-edicao-srd";

    // Retorna o caminho completo para um arquivo/pasta do SRD.
    // Prioridade: persistentDataPath (instalado pelo bootstrapper) → StreamingAssets (fallback).
    public static string Resolver(string relativePath)
    {
        string subPath = NormalizarSubPath(relativePath);

        string persistentPath = Path.Combine(
            Application.persistentDataPath,
            SrdRoot.Replace('/', Path.DirectorySeparatorChar),
            subPath.Replace('/', Path.DirectorySeparatorChar));

        // Caminho já instalado no persistentDataPath — retorna diretamente.
        // Para diretórios, exige que haja ao menos um arquivo para não bloquear
        // o fallback quando a pasta existe mas está vazia (criada pelo bootstrapper).
        if (File.Exists(persistentPath))
            return persistentPath;
        if (Directory.Exists(persistentPath) && Directory.GetFiles(persistentPath, "*", SearchOption.AllDirectories).Length > 0)
            return persistentPath;

        // Fallback: lê do StreamingAssets (Editor ou falha na cópia)
        string streamingPath = Path.Combine(
            Application.streamingAssetsPath,
            SrdStreaming,
            subPath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(streamingPath) || Directory.Exists(streamingPath))
            return streamingPath;

        // Retorna o caminho persistente como padrão (mesmo que não exista ainda)
        return persistentPath;
    }

    // Retorna a pasta raiz do SRD (persistente se existir, streaming como fallback).
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

        // Converte caminho legado
        if (normalized.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase))
            normalized = NewPrefix + normalized.Substring(LegacyPrefix.Length);

        // Remove prefixo "sistemas/quinta-edicao-srd/" se presente (já está na base)
        if (normalized.StartsWith(NewPrefix, StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(NewPrefix.Length).TrimStart('/');

        if (normalized.StartsWith(SrdRoot, StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(SrdRoot.Length).TrimStart('/');

        return normalized;
    }
}