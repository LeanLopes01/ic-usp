using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

[Serializable]
public class LocalFichaData
{
    // ── Identidade ────────────────────────────────────────────────
    // player_id/steam_id foram removidos de propósito: fichas não têm mais nenhum vínculo com quem
    // criou — o objetivo é poder compartilhar o arquivo .json livremente (ex: copiar pra outra
    // máquina) e ele continuar aparecendo normalmente na listagem, identificado só pelos próprios
    // IDs (fichas_general_id/fichas_dnd_id).
    public int    sistema_id;
    public string name_pc;
    public int    level_pc;
    public string level_pc_detalhe;   // "Bárbaro 5 / Bardo 3"
    public int    fichas_general_id;
    public int    fichas_dnd_id;
    public string created_at_utc;
    public string exported_at_utc;
    public string sistema_nome;
    public string metodo_atributos;
    public string token_image_path;

    // ── Vida ──────────────────────────────────────────────────────
    public int    vida_maxima;
    public int    vida_atual;
    public int    vida_temporaria;
    public string dado_vida;
    public string vida_pc;
    public int    deslocamento;

    // ── Raça (objeto direto — substituiu race_pc/race_id/race_info_json) ──
    public RacaData raca = new RacaData();

    // ── Classes (array direto — substituiu class_pc/class_id/class_info_json/multiclasse_json) ──
    public List<ClasseData> classes = new List<ClasseData>();

    // ── Antecedente ───────────────────────────────────────────────
    public string background_id;
    public string background_info_json;  // refatoração futura → antecedente objeto

    // ── Proficiências Mescladas ───────────────────────────────────
    public string proficiencies_info_json; // refatoração futura → proficiencias objeto

    // ── Atributos ─────────────────────────────────────────────────
    public AtributosData atributos = new AtributosData();

    // ── Inventário ────────────────────────────────────────────────
    // Objeto direto (não mais string escapada) — ver InventoryWrapper logo abaixo.
    public InventoryWrapper inventario_json = new InventoryWrapper();

    // ── Magias ────────────────────────────────────────────────────
    // slots_magias_json continua string: é um dicionário de verdade ("1":4,"2":3,...) e JsonUtility
    // não serializa Dictionary<K,V> de jeito nenhum, nem aninhado — sem trocar de biblioteca de JSON
    // (Newtonsoft não está no projeto) não tem como virar objeto aninhado mantendo esse formato.
    public string slots_magias_json;
    // Objeto direto (não mais string escapada) — ver GrimorioWrapper logo abaixo.
    public GrimorioWrapper magias_json = new GrimorioWrapper();

    // ── Extras Combate / Ações ────────────────────────────────────
    public string acoes_info_json;
    public string habilidades_overrides_json;
    public string combate_extra_json;
    public string detalhes_principais_json;
    public string atributos_json;    // legado — atributos individuais já estão nos campos acima
}

/// <summary>
/// Formato canônico e único de inventário/grimório — usado por LocalFichaData.inventario_json/
/// magias_json e por TODO consumidor (EquipmentSelector, SpellSelector, FichaGeralManager,
/// FichaDisplayUi, CriacaoFichaProgressaoDnd, tooltips de sala). Antes desta classe existir, cada um
/// desses arquivos declarava sua PRÓPRIA cópia privada de "InventoryWrapper"/"InventorySaveItem" —
/// só "funcionavam juntas" porque JsonUtility casa campos por nome ao (des)serializar uma string;
/// agora que o campo em LocalFichaData é o objeto direto (não mais string escapada), só um tipo
/// pode existir de verdade. Superset de campos (inclui todos os *Override que FichaGeralManager já
/// usa) — nenhum consumidor com formato mais simples perde dado nesse round-trip.
/// </summary>
[Serializable]
public class InventorySaveItem
{
    public string instanceId;
    public int    id;
    public string equipSlot;
    public string customName;
    public string tipoOverride;
    public string caOverride;
    public string danoOverride;
    public string curaOverride;
    public string pesoOverride;
    public string descricaoOverride;
    public int    quantidade = 1;
}

[Serializable]
public class InventoryWrapper
{
    public float gp;
    public List<InventorySaveItem> itens = new List<InventorySaveItem>();
}

/// <summary>Mesma ideia de InventorySaveItem/InventoryWrapper, mas pro grimório de magias — inclui
/// classeOrigemId (separa o orçamento de magias conhecidas por classe em multiclasse) e todos os
/// *Override que FichaGeralManager já usa na visualização da ficha.</summary>
[Serializable]
public class GrimorioSaveItem
{
    public string instanceId;
    public int    id;
    public bool   preparada;
    public string classeOrigemId;
    public string nomeOverride;
    public string nivelOverride;
    public string tempoOverride;
    public string alcanceOverride;
    public string duracaoOverride;
    public string componentesOverride;
    public string descricaoOverride;
}

[Serializable]
public class GrimorioWrapper
{
    public List<GrimorioSaveItem> magias = new List<GrimorioSaveItem>();
}

/// <summary>
/// Campos legados presentes em fichas salvas antes do refactor de raça/classe.
/// Usado APENAS para migração em TryGetFichaSnapshot — nunca salvo em fichas novas.
/// </summary>
[Serializable]
internal class LocalFichaLegacy
{
    public string race_pc;
    public string race_id;
    public string race_info_json;
    public string class_pc;
    public string class_id;
    public string class_info_json;
    public string multiclasse_json;
}

public static class LocalFichaStorage
{
    private const string FolderName = "fichas";

    public static string BasePath => Path.Combine(Application.persistentDataPath, FolderName);

    // steamId/playerId mantidos na assinatura só pra não quebrar os muitos chamadores existentes —
    // não são mais usados: fichas não têm vínculo de identidade (ver LocalFichaData).
    public static LocalFichaData SaveFicha(string steamId, int playerId, int sistemaid, string nome, int level, string metodoAtributos = null)
    {
        EnsureDirectory();

        var ficha = new LocalFichaData
        {
            sistema_id = sistemaid,
            name_pc = string.IsNullOrWhiteSpace(nome) ? "Sem Nome" : nome.Trim(),
            level_pc = Mathf.Max(1, level),
            fichas_general_id = GenerateId(),
            fichas_dnd_id = GenerateId(),
            created_at_utc = DateTime.UtcNow.ToString("o"),
            metodo_atributos = metodoAtributos ?? string.Empty
        };

        string filePath = Path.Combine(BasePath, BuildFileName(ficha));
        string json = JsonUtility.ToJson(ficha, true);
        File.WriteAllText(filePath, json);
        return ficha;
    }

    public static LocalFichaData SaveDndFicha(string steamId, int playerId, string nome, int level, string metodoAtributos)
    {
        return SaveFicha(steamId, playerId, 1, nome, level, metodoAtributos);
    }

    // steamId/fallbackPlayerId mantidos na assinatura só pra não quebrar os chamadores existentes —
    // a listagem não filtra mais por identidade (ver LocalFichaData): toda ficha do sistema pedido,
    // de qualquer origem, aparece — é isso que permite compartilhar o arquivo .json livremente
    // (ex: copiar pra outra máquina) e ele continuar aparecendo normalmente.
    public static FichaData[] LoadFichasForSystem(string steamId, int fallbackPlayerId, int sistemaid)
    {
        EnsureDirectory();
        var result = new List<FichaData>();
        string[] files = Directory.GetFiles(BasePath, "*.json", SearchOption.TopDirectoryOnly);

        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            try
            {
                string json = File.ReadAllText(path);
                LocalFichaData local = JsonUtility.FromJson<LocalFichaData>(json);
                if (local == null) continue;
                if (local.sistema_id != sistemaid) continue;

                string raceNome    = local.raca?.raca_nome ?? "-";
                string classDisplay = !string.IsNullOrWhiteSpace(local.level_pc_detalhe)
                    ? local.level_pc_detalhe
                    : (local.classes != null && local.classes.Count > 0
                        ? local.classes[0].class_name : "-");

                result.Add(new FichaData
                {
                    name_pc           = local.name_pc,
                    level_pc          = local.level_pc,
                    level_pc_detalhe  = local.level_pc_detalhe ?? "",
                    race_pc           = raceNome,
                    class_pc          = classDisplay,
                    fichas_general_id = local.fichas_general_id,
                    fichas_dnd_id     = local.fichas_dnd_id,
                    raca              = local.raca ?? new RacaData(),
                    classes           = local.classes ?? new List<ClasseData>(),
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalFichaStorage] Arquivo inválido ignorado: {path} | {ex.Message}");
            }
        }

        result.Sort((a, b) => string.Compare(a?.name_pc, b?.name_pc, StringComparison.OrdinalIgnoreCase));
        return result.ToArray();
    }

    /// <summary>Lista TODAS as fichas locais da pasta (sem nenhum filtro de identidade — ver
    /// LocalFichaData) filtradas por sistema — usado pelo painel "Conteudo &gt; Ficha" em
    /// BarraFerramentasManager. sistemaId &lt;= 0 desliga o filtro de sistema; sistema_id == 0 num
    /// arquivo (campo legado/não preenchido) sempre passa — mesma regra "deixa passar" de
    /// SalaAbasFichasManager.FichaMatchSistema. Ao contrário de TryGetFichaSnapshot, NÃO roda
    /// MigrarCamposLegados (evita reescrever arquivos em disco durante uma simples listagem).</summary>
    public static List<LocalFichaData> LoadFichasForSistemaESteamId(string steamId, int fallbackPlayerId, int sistemaId)
    {
        EnsureDirectory();
        var result = new List<LocalFichaData>();
        string[] files = Directory.GetFiles(BasePath, "*.json", SearchOption.TopDirectoryOnly);

        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            try
            {
                LocalFichaData local = JsonUtility.FromJson<LocalFichaData>(File.ReadAllText(path));
                if (local == null) continue;

                if (sistemaId > 0 && local.sistema_id != 0 && local.sistema_id != sistemaId) continue;

                result.Add(local);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalFichaStorage] Arquivo inválido ignorado: {path} | {ex.Message}");
            }
        }

        result.Sort((a, b) => string.Compare(a?.name_pc, b?.name_pc, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    public static FichaData[] LoadDndFichasForSteamId(string steamId, int fallbackPlayerId)
    {
        return LoadFichasForSystem(steamId, fallbackPlayerId, 1);
    }

    public static FichaData[] LoadT20PadraoFichasForSteamId(string steamId, int fallbackPlayerId)
    {
        return LoadFichasForSystem(steamId, fallbackPlayerId, 2);
    }

    public static FichaData[] LoadT20AlternativoFichasForSteamId(string steamId, int fallbackPlayerId)
    {
        return LoadFichasForSystem(steamId, fallbackPlayerId, 3);
    }

    public static bool UpdateDndFichaIdentity(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, string novoNome, int novoLevel)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            ficha.name_pc = string.IsNullOrWhiteSpace(novoNome) ? ficha.name_pc : novoNome.Trim();
            ficha.level_pc = Mathf.Max(1, novoLevel);
        });
    }

    /// <summary>
    /// Salva dados de raça no novo formato (objeto raca direto).
    /// Mantém assinatura legada para não quebrar chamadores em RaceSelector.
    /// O parâmetro raceInfoJson é lido para extrair subraca e proficiências.
    /// </summary>
    public static bool SaveRaceInfo(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, string raceId, string raceInfoJson, string raceName = null)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            if (ficha.raca == null) ficha.raca = new RacaData();
            ficha.raca.raca_id   = raceId   ?? string.Empty;
            ficha.raca.raca_nome = raceName?.Trim() ?? ficha.raca.raca_nome;

            if (!string.IsNullOrWhiteSpace(raceInfoJson) && raceInfoJson != "{}")
            {
                try
                {
                    var info = JsonUtility.FromJson<RaceSelector.RacaInfoData>(raceInfoJson);
                    if (info != null)
                    {
                        ficha.raca.subraca_id   = info.subrace_id   ?? "";
                        ficha.raca.subraca_nome = info.subrace_name ?? "";
                        ficha.raca.skills    = info.skills    ?? ficha.raca.skills;
                        ficha.raca.armors    = info.armors    ?? ficha.raca.armors;
                        ficha.raca.weapons   = info.weapons   ?? ficha.raca.weapons;
                        ficha.raca.tools     = info.tools     ?? ficha.raca.tools;
                        ficha.raca.languages = info.languages ?? ficha.raca.languages;
                    }
                }
                catch { }
            }
        });
    }

    /// <summary>
    /// Mantido para compatibilidade com chamadores existentes (ClassSelector).
    /// Salva apenas vida_maxima — class_id/class_pc/class_info_json foram migrados para classes[].
    /// Use SaveClassData para salvar dados completos de classe.
    /// </summary>
    public static bool SaveClassInfo(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, string classId, int vidaMaxima, string classInfoJson, string className = null)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            if (vidaMaxima > 0)
                ficha.vida_maxima = vidaMaxima;
        });
    }

    /// <summary>
    /// Salva ou atualiza os dados de UMA classe específica dentro de ficha.classes (array direto).
    /// Preserva os dados das outras classes (multiclasse). Se is_primary, também
    /// atualiza class_pc e class_id para compatibilidade com código legado.
    /// </summary>
    public static bool SaveClassData(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, ClasseData classeData)
    {
        if (classeData == null) return false;
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            if (ficha.classes == null) ficha.classes = new List<ClasseData>();
            ficha.classes.UpsertClass(classeData);

            // Mantém campos legados em sincronia com a classe primária
            // class_pc/class_id foram removidos — dados de classe ficam em ficha.classes
        });
    }

    public static bool SaveBackgroundInfo(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, string backgroundId, string backgroundInfoJson)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            ficha.background_id = backgroundId ?? string.Empty;
            ficha.background_info_json = backgroundInfoJson ?? "{}";
        });
    }

    public static bool SaveProficienciesInfo(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, string proficienciesInfoJson)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            ficha.proficiencies_info_json = proficienciesInfoJson ?? "{}";
        });
    }

    public static bool SaveInventory(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, InventoryWrapper inventoryData)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            ficha.inventario_json = inventoryData ?? new InventoryWrapper();
        });
    }

    public static InventoryWrapper LoadInventory(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId)
    {
        if (TryGetFichaSnapshot(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, out LocalFichaData ficha))
            return ficha?.inventario_json;
        return null;
    }

    public static bool SaveSpells(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, string slotsJson, GrimorioWrapper magiasData)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            ficha.slots_magias_json = slotsJson ?? "{}";
            ficha.magias_json = magiasData ?? new GrimorioWrapper();
        });
    }

    /// <summary>
    /// Legado: multiclasse_json foi removido — dados ficam em ficha.classes[].level.
    /// Mantido como no-op para não quebrar chamadores existentes.
    /// </summary>
    public static bool SaveMulticlasse(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, string multiclasseJson)
    {
        return true; // no-op — informação já está em ficha.classes
    }

    /// <summary>
    /// Expõe UpdateFichaData para que FichaSaveManager possa gravar campos de exportação
    /// diretamente no mesmo arquivo do snapshot, sem criar um segundo arquivo.
    /// </summary>
    public static bool UpdateExportFields(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, Action<LocalFichaData> updater)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, updater);
    }

    /// <summary>
    /// Retorna o caminho completo do arquivo JSON de uma ficha, ou null se não encontrado.
    /// </summary>
    public static string TryGetFichaFilePath(int fichasGeneralId, int fichasDndId)
    {
        EnsureDirectory();
        string[] files = Directory.GetFiles(BasePath, "*.json", SearchOption.TopDirectoryOnly);
        foreach (string path in files)
        {
            try
            {
                string json = File.ReadAllText(path);
                LocalFichaData local = JsonUtility.FromJson<LocalFichaData>(json);
                if (local == null) continue;
                if (local.fichas_dnd_id == fichasDndId || local.fichas_general_id == fichasGeneralId)
                    return path;
            }
            catch { }
        }
        return null;
    }

    public static bool SaveTokenImagePath(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, string tokenImagePath)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            ficha.token_image_path = tokenImagePath ?? string.Empty;
        });
    }

    public static bool SaveAttributes(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId,
        int forca, int destreza, int constituicao, int inteligencia, int sabedoria, int carisma)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            ficha.atributos.forca = forca;
            ficha.atributos.destreza = destreza;
            ficha.atributos.constituicao = constituicao;
            ficha.atributos.inteligencia = inteligencia;
            ficha.atributos.sabedoria = sabedoria;
            ficha.atributos.carisma = carisma;
            ficha.atributos_json = JsonUtility.ToJson(new LocalAtributosPayload
            {
                atributo_forca = forca,
                atributo_destreza = destreza,
                atributo_constituicao = constituicao,
                atributo_inteligencia = inteligencia,
                atributo_sabedoria = sabedoria,
                atributo_carisma = carisma
            });
        });
    }

    public static bool SaveAcoesInfo(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, string acoesInfoJson)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            ficha.acoes_info_json = acoesInfoJson ?? "{}";
        });
    }

    public static bool SaveHabilidadesOverrides(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, string habilidadesOverridesJson)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            ficha.habilidades_overrides_json = habilidadesOverridesJson ?? "{}";
        });
    }

    public static bool SaveCombatState(
        string steamId,
        int fallbackPlayerId,
        int fichasGeneralId,
        int fichasDndId,
        int vidaAtual,
        int vidaTemporaria,
        string dadoVida,
        string combateExtraJson)
    {
        return UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            ficha.vida_atual = Mathf.Max(0, vidaAtual);
            ficha.vida_temporaria = Mathf.Max(0, vidaTemporaria);
            ficha.dado_vida = dadoVida ?? string.Empty;
            ficha.combate_extra_json = combateExtraJson ?? "{}";
        });
    }

    public static bool TryLoadAttributes(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId,
        out int forca, out int destreza, out int constituicao, out int inteligencia, out int sabedoria, out int carisma)
    {
        int tmpForca = 10;
        int tmpDestreza = 10;
        int tmpConstituicao = 10;
        int tmpInteligencia = 10;
        int tmpSabedoria = 10;
        int tmpCarisma = 10;

        bool found = UpdateFichaData(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, ficha =>
        {
            tmpForca = ficha.atributos.forca > 0 ? ficha.atributos.forca : tmpForca;
            tmpDestreza = ficha.atributos.destreza > 0 ? ficha.atributos.destreza : tmpDestreza;
            tmpConstituicao = ficha.atributos.constituicao > 0 ? ficha.atributos.constituicao : tmpConstituicao;
            tmpInteligencia = ficha.atributos.inteligencia > 0 ? ficha.atributos.inteligencia : tmpInteligencia;
            tmpSabedoria = ficha.atributos.sabedoria > 0 ? ficha.atributos.sabedoria : tmpSabedoria;
            tmpCarisma = ficha.atributos.carisma > 0 ? ficha.atributos.carisma : tmpCarisma;
        }, false);

        forca = tmpForca;
        destreza = tmpDestreza;
        constituicao = tmpConstituicao;
        inteligencia = tmpInteligencia;
        sabedoria = tmpSabedoria;
        carisma = tmpCarisma;

        return found;
    }

    // steamId/fallbackPlayerId mantidos na assinatura só pra não quebrar os muitos chamadores
    // existentes — não filtram mais nada: uma ficha é identificada só pelos próprios IDs (ver
    // LocalFichaData), então ela pode ser aberta/editada independente de quem a criou.
    public static bool TryGetFichaSnapshot(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, out LocalFichaData ficha)
    {
        ficha = null;
        EnsureDirectory();

        string[] files = Directory.GetFiles(BasePath, "*.json", SearchOption.TopDirectoryOnly);

        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            try
            {
                string json = File.ReadAllText(path);
                LocalFichaData local = JsonUtility.FromJson<LocalFichaData>(json);
                if (local == null) continue;

                bool idMatch = (local.fichas_dnd_id == fichasDndId) || (local.fichas_general_id == fichasGeneralId);
                if (!idMatch) continue;

                // Migração de fichas antigas: se raca/classes estão vazios, lê campos legados do JSON
                MigrarCamposLegados(local, json, path);

                ficha = local;
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalFichaStorage] Falha ao ler snapshot da ficha: {path} | {ex.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// Migra fichas antigas que ainda têm race_info_json/class_info_json/multiclasse_json
    /// para o novo formato com raca e classes objetos diretos.
    /// Após migrar, reescreve o arquivo já no novo formato.
    /// </summary>
    private static void MigrarCamposLegados(LocalFichaData local, string json, string filePath)
    {
        bool precisaMigrar = false;

        // Lê campos legados do JSON (ignorados pelo LocalFichaData moderno)
        LocalFichaLegacy leg = null;
        try { leg = JsonUtility.FromJson<LocalFichaLegacy>(json); } catch { }
        if (leg == null) return;

        // Migra raça
        if ((local.raca == null || string.IsNullOrEmpty(local.raca.raca_id))
            && !string.IsNullOrEmpty(leg.race_id))
        {
            if (local.raca == null) local.raca = new RacaData();
            local.raca.raca_id   = leg.race_id;
            local.raca.raca_nome = leg.race_pc ?? "";

            if (!string.IsNullOrEmpty(leg.race_info_json))
            {
                try
                {
                    var ri = JsonUtility.FromJson<RaceSelector.RacaInfoData>(leg.race_info_json);
                    if (ri != null)
                    {
                        local.raca.subraca_id   = ri.subrace_id   ?? "";
                        local.raca.subraca_nome = ri.subrace_name ?? "";
                        local.raca.skills    = ri.skills    ?? new List<string>();
                        local.raca.armors    = ri.armors    ?? new List<string>();
                        local.raca.weapons   = ri.weapons   ?? new List<string>();
                        local.raca.tools     = ri.tools     ?? new List<string>();
                        local.raca.languages = ri.languages ?? new List<string>();
                    }
                }
                catch { }
            }
            precisaMigrar = true;
        }

        // Migra classes
        bool classesVazias = local.classes == null || local.classes.Count == 0;
        if (classesVazias && !string.IsNullOrEmpty(leg.class_id))
        {
            if (local.classes == null) local.classes = new List<ClasseData>();

            // Determina classes a migrar (multiclasse_json ou só a primária)
            var multiWrapper = MulticlassDataWrapper.FromJson(leg.multiclasse_json);
            bool temMulti = multiWrapper.classes != null && multiWrapper.classes.Count > 1;

            if (temMulti)
            {
                // Cada entrada do multiclasse_json vira um ClasseData mínimo
                for (int m = 0; m < multiWrapper.classes.Count; m++)
                {
                    var mc = multiWrapper.classes[m];
                    local.classes.Add(new ClasseData
                    {
                        class_id   = m == 0 ? (leg.class_id ?? "") : mc.className.ToLower().Replace(" ", "-"),
                        class_name = mc.className,
                        level      = mc.level,
                        is_primary = m == 0,
                    });
                }
            }
            else
            {
                local.classes.Add(new ClasseData
                {
                    class_id   = leg.class_id ?? "",
                    class_name = leg.class_pc ?? "",
                    level      = local.level_pc,
                    is_primary = true,
                });
            }

            // Popula proficiências e poderes da classe primária via class_info_json
            if (!string.IsNullOrEmpty(leg.class_info_json) && local.classes.Count > 0)
            {
                try
                {
                    var det = JsonUtility.FromJson<ClassSelector.DetalhesUpdate>(leg.class_info_json)?.classe_info;
                    if (det != null)
                    {
                        var primary = local.classes[0];
                        primary.subclass_id   = det.subclass_id   ?? "";
                        primary.subclass_name = det.subclass_name ?? "";
                        primary.saves     = det.saves     ?? primary.saves;
                        primary.skills    = det.skills    ?? primary.skills;
                        primary.armors    = det.armors    ?? primary.armors;
                        primary.weapons   = det.weapons   ?? primary.weapons;
                        primary.tools     = det.tools     ?? primary.tools;
                        primary.languages = det.languages ?? primary.languages;
                        primary.equipment = det.equipment ?? primary.equipment;
                        primary.asi_feats = det.asi_feats ?? primary.asi_feats;
                        foreach (var p in det.class_powers ?? new List<ClassSelector.ClassPowerData>())
                            primary.class_powers.Add(new ClassPowerEntry { id = p.id, name = p.name, level = p.level, description = p.description });
                        foreach (var p in det.subclass_powers ?? new List<ClassSelector.ClassPowerData>())
                            primary.subclass_powers.Add(new ClassPowerEntry { id = p.id, name = p.name, level = p.level, description = p.description });
                        foreach (var c in det.class_power_choices ?? new List<ClassSelector.ClassPowerChoiceData>())
                            primary.class_power_choices.Add(new ClassPowerChoiceEntry { feature_id = c.feature_id, feature_name = c.feature_name, level = c.level, selected_option = c.selected_option });
                    }
                }
                catch { }
            }
            precisaMigrar = true;
        }

        // Migração adicional: se classes só tem 1 entrada mas level_pc_detalhe indica multiclasse,
        // adiciona entradas placeholder para classes ausentes (serão preenchidas quando o usuário editar)
        if (local.classes != null && local.classes.Count == 1
            && !string.IsNullOrWhiteSpace(local.level_pc_detalhe)
            && local.level_pc_detalhe.Contains("/"))
        {
            var partes = local.level_pc_detalhe.Split('/');
            int idx = 0;
            foreach (var parte in partes)
            {
                var tokens = parte.Trim().Split(' ');
                if (tokens.Length < 2 || !int.TryParse(tokens[tokens.Length - 1], out int lvl)) { idx++; continue; }
                string className = string.Join(" ", tokens.Take(tokens.Length - 1)).Trim();
                if (string.IsNullOrEmpty(className)) { idx++; continue; }

                // Já existe no classes array?
                bool jaExiste = false;
                foreach (var cd in local.classes)
                    if (string.Equals(cd?.class_name, className, StringComparison.OrdinalIgnoreCase)) { jaExiste = true; break; }

                if (!jaExiste)
                {
                    local.classes.Add(new ClasseData
                    {
                        class_id   = className.ToLower().Replace(" ", "-").Replace("é", "e").Replace("á", "a").Replace("ó", "o"),
                        class_name = className,
                        level      = lvl,
                        is_primary = false,
                    });
                    precisaMigrar = true;
                    Debug.Log($"[LocalFichaStorage] Adicionado placeholder para '{className}' a partir de level_pc_detalhe.");
                }
                idx++;
            }
        }

        // Salva em disco no novo formato somente se houve migração
        if (precisaMigrar)
        {
            try
            {
                string novoPatch = filePath;
                File.WriteAllText(novoPatch, JsonUtility.ToJson(local, true));
                Debug.Log($"[LocalFichaStorage] Migração legado→novo concluída: {System.IO.Path.GetFileName(filePath)}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalFichaStorage] Falha ao salvar migração: {ex.Message}");
            }
        }
    }

    // steamId/fallbackPlayerId mantidos na assinatura só pra não quebrar os muitos chamadores
    // existentes — não filtram mais nada: uma ficha é identificada só pelos próprios IDs (ver
    // LocalFichaData), então ela pode ser aberta/editada independente de quem a criou.
    private static bool UpdateFichaData(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId, Action<LocalFichaData> updater, bool writeChanges = true)
    {
        EnsureDirectory();
        string[] files = Directory.GetFiles(BasePath, "*.json", SearchOption.TopDirectoryOnly);

        for (int i = 0; i < files.Length; i++)
        {
            string path = files[i];
            try
            {
                string json = File.ReadAllText(path);
                LocalFichaData local = JsonUtility.FromJson<LocalFichaData>(json);
                if (local == null) continue;

                bool idMatch = (local.fichas_dnd_id == fichasDndId) || (local.fichas_general_id == fichasGeneralId);
                if (!idMatch) continue;

                updater?.Invoke(local);

                if (writeChanges)
                {
                    string updatedJson = JsonUtility.ToJson(local, true);
                    File.WriteAllText(path, updatedJson);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[LocalFichaStorage] Falha ao atualizar ficha local: {path} | {ex.Message}");
            }
        }

        return false;
    }

    [Serializable]
    private class LocalAtributosPayload
    {
        public int atributo_forca;
        public int atributo_destreza;
        public int atributo_constituicao;
        public int atributo_inteligencia;
        public int atributo_sabedoria;
        public int atributo_carisma;
    }

    private static string BuildFileName(LocalFichaData ficha)
    {
        string safeName = SanitizeFileName(ficha.name_pc);
        return $"{safeName}_{ficha.fichas_dnd_id}.json";
    }

    private static string SanitizeFileName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "ficha";
        char[] invalid = Path.GetInvalidFileNameChars();
        string output = input;
        for (int i = 0; i < invalid.Length; i++)
            output = output.Replace(invalid[i], '_');
        output = output.Replace(' ', '_');
        return output.Length > 40 ? output.Substring(0, 40) : output;
    }

    private static int GenerateId()
    {
        long ticks = DateTime.UtcNow.Ticks;
        int id = (int)(ticks % int.MaxValue);
        return Mathf.Max(1, id);
    }

    private static void EnsureDirectory()
    {
        if (!Directory.Exists(BasePath))
            Directory.CreateDirectory(BasePath);
    }

    /// <summary>
    /// Detecta se uma ficha D&D ainda está em processo de criação (incompleta)
    /// Uma ficha é considerada completa se possui: raça, classe, antecedentes e atributos selecionados
    /// </summary>
    public static bool IsFichaIncompleta(string steamId, int fallbackPlayerId, int fichasGeneralId, int fichasDndId)
    {
        EnsureDirectory();
        
        LocalFichaData snapshot;
        if (!TryGetFichaSnapshot(steamId, fallbackPlayerId, fichasGeneralId, fichasDndId, out snapshot))
        {
            return true; // Se não encontrar, considera incompleta
        }

        if (snapshot == null) return true;

        // Verificar se raça está selecionada (não vazia, não "-", com race_info_json)
        bool racaSelecionada = snapshot.raca != null && !string.IsNullOrWhiteSpace(snapshot.raca.raca_id);

        bool classeSelecionada = snapshot.classes != null && snapshot.classes.Count > 0;

        // Verificar se antecedentes está selecionado (background_id preenchido)
        bool antecedenteSelecionado = !string.IsNullOrWhiteSpace(snapshot.background_id);

        // Verificar se atributos foram selecionados (pelo menos um diferente de 0)
        bool atributosSelecionados = snapshot.atributos.forca > 0 ||
                                     snapshot.atributos.destreza > 0 ||
                                     snapshot.atributos.constituicao > 0 ||
                                     snapshot.atributos.inteligencia > 0 ||
                                     snapshot.atributos.sabedoria > 0 ||
                                     snapshot.atributos.carisma > 0;

        // Retorna true (incompleta) se qualquer um dos campos está vazio
        bool incompleta = !racaSelecionada || !classeSelecionada || !antecedenteSelecionado || !atributosSelecionados;
        
        Debug.Log($"[LocalFichaStorage] Ficha {fichasDndId} - Raca: {racaSelecionada}, Classe: {classeSelecionada}, " +
                  $"Antecedente: {antecedenteSelecionado}, Atributos: {atributosSelecionados} => Incompleta: {incompleta}");

        return incompleta;
    }
}