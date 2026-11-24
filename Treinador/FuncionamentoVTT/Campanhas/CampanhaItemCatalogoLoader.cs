using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

/// <summary>
/// Carregamento do catálogo de itens SRD (D&amp;D) para uso em Campanhas (ver CampanhaNpcLoreUI).
///
/// Cópia INDEPENDENTE do carregamento equivalente em Fichas/D&amp;D/CriarFicha/EquipmentSelector.cs —
/// duplicado de propósito, e não extraído/compartilhado, pra edição/manutenção deste recurso de
/// campanha nunca arriscar quebrar a loja de equipamento da ficha de jogador, que é um sistema já
/// em uso. Mesmo algoritmo (mesmo dedup de arquivos duplicados com/sem stats reais).
/// </summary>
public static class CampanhaItemCatalogoLoader
{
    [Serializable]
    public class ItemData
    {
        public int    id;
        public string nome;
        public string categoria;
        public string xml;
        public string idLogico;
    }

    private static readonly Dictionary<string, Dictionary<int, ItemData>> _cachePorPasta
        = new Dictionary<string, Dictionary<int, ItemData>>();
    private static readonly Dictionary<string, Dictionary<string, List<ItemData>>> _cachePorCategoria
        = new Dictionary<string, Dictionary<string, List<ItemData>>>();

    private static readonly char[] CaracteresBomParaRemover = { '﻿' };

    /// <summary>Carrega (ou devolve do cache) o catálogo de itens da pasta relativa informada — ver
    /// DndSrdLocalPath.Resolver para a resolução do caminho real (persistentDataPath ou StreamingAssets).</summary>
    public static Dictionary<int, ItemData> Carregar(string pastaRelativa, out Dictionary<string, List<ItemData>> porCategoria)
    {
        if (_cachePorPasta.TryGetValue(pastaRelativa, out Dictionary<int, ItemData> cacheado))
        {
            porCategoria = _cachePorCategoria[pastaRelativa];
            return cacheado;
        }

        var todosOsItens      = new Dictionary<int, ItemData>();
        var itensPorCategoria = new Dictionary<string, List<ItemData>>();

        try
        {
            string pasta = DndSrdLocalPath.Resolver(pastaRelativa);
            if (!Directory.Exists(pasta))
            {
                Debug.LogError("[CampanhaItemCatalogoLoader] Pasta local de itens não encontrada: " + pasta);
                porCategoria = itensPorCategoria;
                return todosOsItens;
            }

            foreach (string arquivo in Directory.GetFiles(pasta, "*.xml", SearchOption.AllDirectories))
            {
                try
                {
                    string xml = File.ReadAllText(arquivo, Encoding.UTF8).Trim().TrimStart(CaracteresBomParaRemover);
                    XDocument doc = XDocument.Parse(xml);
                    XElement root = doc.Element("item") ?? doc.Root;
                    if (root == null) continue;

                    string nome = root.Element("name")?.Value?.Trim();
                    if (string.IsNullOrEmpty(nome)) continue;

                    string categoria = root.Element("metadata")?.Element("category")?.Value?.Trim();
                    if (string.IsNullOrEmpty(categoria)) categoria = "Outros";

                    string idLogico = root.Attribute("id")?.Value;
                    int id = GerarIdDeterministico(idLogico, nome);

                    ItemData item = new ItemData
                    {
                        id = id,
                        nome = nome,
                        categoria = categoria,
                        xml = xml,
                        idLogico = idLogico
                    };

                    // Existem arquivos duplicados com o mesmo id lógico (ex: escudos/Escudo.xml e
                    // Escudo_2.xml, ambos id="shield"): um traz o bloco <armor>/<weapon> completo,
                    // o outro é um placeholder sem stats. Sem esta checagem, o placeholder podia
                    // sobrescrever a versão completa dependendo da ordem de leitura.
                    if (todosOsItens.TryGetValue(id, out ItemData existente) && existente != null)
                    {
                        bool existenteTemStats = existente.xml.Contains("<armor>") || existente.xml.Contains("<weapon>");
                        bool novoTemStats = xml.Contains("<armor>") || xml.Contains("<weapon>");
                        if (existenteTemStats && !novoTemStats) continue;
                    }

                    todosOsItens[id] = item;
                    if (!itensPorCategoria.ContainsKey(categoria)) itensPorCategoria[categoria] = new List<ItemData>();
                    itensPorCategoria[categoria].Add(item);
                }
                catch (Exception eArquivo)
                {
                    Debug.LogWarning("[CampanhaItemCatalogoLoader] Falha ao ler item local em '" + arquivo + "': " + eArquivo.Message);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("[CampanhaItemCatalogoLoader] Erro no fallback local de itens: " + e.Message);
        }

        _cachePorPasta[pastaRelativa]     = todosOsItens;
        _cachePorCategoria[pastaRelativa] = itensPorCategoria;
        porCategoria = itensPorCategoria;
        return todosOsItens;
    }

    private static int GerarIdDeterministico(string idLogico, string nome)
    {
        string chave = (idLogico ?? "") + "|" + (nome ?? "");
        unchecked
        {
            int hash = 23;
            for (int i = 0; i < chave.Length; i++) hash = (hash * 31) + chave[i];
            if (hash == int.MinValue) hash = int.MaxValue;
            hash = Mathf.Abs(hash);
            return hash == 0 ? 1 : hash;
        }
    }
}
