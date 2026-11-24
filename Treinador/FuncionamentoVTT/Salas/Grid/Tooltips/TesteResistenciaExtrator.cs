using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

// Nem ficha nem monstro têm campo estruturado de "tipo de teste" (ataque vs resistência) — melhor
// esforço via regex sobre o texto livre da descrição, mesmo espírito de AlcanceExtrator/
// FormaAreaExtrator. Sem match, quem chama trata como ataque (comportamento de antes desta fase).
public static class TesteResistenciaExtrator
{
    // Nomes canônicos IDÊNTICOS aos usados em TesteStatBlock.nome (ver TokenStatBlockBuilder —
    // "Força","Destreza","Constituição","Inteligência","Sabedoria","Carisma") pra bater direto na
    // lista de salvaguardas do alvo sem precisar de outra tradução.
    private static readonly Dictionary<string, string> NomesCanonicos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "força", "Força" }, { "forca", "Força" }, { "strength", "Força" },
        { "destreza", "Destreza" }, { "dexterity", "Destreza" },
        { "constituição", "Constituição" }, { "constituicao", "Constituição" }, { "constitution", "Constituição" },
        { "inteligência", "Inteligência" }, { "inteligencia", "Inteligência" }, { "intelligence", "Inteligência" },
        { "sabedoria", "Sabedoria" }, { "wisdom", "Sabedoria" },
        { "carisma", "Carisma" }, { "charisma", "Carisma" },
    };

    // "teste de Destreza", "resistência de Constituição", "salvaguarda de Sabedoria" (pt) ou
    // "Dexterity saving throw" / "Constitution save" (en).
    private static readonly Regex RegexResistencia = new Regex(
        @"(?:teste|resist[êe]ncia|salvaguarda)\s+de\s+(?<attr>força|forca|destreza|constitui[çc][ãa]o|intelig[êe]ncia|sabedoria|carisma)" +
        @"|(?<attr>strength|dexterity|constitution|intelligence|wisdom|charisma)\s+sav(?:e|ing throw)",
        RegexOptions.IgnoreCase);

    public static (bool ehResistencia, string atributo) Extrair(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return (false, null);

        Match match = RegexResistencia.Match(texto);
        if (!match.Success) return (false, null);

        return NomesCanonicos.TryGetValue(match.Groups["attr"].Value, out string canonico)
            ? (true, canonico)
            : (false, null);
    }

    // "CD 13", "DC 15" — CD de magia normalmente NÃO vem no texto (é sempre calculada do conjurador,
    // ver TokenStatBlockData.cdMagico), mas ação de monstro sempre imprime a própria CD no texto.
    private static readonly Regex RegexCD = new Regex(@"\b(?:CD|DC)\s*(\d+)\b", RegexOptions.IgnoreCase);

    public static int ExtrairCD(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;

        Match match = RegexCD.Match(texto);
        return match.Success && int.TryParse(match.Groups[1].Value, out int cd) ? cd : 0;
    }
}
