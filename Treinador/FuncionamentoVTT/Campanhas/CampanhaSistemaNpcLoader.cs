using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

/// <summary>
/// Lê os monstros/criaturas de um sistema (ex: persistentDataPath/sistemas/quinta-edicao-srd/) e
/// converte cada arquivo num EntryData no mesmo formato de Dnd5eNpcFieldCatalog — pra que
/// CampanhaNpcGaleriaUI possa listar NPCs do sistema lado a lado com os criados/importados da
/// campanha, reaproveitando CampanhaNpcCardUI (detalhes expansíveis, token) e
/// CampanhaMapManager.TentarSoltarTokenNoMapa (drag pro grid) sem nenhum código novo neles.
///
/// Escopo desta primeira versão: só as pastas "monstros" e "criaturas" na raiz do sistema (formato
/// usado hoje pela quinta-edicao-srd — raiz &lt;monster id="..."&gt;/&lt;creature id="..."&gt; com
/// &lt;stats&gt;/&lt;details&gt; aninhados). Se um sistema não tiver essas pastas, simplesmente não
/// contribui NPCs nenhum — não há erro, é best-effort igual a CampanhaNpcImporter.
///
/// NPCs do sistema são sempre EntryData SINTÉTICOS — nunca têm CaminhoArquivoOrigem, nunca são
/// gravados de volta em persistentDataPath/sistemas/... por este loader. Editar um (via "Editar" no
/// card) e salvar cria uma cópia NOVA dentro da pasta de NPCs da campanha (mesmo comportamento de
/// "sem CaminhoArquivoOrigem = arquivo novo" de DynamicXmlBuilder.SaveEntriesAsXml) — o original do
/// sistema nunca é tocado.
///
/// Um token solto no grid a partir de um NPC de sistema (ver CampanhaMapManager.
/// TentarSoltarTokenNoMapa) sobrevive a um reload da cena porque CampanhaMapManager.RestaurarTokens
/// também chama este loader (via CampanhaMapManager.DefinirSistemaCampanha, ligado por
/// CampanhasManager.AbrirCampanha) pra re-resolver o npcId salvo, não só a pasta de NPCs da própria
/// campanha — um NPC criado na campanha com o MESMO nome de um monstro do sistema tem prioridade
/// nessa resolução (ver o comentário em RestaurarTokens).
/// </summary>
public static class CampanhaSistemaNpcLoader
{
    private static readonly string[] TopicosDeMonstro = { "monstros", "criaturas" };
    private static readonly string[] ExtensoesDeToken = { ".png", ".jpg", ".jpeg" };

    public static List<EntryData> CarregarNpcsDoSistema(string sistemaFolderName)
    {
        var resultado = new List<EntryData>();
        if (string.IsNullOrWhiteSpace(sistemaFolderName)) return resultado;

        string pastaSistema = Path.Combine(Application.persistentDataPath, "sistemas", sistemaFolderName);
        string pastaTokens  = Path.Combine(pastaSistema, "tokens");

        foreach (string topico in TopicosDeMonstro)
        {
            string pastaTopico = Path.Combine(pastaSistema, topico);
            if (!Directory.Exists(pastaTopico)) continue;

            foreach (string arquivo in Directory.GetFiles(pastaTopico, "*.xml"))
            {
                EntryData entry = TentarConverter(arquivo, pastaTokens);
                if (entry != null) resultado.Add(entry);
            }
        }

        return resultado;
    }

    private static EntryData TentarConverter(string arquivo, string pastaTokens)
    {
        try
        {
            XDocument doc = XDocument.Load(arquivo);
            XElement raiz = doc.Root;
            if (raiz == null) return null;

            string id = raiz.Attribute("id")?.Value ?? Path.GetFileNameWithoutExtension(arquivo);
            return ConverterMonstro(raiz, id, pastaTokens);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CampanhaSistemaNpcLoader] Falha ao ler '{Path.GetFileName(arquivo)}': {e.Message}");
            return null;
        }
    }

    private static EntryData ConverterMonstro(XElement raiz, string id, string pastaTokens)
    {
        var entry = new EntryData { TopicTag = "npcs" };
        void AdicionarTexto(XmlFieldDef def, string texto)
        {
            if (def == null || string.IsNullOrWhiteSpace(texto)) return;
            entry.Fields.Add(new FieldValue { Definition = def, TextValue = texto.Trim() });
        }

        AdicionarTexto(Dnd5eNpcFieldCatalog.NomeField, raiz.Element("name")?.Value);

        string retrato = ResolverTokenPath(pastaTokens, id);
        AdicionarTexto(Dnd5eNpcFieldCatalog.RetratoField, retrato);

        // "Aberração Grande, Leal e Mau" — tipo/tamanho antes da última vírgula, alinhamento depois.
        // Heurística "melhor esforço" (mesmo espírito de CampanhaNpcImporter): nem todo alinhamento é
        // uma palavra só, então divide pela ÚLTIMA vírgula, não pela primeira.
        string meta = raiz.Element("meta")?.Value;
        if (!string.IsNullOrWhiteSpace(meta))
        {
            int idx = meta.LastIndexOf(',');
            if (idx >= 0)
            {
                AdicionarTexto(BuscarField("npc-size"), meta.Substring(0, idx));
                AdicionarTexto(BuscarField("npc-alignment"), meta.Substring(idx + 1));
            }
            else
            {
                AdicionarTexto(BuscarField("npc-size"), meta);
            }
        }

        XElement stats = raiz.Element("stats");
        AdicionarTexto(BuscarField("npc-ac"),    stats?.Element("armor-class")?.Value);
        AdicionarTexto(BuscarField("npc-hp"),    stats?.Element("hit-points")?.Value);
        AdicionarTexto(BuscarField("npc-speed"), stats?.Element("speed")?.Value);

        XElement details = raiz.Element("details");
        AdicionarTexto(BuscarField("npc-cr"),    details?.Element("challenge")?.Value);
        AdicionarTexto(BuscarField("npc-saves"), details?.Element("saving-throws")?.Value);
        AdicionarListaSeparadaPorVirgula(entry, BuscarField("npc-senses"),    details?.Element("senses")?.Value);
        AdicionarListaSeparadaPorVirgula(entry, BuscarField("npc-languages"), details?.Element("languages")?.Value);

        // Ações lendárias não têm campo próprio no catálogo — entram junto de npc-traits com um
        // prefixo, mesma solução já usada por CampanhaNpcImporter.ConverterMonstro5eSrd pro dataset
        // JSON "5e SRD Monsters".
        var traits = new List<(string nome, string descricao)>();
        foreach (XElement trait in raiz.Element("traits")?.Elements("trait") ?? Enumerable.Empty<XElement>())
            traits.Add((trait.Element("name")?.Value, trait.Element("description")?.Value));
        foreach (XElement legendaria in raiz.Element("legendary-actions")?.Elements("action") ?? Enumerable.Empty<XElement>())
            traits.Add(($"[Lendária] {legendaria.Element("name")?.Value}", legendaria.Element("description")?.Value));
        AdicionarObjectListNomeDescricao(entry, BuscarField("npc-traits"), traits);

        var acoes = new List<(string nome, string descricao)>();
        foreach (XElement acao in raiz.Element("actions")?.Elements("action") ?? Enumerable.Empty<XElement>())
            acoes.Add((acao.Element("name")?.Value, acao.Element("description")?.Value));
        AdicionarObjectListNomeDescricao(entry, BuscarField("npc-actions"), acoes);

        return entry;
    }

    private static string ResolverTokenPath(string pastaTokens, string id)
    {
        if (string.IsNullOrWhiteSpace(pastaTokens) || string.IsNullOrWhiteSpace(id)) return null;
        foreach (string extensao in ExtensoesDeToken)
        {
            string caminho = Path.Combine(pastaTokens, id + extensao);
            if (File.Exists(caminho)) return caminho;
        }
        return null;
    }

    private static XmlFieldDef BuscarField(string fieldId) =>
        Dnd5eNpcFieldCatalog.Fields.Find(f => f.FieldId == fieldId);

    private static void AdicionarListaSeparadaPorVirgula(EntryData entry, XmlFieldDef def, string textoComVirgulas)
    {
        if (def == null || string.IsNullOrWhiteSpace(textoComVirgulas) || textoComVirgulas.Trim() == "—") return;

        var fv = new FieldValue { Definition = def };
        foreach (string parte in textoComVirgulas.Split(','))
        {
            string limpo = parte.Trim();
            if (!string.IsNullOrEmpty(limpo)) fv.ListValues.Add(limpo);
        }
        if (fv.ListValues.Count > 0) entry.Fields.Add(fv);
    }

    // Reaproveitado por traits (npc-traits) e ações (npc-actions) — ambos têm nome/descrição como os
    // dois primeiros subcampos no catálogo (Dnd5eNpcFieldCatalog), só o resto muda.
    private static void AdicionarObjectListNomeDescricao(EntryData entry, XmlFieldDef def, List<(string nome, string descricao)> itens)
    {
        itens.RemoveAll(i => string.IsNullOrWhiteSpace(i.nome) && string.IsNullOrWhiteSpace(i.descricao));
        if (itens.Count == 0 || def?.SubFields == null || def.SubFields.Count < 2) return;

        XmlFieldDef defNome = def.SubFields[0];
        XmlFieldDef defDescricao = def.SubFields[1];

        var fv = new FieldValue { Definition = def };
        foreach ((string nome, string descricao) in itens)
        {
            var sub = new EntryData { TopicTag = def.XmlItemTag ?? "item" };
            if (!string.IsNullOrWhiteSpace(nome)) sub.Fields.Add(new FieldValue { Definition = defNome, TextValue = nome.Trim() });
            if (!string.IsNullOrWhiteSpace(descricao)) sub.Fields.Add(new FieldValue { Definition = defDescricao, TextValue = descricao.Trim() });
            fv.SubEntries.Add(sub);
        }
        entry.Fields.Add(fv);
    }
}
