using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using UnityEngine;

/// <summary>
/// Busca um MonstroDado isolado por ID sem depender do MonstroCatalogoPanelUI estar aberto —
/// usado pelo token linkado a um monstro (MapTokenDraggable.ObterDadosDoMonstro) e pelo tooltip.
/// Mesma lógica de parsing de MonstroCatalogoPanelUI.ParsearCreatura, duplicada aqui de propósito
/// (evita acoplar o painel de navegação a uma busca pontual por ID).
/// </summary>
public static class MonstroCatalogoLoader
{
    private const string SistemaD5E = "quinta-edicao-srd";

    private static readonly Dictionary<string, MonstroDado> _cachePorId = new Dictionary<string, MonstroDado>();
    private static bool _indiceCarregado;

    public static string ObterCaminhoTokenPorId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return string.Empty;
        return Path.Combine(Application.persistentDataPath, "sistemas", SistemaD5E, "tokens", id + ".png");
    }

    public static MonstroDado CarregarPorId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        GarantirIndiceCarregado();
        _cachePorId.TryGetValue(id, out MonstroDado dado);
        return dado;
    }

    private static void GarantirIndiceCarregado()
    {
        if (_indiceCarregado) return;
        _indiceCarregado = true;

        string basePath = Path.Combine(Application.persistentDataPath, "sistemas", SistemaD5E);
        string[] pastas = { Path.Combine(basePath, "criaturas"), Path.Combine(basePath, "monstros") };

        foreach (string pasta in pastas)
        {
            if (!Directory.Exists(pasta)) continue;

            foreach (string arquivo in ImportadorConteudoUniversal.EnumerarArquivosXmlEJson(pasta))
            {
                MonstroDado dado = ParsearCreatura(arquivo);
                if (dado != null && !_cachePorId.ContainsKey(dado.id))
                    _cachePorId[dado.id] = dado;
            }
        }
    }

    private static MonstroDado ParsearCreatura(string caminho)
    {
        try
        {
            XElement root;
            if (ImportadorConteudoUniversal.EhArquivoJson(caminho))
            {
                string json = ImportadorConteudoUniversal.LerTextoComFallbackDeEncoding(caminho);
                string xml = json != null ? ImportadorConteudoUniversal.ConverterJsonParaXml(json, "monster") : null;
                if (xml == null)
                {
                    Debug.LogWarning($"[MonstroCatalogoLoader] JSON inválido em '{Path.GetFileName(caminho)}' — ignorando.");
                    return null;
                }
                root = XElement.Parse(xml);
            }
            else
            {
                root = XElement.Load(caminho);
            }
            MonstroDado dado = new MonstroDado
            {
                id   = (string)root.Attribute("id") ?? Path.GetFileNameWithoutExtension(caminho),
                nome = root.Element("name")?.Value  ?? Path.GetFileNameWithoutExtension(caminho),
                meta = root.Element("meta")?.Value  ?? string.Empty
            };

            XElement cats = root.Element("categories");
            if (cats != null)
                foreach (XElement cat in cats.Elements("category"))
                    if (!string.IsNullOrWhiteSpace(cat.Value))
                        dado.categorias.Add(cat.Value.Trim());

            XElement stats = root.Element("stats");
            if (stats != null)
            {
                dado.ca         = stats.Element("armor-class")?.Value ?? string.Empty;
                dado.hp         = stats.Element("hit-points")?.Value  ?? string.Empty;
                dado.velocidade = stats.Element("speed")?.Value       ?? string.Empty;
            }

            XElement attrs = root.Element("attributes");
            if (attrs != null)
            {
                foreach (XElement attr in attrs.Elements("attribute"))
                {
                    string attrName = ((string)attr.Attribute("name") ?? string.Empty).ToUpperInvariant();
                    string val      = attr.Value ?? string.Empty;
                    switch (attrName)
                    {
                        case "STR": dado.str   = val; break;
                        case "DEX": dado.dex   = val; break;
                        case "CON": dado.con   = val; break;
                        case "INT": dado.intel = val; break;
                        case "WIS": dado.wis   = val; break;
                        case "CHA": dado.cha   = val; break;
                    }
                }
            }

            XElement details = root.Element("details");
            if (details != null)
            {
                dado.pericias = details.Element("skills")?.Value    ?? string.Empty;
                dado.sentidos = details.Element("senses")?.Value    ?? string.Empty;
                dado.idiomas  = details.Element("languages")?.Value ?? string.Empty;
                dado.desafio  = details.Element("challenge")?.Value ?? string.Empty;
            }

            XElement actions = root.Element("actions");
            if (actions != null)
                foreach (XElement action in actions.Elements("action"))
                    dado.acoes.Add(new MonstroAcao
                    {
                        nome      = action.Element("name")?.Value        ?? string.Empty,
                        descricao = action.Element("description")?.Value ?? string.Empty
                    });

            return dado;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[MonstroCatalogoLoader] Erro ao parsear '{Path.GetFileName(caminho)}': {e.Message}");
            return null;
        }
    }
}
