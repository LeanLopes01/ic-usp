using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Formatação somente-leitura de um EntryData de NPC (Dnd5eNpcFieldCatalog) para exibição em texto
/// — compartilhado entre CampanhaNpcCardUI (listagem) e CampanhaTokenTooltipUI (grid), pra não
/// duplicar a mesma lógica de "como mostrar cada FieldType" nos dois lugares.
/// </summary>
public static class NpcEntryFormatter
{
    public static string ObterNome(EntryData data)
        => data?.Fields.Find(f => f.Definition.FieldId == Dnd5eNpcFieldCatalog.NomeField.FieldId)?.TextValue;

    public static string ObterRetratoPath(EntryData data)
        => data?.Fields.Find(f => f.Definition.FieldId == Dnd5eNpcFieldCatalog.RetratoField.FieldId)?.TextValue;

    /// <summary>true se este EntryData foi trazido por CampanhasManager.ImportarNpcsDeCaminho —
    /// usado pelo filtro de origem em CampanhaNpcGaleriaUI pra distinguir "Importados" de "Criados"
    /// (NPCs de sistema nunca passam por aqui, são classificados à parte por quem os carregou).</summary>
    public static bool EhImportado(EntryData data)
        => data?.Fields.Find(f => f.Definition.FieldId == Dnd5eNpcFieldCatalog.OrigemField.FieldId)?.TextValue == "imported";

    /// <summary>Índice de desafio como número, pra ordenação — aceita frações ("1/2", "1/4") e o
    /// formato "10 (5.900 XP)" salvo pelo sistema (só a parte antes do parêntese importa). Null se
    /// não conseguir interpretar (fica de fora da ordenação numérica, não trava a listagem).</summary>
    public static float? ObterDificuldadeNumerica(EntryData data)
    {
        string texto = data?.Fields.Find(f => f.Definition.FieldId == "npc-cr")?.TextValue;
        if (string.IsNullOrWhiteSpace(texto)) return null;

        string primeiraParte = texto.Split('(')[0].Trim();
        if (primeiraParte.Contains("/"))
        {
            string[] partes = primeiraParte.Split('/');
            if (partes.Length == 2
                && float.TryParse(partes[0], NumberStyles.Any, CultureInfo.InvariantCulture, out float num)
                && float.TryParse(partes[1], NumberStyles.Any, CultureInfo.InvariantCulture, out float den)
                && den != 0)
                return num / den;
            return null;
        }

        return float.TryParse(primeiraParte, NumberStyles.Any, CultureInfo.InvariantCulture, out float valor) ? valor : (float?)null;
    }

    /// <summary>Vida (pontos de vida) como número, pra ordenação — extrai o primeiro inteiro do
    /// texto, aceitando tanto um valor simples ("25") quanto o formato "135 (18d10 + 36)" salvo
    /// pelo sistema. Null se não conseguir interpretar.</summary>
    public static float? ObterVidaNumerica(EntryData data)
    {
        string texto = data?.Fields.Find(f => f.Definition.FieldId == "npc-hp")?.TextValue;
        if (string.IsNullOrWhiteSpace(texto)) return null;

        Match match = Regex.Match(texto, @"\d+");
        return match.Success && float.TryParse(match.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out float valor)
            ? valor : (float?)null;
    }

    public static bool TemValor(FieldValue fv)
    {
        if (fv == null) return false;
        switch (fv.Definition.Type)
        {
            case FieldType.Group:      return fv.GroupFields.Any(g => !string.IsNullOrWhiteSpace(g.TextValue));
            case FieldType.StringList: return fv.ListValues.Count > 0;
            case FieldType.ObjectList: return fv.SubEntries.Count > 0;
            default:                   return !string.IsNullOrWhiteSpace(fv.TextValue);
        }
    }

    public static string FormatarValorTexto(FieldValue fv)
    {
        switch (fv.Definition.Type)
        {
            case FieldType.UnitValue:
                return string.IsNullOrWhiteSpace(fv.UnitValue) ? fv.TextValue : $"{fv.TextValue} ({fv.UnitValue})";

            case FieldType.Group:
                return string.Join("   ", fv.GroupFields
                    .Where(g => !string.IsNullOrWhiteSpace(g.TextValue))
                    .Select(g => $"{g.Definition.Label} {g.TextValue}"));

            default:
                return fv.TextValue;
        }
    }

    public static List<string> ObterLinhas(FieldValue fv)
    {
        if (fv == null) return new List<string>();
        if (fv.Definition.Type == FieldType.StringList) return fv.ListValues;
        if (fv.Definition.Type == FieldType.ObjectList) return fv.SubEntries.Select(FormatarSubEntry).ToList();
        return new List<string>();
    }

    public static string FormatarSubEntry(EntryData sub)
    {
        string nome = sub.Fields.Find(f => f.Definition.XmlTag == "name")?.TextValue ?? "";
        string desc = sub.Fields.Find(f => f.Definition.XmlTag == "description")?.TextValue ?? "";
        string extra = string.Join(", ", sub.Fields
            .Where(f => f.Definition.XmlTag != "name" && f.Definition.XmlTag != "description" && !string.IsNullOrWhiteSpace(f.TextValue))
            .Select(f => $"{f.Definition.Label}: {f.TextValue}"));

        string linha = string.IsNullOrWhiteSpace(nome) ? "(sem nome)" : nome;
        if (!string.IsNullOrWhiteSpace(extra)) linha += $" ({extra})";
        if (!string.IsNullOrWhiteSpace(desc)) linha += $" — {desc}";
        return linha;
    }
}
