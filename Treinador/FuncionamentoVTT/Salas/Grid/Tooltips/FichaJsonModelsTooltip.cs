using System;
using System.Collections.Generic;

// DTOs para JsonUtility.FromJson dos blobs *_json de LocalFichaData. Os nomes de campo aqui
// precisam bater EXATAMENTE com os usados por FichaGeralManager.cs (que grava esses JSONs) —
// duplicados aqui de propósito porque as classes originais são privadas naquele arquivo e o
// tooltip do tabuleiro não deve depender/acoplar na tela de visualização de ficha.

[Serializable]
public class ProficienciesInfoWrapperTooltip
{
    public List<SkillSaveItemTooltip> skills = new List<SkillSaveItemTooltip>();
    public List<SkillSaveItemTooltip> saves = new List<SkillSaveItemTooltip>();
}

[Serializable]
public class SkillSaveItemTooltip
{
    public string nome;
    public bool treinado;
    public int attrIndex;
    public int miscBonus;
    public int valorTotal;
}

[Serializable]
public class CombateExtraDataTooltip
{
    public int cd_magia_manual = -1;
    public int bonus_ataque_magico_manual = -1;
}

// GrimorioWrapper/GrimorioSaveItem e InventoryWrapper/InventorySaveItem (para magias_json e
// inventario_json) agora são as classes canônicas e públicas de LocalFichaStorage.cs — não
// precisam mais de um DTO "Tooltip" duplicado aqui, já que deixaram de ser privadas ao arquivo
// que grava esses JSONs (FichaGeralManager.cs).

[Serializable]
public class AcaoDataTooltip
{
    public string nome, ataque, dano, tipoDano, distancia, critico, cd, detalhes;
}

[Serializable]
public class AcoesInfoDataTooltip
{
    public List<AcaoDataTooltip> actions = new List<AcaoDataTooltip>();
}

