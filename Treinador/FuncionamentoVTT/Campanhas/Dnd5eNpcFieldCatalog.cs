using System.Collections.Generic;

/// <summary>
/// Catálogo de campos de NPC específico da 5ª edição (D&amp;D SRD), no mesmo padrão de
/// XmlFieldDef.Catalog (Sistemas/SistemasCustom/XmlFieldDef.cs), mas em arquivo próprio para não
/// misturar com o catálogo genérico da Forja de Sistemas.
/// </summary>
public static class Dnd5eNpcFieldCatalog
{
    /// <summary>
    /// Campo de nome do NPC — tratado direto no cabeçalho de CampanhaTopicEditorUI (não é um
    /// toggle). FieldId "name" é reconhecido pelo DynamicXmlBuilder para nomear o arquivo .xml a
    /// partir do nome do NPC.
    /// </summary>
    public static readonly XmlFieldDef NomeField = new XmlFieldDef("name", "Nome", "name", FieldType.Text, "Identificação");

    /// <summary>Campo de retrato do NPC — tratado direto no cabeçalho de CampanhaTopicEditorUI (não é um toggle).</summary>
    public static readonly XmlFieldDef RetratoField = new XmlFieldDef("portrait", "Retrato", "portrait", FieldType.Image, "Identificação");

    /// <summary>Marcador interno de origem ("imported" pra NPC trazido por CampanhasManager.
    /// ImportarNpcsDeCaminho, ausente/vazio pra NPC criado à mão pelo próprio editor de tópico) —
    /// não é um toggle (fora de <see cref="Fields"/>), só entra em <see cref="AllFields"/> pra
    /// sobreviver ao round-trip de salvar/reler. Usado pelo filtro de origem em
    /// CampanhaNpcGaleriaUI (Criados/Importados/Sistema) — NPCs vindos do sistema (ver
    /// CampanhaSistemaNpcLoader) nunca passam por aqui, são sempre EntryData sintéticos, nunca
    /// salvos com este campo.</summary>
    public static readonly XmlFieldDef OrigemField = new XmlFieldDef("npc-origem", "Origem (interno)", "origem", FieldType.Text, "");

    /// <summary>
    /// Campos de lore — separados do stat block de propósito: NÃO entram em <see cref="Fields"/>
    /// (não viram toggle na tela de stat block, não aparecem no card via CampanhaNpcCardUI.
    /// PreencherCampos, que só itera Fields), só em <see cref="AllFields"/> pra serem reconhecidos ao
    /// reler XMLs salvos. Editados por CampanhaNpcLoreUI, num painel à parte. Ver também o mecanismo
    /// de preservação em CampanhaTopicEditorUI (_camposExtrasPreservados) — sem ele, salvar o stat
    /// block apagaria esses campos, já que DynamicXmlBuilder.SaveEntriesAsXml sempre reescreve o
    /// arquivo inteiro a partir do EntryData.Fields recebido, sem merge.
    /// </summary>
    public static readonly XmlFieldDef LoreDescricaoField = new XmlFieldDef(
        "lore-descricao", "Lore", "lore-descricao", FieldType.TextArea, "");

    /// <summary>Subcampos de cada item em LoreItensField — expostos à parte (não só inline) pra
    /// CampanhaNpcLoreUI poder referenciá-los diretamente ao montar cada FieldValue sub-entry, sem
    /// depender de índice dentro de LoreItensField.SubFields.</summary>
    public static readonly XmlFieldDef LoreItemIdField   = new XmlFieldDef("lore-item-id",   "ID do Item", "id",   FieldType.Attribute, "");
    public static readonly XmlFieldDef LoreItemNomeField = new XmlFieldDef("lore-item-nome", "Nome",       "nome", FieldType.Text,      "");

    /// <summary>Itens de lore anexados ao NPC — referenciam o catálogo real de itens SRD (ver
    /// Campanhas/CampanhaItemCatalogoLoader.cs), não texto livre. Cada &lt;item&gt; guarda o id
    /// determinístico do catálogo (atributo) + o nome no momento da anexação (cache de exibição).</summary>
    public static readonly XmlFieldDef LoreItensField = new XmlFieldDef(
        "lore-itens", "Itens da Lore", "lore-itens", FieldType.ObjectList, "", xmlItemTag: "item",
        subFields: new List<XmlFieldDef> { LoreItemIdField, LoreItemNomeField });

    /// <summary>Campos específicos de 5e exibidos como toggle para o usuário escolher.</summary>
    public static readonly List<XmlFieldDef> Fields = new List<XmlFieldDef>
    {
        // ── Básico ────────────────────────────────────────────────────────────
        new XmlFieldDef("npc-size",      "Tamanho",     "size",      FieldType.Text, "Básico"),
        new XmlFieldDef("npc-alignment", "Alinhamento", "alignment", FieldType.Text, "Básico"),

        // ── Combate ───────────────────────────────────────────────────────────
        new XmlFieldDef("npc-ac",           "Classe de Armadura",    "armor-class",       FieldType.Number,   "Combate"),
        new XmlFieldDef("npc-hp",           "Vida",                  "hit-points",        FieldType.UnitValue, "Combate", defaultUnit: ""),
        new XmlFieldDef("npc-speed",        "Deslocamento",          "speed",             FieldType.UnitValue, "Combate", defaultUnit: "ft"),
        new XmlFieldDef("npc-cr",           "Desafio",               "challenge-rating",  FieldType.Text,     "Combate"),
        new XmlFieldDef("npc-proficiency",  "Bônus de Proficiência", "proficiency-bonus", FieldType.Number,   "Combate"),

        // ── Atributos ─────────────────────────────────────────────────────────
        new XmlFieldDef("npc-abilities", "Atributos Básicos", "ability-scores", FieldType.Group, "Atributos",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("npc-attr-for", "Força",          "for", FieldType.Number, ""),
                new XmlFieldDef("npc-attr-des", "Destreza",       "des", FieldType.Number, ""),
                new XmlFieldDef("npc-attr-con", "Constituição",   "con", FieldType.Number, ""),
                new XmlFieldDef("npc-attr-int", "Inteligência",   "int", FieldType.Number, ""),
                new XmlFieldDef("npc-attr-sab", "Sabedoria",      "sab", FieldType.Number, ""),
                new XmlFieldDef("npc-attr-car", "Carisma",        "car", FieldType.Number, ""),
            }),
        new XmlFieldDef("npc-saves", "Salvaguardas", "saving-throws", FieldType.Text, "Atributos"),

        // ── Perícias ──────────────────────────────────────────────────────────
        new XmlFieldDef("npc-senses",    "Sentidos", "senses",    FieldType.StringList, "Perícias", xmlItemTag: "sense"),
        new XmlFieldDef("npc-languages", "Línguas",  "languages", FieldType.StringList, "Perícias", xmlItemTag: "language"),

        // ── Habilidades ───────────────────────────────────────────────────────
        new XmlFieldDef("npc-traits", "Habilidades", "traits", FieldType.ObjectList, "Habilidades", xmlItemTag: "trait",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("npc-trait-name", "Nome",       "name",        FieldType.Text,     ""),
                new XmlFieldDef("npc-trait-desc", "Descrição",  "description", FieldType.TextArea, ""),
            }),
        new XmlFieldDef("npc-actions", "Ações", "actions", FieldType.ObjectList, "Habilidades", xmlItemTag: "action",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("npc-action-name",   "Nome",             "name",         FieldType.Text,     ""),
                new XmlFieldDef("npc-action-desc",   "Descrição",        "description",  FieldType.TextArea, ""),
                new XmlFieldDef("npc-action-attack", "Bônus de Ataque",  "attack-bonus", FieldType.Text,     ""),
                new XmlFieldDef("npc-action-damage", "Dano",             "damage",       FieldType.Text,     ""),
            }),
        new XmlFieldDef("npc-bonus-actions", "Ações Bônus", "bonus-actions", FieldType.ObjectList, "Habilidades", xmlItemTag: "bonus-action",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("npc-bonus-name", "Nome",      "name",        FieldType.Text,     ""),
                new XmlFieldDef("npc-bonus-desc", "Descrição", "description", FieldType.TextArea, ""),
            }),
        new XmlFieldDef("npc-reactions", "Reações", "reactions", FieldType.ObjectList, "Habilidades", xmlItemTag: "reaction",
            subFields: new List<XmlFieldDef>
            {
                new XmlFieldDef("npc-reaction-name", "Nome",      "name",        FieldType.Text,     ""),
                new XmlFieldDef("npc-reaction-desc", "Descrição", "description", FieldType.TextArea, ""),
            }),
    };

    /// <summary>NomeField + RetratoField + Fields + campos de lore — usado pra reconhecer todos os
    /// campos de um NPC ao ler XMLs já salvos (ver CampanhaNpcGaleriaUI), já que nem o cabeçalho
    /// (nome/retrato) nem a lore fazem parte de Fields (não são togglable).</summary>
    public static readonly List<XmlFieldDef> AllFields = BuildAllFields();

    private static List<XmlFieldDef> BuildAllFields()
    {
        var lista = new List<XmlFieldDef> { NomeField, RetratoField, OrigemField, LoreDescricaoField, LoreItensField };
        lista.AddRange(Fields);
        return lista;
    }
}
