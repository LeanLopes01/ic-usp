using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using UnityEngine;

/// <summary>
/// Réplica mínima do catálogo local de itens (equipamentos/consumíveis) que FichaGeralManager usa
/// pra resolver nome/descrição a partir do ID salvo em LocalFichaData.inventario_json
/// (InventorySaveItem.id). Mesmo hash determinístico de FichaMagiaCatalogoLoader/FichaGeralManager.
/// Não faz a resolução de "melhor XML com stats" que FichaGeralManager faz (isso é pra combate de
/// verdade — aqui só precisamos de nome/descrição pra identificar itens de cura no tooltip).
/// </summary>
public static class ItemCatalogoLoader
{
    private const string PastaItensRelativa = "itens";

    private static readonly Dictionary<int, (string nome, string descricao)> _cache = new Dictionary<int, (string, string)>();
    private static bool _carregado;

    public static (string nome, string descricao) ObterInfo(int id)
    {
        GarantirCarregado();
        return _cache.TryGetValue(id, out var dados) ? dados : (string.Empty, string.Empty);
    }

    private static void GarantirCarregado()
    {
        if (_carregado) return;
        _carregado = true;

        string pasta = DndSrdLocalPath.Resolver(PastaItensRelativa);
        if (!Directory.Exists(pasta)) return;

        foreach (string arquivo in ImportadorConteudoUniversal.EnumerarArquivosXmlEJson(pasta, SearchOption.AllDirectories))
        {
            try
            {
                string xml;
                if (ImportadorConteudoUniversal.EhArquivoJson(arquivo))
                {
                    string json = ImportadorConteudoUniversal.LerTextoComFallbackDeEncoding(arquivo);
                    xml = json != null ? ImportadorConteudoUniversal.ConverterJsonParaXml(json, "item") : null;
                    if (xml == null)
                    {
                        Debug.LogWarning($"[ItemCatalogoLoader] JSON inválido em '{Path.GetFileName(arquivo)}' — ignorando.");
                        continue;
                    }
                }
                else
                {
                    xml = File.ReadAllText(arquivo).Trim().Replace(((char)0xFEFF).ToString(), "");
                }
                XDocument doc = XDocument.Parse(xml);
                XElement root = doc.Element("item") ?? doc.Root;
                if (root == null) continue;

                string nome = root.Element("name")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(nome)) continue;

                string descricao = root.Element("description")?.Value?.Trim() ?? string.Empty;
                string efeito = root.Element("properties")?.Element("effect")?.Value?.Trim();
                if (!string.IsNullOrWhiteSpace(efeito))
                    descricao = string.IsNullOrEmpty(descricao) ? efeito : $"{descricao} {efeito}";

                int id = GerarIdDeterministico(root.Attribute("id")?.Value, nome);
                _cache[id] = (nome, descricao);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ItemCatalogoLoader] Ignorando arquivo inválido '{Path.GetFileName(arquivo)}': {e.Message}");
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
