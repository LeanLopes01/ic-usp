using System;
using System.Collections.Generic;

/// <summary>Tipo de campo XML — determina qual widget de input é exibido.</summary>
public enum FieldType
{
    Attribute,   // atributo no elemento raiz: <root id="..." source="...">
    Text,        // elemento texto simples:    <name>texto</name>
    Number,      // elemento numérico:         <level>5</level>
    TextArea,    // texto longo (multiline):   <description>...</description>
    UnitValue,   // elemento com atributo unit: <speed unit="ft">30</speed>
    StringList,  // lista de strings:          <languages><language>x</language></languages>
    ObjectList,  // lista de objetos:          <traits><trait>...</trait></traits>
    Group,       // grupo de sub-elementos:    <metadata>...</metadata>
    Image,       // caminho de imagem:         <portrait>C:\...\arquivo.png</portrait>
}

/// <summary>
/// Definição de um campo XML — base para os toggles e para a construção dinâmica.
/// </summary>
[Serializable]
public class XmlFieldDef
{
    public string           FieldId;     // ID único interno
    public string           Label;       // texto exibido no toggle
    public string           XmlTag;      // tag XML gerada
    public string           XmlItemTag;  // tag de cada item em listas
    public FieldType        Type;
    public string           Category;    // agrupamento visual dos toggles
    public string           DefaultUnit;
    public List<XmlFieldDef> SubFields;  // sub-campos de Group / ObjectList

    // XmlTag reservado pra um sub-campo que representa o TEXTO PRÓPRIO do elemento pai, não um filho
    // (ex: <option id="Sagrao Sagrada">Uma sagrao sagrada</option> — "id" é atributo, mas o texto
    // "Uma sagrao sagrada" não tem tag nenhuma, é o InnerText do próprio <option>). "#text" é o
    // mesmo nome que XmlDocument já usa pra um nó de texto (XmlText.Name == "#text"), então não
    // colide com nenhuma tag XML real possível. Ver DynamicXmlBuilder.ParseFields/WriteField (lê/
    // escreve) e CustomSystemManager.InferirSubCampos (infere quando descobre um campo novo).
    public const string MarcadorTextoProprio = "#text";

    public XmlFieldDef(string fieldId, string label, string xmlTag, FieldType type, string category,
                       string xmlItemTag = null, string defaultUnit = null,
                       List<XmlFieldDef> subFields = null)
    {
        FieldId     = fieldId;
        Label       = label;
        XmlTag      = xmlTag;
        XmlItemTag  = xmlItemTag;
        Type        = type;
        Category    = category;
        DefaultUnit = defaultUnit;
        SubFields   = subFields ?? new List<XmlFieldDef>();
    }

    // ── Catálogo completo de campos disponíveis ───────────────────────────────

    public static readonly List<XmlFieldDef> Catalog = new List<XmlFieldDef>
    {
        // ── Identificação ─────────────────────────────────────────────────────
        new XmlFieldDef("attr-id",     "ID (atributo)",      "id",             FieldType.Attribute, "Identificação"),
        new XmlFieldDef("attr-source", "Source (atributo)",  "source",         FieldType.Attribute, "Identificação"),
        new XmlFieldDef("name",        "Nome",               "name",           FieldType.Text,      "Identificação"),
        new XmlFieldDef("summary",     "Resumo",             "summary",        FieldType.Text,      "Identificação"),
        new XmlFieldDef("description", "Descrição",          "description",    FieldType.TextArea,  "Identificação"),
        new XmlFieldDef("notes",       "Notas",              "notes",          FieldType.TextArea,  "Identificação"),
        new XmlFieldDef("origin",      "Origem/Fonte",       "origin",         FieldType.Text,      "Identificação"),

        // ── Metadados ─────────────────────────────────────────────────────────
        new XmlFieldDef("size",            "Tamanho",                "size",            FieldType.Text,      "Metadados"),
        new XmlFieldDef("speed",           "Deslocamento",           "speed",           FieldType.UnitValue, "Metadados", defaultUnit: "ft"),
        new XmlFieldDef("alignment",       "Alinhamento",            "alignment",       FieldType.Text,      "Metadados"),
        new XmlFieldDef("age",             "Idade",                  "age",             FieldType.Text,      "Metadados"),
        new XmlFieldDef("avg-height",      "Altura Média",           "average-height",  FieldType.Text,      "Metadados"),
        new XmlFieldDef("avg-weight",      "Peso Médio",             "average-weight",  FieldType.Text,      "Metadados"),
        new XmlFieldDef("level",           "Nível",                  "level",           FieldType.Number,    "Metadados"),
        new XmlFieldDef("hit-die",         "Dado de Vida",           "hit-die",         FieldType.Text,      "Metadados"),
        new XmlFieldDef("primary-ability", "Atributo Principal",     "primary-ability", FieldType.Text,      "Metadados"),
        new XmlFieldDef("saving-throws",   "Salvaguardas",           "saving-throws",   FieldType.Text,      "Metadados"),
        new XmlFieldDef("casting-time",    "Tempo de Conjuração",    "casting-time",    FieldType.Text,      "Metadados"),
        new XmlFieldDef("spell-range",     "Alcance (magia)",        "range",           FieldType.UnitValue, "Metadados", defaultUnit: "ft"),
        new XmlFieldDef("duration",        "Duração",                "duration",        FieldType.Text,      "Metadados"),
        new XmlFieldDef("school",          "Escola de Magia",        "school",          FieldType.Text,      "Metadados"),
        new XmlFieldDef("damage",          "Dano",                   "damage",          FieldType.Text,      "Metadados"),
        new XmlFieldDef("damage-type",     "Tipo de Dano",           "damage-type",     FieldType.Text,      "Metadados"),
        new XmlFieldDef("ac",              "Classe de Armadura",     "armor-class",     FieldType.Number,    "Metadados"),
        new XmlFieldDef("cr",              "Índice de Desafio",      "challenge-rating", FieldType.Text,     "Metadados"),
        new XmlFieldDef("initiative",      "Iniciativa",             "initiative",      FieldType.Number,    "Metadados"),
        new XmlFieldDef("proficiency",     "Bônus de Proficiência",  "proficiency-bonus", FieldType.Number,  "Metadados"),
        new XmlFieldDef("passive-perc",    "Percepção Passiva",      "passive-perception", FieldType.Number, "Metadados"),

        // ── Aparência ────────────────────────────────────────────────────────
        new XmlFieldDef("appearance", "Aparência (grupo)", "appearance", FieldType.Group, "Aparência",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("app-desc",     "Descrição",       "description",      FieldType.TextArea,   ""),
                new XmlFieldDef("app-features", "Traços Notáveis", "notable-features", FieldType.StringList, "", xmlItemTag: "feature"),
            }),

        // ── Listas simples ───────────────────────────────────────────────────
        new XmlFieldDef("languages",     "Idiomas",               "languages",     FieldType.StringList, "Listas", xmlItemTag: "language"),
        new XmlFieldDef("components",    "Componentes",           "components",    FieldType.StringList, "Listas", xmlItemTag: "component"),
        new XmlFieldDef("proficiencies", "Proficiências",         "proficiencies", FieldType.StringList, "Listas", xmlItemTag: "proficiency"),
        new XmlFieldDef("equipment",     "Equipamento Inicial",   "equipment",     FieldType.StringList, "Listas", xmlItemTag: "item"),
        new XmlFieldDef("conditions",    "Condições/Imunidades",  "conditions",    FieldType.StringList, "Listas", xmlItemTag: "condition"),
        new XmlFieldDef("resistances",   "Resistências",          "resistances",   FieldType.StringList, "Listas", xmlItemTag: "resistance"),
        new XmlFieldDef("immunities",    "Imunidades a Dano",     "damage-immunities", FieldType.StringList, "Listas", xmlItemTag: "immunity"),
        new XmlFieldDef("senses",        "Sentidos",              "senses",        FieldType.StringList, "Listas", xmlItemTag: "sense"),
        new XmlFieldDef("keywords",      "Palavras-chave",        "keywords",      FieldType.StringList, "Listas", xmlItemTag: "keyword"),

        // ── Habilidades / Traços ─────────────────────────────────────────────
        new XmlFieldDef("traits", "Traços / Habilidades", "traits", FieldType.ObjectList, "Habilidades", xmlItemTag: "trait",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("t-id",    "ID",           "id",          FieldType.Attribute, ""),
                new XmlFieldDef("t-name",  "Nome",         "name",        FieldType.Text,      ""),
                new XmlFieldDef("t-desc",  "Descrição",    "description", FieldType.TextArea,  ""),
                new XmlFieldDef("t-type",  "Tipo de Uso",  "usage-type",  FieldType.Text,      ""),
                new XmlFieldDef("t-limit", "Limite",       "usage-limit", FieldType.Text,      ""),
                new XmlFieldDef("t-range", "Alcance",      "range",       FieldType.UnitValue, "", defaultUnit: "ft"),
            }),

        new XmlFieldDef("features", "Features de Classe", "features", FieldType.ObjectList, "Habilidades", xmlItemTag: "feature",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("f-id",    "ID",        "id",          FieldType.Attribute, ""),
                new XmlFieldDef("f-level", "Nível",     "level",       FieldType.Number,    ""),
                new XmlFieldDef("f-name",  "Nome",      "name",        FieldType.Text,      ""),
                new XmlFieldDef("f-desc",  "Descrição", "description", FieldType.TextArea,  ""),
            }),

        new XmlFieldDef("actions", "Ações de Combate", "actions", FieldType.ObjectList, "Habilidades", xmlItemTag: "action",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("a-name",   "Nome",          "name",         FieldType.Text,     ""),
                new XmlFieldDef("a-desc",   "Descrição",     "description",  FieldType.TextArea, ""),
                new XmlFieldDef("a-attack", "Bônus de Ataque", "attack-bonus", FieldType.Text,   ""),
                new XmlFieldDef("a-damage", "Dano",          "damage",       FieldType.Text,     ""),
                new XmlFieldDef("a-type",   "Tipo de Ação",  "action-type",  FieldType.Text,     ""),
            }),

        new XmlFieldDef("reactions", "Reações", "reactions", FieldType.ObjectList, "Habilidades", xmlItemTag: "reaction",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("r-name", "Nome",       "name",        FieldType.Text,     ""),
                new XmlFieldDef("r-desc", "Descrição",  "description", FieldType.TextArea, ""),
            }),

        new XmlFieldDef("legendary-actions", "Ações Lendárias", "legendary-actions", FieldType.ObjectList, "Habilidades", xmlItemTag: "legendary-action",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("la-name", "Nome",      "name",        FieldType.Text,     ""),
                new XmlFieldDef("la-desc", "Descrição", "description", FieldType.TextArea, ""),
                new XmlFieldDef("la-cost", "Custo",     "cost",        FieldType.Number,   ""),
            }),

        // ── Magia ─────────────────────────────────────────────────────────────
        new XmlFieldDef("spells", "Magias Conhecidas", "spells", FieldType.ObjectList, "Magia", xmlItemTag: "spell",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("s-name",  "Nome",  "name",  FieldType.Text,   ""),
                new XmlFieldDef("s-level", "Nível", "level", FieldType.Number, ""),
                new XmlFieldDef("s-school","Escola","school",FieldType.Text,   ""),
            }),

        new XmlFieldDef("spell-slots", "Slots de Magia", "spell-slots", FieldType.ObjectList, "Magia", xmlItemTag: "slot",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("ss-level", "Nível",      "level", FieldType.Number, ""),
                new XmlFieldDef("ss-count", "Quantidade", "count", FieldType.Number, ""),
            }),

        // ── Progressão ───────────────────────────────────────────────────────
        new XmlFieldDef("subclasses", "Subclasses", "subclasses", FieldType.ObjectList, "Progressão", xmlItemTag: "subclass",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("sc-id",    "ID",         "id",          FieldType.Attribute, ""),
                new XmlFieldDef("sc-name",  "Nome",       "name",        FieldType.Text,      ""),
                new XmlFieldDef("sc-level", "Nível Base", "base-level",  FieldType.Number,    ""),
                new XmlFieldDef("sc-desc",  "Descrição",  "description", FieldType.TextArea,  ""),
            }),

        new XmlFieldDef("subraces", "Sub-Raças", "subraces", FieldType.ObjectList, "Progressão", xmlItemTag: "subrace",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("sr-id",   "ID",        "id",          FieldType.Attribute, ""),
                new XmlFieldDef("sr-name", "Nome",      "name",        FieldType.Text,      ""),
                new XmlFieldDef("sr-desc", "Descrição", "description", FieldType.TextArea,  ""),
            }),

        new XmlFieldDef("ability-scores", "Bônus de Atributos", "ability-scores", FieldType.ObjectList, "Atributos", xmlItemTag: "score",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("as-attr",  "Atributo", "attribute", FieldType.Text,   ""),
                new XmlFieldDef("as-bonus", "Bônus",    "bonus",     FieldType.Number, ""),
            }),

        new XmlFieldDef("ability-values", "Atributos (FOR/DES/CON...)", "ability-values", FieldType.ObjectList, "Atributos", xmlItemTag: "ability",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("av-name",  "Atributo", "name",  FieldType.Text,   ""),
                new XmlFieldDef("av-value", "Valor",    "value", FieldType.Number, ""),
                new XmlFieldDef("av-mod",   "Mod",      "mod",   FieldType.Number, ""),
            }),
    };
}

// ── Modelos de dados em runtime ───────────────────────────────────────────────

[Serializable]
public class FieldValue
{
    public XmlFieldDef     Definition;
    public string          TextValue   = "";
    public string          UnitValue   = "";
    public List<string>    ListValues  = new List<string>();
    public List<EntryData> SubEntries  = new List<EntryData>();
    public List<FieldValue> GroupFields = new List<FieldValue>();
}

[Serializable]
public class EntryData
{
    public string           TopicTag;
    public List<FieldValue> Fields = new List<FieldValue>();

    // Caminho do arquivo de onde esta entrada foi carregada (ver DynamicXmlBuilder.LoadEntriesFromFolder) —
    // null/vazio pra uma entrada nova, ainda não salva nenhuma vez (ver
    // CustomSystemManager.ClicouAdicionarEntrada). SaveEntriesAsXml SEMPRE reescreve neste caminho
    // quando ele existe, em vez de recalcular um nome a partir do id/nome atual — sem isso, uma
    // entrada carregada de um arquivo cujo nome não bate com seu próprio atributo "id" (ex: um
    // arquivo renomeado à mão) ganhava um arquivo NOVO a cada salvamento, deixando o antigo pra trás
    // como um duplicado órfão.
    public string CaminhoArquivoOrigem;
}
