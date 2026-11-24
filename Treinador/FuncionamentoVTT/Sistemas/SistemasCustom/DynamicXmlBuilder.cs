using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEngine;

/// <summary>
/// Constrói e salva arquivos XML de forma totalmente dinâmica,
/// sem classes concretas — funciona para qualquer tópico do sistema.
/// </summary>
public static class DynamicXmlBuilder
{
    public static void SaveEntriesAsXml(List<EntryData> entries, string folderPath, string topicTag)
    {
        Directory.CreateDirectory(folderPath);

        // Uma entrada carregada de um arquivo existente SEMPRE volta pro MESMO caminho (ver
        // EntryData.CaminhoArquivoOrigem) — nunca recalcula um nome a partir do id/nome atual pra
        // ela. Sem isso, uma entrada cujo id não batesse com o nome do próprio arquivo (ex: arquivo
        // renomeado à mão, ou qualquer id/nome mudado na Estrutura depois de carregado) ganhava um
        // arquivo NOVO a cada salvamento, deixando o original pra trás como um duplicado órfão —
        // era exatamente assim que arquivos tipo "acolyte.xml" nasciam do nada ao lado de um
        // "backAcolito.xml" já existente com id="acolyte" mas nome de arquivo diferente.
        //
        // Só entradas NOVAS (sem CaminhoArquivoOrigem, ver CustomSystemManager.ClicouAdicionarEntrada)
        // ganham um caminho novo derivado do id/nome — e são as únicas que podem colidir entre si ou
        // com o caminho de uma entrada já existente, checado abaixo antes de escrever qualquer coisa.
        var caminhos = new string[entries.Count];
        for (int i = 0; i < entries.Count; i++)
        {
            caminhos[i] = !string.IsNullOrWhiteSpace(entries[i].CaminhoArquivoOrigem)
                ? entries[i].CaminhoArquivoOrigem
                : Path.Combine(folderPath, $"{ResolverEntryId(entries[i], i)}.xml");
        }

        var contagemPorCaminho = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
        foreach (string caminho in caminhos)
            contagemPorCaminho[caminho] = contagemPorCaminho.GetValueOrDefault(caminho) + 1;

        for (int i = 0; i < entries.Count; i++)
        {
            string filePath = caminhos[i];
            if (contagemPorCaminho[filePath] > 1)
            {
                VTTLog.ForjaError($"'{Path.GetFileName(filePath)}' — {contagemPorCaminho[filePath]} entradas " +
                    "deste tópico resolveram pro mesmo arquivo (id/nome duplicado entre entradas novas, ou " +
                    "uma entrada nova com o mesmo id de uma já existente). Nenhuma delas foi salva agora — " +
                    "ajuste o id/nome antes de salvar de novo.");
                continue;
            }

            XmlDocument doc = BuildDocument(entries[i], topicTag);

            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent      = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                Encoding    = System.Text.Encoding.UTF8
            };
            using XmlWriter writer = XmlWriter.Create(filePath, settings);
            doc.Save(writer);

            entries[i].CaminhoArquivoOrigem = filePath; // uma entrada recém-criada passa a ter dono a partir daqui
            VTTLog.Forja($"Salvo: {filePath}");
        }
    }

    // ── Construção do documento ───────────────────────────────────────────────

    private static XmlDocument BuildDocument(EntryData entry, string topicTag)
    {
        XmlDocument doc = new XmlDocument();
        doc.AppendChild(doc.CreateXmlDeclaration("1.0", "UTF-8", null));

        XmlElement root = doc.CreateElement(topicTag);
        doc.AppendChild(root);

        // Atributos primeiro
        foreach (FieldValue fv in entry.Fields)
            if (fv.Definition.Type == FieldType.Attribute && !string.IsNullOrWhiteSpace(fv.TextValue))
                root.SetAttribute(fv.Definition.XmlTag, fv.TextValue);

        // Elementos
        foreach (FieldValue fv in entry.Fields)
            if (fv.Definition.Type != FieldType.Attribute)
                WriteField(doc, root, fv);

        return doc;
    }

    private static void WriteField(XmlDocument doc, XmlElement parent, FieldValue fv)
    {
        XmlFieldDef def = fv.Definition;

        switch (def.Type)
        {
            case FieldType.Text:
            case FieldType.TextArea:
            case FieldType.Number:
            case FieldType.Image:
                if (!string.IsNullOrWhiteSpace(fv.TextValue))
                {
                    XmlElement el = doc.CreateElement(def.XmlTag);
                    el.InnerText = fv.TextValue;
                    parent.AppendChild(el);
                }
                break;

            case FieldType.UnitValue:
                if (!string.IsNullOrWhiteSpace(fv.TextValue))
                {
                    XmlElement el = doc.CreateElement(def.XmlTag);
                    if (!string.IsNullOrWhiteSpace(fv.UnitValue))
                        el.SetAttribute("unit", fv.UnitValue);
                    el.InnerText = fv.TextValue;
                    parent.AppendChild(el);
                }
                break;

            case FieldType.StringList:
                if (fv.ListValues.Count > 0)
                {
                    XmlElement list = doc.CreateElement(def.XmlTag);
                    parent.AppendChild(list);
                    string itemTag = string.IsNullOrWhiteSpace(def.XmlItemTag) ? "item" : def.XmlItemTag;
                    foreach (string val in fv.ListValues)
                    {
                        if (string.IsNullOrWhiteSpace(val)) continue;
                        XmlElement item = doc.CreateElement(itemTag);
                        item.InnerText = val;
                        list.AppendChild(item);
                    }
                }
                break;

            case FieldType.ObjectList:
                if (fv.SubEntries.Count > 0)
                {
                    XmlElement list = doc.CreateElement(def.XmlTag);
                    parent.AppendChild(list);
                    string itemTag = string.IsNullOrWhiteSpace(def.XmlItemTag) ? "item" : def.XmlItemTag;
                    foreach (EntryData sub in fv.SubEntries)
                    {
                        XmlElement el = doc.CreateElement(itemTag);
                        list.AppendChild(el);
                        // Atributos
                        foreach (FieldValue sfv in sub.Fields)
                            if (sfv.Definition.Type == FieldType.Attribute && !string.IsNullOrWhiteSpace(sfv.TextValue))
                                el.SetAttribute(sfv.Definition.XmlTag, sfv.TextValue);
                        // Texto próprio do elemento (ex: <option id="...">TEXTO</option>) — não é um
                        // filho, é o InnerText do próprio <item> (ver ParseFields/InferirSubCampos/
                        // XmlFieldDef.MarcadorTextoProprio). NUNCA passa por WriteField — "#text" não
                        // é um nome de tag XML válido, doc.CreateElement("#text") lançaria XmlException.
                        FieldValue textoProprio = sub.Fields.Find(f => f.Definition.XmlTag == XmlFieldDef.MarcadorTextoProprio);
                        if (textoProprio != null && !string.IsNullOrWhiteSpace(textoProprio.TextValue))
                            el.InnerText = textoProprio.TextValue;
                        // Elementos de verdade (filhos)
                        foreach (FieldValue sfv in sub.Fields)
                            if (sfv.Definition.Type != FieldType.Attribute && sfv.Definition.XmlTag != XmlFieldDef.MarcadorTextoProprio)
                                WriteField(doc, el, sfv);
                    }
                }
                break;

            case FieldType.Group:
                if (fv.GroupFields.Count > 0)
                {
                    XmlElement group = doc.CreateElement(def.XmlTag);
                    parent.AppendChild(group);
                    // Mesma regra de ObjectList acima — "#text" (se algum dia aparecer aqui) é o
                    // InnerText do próprio group, não um filho; nunca passa por WriteField.
                    FieldValue textoProprio = fv.GroupFields.Find(f => f.Definition.XmlTag == XmlFieldDef.MarcadorTextoProprio);
                    if (textoProprio != null && !string.IsNullOrWhiteSpace(textoProprio.TextValue))
                        group.InnerText = textoProprio.TextValue;
                    foreach (FieldValue gfv in fv.GroupFields)
                        if (gfv.Definition.XmlTag != XmlFieldDef.MarcadorTextoProprio)
                            WriteField(doc, group, gfv);
                }
                break;
        }
    }

    // ── Carregamento ─────────────────────────────────────────────────────────

    public static List<EntryData> LoadEntriesFromFolder(string folderPath, string topicTag, List<XmlFieldDef> catalog = null)
    {
        catalog ??= XmlFieldDef.Catalog;

        var result = new List<EntryData>();
        if (!Directory.Exists(folderPath)) return result;

        string[] files = Directory.GetFiles(folderPath, "*.xml");
        System.Array.Sort(files);

        foreach (string file in files)
        {
            try
            {
                XmlDocument doc = new XmlDocument();
                doc.Load(file);
                XmlElement root = doc.DocumentElement;
                if (root == null) continue;

                EntryData entry = new EntryData { TopicTag = topicTag, CaminhoArquivoOrigem = file };
                entry.Fields = ParseFields(root, catalog);
                result.Add(entry);
            }
            catch (System.Exception e)
            {
                VTTLog.ForjaError($"Erro ao carregar '{file}': {e.Message}");
            }
        }

        return result;
    }

    private static List<FieldValue> ParseFields(XmlElement root, List<XmlFieldDef> catalog)
    {
        var fields = new List<FieldValue>();

        foreach (XmlFieldDef def in catalog)
        {
            if (def.Type != FieldType.Attribute) continue;
            if (root.HasAttribute(def.XmlTag))
                fields.Add(new FieldValue { Definition = def, TextValue = root.GetAttribute(def.XmlTag) });
        }

        foreach (XmlFieldDef def in catalog)
        {
            if (def.Type == FieldType.Attribute) continue;

            // Sub-campo de "texto próprio" (ex: <option id="...">TEXTO</option>) — não é um filho
            // elemento, é o InnerText do próprio "root" (ver InferirSubCampos/WriteField/
            // XmlFieldDef.MarcadorTextoProprio). Sem este caso, esse texto some da leitura por
            // completo — root[def.XmlTag] nunca acharia um elemento chamado "#text".
            if (def.XmlTag == XmlFieldDef.MarcadorTextoProprio)
            {
                fields.Add(new FieldValue { Definition = def, TextValue = (root.InnerText ?? "").Trim() });
                continue;
            }

            XmlElement el = root[def.XmlTag];
            if (el == null) continue;

            FieldValue fv = new FieldValue { Definition = def };
            ParseFieldValue(el, def, fv);
            fields.Add(fv);
        }

        return fields;
    }

    private static void ParseFieldValue(XmlElement el, XmlFieldDef def, FieldValue fv)
    {
        switch (def.Type)
        {
            case FieldType.Text:
            case FieldType.TextArea:
            case FieldType.Number:
            case FieldType.Image:
                fv.TextValue = el.InnerText;
                break;

            case FieldType.UnitValue:
                fv.TextValue = el.InnerText;
                fv.UnitValue = el.GetAttribute("unit");
                break;

            case FieldType.StringList:
                string itemTag = string.IsNullOrWhiteSpace(def.XmlItemTag) ? "item" : def.XmlItemTag;
                foreach (XmlNode node in el.ChildNodes)
                    if (node is XmlElement item && item.Name == itemTag)
                        fv.ListValues.Add(item.InnerText);
                break;

            case FieldType.ObjectList:
                string objTag = string.IsNullOrWhiteSpace(def.XmlItemTag) ? "item" : def.XmlItemTag;
                foreach (XmlNode node in el.ChildNodes)
                {
                    if (node is XmlElement subEl && subEl.Name == objTag)
                    {
                        EntryData sub = new EntryData { TopicTag = objTag };
                        sub.Fields = ParseFields(subEl, def.SubFields);
                        fv.SubEntries.Add(sub);
                    }
                }
                break;

            case FieldType.Group:
                foreach (XmlFieldDef subDef in def.SubFields)
                {
                    // Mesmo caso especial de ParseFields acima — ver XmlFieldDef.MarcadorTextoProprio.
                    if (subDef.XmlTag == XmlFieldDef.MarcadorTextoProprio)
                    {
                        fv.GroupFields.Add(new FieldValue { Definition = subDef, TextValue = (el.InnerText ?? "").Trim() });
                        continue;
                    }

                    XmlElement subEl = el[subDef.XmlTag];
                    if (subEl == null) continue;
                    FieldValue subFv = new FieldValue { Definition = subDef };
                    ParseFieldValue(subEl, subDef, subFv);
                    fv.GroupFields.Add(subFv);
                }
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public static string ResolverEntryId(EntryData entry, int index)
    {
        foreach (FieldValue fv in entry.Fields)
        {
            if (fv.Definition.FieldId == "attr-id" && !string.IsNullOrWhiteSpace(fv.TextValue))
                return fv.TextValue;
            if (fv.Definition.FieldId == "name" && !string.IsNullOrWhiteSpace(fv.TextValue))
                return FormatarId(fv.TextValue);
        }
        return $"entry-{index + 1}";
    }

    private static string FormatarId(string texto)
    {
        texto = texto.Trim().ToLowerInvariant().Replace(" ", "-");
        return Regex.Replace(texto, @"[^a-z0-9\-]", "").Trim('-');
    }
}
