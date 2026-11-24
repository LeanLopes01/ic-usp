using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Infraestrutura transversal de importação de conteúdo homebrew (classes, raças, antecedentes,
/// monstros, itens, magias, motor de dados) em formato JSON, convertendo para o MESMO formato de
/// string XML que cada Selector/Loader já sabe interpretar via XDocument.Parse/XElement.Load — nenhum
/// método de extração existente é alterado por esta classe, só o ponto de descoberta de arquivo de
/// cada consumidor passa a chamar ConverterJsonParaXml antes de entregar a string pro parser de
/// sempre.
///
/// A conversão usa Newtonsoft.Json.JsonConvert.DeserializeXmlNode (Json.NET, já presente no projeto
/// como dependência transitiva do UnityFigmaBridge — ver Packages/manifest.json): "@atributo" vira
/// atributo XML, "#text" vira texto misto (elemento com atributos E texto), e um valor-array vira
/// múltiplos elementos irmãos com o mesmo nome — convenção documentada pro jogador em separado, não
/// inventada aqui. Um arquivo de conteúdo homebrew em JSON tem exatamente UMA chave de nível
/// superior (ex: {"class": {...}}), que vira o elemento raiz.
///
/// MiniJsonParser.cs (Sistemas/SistemasCustom/) não é usado aqui de propósito: ele produz um grafo
/// genérico em memória (Dictionary/List), útil pra CampanhaNpcImporter consumir direto em C#, mas
/// não uma string XML — pra alimentar os parsers XML existentes sem reescrevê-los, precisamos
/// exatamente do que o Json.NET já faz pronto.
/// </summary>
public static class ImportadorConteudoUniversal
{
    // Encoding.UTF8 padrão do .NET NUNCA lança em bytes inválidos (usa substituição silenciosa,
    // produzindo caracteres de reposição U+FFFD) — precisamos de uma instância explícita com
    // throwOnInvalidBytes pra detectar de verdade um arquivo salvo em outro encoding antes de cair
    // pro fallback Windows-1252 (permissivo por natureza: todo byte 0-255 mapeia pra algo nesse
    // encoding, então ele nunca lança — é o fallback final, não uma segunda tentativa "estrita").
    private static readonly UTF8Encoding _utf8Estrito =
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Lê um arquivo de texto tentando UTF-8 estrito primeiro; se os bytes não forem UTF-8
    /// válido, tenta de novo como Windows-1252 (cobre JSON/XML salvo por um editor antigo/Excel).
    /// Retorna null (e loga um warning) só se nem ler os bytes do arquivo for possível.</summary>
    public static string LerTextoComFallbackDeEncoding(string caminhoArquivo)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(caminhoArquivo);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ImportadorConteudoUniversal] Não foi possível ler '{Path.GetFileName(caminhoArquivo)}': {e.Message}");
            return null;
        }

        try
        {
            return _utf8Estrito.GetString(bytes).Trim().TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            Debug.LogWarning($"[ImportadorConteudoUniversal] '{Path.GetFileName(caminhoArquivo)}' não é UTF-8 válido — tentando Windows-1252.");
            return Encoding.GetEncoding(1252).GetString(bytes).Trim();
        }
    }

    /// <summary>Converte uma string JSON pra uma string XML equivalente (convenção do Json.NET —
    /// "@atributo"/"#text"/array-vira-elementos-repetidos). Retorna null e loga um warning se o JSON
    /// for inválido — nunca lança pro chamador. nomeElementoRaizFallback só é usado se o JSON não
    /// tiver uma única chave de nível superior (ex: o jogador esqueceu de envolver o conteúdo em
    /// {"class": {...}}) — nesse caso o conteúdo inteiro é envolvido nesse elemento como rede de
    /// segurança, em vez de simplesmente falhar.</summary>
    public static string ConverterJsonParaXml(string json, string nomeElementoRaizFallback = null)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        XmlDocument xmlDoc;
        try
        {
            xmlDoc = JsonConvert.DeserializeXmlNode(json);
        }
        catch (Exception) when (!string.IsNullOrWhiteSpace(nomeElementoRaizFallback))
        {
            try
            {
                xmlDoc = JsonConvert.DeserializeXmlNode(json, nomeElementoRaizFallback);
            }
            catch (Exception e2)
            {
                Debug.LogWarning($"[ImportadorConteudoUniversal] JSON inválido (mesmo com raiz de fallback '{nomeElementoRaizFallback}'): {e2.Message}");
                return null;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ImportadorConteudoUniversal] JSON inválido: {e.Message}");
            return null;
        }

        if (xmlDoc?.DocumentElement == null) return null;
        return xmlDoc.OuterXml;
    }

    /// <summary>Enumera arquivos .xml e .json numa pasta. Não filtra nada (nem "template", nem
    /// "copy") — cada chamador aplica seu PRÓPRIO filtro já existente sobre o resultado combinado,
    /// pra não alterar filtros específicos já testados de cada Selector/Loader.</summary>
    public static IEnumerable<string> EnumerarArquivosXmlEJson(string pasta, SearchOption opcao = SearchOption.TopDirectoryOnly)
    {
        foreach (string arquivoXml in Directory.GetFiles(pasta, "*.xml", opcao))
            yield return arquivoXml;
        foreach (string arquivoJson in Directory.GetFiles(pasta, "*.json", opcao))
            yield return arquivoJson;
    }

    /// <summary>Atalho pros consumidores: true se o caminho termina em ".json" (case-insensitive).</summary>
    public static bool EhArquivoJson(string caminhoArquivo) =>
        string.Equals(Path.GetExtension(caminhoArquivo), ".json", StringComparison.OrdinalIgnoreCase);
}
