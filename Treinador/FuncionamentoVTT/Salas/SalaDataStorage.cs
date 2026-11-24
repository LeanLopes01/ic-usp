using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

// ---------------------------------------------------------------------------
// Modelos de dados VTT salvos por sala
// ---------------------------------------------------------------------------

[Serializable]
public class MapSaveData
{
    // Id estável (GUID) — vazio em mapas salvos antes desse campo existir; MapManager.
    // CarregarEstadoVTT gera e persiste um id nesse caso (self-healing, ver comentário lá).
    public string mapaId;
    public string nome;
    public string imagemPath;
    public int gridColunas = 20;
    public int gridLinhas = 20;
    public float unidadesPorCelula = 1.5f;
    public float escalaVisual = 1f;
    public bool ehAtivo;

    // Paredes vetoriais (mesmo modelo de Campanhas/Grid/CampanhaMapaData.cs — WallLine é livre de
    // grid, coordenadas normalizadas 0..1 relativas à imagem). Vazio/vetorizado=false em mapas
    // criados antes desse campo existir ou que nunca passaram por Campanhas.
    public List<WallLine> paredes = new List<WallLine>();
    public bool vetorizado;

    // Retângulos de fog manual (ver Salas/Grid/FogRect.cs) — presos a células do grid, não à imagem
    // do mapa. Vazio em mapas salvos antes desse campo existir.
    public List<FogRect> fogRects = new List<FogRect>();

    // Configuração de iluminação dinâmica deste mapa (ver MapConfig.alphaMaximoVisaoDinamica e
    // vizinhos) — ferramenta do Mestre. Defaults idênticos aos de MapConfig/MapManager.cs, pra mapas
    // salvos antes desses campos existirem se comportarem como antes.
    public float alphaMaximoVisaoDinamica = 0.85f;
    public float transicaoGradienteEmCelulas = 2f;
    public float alphaVisaoGeralTokens = 0.6f;
    public float alphaFogParaHost = 0.5f;
    public int raiosBaseVisao = 48;

    // Filtro de cor de luz sobre a área visível (ver MapManager.corLuzVisao/LuzVisivel). Color.white
    // é o sentinel de "sem filtro" — tem que ser o default aqui, senão mapas salvos antes deste
    // campo existir desserializariam com Color default (0,0,0,0) e ganhariam um tingimento não pedido.
    public Color corLuzVisao = Color.white;

    // Fontes de luz estática (tocha — ver Salas/Grid/LightSource.cs) — posição normalizada, não
    // presa a célula de grid. Vazio em mapas salvos antes desse campo existir.
    public List<LightSource> fontesDeLuz = new List<LightSource>();

    // Tokens posicionados NESTE mapa (ver MapConfig.tokensNoMapa) — substitui SalaData.tokensNoMapa
    // (agora só mantido pra migração de saves antigos, ver MapManager.CarregarEstadoVTT).
    public List<TokenInstanceSaveData> tokensNoMapa = new List<TokenInstanceSaveData>();
}

[Serializable]
public class TokenCatalogSaveData
{
    public string nome;
    public string imagemPath;
    // Link com a ficha de origem (0 = token sem ficha linkada, ex: monstro genérico)
    public int fichasGeneralId;
    public int fichasDndId;
    public string ownerSteamId;
    public int ownerPlayerId;
    // Link com o monstro de origem (catálogo local) — mutuamente exclusivo com o link de ficha
    public string monstroId;
    // Link com um NPC de Campanha enviado pra esta sala (CampanhaParaSalaConverter) — mutuamente
    // exclusivo com fichasDndId/monstroId. Vazio = token comum, sem stat block/lore associado.
    public string npcIdCampanha;
}

[Serializable]
public class TokenInstanceSaveData
{
    public string nomeToken;
    public string imagemPath;
    public string tokenId; // ID estável para sincronização P2P entre sessões
    public float posX;
    public float posY;
    public float posZ;
    // Link com a ficha de origem (0 = token sem ficha linkada)
    public int fichasGeneralId;
    public int fichasDndId;
    public string ownerSteamId;
    public int ownerPlayerId;
    // Link com o monstro de origem (catálogo local) — mutuamente exclusivo com o link de ficha
    public string monstroId;
    // Link com um NPC de Campanha enviado pra esta sala (CampanhaParaSalaConverter) — mutuamente
    // exclusivo com fichasDndId/monstroId. Vazio = token comum. Usado por
    // MapManager.MostrarLoreDoNpc pra achar o EntryData salvo em SalaDataStorage.NpcsCampanhaFolderPath.
    public string npcIdCampanha;
    // Raio de visão (metros) pra visão dinâmica — mesma convenção de Campanhas (0 nos saves antigos,
    // cai no padrão RaioVisaoPadraoMetros do MapManager).
    public float raioVisaoMetros;
    // Vida atual (dano/cura aplicado — ver MapTokenDraggable.AplicarDano). -1 = ausente/save antigo,
    // MapTokenDraggable.Setup cai pra vida cheia da ficha/monstro vinculado (JsonUtility preserva
    // este valor default quando a chave não existe no JSON salvo, não zera).
    public int vidaAtual = -1;
    // Lock (impede arrasto) e Esconder (some pra jogadores comuns, Mestre continua vendo) — ver
    // content de ferramentas em Salas/Grid/TokenButtonUI.cs.
    public bool bloqueado;
    public bool escondido;
}

// ---------------------------------------------------------------------------

[Serializable]
public class FichaImportadaSaveData
{
    public int    fichas_general_id;
    public int    fichas_dnd_id;
    public string name_pc;
    public int    level_pc;
    public string race_pc;
    public string class_pc;
    public string tokenPath;      // caminho absoluto para o token da ficha
    public string owner_steam_id; // vazio = ficha própria (saves antigos, tratado como "minha" ao restaurar)
}

[Serializable]
public class SalaData
{
    public string salaLocalId;
    public string salaSteamId;
    public string nomeSala;
    public string ultimaAtualizacaoUtc;

    // Estado VTT
    public float multiplicadorGrid = 2f;
    public MapSaveData[]           mapas           = Array.Empty<MapSaveData>();
    public TokenCatalogSaveData[]  tokensCatalogo  = Array.Empty<TokenCatalogSaveData>();
    // LEGADO — só lido uma vez, na migração (ver MapManager.CarregarEstadoVTT): antes dos tokens
    // ficarem escopados por mapa (MapSaveData.tokensNoMapa), essa era uma lista única pra sala
    // inteira, o que deixava tokens de mapas diferentes sobrepostos ao trocar de mapa. Nunca mais
    // escrito depois da migração — mantido só pra não quebrar deserialização de saves antigos.
    public TokenInstanceSaveData[] tokensNoMapa    = Array.Empty<TokenInstanceSaveData>();
    public FichaImportadaSaveData[] fichasImportadas = Array.Empty<FichaImportadaSaveData>();
}

// ---------------------------------------------------------------------------
// Estrutura de pastas (uma por sala):
//
//   Application.persistentDataPath/salas/
//     {localId}/
//       data.json
//       imagens/
//         mapa_0.png
//         token_0.png
// ---------------------------------------------------------------------------

public static class SalaDataStorage
{
    private const string DataFileName = "data.json";
    private const string ImagesFolderName = "imagens";
    private const string NpcsCampanhaFolderName = "npcs_campanha";

    public static string BasePath => Path.Combine(Application.persistentDataPath, "salas");

    // -----------------------------------------------------------------------
    // Caminhos por sala
    // -----------------------------------------------------------------------

    public static string GetRoomPath(string salaId) =>
        Path.Combine(BasePath, SanitizeId(salaId));

    public static string GetRoomDataFile(string salaId) =>
        Path.Combine(GetRoomPath(salaId), DataFileName);

    public static string GetImagesPath(string salaId) =>
        Path.Combine(GetRoomPath(salaId), ImagesFolderName);

    /// <summary>Pasta com um XML por NPC de Campanha enviado pra esta sala (mesmo formato/pipeline de
    /// CampanhasLocalStorage.NpcsFolderPath — DynamicXmlBuilder.SaveEntriesAsXml/LoadEntriesFromFolder
    /// com Dnd5eNpcFieldCatalog.AllFields), usada por MapManager.MostrarLoreDoNpc (visualização
    /// somente-leitura) e pelo NetworkSyncManager pra enviar aos outros clientes sob demanda.</summary>
    public static string NpcsCampanhaFolderPath(string salaId) =>
        Path.Combine(GetRoomPath(salaId), NpcsCampanhaFolderName);

    // -----------------------------------------------------------------------
    // CRUD
    // -----------------------------------------------------------------------

    public static void SaveSalaData(SalaData salaData)
    {
        if (salaData == null)
        {
            Debug.LogWarning("[SalaDataStorage] Dados nulos, nada foi salvo.");
            return;
        }

        if (string.IsNullOrWhiteSpace(salaData.salaLocalId))
        {
            Debug.LogWarning("[SalaDataStorage] salaLocalId vazio — dados não salvos.");
            return;
        }

        salaData.ultimaAtualizacaoUtc = DateTime.UtcNow.ToString("o");

        try
        {
            EnsureRoomDirectory(salaData.salaLocalId);
            string path = GetRoomDataFile(salaData.salaLocalId);
            File.WriteAllText(path, JsonUtility.ToJson(salaData, true));
            Debug.Log($"[SalaDataStorage] Salvo: {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SalaDataStorage] Erro ao salvar: {e.Message}");
        }
    }

    public static SalaData LoadSalaData(string salaId)
    {
        if (string.IsNullOrWhiteSpace(salaId))
            return null;

        string path = GetRoomDataFile(salaId);
        if (!File.Exists(path))
        {
            Debug.Log($"[SalaDataStorage] Arquivo não encontrado: {path}");
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            SalaData dados = JsonUtility.FromJson<SalaData>(json);
            Debug.Log($"[SalaDataStorage] Carregado: {path}");
            return dados;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SalaDataStorage] Erro ao carregar: {e.Message}");
            return null;
        }
    }

    public static bool HasSalaData(string salaId)
    {
        if (string.IsNullOrWhiteSpace(salaId))
            return false;

        return File.Exists(GetRoomDataFile(salaId));
    }

    public static bool DeleteSalaData(string salaId)
    {
        if (string.IsNullOrWhiteSpace(salaId))
            return false;

        string roomPath = GetRoomPath(salaId);
        if (!Directory.Exists(roomPath))
            return false;

        try
        {
            Directory.Delete(roomPath, true);
            Debug.Log($"[SalaDataStorage] Pasta da sala deletada: {roomPath}");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[SalaDataStorage] Erro ao deletar pasta da sala: {e.Message}");
            return false;
        }
    }

    public static SalaData CreateDefaultSalaData(SalaLocalSnapshot snapshot)
    {
        if (snapshot == null)
            return null;

        // Garante que o snapshot tenha um localId estável
        if (string.IsNullOrWhiteSpace(snapshot.salaLocalId))
            snapshot.salaLocalId = Guid.NewGuid().ToString("N");

        var dados = new SalaData
        {
            salaLocalId  = snapshot.salaLocalId,
            salaSteamId  = snapshot.salaSteamId ?? string.Empty,
            nomeSala     = snapshot.nomeSala ?? string.Empty,
            multiplicadorGrid = 2f,
            mapas           = Array.Empty<MapSaveData>(),
            tokensCatalogo  = Array.Empty<TokenCatalogSaveData>(),
            tokensNoMapa    = Array.Empty<TokenInstanceSaveData>()
        };

        SaveSalaData(dados);
        return dados;
    }

    // -----------------------------------------------------------------------
    // Imagens
    // -----------------------------------------------------------------------

    /// <summary>
    /// Copia uma imagem para a pasta da sala e retorna o caminho persistido.
    /// Seguro chamar com origemPath já no destino (não re-copia).
    /// </summary>
    public static string PersistirImagem(string origemPath, string salaId, string nomeArquivo)
    {
        if (string.IsNullOrWhiteSpace(origemPath) || !File.Exists(origemPath))
            return origemPath ?? string.Empty;

        try
        {
            string imgsDir = GetImagesPath(salaId);
            if (!Directory.Exists(imgsDir))
                Directory.CreateDirectory(imgsDir);

            string ext       = Path.GetExtension(origemPath);
            if (string.IsNullOrWhiteSpace(ext)) ext = ".png";

            string destino    = Path.Combine(imgsDir, SanitizeId(nomeArquivo) + ext);
            string destinoFull = Path.GetFullPath(destino);
            string origemFull  = Path.GetFullPath(origemPath);

            if (!string.Equals(origemFull, destinoFull, StringComparison.OrdinalIgnoreCase))
                File.Copy(origemFull, destinoFull, true);

            return destinoFull;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SalaDataStorage] Falha ao persistir imagem: {e.Message}");
            return origemPath;
        }
    }

    /// <summary>
    /// Remove da pasta de imagens da sala todos os ficheiros que não constem
    /// em <paramref name="pathsPersistidos"/>. Deve ser chamado após SalvarEstadoVTT
    /// para eliminar imagens de tokens/mapas entretanto removidos.
    /// </summary>
    public static void PurgarImagensOrfas(string salaId, System.Collections.Generic.HashSet<string> pathsPersistidos)
    {
        if (string.IsNullOrWhiteSpace(salaId) || pathsPersistidos == null)
            return;

        string imgsDir = GetImagesPath(salaId);
        if (!Directory.Exists(imgsDir))
            return;

        try
        {
            foreach (string arquivo in Directory.GetFiles(imgsDir))
            {
                string fullPath = Path.GetFullPath(arquivo);
                if (!pathsPersistidos.Contains(fullPath))
                {
                    File.Delete(fullPath);
                    Debug.Log($"[SalaDataStorage] Imagem órfã removida: {fullPath}");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SalaDataStorage] Erro ao purgar imagens: {e.Message}");
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void EnsureRoomDirectory(string salaId)
    {
        string roomPath = GetRoomPath(salaId);
        if (!Directory.Exists(roomPath))
            Directory.CreateDirectory(roomPath);

        string imgsPath = GetImagesPath(salaId);
        if (!Directory.Exists(imgsPath))
            Directory.CreateDirectory(imgsPath);
    }

    private static string SanitizeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return "unknown";

        char[] inv   = Path.GetInvalidFileNameChars();
        char[] chars = id.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(inv, chars[i]) >= 0)
                chars[i] = '_';
        }

        string result = new string(chars);
        return string.IsNullOrWhiteSpace(result) ? "unknown" : result;
    }
}
