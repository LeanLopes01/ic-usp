using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;

/// <summary>
/// Converte arquivos externos (.xml ou .json) de uma pasta em EntryData prontos pra
/// DynamicXmlBuilder.SaveEntriesAsXml — usado por CampanhasManager pra "importar" NPCs de fora do
/// projeto. Cada arquivo vira UMA entrada (mesma convenção de "um arquivo por NPC" já usada em
/// CampanhasLocalStorage.NpcsFolderPath/DynamicXmlBuilder).
///
/// XML: casa cada filho da raiz do arquivo pelo XmlFieldDef.XmlTag do catálogo — não exige que a
/// raiz se chame "npcs", só que os NOMES DAS TAGS FILHAS batam com o catálogo pro campo entrar.
/// JSON: sem pacote externo (Newtonsoft.Json não é dependência deste projeto, JsonUtility não lê
/// objetos arbitrários) — usa MiniJsonParser pra um grafo genérico e casa cada chave pelo FieldId OU
/// XmlTag do catálogo (case-insensitive).
///
/// Em ambos os formatos: campo do arquivo que não está no catálogo é ignorado; campo do catálogo
/// ausente no arquivo fica em branco. "Melhor esforço", mesmo espírito dos outros extractors do
/// projeto (AlcanceExtrator, FormaAreaExtrator, etc.) — não valida se o resultado "faz sentido".
/// </summary>
public static class CampanhaNpcImporter
{
    /// <summary>Ponto de entrada único — aceita tanto uma PASTA (escaneia .xml/.json dentro dela)
    /// quanto um ARQUIVO único (.xml ou .json, processado sozinho). O explorador de arquivos mostra
    /// arquivos também (ver CampanhasManager.OnClickImportarNpcs, PickMode.FilesAndFolders), então
    /// clicar direto num arquivo em vez de navegar até a pasta e confirmar ela também precisa funcionar.</summary>
    public static List<EntryData> ImportarDeCaminho(string caminho, List<XmlFieldDef> catalogo, string topicTag, out int falhas)
    {
        var entradas = new List<EntryData>();
        falhas = 0;

        if (string.IsNullOrWhiteSpace(caminho))
            return entradas;

        if (Directory.Exists(caminho))
        {
            foreach (string arquivo in Directory.GetFiles(caminho, "*.xml"))
                ProcessarArquivoXml(arquivo, catalogo, topicTag, entradas, ref falhas);

            foreach (string arquivo in Directory.GetFiles(caminho, "*.json"))
                ProcessarArquivoJson(arquivo, catalogo, topicTag, entradas, ref falhas);
        }
        else if (File.Exists(caminho))
        {
            string extensao = Path.GetExtension(caminho);
            if (string.Equals(extensao, ".xml", StringComparison.OrdinalIgnoreCase))
                ProcessarArquivoXml(caminho, catalogo, topicTag, entradas, ref falhas);
            else if (string.Equals(extensao, ".json", StringComparison.OrdinalIgnoreCase))
                ProcessarArquivoJson(caminho, catalogo, topicTag, entradas, ref falhas);
            else
                falhas++;
        }

        return entradas;
    }

    private static void ProcessarArquivoXml(string arquivo, List<XmlFieldDef> catalogo, string topicTag, List<EntryData> entradas, ref int falhas)
    {
        try
        {
            XDocument doc = XDocument.Load(arquivo);
            if (doc.Root == null) { falhas++; return; }
            entradas.Add(ConverterXml(doc.Root, topicTag, catalogo));
        }
        catch (Exception e)
        {
            falhas++;
            VTTLog.ForjaError($"[CampanhaNpcImporter] Falha ao ler '{Path.GetFileName(arquivo)}': {e.Message}");
        }
    }

    private static void ProcessarArquivoJson(string arquivo, List<XmlFieldDef> catalogo, string topicTag, List<EntryData> entradas, ref int falhas)
    {
        try
        {
            object grafo = MiniJsonParser.Parse(File.ReadAllText(arquivo));

            if (grafo is Dictionary<string, object> objetoUnico)
            {
                entradas.Add(ConverterJson(objetoUnico, topicTag, catalogo));
            }
            else if (grafo is List<object> lista)
            {
                // Arquivo com VÁRIOS NPCs juntos num array só (ex: datasets públicos tipo "5e SRD
                // Monsters") — cada elemento vira uma entrada própria, em vez do arquivo inteiro
                // virar um NPC só.
                foreach (object item in lista)
                {
                    if (!(item is Dictionary<string, object> objetoItem)) { falhas++; continue; }

                    entradas.Add(PareceMonstro5eSrd(objetoItem)
                        ? ConverterMonstro5eSrd(objetoItem, topicTag, catalogo)
                        : ConverterJson(objetoItem, topicTag, catalogo));
                }
            }
            else
            {
                falhas++;
            }
        }
        catch (Exception e)
        {
            falhas++;
            VTTLog.ForjaError($"[CampanhaNpcImporter] Falha ao ler '{Path.GetFileName(arquivo)}': {e.Message}");
        }
    }

    // ── XML ──────────────────────────────────────────────────────────────────

    private static EntryData ConverterXml(XElement raiz, string topicTag, List<XmlFieldDef> catalogo)
    {
        var entry = new EntryData { TopicTag = topicTag };
        foreach (XmlFieldDef def in catalogo)
        {
            XElement elemento = raiz.Element(def.XmlTag);
            if (elemento == null) continue;
            entry.Fields.Add(ConverterElementoXml(def, elemento));
        }
        return entry;
    }

    private static FieldValue ConverterElementoXml(XmlFieldDef def, XElement elemento)
    {
        var fv = new FieldValue { Definition = def };
        switch (def.Type)
        {
            case FieldType.StringList:
                foreach (XElement item in elemento.Elements(def.XmlItemTag ?? "item"))
                    fv.ListValues.Add(item.Value?.Trim() ?? string.Empty);
                break;

            case FieldType.ObjectList:
                foreach (XElement item in elemento.Elements(def.XmlItemTag ?? "item"))
                    fv.SubEntries.Add(ConverterXml(item, def.XmlItemTag ?? def.XmlTag, def.SubFields ?? new List<XmlFieldDef>()));
                break;

            case FieldType.Group:
                if (def.SubFields != null)
                    foreach (XmlFieldDef sub in def.SubFields)
                    {
                        XElement subEl = elemento.Element(sub.XmlTag);
                        if (subEl != null) fv.GroupFields.Add(ConverterElementoXml(sub, subEl));
                    }
                break;

            case FieldType.UnitValue:
                fv.TextValue = elemento.Value?.Trim() ?? string.Empty;
                fv.UnitValue = elemento.Attribute("unidade")?.Value ?? elemento.Attribute("unit")?.Value ?? def.DefaultUnit ?? string.Empty;
                break;

            default: // Text, Number, TextArea, Attribute, Image
                fv.TextValue = elemento.Value?.Trim() ?? string.Empty;
                break;
        }
        return fv;
    }

    // ── JSON ─────────────────────────────────────────────────────────────────

    private static EntryData ConverterJson(Dictionary<string, object> objeto, string topicTag, List<XmlFieldDef> catalogo)
    {
        var entry = new EntryData { TopicTag = topicTag };
        foreach (XmlFieldDef def in catalogo)
        {
            if (!TryObterValorJson(objeto, def, out object valor)) continue;
            entry.Fields.Add(ConverterValorJson(def, valor));
        }
        return entry;
    }

    private static bool TryObterValorJson(Dictionary<string, object> objeto, XmlFieldDef def, out object valor)
    {
        if (objeto.TryGetValue(def.FieldId, out valor)) return true;
        if (objeto.TryGetValue(def.XmlTag, out valor)) return true;

        foreach (KeyValuePair<string, object> kvp in objeto)
        {
            if (string.Equals(kvp.Key, def.FieldId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kvp.Key, def.XmlTag, StringComparison.OrdinalIgnoreCase))
            {
                valor = kvp.Value;
                return true;
            }
        }

        valor = null;
        return false;
    }

    private static FieldValue ConverterValorJson(XmlFieldDef def, object valorBruto)
    {
        var fv = new FieldValue { Definition = def };
        switch (def.Type)
        {
            case FieldType.StringList:
                if (valorBruto is List<object> lista)
                    foreach (object item in lista)
                        fv.ListValues.Add(FormatarValor(item));
                break;

            case FieldType.ObjectList:
                if (valorBruto is List<object> subItens)
                    foreach (object item in subItens)
                        if (item is Dictionary<string, object> subObj)
                            fv.SubEntries.Add(ConverterJson(subObj, def.XmlItemTag ?? def.XmlTag, def.SubFields ?? new List<XmlFieldDef>()));
                break;

            case FieldType.Group:
                if (valorBruto is Dictionary<string, object> grupoObj && def.SubFields != null)
                    foreach (XmlFieldDef sub in def.SubFields)
                        if (TryObterValorJson(grupoObj, sub, out object subValor))
                            fv.GroupFields.Add(ConverterValorJson(sub, subValor));
                break;

            case FieldType.UnitValue:
                if (valorBruto is Dictionary<string, object> unidadeObj)
                {
                    if (unidadeObj.TryGetValue("valor", out object vv)) fv.TextValue = FormatarValor(vv);
                    if (unidadeObj.TryGetValue("unidade", out object uv)) fv.UnitValue = FormatarValor(uv);
                }
                else
                {
                    fv.TextValue = FormatarValor(valorBruto);
                    fv.UnitValue = def.DefaultUnit ?? string.Empty;
                }
                break;

            default: // Text, Number, TextArea, Attribute, Image
                fv.TextValue = FormatarValor(valorBruto);
                break;
        }
        return fv;
    }

    private static string FormatarValor(object valor)
    {
        if (valor == null) return string.Empty;
        if (valor is double numero) return numero.ToString(CultureInfo.InvariantCulture);
        if (valor is bool booleano) return booleano ? "true" : "false";
        return valor.ToString();
    }

    // ── Formato "5e SRD Monsters" (dataset público bem conhecido — 5e-database/open5e) ─────────
    //
    // Schema BEM diferente do catálogo interno (chaves com underscore em vez de hífen, atributos
    // soltos em vez de agrupados, "desc" em vez de "description", sem campo próprio pra ação
    // lendária/perícia/resistência) — o casamento genérico por nome (ConverterJson) deixaria quase
    // tudo em branco, por isso tem um mapeamento DEDICADO. Detecção: presença de "hit_points" +
    // "armor_class" no objeto (assinatura simples, sem exigir o dataset inteiro).

    private static bool PareceMonstro5eSrd(Dictionary<string, object> objeto) =>
        objeto.ContainsKey("hit_points") && objeto.ContainsKey("armor_class");

    private static readonly (string chaveOrigem, string abreviacao)[] MapaSalvaguardas =
    {
        ("strength_save", "FOR"), ("dexterity_save", "DES"), ("constitution_save", "CON"),
        ("intelligence_save", "INT"), ("wisdom_save", "SAB"), ("charisma_save", "CAR"),
    };

    private static readonly (string fieldId, string chaveOrigem)[] MapaAtributos =
    {
        ("npc-attr-for", "strength"), ("npc-attr-des", "dexterity"), ("npc-attr-con", "constitution"),
        ("npc-attr-int", "intelligence"), ("npc-attr-sab", "wisdom"), ("npc-attr-car", "charisma"),
    };

    // Nomes de perícia da 5e (snake_case, mesma convenção do dataset) — sem campo próprio no
    // catálogo, viram um "trait" sintético "Perícias" (ver ConverterMonstro5eSrd) pra não perder o
    // dado, em vez de silenciosamente descartar.
    private static readonly string[] ChavesPericias =
    {
        "acrobatics", "animal_handling", "arcana", "athletics", "deception", "history", "insight",
        "intimidation", "investigation", "medicine", "nature", "perception", "performance",
        "persuasion", "religion", "sleight_of_hand", "stealth", "survival",
    };

    private static EntryData ConverterMonstro5eSrd(Dictionary<string, object> objeto, string topicTag, List<XmlFieldDef> catalogo)
    {
        var entry = new EntryData { TopicTag = topicTag };
        string ObterTexto(string chave) => objeto.TryGetValue(chave, out object v) ? FormatarValor(v) : string.Empty;
        void AdicionarTexto(string fieldId, string texto)
        {
            if (string.IsNullOrWhiteSpace(texto)) return;
            XmlFieldDef def = EncontrarDef(catalogo, fieldId);
            if (def != null) entry.Fields.Add(new FieldValue { Definition = def, TextValue = texto });
        }

        // Identificação/básico — tamanho + tipo/subtipo combinados num texto só (o catálogo não
        // separa isso em campos próprios).
        AdicionarTexto("name", ObterTexto("name"));
        string tipo = ObterTexto("type");
        string subtipo = ObterTexto("subtype");
        string tamanho = ObterTexto("size");
        if (!string.IsNullOrWhiteSpace(tipo))
            tamanho += string.IsNullOrWhiteSpace(subtipo) ? $" ({tipo})" : $" ({tipo}, {subtipo})";
        AdicionarTexto("npc-size", tamanho);
        AdicionarTexto("npc-alignment", ObterTexto("alignment"));

        // Combate
        AdicionarTexto("npc-ac", ObterTexto("armor_class"));
        string hp = ObterTexto("hit_points");
        string dadoVida = ObterTexto("hit_dice");
        AdicionarTexto("npc-hp", string.IsNullOrWhiteSpace(dadoVida) ? hp : $"{hp} ({dadoVida})");
        AdicionarTexto("npc-speed", ObterTexto("speed"));
        AdicionarTexto("npc-cr", ObterTexto("challenge_rating"));

        // Atributos (grupo) — origem tem strength/dexterity/etc soltos, catálogo quer agrupado.
        XmlFieldDef defAtributos = EncontrarDef(catalogo, "npc-abilities");
        if (defAtributos?.SubFields != null)
        {
            var fvGrupo = new FieldValue { Definition = defAtributos };
            foreach ((string fieldId, string chaveOrigem) in MapaAtributos)
            {
                XmlFieldDef subDef = defAtributos.SubFields.Find(d => d.FieldId == fieldId);
                string valor = ObterTexto(chaveOrigem);
                if (subDef != null && !string.IsNullOrWhiteSpace(valor))
                    fvGrupo.GroupFields.Add(new FieldValue { Definition = subDef, TextValue = valor });
            }
            if (fvGrupo.GroupFields.Count > 0) entry.Fields.Add(fvGrupo);
        }

        // Salvaguardas — só as presentes (nem todo monstro é proficiente em todas), formatadas como
        // texto legível (o campo do catálogo é FieldType.Text, não uma lista estruturada).
        var salvaguardas = new List<string>();
        foreach ((string chaveOrigem, string abreviacao) in MapaSalvaguardas)
            if (objeto.ContainsKey(chaveOrigem))
                salvaguardas.Add($"{abreviacao} +{ObterTexto(chaveOrigem)}");
        AdicionarTexto("npc-saves", string.Join(", ", salvaguardas));

        // Sentidos/línguas — origem é texto com vírgula, catálogo quer StringList.
        AdicionarListaSeparadaPorVirgula(entry, catalogo, "npc-senses", ObterTexto("senses"));
        AdicionarListaSeparadaPorVirgula(entry, catalogo, "npc-languages", ObterTexto("languages"));

        // Habilidades: special_abilities + ações lendárias (sem campo próprio, marcadas com prefixo)
        // + perícias/resistências sintetizadas em traits extras — nada é descartado silenciosamente.
        var traits = new List<(string nome, string descricao)>();
        foreach (Dictionary<string, object> item in ObterListaDeObjetos(objeto, "special_abilities"))
            traits.Add((ObterTextoDe(item, "name"), ObterTextoDe(item, "desc")));
        foreach (Dictionary<string, object> item in ObterListaDeObjetos(objeto, "legendary_actions"))
            traits.Add(($"[Lendária] {ObterTextoDe(item, "name")}", ObterTextoDe(item, "desc")));

        string pericias = FormatarPericias(objeto);
        if (!string.IsNullOrWhiteSpace(pericias)) traits.Add(("Perícias", pericias));

        string resistencias = FormatarResistencias(objeto);
        if (!string.IsNullOrWhiteSpace(resistencias)) traits.Add(("Resistências e Imunidades", resistencias));

        AdicionarObjectListNomeDescricao(entry, catalogo, "npc-traits", traits);

        // Ações — único ObjectList com 4 subcampos (nome/descrição/ataque/dano) em vez de 2.
        XmlFieldDef defAcoes = EncontrarDef(catalogo, "npc-actions");
        if (defAcoes?.SubFields != null && defAcoes.SubFields.Count >= 4)
        {
            XmlFieldDef defNome = defAcoes.SubFields[0];
            XmlFieldDef defDesc = defAcoes.SubFields[1];
            XmlFieldDef defAtaque = defAcoes.SubFields[2];
            XmlFieldDef defDano = defAcoes.SubFields[3];

            var acoes = new List<EntryData>();
            foreach (Dictionary<string, object> item in ObterListaDeObjetos(objeto, "actions"))
            {
                var acaoEntry = new EntryData { TopicTag = defAcoes.XmlItemTag ?? "action" };
                string nome = ObterTextoDe(item, "name");
                string desc = ObterTextoDe(item, "desc");
                string attackBonus = ObterTextoDe(item, "attack_bonus");
                string danoDado = ObterTextoDe(item, "damage_dice");
                string danoBonus = ObterTextoDe(item, "damage_bonus");

                if (!string.IsNullOrWhiteSpace(nome)) acaoEntry.Fields.Add(new FieldValue { Definition = defNome, TextValue = nome });
                if (!string.IsNullOrWhiteSpace(desc)) acaoEntry.Fields.Add(new FieldValue { Definition = defDesc, TextValue = desc });
                if (!string.IsNullOrWhiteSpace(attackBonus) && attackBonus != "0")
                    acaoEntry.Fields.Add(new FieldValue { Definition = defAtaque, TextValue = $"+{attackBonus}" });
                if (!string.IsNullOrWhiteSpace(danoDado))
                {
                    string danoTexto = string.IsNullOrWhiteSpace(danoBonus) || danoBonus == "0" ? danoDado : $"{danoDado}+{danoBonus}";
                    acaoEntry.Fields.Add(new FieldValue { Definition = defDano, TextValue = danoTexto });
                }
                acoes.Add(acaoEntry);
            }
            if (acoes.Count > 0) entry.Fields.Add(new FieldValue { Definition = defAcoes, SubEntries = acoes });
        }

        // Reações — mapeamento direto (mesmo formato nome/desc de special_abilities).
        var reacoes = new List<(string nome, string descricao)>();
        foreach (Dictionary<string, object> item in ObterListaDeObjetos(objeto, "reactions"))
            reacoes.Add((ObterTextoDe(item, "name"), ObterTextoDe(item, "desc")));
        AdicionarObjectListNomeDescricao(entry, catalogo, "npc-reactions", reacoes);

        return entry;
    }

    private static XmlFieldDef EncontrarDef(List<XmlFieldDef> catalogo, string fieldId)
    {
        foreach (XmlFieldDef def in catalogo)
            if (def.FieldId == fieldId) return def;
        return null;
    }

    private static List<Dictionary<string, object>> ObterListaDeObjetos(Dictionary<string, object> objeto, string chave)
    {
        var resultado = new List<Dictionary<string, object>>();
        if (objeto.TryGetValue(chave, out object valor) && valor is List<object> lista)
            foreach (object item in lista)
                if (item is Dictionary<string, object> sub)
                    resultado.Add(sub);
        return resultado;
    }

    private static string ObterTextoDe(Dictionary<string, object> objeto, string chave) =>
        objeto.TryGetValue(chave, out object v) ? FormatarValor(v) : string.Empty;

    private static void AdicionarListaSeparadaPorVirgula(EntryData entry, List<XmlFieldDef> catalogo, string fieldId, string textoComVirgulas)
    {
        if (string.IsNullOrWhiteSpace(textoComVirgulas)) return;
        XmlFieldDef def = EncontrarDef(catalogo, fieldId);
        if (def == null) return;

        var fv = new FieldValue { Definition = def };
        foreach (string parte in textoComVirgulas.Split(','))
        {
            string limpo = parte.Trim();
            if (!string.IsNullOrEmpty(limpo)) fv.ListValues.Add(limpo);
        }
        if (fv.ListValues.Count > 0) entry.Fields.Add(fv);
    }

    // Reaproveitado por traits (npc-traits) E reações (npc-reactions) — ambos têm exatamente 2
    // subcampos na mesma ordem no catálogo (nome, descrição), só o rótulo/tag muda.
    private static void AdicionarObjectListNomeDescricao(EntryData entry, List<XmlFieldDef> catalogo, string fieldId, List<(string nome, string descricao)> itens)
    {
        itens.RemoveAll(i => string.IsNullOrWhiteSpace(i.nome) && string.IsNullOrWhiteSpace(i.descricao));
        if (itens.Count == 0) return;

        XmlFieldDef def = EncontrarDef(catalogo, fieldId);
        if (def?.SubFields == null || def.SubFields.Count < 2) return;

        XmlFieldDef defNome = def.SubFields[0];
        XmlFieldDef defDescricao = def.SubFields[1];

        var fv = new FieldValue { Definition = def };
        foreach ((string nome, string descricao) in itens)
        {
            var sub = new EntryData { TopicTag = def.XmlItemTag ?? "item" };
            if (!string.IsNullOrWhiteSpace(nome)) sub.Fields.Add(new FieldValue { Definition = defNome, TextValue = nome });
            if (!string.IsNullOrWhiteSpace(descricao)) sub.Fields.Add(new FieldValue { Definition = defDescricao, TextValue = descricao });
            fv.SubEntries.Add(sub);
        }
        entry.Fields.Add(fv);
    }

    private static string FormatarPericias(Dictionary<string, object> objeto)
    {
        var partes = new List<string>();
        foreach (string chave in ChavesPericias)
            if (objeto.ContainsKey(chave))
                partes.Add($"{FormatarNomePericia(chave)} +{ObterTextoDe(objeto, chave)}");
        return string.Join(", ", partes);
    }

    private static string FormatarNomePericia(string chaveSnakeCase)
    {
        string[] partes = chaveSnakeCase.Split('_');
        for (int i = 0; i < partes.Length; i++)
            if (partes[i].Length > 0)
                partes[i] = char.ToUpperInvariant(partes[i][0]) + partes[i].Substring(1);
        return string.Join(" ", partes);
    }

    private static string FormatarResistencias(Dictionary<string, object> objeto)
    {
        var partes = new List<string>();
        void Adicionar(string chave, string label)
        {
            string valor = ObterTextoDe(objeto, chave);
            if (!string.IsNullOrWhiteSpace(valor)) partes.Add($"{label}: {valor}");
        }
        Adicionar("damage_vulnerabilities", "Vulnerabilidades");
        Adicionar("damage_resistances", "Resistências");
        Adicionar("damage_immunities", "Imunidades a Dano");
        Adicionar("condition_immunities", "Imunidades a Condição");
        return string.Join("; ", partes);
    }
}
