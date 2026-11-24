using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Representa uma única entrada de classe em um personagem multiclasse.
/// </summary>
[Serializable]
public class MulticlassData
{
    public string className;
    public int    level;
}

/// <summary>
/// Wrapper serializável para armazenar a lista via JsonUtility.
/// </summary>
[Serializable]
public class MulticlassDataWrapper
{
    public List<MulticlassData> classes = new List<MulticlassData>();

    public static MulticlassDataWrapper FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new MulticlassDataWrapper();
        try   { return JsonUtility.FromJson<MulticlassDataWrapper>(json) ?? new MulticlassDataWrapper(); }
        catch { return new MulticlassDataWrapper(); }
    }

    public string ToJson() => JsonUtility.ToJson(this);
}

// ─────────────────────────────────────────────────────────────────────────────
// Novo formato unificado: armazena dados completos de cada classe separadamente,
// resolvendo o bug de multiclasse sobrescrever class_info_json de outra classe.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Raça do personagem — identificação + proficiências concedidas pela raça.</summary>
[Serializable]
public class RacaData
{
    public string raca_id;     // "dwarf", "gnome", etc.
    public string raca_nome;   // "Anão", "Gnomo"
    public string subraca_id;
    public string subraca_nome;
    // Proficiências e bônus concedidos pela raça
    public List<string> skills    = new List<string>();
    public List<string> armors    = new List<string>();
    public List<string> weapons   = new List<string>();
    public List<string> tools     = new List<string>();
    public List<string> languages = new List<string>();
}

/// <summary>Poder ou habilidade de classe/subclasse.</summary>
[Serializable]
public class ClassPowerEntry
{
    public string id;
    public string name;
    public int    level;
    public string description;
}

/// <summary>Escolha de poder de classe (feature choices como Fighting Style, etc.).</summary>
[Serializable]
public class ClassPowerChoiceEntry
{
    public string feature_id;
    public string feature_name;
    public int    level;
    public string selected_option;
}

/// <summary>
/// Dados de UMA classe num personagem (primária ou multiclasse).
/// Armazenado diretamente como array em LocalFichaData.classes —
/// sem JSON-dentro-de-string, formato legível e extensível.
/// </summary>
[Serializable]
public class ClasseData
{
    public string class_id;    // "cleric", "bard", etc.
    public string class_name;  // "Clérigo", "Bardo"
    public int    level;
    public bool   is_primary;  // true = classe que deu saves/proficiências iniciais
    public string subclass_id;
    public string subclass_name;
    // Proficiências concedidas por esta classe
    public List<string> saves     = new List<string>();
    public List<string> skills    = new List<string>();
    public List<string> armors    = new List<string>();
    public List<string> weapons   = new List<string>();
    public List<string> tools     = new List<string>();
    public List<string> languages = new List<string>();
    public List<string> equipment = new List<string>();
    // Poderes e habilidades desta classe
    public List<ClassPowerEntry>       class_powers        = new List<ClassPowerEntry>();
    public List<ClassPowerEntry>       subclass_powers     = new List<ClassPowerEntry>();
    public List<ClassPowerChoiceEntry> class_power_choices = new List<ClassPowerChoiceEntry>();
    public List<string>                asi_feats           = new List<string>();
}

/// <summary>
/// Utilitários para operar sobre uma List&lt;ClasseData&gt;.
/// Não é usada para serialização — LocalFichaData.classes é o campo direto.
/// </summary>
public static class ClassesListUtils
{
    /// <summary>Retorna a entrada da classe com o class_id dado, ou null.</summary>
    public static ClasseData FindByClassId(this List<ClasseData> list, string classId)
    {
        if (string.IsNullOrEmpty(classId) || list == null) return null;
        foreach (var c in list)
            if (string.Equals(c?.class_id, classId, StringComparison.OrdinalIgnoreCase))
                return c;
        return null;
    }

    /// <summary>Insere ou atualiza a entrada da classe identificada por class_id.</summary>
    public static void UpsertClass(this List<ClasseData> list, ClasseData data)
    {
        if (data == null || list == null) return;
        for (int i = 0; i < list.Count; i++)
        {
            if (string.Equals(list[i]?.class_id, data.class_id, StringComparison.OrdinalIgnoreCase))
            {
                list[i] = data;
                return;
            }
        }
        list.Add(data);
    }

    /// <summary>Retorna a classe marcada como primária, ou a primeira da lista.</summary>
    public static ClasseData PrimaryClass(this List<ClasseData> list)
    {
        if (list == null || list.Count == 0) return null;
        foreach (var c in list)
            if (c != null && c.is_primary) return c;
        return list[0];
    }
}

/// <summary>
/// Mantido para compatibilidade com código legado que usa ClassesWrapper.FromJson().
/// Novas fichas usam LocalFichaData.classes diretamente.
/// </summary>
[Serializable]
public class ClassesWrapper
{
    public List<ClasseData> classes = new List<ClasseData>();

    public static ClassesWrapper FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ClassesWrapper();
        try   { return JsonUtility.FromJson<ClassesWrapper>(json) ?? new ClassesWrapper(); }
        catch { return new ClassesWrapper(); }
    }

    public string ToJson() => JsonUtility.ToJson(this);
}
