using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

/// <summary>
/// Leitura/escrita do arquivo motor-de-dados.xml, na raiz do sistema (irmão de
/// schema_&lt;topico&gt;.xml e da pasta dados/ — ver Sistemas/SystemManager.SistemasFolderName/
/// DadosFolderName — propriedades/motor de dados são do SISTEMA inteiro, não de um tópico
/// específico). Usa System.Xml.Linq (XDocument), mesmo estilo já usado por
/// Sistemas/SystemManager.cs pra ler conteúdo na raiz do sistema (regras/Descricao.xml) — este é um
/// formato de arquivo NOVO, sem leitor existente pra herdar consistência, então segue o precedente
/// mais próximo em vez do XmlDocument que Sistemas/SistemasCustom/CustomSystemManager.cs usa pro
/// modelo de schema/entradas (objeto bem diferente, sem motivo pra reaproveitar aquela API aqui).
/// </summary>
public static class CarregadorMotorDeDados
{
    public const string NomeArquivo = "motor-de-dados.xml";

    public static DefinicaoMotorDeDados Carregar(string caminhoArquivo)
    {
        if (string.IsNullOrWhiteSpace(caminhoArquivo) || !File.Exists(caminhoArquivo))
            return new DefinicaoMotorDeDados();

        try
        {
            return CarregarDeDocumento(XDocument.Load(caminhoArquivo));
        }
        catch (Exception e)
        {
            Debug.LogError($"[CarregadorMotorDeDados] Falha ao ler '{caminhoArquivo}': {e.Message}");
            return new DefinicaoMotorDeDados();
        }
    }

    /// <summary>Mesmo comportamento de Carregar, mas aceita motor-de-dados.json como alternativa: se
    /// caminhoArquivoXml não existir, tenta o irmão .json (mesmo nome, Path.ChangeExtension),
    /// convertendo pra XML via ImportadorConteudoUniversal antes de reaproveitar CarregarDeString
    /// sem alteração nenhuma.</summary>
    public static DefinicaoMotorDeDados CarregarComSuporteJson(string caminhoArquivoXml)
    {
        if (string.IsNullOrWhiteSpace(caminhoArquivoXml))
            return new DefinicaoMotorDeDados();

        if (File.Exists(caminhoArquivoXml))
            return Carregar(caminhoArquivoXml);

        string caminhoJson = Path.ChangeExtension(caminhoArquivoXml, ".json");
        if (!File.Exists(caminhoJson))
            return new DefinicaoMotorDeDados();

        string json = ImportadorConteudoUniversal.LerTextoComFallbackDeEncoding(caminhoJson);
        string xml = json != null ? ImportadorConteudoUniversal.ConverterJsonParaXml(json, "MotorDeDados") : null;
        if (xml == null)
        {
            Debug.LogWarning($"[CarregadorMotorDeDados] JSON inválido em '{caminhoJson}' — ignorando.");
            return new DefinicaoMotorDeDados();
        }

        return CarregarDeString(xml);
    }

    /// <summary>Parse direto de uma string XML já em memória — sem I/O, útil pra colar/testar um
    /// exemplo sem precisar gravar em disco primeiro.</summary>
    public static DefinicaoMotorDeDados CarregarDeString(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return new DefinicaoMotorDeDados();

        try
        {
            return CarregarDeDocumento(XDocument.Parse(xml));
        }
        catch (Exception e)
        {
            Debug.LogError($"[CarregadorMotorDeDados] Falha ao interpretar XML: {e.Message}");
            return new DefinicaoMotorDeDados();
        }
    }

    public static void Salvar(DefinicaoMotorDeDados definicao, string caminhoArquivo)
    {
        if (definicao == null || string.IsNullOrWhiteSpace(caminhoArquivo)) return;

        try
        {
            string pasta = Path.GetDirectoryName(caminhoArquivo);
            if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);

            ConstruirDocumento(definicao).Save(caminhoArquivo);
        }
        catch (Exception e)
        {
            Debug.LogError($"[CarregadorMotorDeDados] Falha ao salvar '{caminhoArquivo}': {e.Message}");
        }
    }

    /// <summary>Popula um EstadoPersonagem NOVO a partir de definicao.propriedades — label é
    /// descartado (só interessa pra Forja, não pro cálculo, ver DefinicaoPropriedade).</summary>
    public static EstadoPersonagem CriarEstadoInicial(DefinicaoMotorDeDados definicao)
    {
        var estado = new EstadoPersonagem();
        if (definicao?.propriedades == null) return estado;

        foreach (DefinicaoPropriedade propriedade in definicao.propriedades)
        {
            if (propriedade == null || string.IsNullOrWhiteSpace(propriedade.id)) continue;

            estado.PropriedadesBase[propriedade.id] = new PropriedadePersonagem
            {
                id = propriedade.id,
                valorBase = propriedade.valorBase,
                formula = propriedade.formula,
            };
        }

        return estado;
    }

    // ── Leitura ───────────────────────────────────────────────────────────────

    private static DefinicaoMotorDeDados CarregarDeDocumento(XDocument doc)
    {
        var definicao = new DefinicaoMotorDeDados();
        XElement raiz = doc.Root;
        if (raiz == null) return definicao;

        foreach (XElement el in raiz.Element("Propriedades")?.Elements("Propriedade") ?? Enumerable.Empty<XElement>())
        {
            definicao.propriedades.Add(new DefinicaoPropriedade
            {
                id        = (string)el.Attribute("id") ?? "",
                label     = (string)el.Attribute("label") ?? "",
                valorBase = ParsearFloat(el.Attribute("valorBase")),
                formula   = (string)el.Attribute("formula") ?? "",
            });
        }

        foreach (XElement el in raiz.Element("TiposDeRolagem")?.Elements("TipoRolagem") ?? Enumerable.Empty<XElement>())
        {
            var tipo = new DefinicaoTipoRolagem
            {
                id            = (string)el.Attribute("id") ?? "",
                dado          = ParsearDado((string)el.Attribute("dado")),
                limiarSucesso = ParsearInt(el.Attribute("limiarSucesso")),
                parMaximo     = ParsearInt(el.Attribute("parMaximo")),
            };

            foreach (XElement grauEl in el.Elements("Grau"))
            {
                tipo.graus.Add(new DefinicaoGrauResultado
                {
                    id       = (string)grauEl.Attribute("id") ?? "",
                    condicao = (string)grauEl.Attribute("condicao") ?? "",
                });
            }

            if (!string.IsNullOrWhiteSpace(tipo.id))
                definicao.tiposDeRolagem[tipo.id] = tipo;
        }

        foreach (XElement el in raiz.Element("AcoesDeTeste")?.Elements("Acao") ?? Enumerable.Empty<XElement>())
        {
            var acao = new DefinicaoAcaoRolagem
            {
                id            = (string)el.Attribute("id") ?? "",
                nome          = (string)el.Attribute("nome") ?? "",
                idTipoRolagem = (string)el.Attribute("tipoRolagem") ?? "",
            };

            foreach (XElement paramEl in el.Elements("Parametro"))
            {
                string chave = (string)paramEl.Attribute("chave");
                if (string.IsNullOrWhiteSpace(chave)) continue;
                acao.parametros[chave] = (string)paramEl.Attribute("valor") ?? "";
            }

            definicao.acoes.Add(acao);
        }

        return definicao;
    }

    // ── Escrita ───────────────────────────────────────────────────────────────

    private static XDocument ConstruirDocumento(DefinicaoMotorDeDados definicao)
    {
        var propriedadesEl = new XElement("Propriedades");
        foreach (DefinicaoPropriedade p in definicao.propriedades)
        {
            var el = new XElement("Propriedade", new XAttribute("id", p.id ?? ""), new XAttribute("label", p.label ?? ""));
            if (!string.IsNullOrEmpty(p.formula)) el.SetAttributeValue("formula", p.formula);
            else el.SetAttributeValue("valorBase", p.valorBase.ToString(CultureInfo.InvariantCulture));
            propriedadesEl.Add(el);
        }

        var tiposEl = new XElement("TiposDeRolagem");
        foreach (DefinicaoTipoRolagem tipo in definicao.tiposDeRolagem.Values)
        {
            var tipoEl = new XElement("TipoRolagem",
                new XAttribute("id", tipo.id ?? ""),
                new XAttribute("dado", FormatarDado(tipo.dado)));
            if (tipo.limiarSucesso != 0) tipoEl.SetAttributeValue("limiarSucesso", tipo.limiarSucesso);
            if (tipo.parMaximo != 0) tipoEl.SetAttributeValue("parMaximo", tipo.parMaximo);

            foreach (DefinicaoGrauResultado grau in tipo.graus)
                tipoEl.Add(new XElement("Grau", new XAttribute("id", grau.id ?? ""), new XAttribute("condicao", grau.condicao ?? "")));

            tiposEl.Add(tipoEl);
        }

        var acoesEl = new XElement("AcoesDeTeste");
        foreach (DefinicaoAcaoRolagem acao in definicao.acoes)
        {
            var acaoEl = new XElement("Acao",
                new XAttribute("id", acao.id ?? ""),
                new XAttribute("nome", acao.nome ?? ""),
                new XAttribute("tipoRolagem", acao.idTipoRolagem ?? ""));

            foreach (var par in acao.parametros)
                acaoEl.Add(new XElement("Parametro", new XAttribute("chave", par.Key), new XAttribute("valor", par.Value ?? "")));

            acoesEl.Add(acaoEl);
        }

        return new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("MotorDeDados", propriedadesEl, tiposEl, acoesEl));
    }

    // ── Parsing auxiliar ──────────────────────────────────────────────────────

    private static float ParsearFloat(XAttribute atributo) =>
        atributo != null && float.TryParse(atributo.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float valor) ? valor : 0f;

    private static int ParsearInt(XAttribute atributo) =>
        atributo != null && int.TryParse(atributo.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int valor) ? valor : 0;

    // "d20" -> 20 (aceita também um número puro, ex: "20", por tolerância).
    private static int ParsearDado(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;
        string limpo = texto.Trim().TrimStart('d', 'D');
        return int.TryParse(limpo, NumberStyles.Integer, CultureInfo.InvariantCulture, out int faces) ? faces : 0;
    }

    private static string FormatarDado(int faces) => $"d{faces}";
}
