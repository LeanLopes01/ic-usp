using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

[Serializable]
public class ItemAcaoEntrada
{
    public int id;
    public string nome;
    public string categoria;
    public string xml;
    public string equipSlot;
}

public class SugestaoAcao 
{
    public string nomeAcao;
    public int bonusAcerto;
    public string danoBase;
    public int bonusDano;
    public string tipoDano;
    public string distancia;
    public string critico;
    public string tipoAcao;
    public string resumo;
}

public class ActionRecommender : MonoBehaviour
{
    private bool cacheLocalCarregado = false;
    private readonly Dictionary<string, string> xmlLocalPorNome = new Dictionary<string, string>();
    private readonly Dictionary<string, string> xmlLocalPorIdXml = new Dictionary<string, string>();

    public List<SugestaoAcao> GerarSugestoes(
        List<ItemAcaoEntrada> inventario,
        IEnumerable<string> proficienciasArmas,
        IEnumerable<string> habilidadesPersonagem)
    {
        List<SugestaoAcao> sugestoes = new List<SugestaoAcao>();
        if (inventario == null || inventario.Count == 0) return sugestoes;

        HashSet<string> profNorm = new HashSet<string>(
            (proficienciasArmas ?? Enumerable.Empty<string>()).Select(Normalizar));

        bool temHexblade = (habilidadesPersonagem ?? Enumerable.Empty<string>())
            .Select(Normalizar)
            .Any(h => h.Contains("hexblade") || h.Contains("laminamaldita") || h.Contains("malditablade"));

        CarregarCacheArmasLocaisSeNecessario();

        // Analisa TODO o inventário, equipado ou não — o jogador precisa ver sugestões de
        // ação para todas as armas que possui, não só as que estão marcadas como equipadas.
        var armasEquipadas = inventario.ToList();

        VTTLog.Rules($"ActionRecommender: {armasEquipadas.Count} itens para analisar");

        foreach (var invItem in armasEquipadas)
        {
            string xmlBruto = ResolverXmlItem(invItem);
            if (string.IsNullOrWhiteSpace(xmlBruto))
            {
                VTTLog.RulesWarn($"ActionRecommender: item sem XML: {invItem.nome} (id={invItem.id})");
                continue;
            }

            string xmlLower = xmlBruto.ToLowerInvariant();
            bool temTagDano = xmlLower.Contains("<damage>");
            bool categoriaArma = Normalizar(invItem.categoria).Contains("arma");

            VTTLog.Rules($"ActionRecommender: analisando '{invItem.nome}' | categoria={invItem.categoria} | temTagDano={temTagDano} | categoriaArma={categoriaArma}");

            if (!temTagDano && !categoriaArma)
            {
                VTTLog.Rules($"ActionRecommender: ignorado (sem dano/arma): {invItem.nome}");
                continue;
            }

            try
            {
                XDocument doc = XDocument.Parse(xmlBruto);
                string danoRoll = LerDano(doc);
                string tipoDano = LerValor(doc, "damage-type", "dmgType", "damageType");
                string critico = LerValor(doc, "crit", "critical", "critical-range");
                string tipoArmaXml = LerValor(doc, "type");

                if (string.IsNullOrWhiteSpace(danoRoll))
                {
                    VTTLog.RulesWarn($"ActionRecommender: XML sem dano utilizável. Item={invItem.nome}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(tipoDano)) tipoDano = "dano";
                if (string.IsNullOrWhiteSpace(critico)) critico = "20";

                var props = doc.Descendants("property").Select(p => p.Value ?? "").ToList();
                bool isFinesse = props.Any(p => Normalizar(p).Contains("finesse") || Normalizar(p).Contains("acuidade"))
                    || xmlLower.Contains("finesse") || xmlLower.Contains("acuidade");
                bool isRanged = props.Any(p => Normalizar(p).Contains("ranged") || Normalizar(p).Contains("distancia"))
                    || xmlLower.Contains("ranged") || xmlLower.Contains("distance") || xmlLower.Contains("à distância")
                    || Normalizar(tipoArmaXml).Contains("ranged");

                // <range> normalmente tem sub-tags (<melee>/<ranged> para armas de arremesso,
                // <normal>/<long> para armas de longo alcance) — ler .Value do nó pai concatena o
                // texto de todos os filhos sem separador (ex: "520/60" em vez de "5" e "20/60"
                // separados). LerAlcance lê a sub-tag certa para o tipo de ataque já identificado.
                string range = LerAlcance(doc, isRanged);
                if (string.IsNullOrWhiteSpace(range)) range = "1,5m";

                int modForca = Mathf.FloorToInt((GlobalFichaData.forca - 10) / 2f);
                int modDestreza = Mathf.FloorToInt((GlobalFichaData.destreza - 10) / 2f);
                int modCarisma = Mathf.FloorToInt((GlobalFichaData.carisma - 10) / 2f);

                int modUsado = modForca;
                string atributo = "Força";

                if (isRanged)
                {
                    modUsado = modDestreza;
                    atributo = "Destreza";
                }
                else if (isFinesse)
                {
                    if (modDestreza >= modForca)
                    {
                        modUsado = modDestreza;
                        atributo = "Destreza";
                    }
                }

                if (temHexblade && modCarisma > modUsado)
                {
                    modUsado = modCarisma;
                    atributo = "Carisma (Hexblade)";
                }

                bool temProficiencia = PossuiProficienciaArma(invItem, profNorm);
                int bonusProf = temProficiencia ? Mathf.FloorToInt((GlobalFichaData.levelPersonagem - 1) / 4f) + 2 : 0;

                string nomeArma = string.IsNullOrWhiteSpace(invItem.nome) ? $"Item {invItem.id}" : invItem.nome;
                string danoCalculado = FormatarDano(danoRoll, modUsado);

                var novaAcao = new SugestaoAcao
                {
                    nomeAcao = nomeArma,
                    bonusAcerto = modUsado + bonusProf,
                    danoBase = danoCalculado,
                    bonusDano = modUsado,
                    tipoDano = tipoDano,
                    distancia = range,
                    critico = string.IsNullOrWhiteSpace(critico) ? "20" : critico,
                    tipoAcao = isRanged ? "Distância" : "Corpo a Corpo",
                    resumo = $"Atributo: {atributo} | Proficiência: {(temProficiencia ? "Sim" : "Não")} | Acerto: +{modUsado + bonusProf} | Dano: {danoCalculado}"
                };

                sugestoes.Add(novaAcao);

                VTTLog.Rules($"ActionRecommender: sugestão criada: {novaAcao.nomeAcao} | dano={novaAcao.danoBase} | tipo={novaAcao.tipoAcao}");
            }
            catch (Exception ex)
            {
                VTTLog.RulesWarn($"ActionRecommender: erro ao ler XML de '{invItem.nome}': {ex.Message}");
            }
        }

        return sugestoes;
    }

    private string ResolverXmlItem(ItemAcaoEntrada item)
    {
        if (!string.IsNullOrWhiteSpace(item?.xml))
            return item.xml.Trim().Replace("\uFEFF", "");

        if (item == null) return "";

        string nomeNorm = Normalizar(item.nome);
        if (!string.IsNullOrWhiteSpace(nomeNorm) && xmlLocalPorNome.TryGetValue(nomeNorm, out string xmlPorNome))
            return xmlPorNome;

        string idNorm = Normalizar(item.id.ToString());
        if (xmlLocalPorIdXml.TryGetValue(idNorm, out string xmlPorId))
            return xmlPorId;

        return "";
    }

    private void CarregarCacheArmasLocaisSeNecessario()
    {
        if (cacheLocalCarregado) return;
        cacheLocalCarregado = true;

        try
        {
            string pasta = DndSrdLocalPath.Resolver("sistemas/quinta-edicao-srd/itens/armas");
            if (!Directory.Exists(pasta))
            {
                VTTLog.RulesWarn("ActionRecommender: pasta local de armas não encontrada: " + pasta);
                return;
            }

            foreach (var arquivo in Directory.GetFiles(pasta, "*.xml", SearchOption.TopDirectoryOnly))
            {
                string xml = File.ReadAllText(arquivo).Trim().Replace("\uFEFF", "");
                if (string.IsNullOrWhiteSpace(xml)) continue;

                XDocument doc = XDocument.Parse(xml);
                XElement item = doc.Element("item") ?? doc.Root;
                if (item == null) continue;

                string nome = item.Element("name")?.Value ?? Path.GetFileNameWithoutExtension(arquivo);
                string idXml = item.Attribute("id")?.Value ?? "";

                string nomeNorm = Normalizar(nome);
                if (!string.IsNullOrWhiteSpace(nomeNorm) && !xmlLocalPorNome.ContainsKey(nomeNorm))
                    xmlLocalPorNome[nomeNorm] = xml;

                string idNorm = Normalizar(idXml);
                if (!string.IsNullOrWhiteSpace(idNorm) && !xmlLocalPorIdXml.ContainsKey(idNorm))
                    xmlLocalPorIdXml[idNorm] = xml;
            }

            VTTLog.Rules($"ActionRecommender: cache local carregado. Nomes={xmlLocalPorNome.Count} | IDs={xmlLocalPorIdXml.Count}");
        }
        catch (Exception ex)
        {
            VTTLog.RulesWarn("ActionRecommender: falha ao carregar cache local de armas: " + ex.Message);
        }
    }

    private string LerValor(XDocument doc, params string[] nomes)
    {
        foreach (string nome in nomes)
        {
            string valor = doc.Descendants(nome).FirstOrDefault()?.Value;
            if (!string.IsNullOrWhiteSpace(valor)) return valor.Trim();
        }

        return "";
    }

    private string LerDano(XDocument doc)
    {
        string valor = LerValor(doc, "one-handed", "damage", "dmg1", "damage-roll");
        return string.IsNullOrWhiteSpace(valor) ? "" : valor;
    }

    // Lê o alcance real da arma. <range> normalmente traz sub-tags em vez de texto direto:
    // <melee>/<ranged> para armas de arremesso (ex: Adaga: melee=5, ranged=20/60) e
    // <normal>/<long> para armas só de longo alcance (ex: Arco: normal=150, long=600).
    private string LerAlcance(XDocument doc, bool isRanged)
    {
        XElement rangeEl = doc.Descendants("range").FirstOrDefault();
        if (rangeEl != null)
        {
            string normal = rangeEl.Element("normal")?.Value?.Trim();
            string longa = rangeEl.Element("long")?.Value?.Trim();
            if (!string.IsNullOrEmpty(normal) || !string.IsNullOrEmpty(longa))
                return string.IsNullOrEmpty(longa) ? $"{normal} pés" : $"{normal}/{longa} pés";

            string melee = rangeEl.Element("melee")?.Value?.Trim();
            string arremesso = rangeEl.Element("ranged")?.Value?.Trim();
            if (!string.IsNullOrEmpty(melee) || !string.IsNullOrEmpty(arremesso))
            {
                if (isRanged && !string.IsNullOrEmpty(arremesso)) return $"{arremesso} pés";
                if (!string.IsNullOrEmpty(melee)) return $"{melee} pés";
                if (!string.IsNullOrEmpty(arremesso)) return $"{arremesso} pés";
            }

            // Nenhuma sub-tag reconhecida: se <range> não tiver filhos, o .Value dele já é o
            // texto direto (sem risco de concatenação de múltiplos filhos).
            if (!rangeEl.HasElements)
            {
                string direto = rangeEl.Value?.Trim();
                if (!string.IsNullOrEmpty(direto)) return direto;
            }
        }

        string reach = doc.Descendants("reach").FirstOrDefault()?.Value?.Trim();
        return string.IsNullOrWhiteSpace(reach) ? "" : reach;
    }

    private bool PossuiProficienciaArma(ItemAcaoEntrada item, HashSet<string> profNorm)
    {
        if (item == null) return false;
        string nome = Normalizar(item.nome);
        string categoria = Normalizar(item.categoria);
        string xml = Normalizar(item.xml);

        if (profNorm.Contains(nome) || profNorm.Contains(categoria)) return true;
        if (profNorm.Contains("all")) return true;
        if (profNorm.Contains("armassimples") && (xml.Contains("simple") || categoria.Contains("simples"))) return true;
        if (profNorm.Contains("armasmarciais") && (xml.Contains("martial") || categoria.Contains("marcial"))) return true;
        return false;
    }

    private string FormatarDano(string danoBase, int mod)
    {
        if (mod == 0) return danoBase;
        return mod > 0 ? $"{danoBase}+{mod}" : $"{danoBase}{mod}";
    }

    private string Normalizar(string valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return "";
        string semAcento = valor.Normalize(System.Text.NormalizationForm.FormD);
        semAcento = new string(semAcento.Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray());
        return new string(semAcento.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }
}