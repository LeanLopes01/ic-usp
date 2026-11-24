using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Persistência local dos mapas de uma campanha — mesmo padrão de CampanhasLocalStorage.cs, mas
/// escopado por campanha: campanhas/{campanhaId}/mapas/mapas_salvos.json + imagens/.
/// </summary>
public static class CampanhaMapasStorage
{
    private const string FolderName = "mapas";
    private const string FileName = "mapas_salvos.json";
    private const string ImagesFolderName = "imagens";
    private const string EstadoFileName = "estado_mapa.json";

    public static string BasePath(string campanhaId) => Path.Combine(CampanhasLocalStorage.CampaignFolderPath(campanhaId), FolderName);
    public static string FilePath(string campanhaId) => Path.Combine(BasePath(campanhaId), FileName);
    public static string ImagesPath(string campanhaId) => Path.Combine(BasePath(campanhaId), ImagesFolderName);
    private static string EstadoFilePath(string campanhaId) => Path.Combine(BasePath(campanhaId), EstadoFileName);

    public static void SaveOrUpdate(string campanhaId, CampanhaMapaData mapa)
    {
        if (mapa == null) return;
        EnsureDirectory(campanhaId);

        if (string.IsNullOrWhiteSpace(mapa.mapaId))
            mapa.mapaId = Guid.NewGuid().ToString("N");

        mapa.atualizadoEmUtc = DateTime.UtcNow.ToString("o");

        var lista = new List<CampanhaMapaData>(LoadAll(campanhaId));
        int index = lista.FindIndex(m => m != null && string.Equals(m.mapaId, mapa.mapaId, StringComparison.Ordinal));

        if (index >= 0 && !string.IsNullOrWhiteSpace(lista[index].criadoEmUtc))
            mapa.criadoEmUtc = lista[index].criadoEmUtc;
        else if (string.IsNullOrWhiteSpace(mapa.criadoEmUtc))
            mapa.criadoEmUtc = mapa.atualizadoEmUtc;

        if (index >= 0) lista[index] = mapa;
        else lista.Add(mapa);

        SaveAll(campanhaId, lista.ToArray());
    }

    public static string EnsureImagePersisted(string campanhaId, string imagePath, string idHint = null)
    {
        EnsureDirectory(campanhaId);
        string hint = string.IsNullOrWhiteSpace(idHint) ? Guid.NewGuid().ToString("N") : idHint;
        return CampanhasLocalStorage.PersistImageIfPossible(imagePath, ImagesPath(campanhaId), hint);
    }

    public static CampanhaMapaData[] LoadAll(string campanhaId)
    {
        if (string.IsNullOrWhiteSpace(campanhaId)) return Array.Empty<CampanhaMapaData>();

        EnsureDirectory(campanhaId);
        string path = FilePath(campanhaId);
        if (!File.Exists(path)) return Array.Empty<CampanhaMapaData>();

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return Array.Empty<CampanhaMapaData>();

            var collection = JsonUtility.FromJson<CampanhaMapaDataCollection>(json);
            CampanhaMapaData[] mapas = collection?.mapas ?? Array.Empty<CampanhaMapaData>();

            // JsonUtility.FromJson não lança em vários casos de JSON malformado — só devolve campos
            // default silenciosamente. Se o arquivo tem conteúdo mas vira 0 mapas, é sinal de
            // corrupção (ex: um float NaN/Infinity gravado por engano quebra o parser) — logamos
            // ALTO em vez de deixar isso passar batido como "campanha sem mapas".
            if (mapas.Length == 0 && json.Length > 20)
                VTTLog.ForjaError($"[CampanhaMapasStorage] '{path}' tem conteúdo ({json.Length} chars) mas não " +
                    "produziu nenhum mapa ao desserializar — arquivo provavelmente corrompido.");

            return mapas;
        }
        catch (Exception e)
        {
            VTTLog.ForjaError($"[CampanhaMapasStorage] Falha ao carregar mapas de '{path}': {e.Message}");
            return Array.Empty<CampanhaMapaData>();
        }
    }

    private static void SaveAll(string campanhaId, CampanhaMapaData[] mapas)
    {
        string path = FilePath(campanhaId);
        try
        {
            mapas ??= Array.Empty<CampanhaMapaData>();
            var collection = new CampanhaMapaDataCollection { mapas = mapas };
            string json = JsonUtility.ToJson(collection, true);

            if (string.IsNullOrWhiteSpace(json) || json.Length < 10)
            {
                VTTLog.ForjaError($"[CampanhaMapasStorage] JsonUtility.ToJson devolveu conteúdo vazio/suspeito " +
                    $"({mapas.Length} mapa(s)) — abortando gravação em '{path}' pra não sobrescrever o arquivo bom com lixo.");
                return;
            }

            File.WriteAllText(path, json);

            // Confirmação de que o dado realmente foi pro disco — inclui a contagem total de paredes
            // salvas, pra facilitar notar na hora se um mapa vetorizado ficou de fora.
            int totalParedes = 0;
            foreach (CampanhaMapaData m in mapas)
                if (m?.paredes != null) totalParedes += m.paredes.Count;

            VTTLog.Forja($"[CampanhaMapasStorage] {mapas.Length} mapa(s) salvos em '{path}' " +
                $"({json.Length} chars, {totalParedes} trecho(s) de parede no total).");
        }
        catch (Exception e)
        {
            VTTLog.ForjaError($"[CampanhaMapasStorage] Falha ao salvar mapas em '{path}': {e.Message}");
        }
    }

    private static void EnsureDirectory(string campanhaId)
    {
        if (!Directory.Exists(BasePath(campanhaId))) Directory.CreateDirectory(BasePath(campanhaId));
        if (!Directory.Exists(ImagesPath(campanhaId))) Directory.CreateDirectory(ImagesPath(campanhaId));
    }

    // ── Última cena ativa ────────────────────────────────────────────────────
    //
    // Guarda só o mapaId da última cena carregada em CampanhaMapManager.CarregarMapa — ao reabrir a
    // campanha, CampanhaMapManager.CarregarUltimoMapaAtivo() lê isso e recarrega a mesma cena
    // automaticamente, sem precisar que o usuário clique em "Carregar" de novo na galeria.

    [Serializable]
    private class EstadoMapaAtivo
    {
        public string mapaIdAtivo;
    }

    public static void SalvarMapaAtivo(string campanhaId, string mapaId)
    {
        if (string.IsNullOrWhiteSpace(campanhaId) || string.IsNullOrWhiteSpace(mapaId)) return;

        try
        {
            EnsureDirectory(campanhaId);
            var estado = new EstadoMapaAtivo { mapaIdAtivo = mapaId };
            File.WriteAllText(EstadoFilePath(campanhaId), JsonUtility.ToJson(estado, true));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CampanhaMapasStorage] Falha ao salvar a cena ativa: {e.Message}");
        }
    }

    /// <summary>Vazio se a campanha nunca teve uma cena carregada (ou o arquivo não existir/estiver corrompido).</summary>
    public static string CarregarMapaAtivoId(string campanhaId)
    {
        if (string.IsNullOrWhiteSpace(campanhaId)) return string.Empty;

        string path = EstadoFilePath(campanhaId);
        if (!File.Exists(path)) return string.Empty;

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return string.Empty;

            var estado = JsonUtility.FromJson<EstadoMapaAtivo>(json);
            return estado?.mapaIdAtivo ?? string.Empty;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[CampanhaMapasStorage] Falha ao carregar a cena ativa: {e.Message}");
            return string.Empty;
        }
    }
}
