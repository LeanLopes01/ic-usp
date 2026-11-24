using System;
using System.IO;
using UnityEngine;

// Copia quinta-edicao-srd do StreamingAssets para persistentDataPath na primeira execução
// ou quando a versão do jogo muda.
// Não requer cena ou GameObject — roda antes de qualquer cena carregar.
public static class SrdDataBootstrapper
{
    private const string SrdPastaStreaming  = "quinta-edicao-srd";
    private const string SrdPastaDestino   = "sistemas/quinta-edicao-srd";
    private const string ArquivoMarcador   = ".srd_versao_instalada";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Inicializar()
    {
        try
        {
            string origem  = Path.Combine(Application.streamingAssetsPath, SrdPastaStreaming);
            string destino = Path.Combine(
                Application.persistentDataPath,
                SrdPastaDestino.Replace('/', Path.DirectorySeparatorChar));
            string marcador = Path.Combine(destino, ArquivoMarcador);

            if (JaEstaAtualizado(marcador))
                return;

            if (!Directory.Exists(origem))
            {
                VTTLog.LoadWarn($"SrdBootstrap: pasta de origem não encontrada: {origem}");
                return;
            }

            VTTLog.Load($"SrdBootstrap: instalando SRD em: {destino}");
            CopiarDiretorioRecursivo(origem, destino);
            File.WriteAllText(marcador, Application.version);
            VTTLog.Load("SrdBootstrap: SRD instalado com sucesso.");
        }
        catch (Exception e)
        {
            VTTLog.LoadError($"SrdBootstrap: falha ao instalar SRD: {e.Message}");
        }
    }

    private static bool JaEstaAtualizado(string marcador)
    {
        if (!File.Exists(marcador))
            return false;

        string versaoInstalada = File.ReadAllText(marcador).Trim();
        return string.Equals(versaoInstalada, Application.version, StringComparison.Ordinal);
    }

    private static void CopiarDiretorioRecursivo(string origem, string destino)
    {
        Directory.CreateDirectory(destino);

        foreach (string arquivo in Directory.GetFiles(origem))
        {
            // Ignora .meta (gerados pelo Unity no Editor, não existem no build)
            if (arquivo.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            string nomeArquivo = Path.GetFileName(arquivo);
            string caminhoDestino = Path.Combine(destino, nomeArquivo);
            File.Copy(arquivo, caminhoDestino, overwrite: true);
        }

        foreach (string subDir in Directory.GetDirectories(origem))
        {
            string nomeSub = Path.GetFileName(subDir);
            CopiarDiretorioRecursivo(subDir, Path.Combine(destino, nomeSub));
        }
    }
}
