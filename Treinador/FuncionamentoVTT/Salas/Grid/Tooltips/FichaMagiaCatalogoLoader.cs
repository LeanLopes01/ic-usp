using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

/// <summary>
/// Réplica mínima do catálogo local de magias que FichaGeralManager usa pra resolver o nome/nível
/// de uma magia a partir do ID salvo em LocalFichaData.magias_json (GrimorioSaveItem.id). O ID é um
/// hash determinístico do id lógico do XML + nome — precisa ser IDÊNTICO ao gerado lá pra bater
/// (GerarIdDeterministico copiado ipsis litteris de FichaGeralManager.cs).
/// </summary>
public static class FichaMagiaCatalogoLoader
{
    private const string PastaMagiasRelativa = "magias";

    // Strings cruas do XML (<metadata>, ver plano de Forma de Área/Detalhes de Magia) — vazio se a
    // tag não existir nesse arquivo ainda (nem toda magia foi editada com os campos novos, ver
    // TokenStatBlockBuilder pro fallback por texto livre quando faltam). Interpretação (parse pro
    // enum, cascata de prioridade, formatação pra exibição) fica em TokenStatBlockBuilder, não aqui.
    private struct MagiaCatalogoInfo
    {
        public string nome;
        public int level;
        public string descricao;
        public string formaXml;
        public string origemXml;
        public string tempoConjuracaoXml;
        public string rangeXml;
        public string componentesXml;
        public string duracaoXml;
    }

    private static readonly Dictionary<int, MagiaCatalogoInfo> _cache = new Dictionary<int, MagiaCatalogoInfo>();
    private static bool _carregado;

    public static (string nome, int level, string descricao, string formaXml, string origemXml,
        string tempoConjuracaoXml, string rangeXml, string componentesXml, string duracaoXml) ObterInfo(int id)
    {
        GarantirCarregado();
        if (_cache.TryGetValue(id, out MagiaCatalogoInfo info))
            return (info.nome, info.level, info.descricao, info.formaXml, info.origemXml,
                info.tempoConjuracaoXml, info.rangeXml, info.componentesXml, info.duracaoXml);

        return ($"Magia #{id}", 0, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
    }

    private static void GarantirCarregado()
    {
        if (_carregado) return;
        _carregado = true;

        string pasta = DndSrdLocalPath.Resolver(PastaMagiasRelativa);
        if (!Directory.Exists(pasta)) return;

        foreach (string arquivo in ImportadorConteudoUniversal.EnumerarArquivosXmlEJson(pasta, SearchOption.AllDirectories))
        {
            try
            {
                string xml;
                if (ImportadorConteudoUniversal.EhArquivoJson(arquivo))
                {
                    string json = ImportadorConteudoUniversal.LerTextoComFallbackDeEncoding(arquivo);
                    xml = json != null ? ImportadorConteudoUniversal.ConverterJsonParaXml(json, "spell") : null;
                    if (xml == null)
                    {
                        Debug.LogWarning($"[FichaMagiaCatalogoLoader] JSON inválido em '{Path.GetFileName(arquivo)}' — ignorando.");
                        continue;
                    }
                }
                else
                {
                    xml = File.ReadAllText(arquivo).Trim().Replace(((char)0xFEFF).ToString(), "");
                }
                XDocument doc = XDocument.Parse(xml);
                XElement root = doc.Element("spell") ?? doc.Root;
                if (root == null) continue;

                string nome = root.Element("name")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(nome)) continue;

                XElement metadata = root.Element("metadata");
                int.TryParse(metadata?.Element("level")?.Value ?? root.Element("level")?.Value, out int level);

                IEnumerable<string> componentesValores = metadata?.Element("components")?.Elements("component").Select(c => c.Value?.Trim());
                string componentesXml = componentesValores != null ? string.Join(", ", componentesValores.Where(v => !string.IsNullOrWhiteSpace(v))) : string.Empty;

                var info = new MagiaCatalogoInfo
                {
                    nome = nome,
                    level = level,
                    descricao = root.Element("description")?.Value?.Trim() ?? string.Empty,
                    formaXml = metadata?.Element("area")?.Value?.Trim() ?? string.Empty,
                    origemXml = metadata?.Element("origem")?.Value?.Trim() ?? string.Empty,
                    tempoConjuracaoXml = metadata?.Element("casting-time")?.Value?.Trim() ?? string.Empty,
                    rangeXml = metadata?.Element("range")?.Value?.Trim() ?? string.Empty,
                    componentesXml = componentesXml,
                    duracaoXml = metadata?.Element("duration")?.Value?.Trim() ?? string.Empty,
                };

                int id = GerarIdDeterministico(root.Attribute("id")?.Value, nome);
                _cache[id] = info;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FichaMagiaCatalogoLoader] Ignorando arquivo inválido '{Path.GetFileName(arquivo)}': {e.Message}");
            }
        }
    }

    private static int GerarIdDeterministico(string idLogico, string nome)
    {
        string chave = (idLogico ?? string.Empty) + "|" + (nome ?? string.Empty);
        unchecked
        {
            int hash = 23;
            for (int i = 0; i < chave.Length; i++) hash = (hash * 31) + chave[i];
            if (hash == int.MinValue) hash = int.MaxValue;
            hash = System.Math.Abs(hash);
            return hash == 0 ? 1 : hash;
        }
    }
}
