using System.Collections.Generic;
using System.Globalization;
using System.Text;

// Parser JSON mínimo e independente — Newtonsoft.Json não é dependência deste projeto e
// JsonUtility não lê objetos arbitrários/dicionários (só desserializa pra uma classe [Serializable]
// já conhecida). Só o suficiente pra ler um arquivo JSON externo de estrutura DESCONHECIDA (ex:
// import de NPC, ver CampanhaNpcImporter) num grafo genérico: Dictionary<string, object> pra
// objetos, List<object> pra arrays, string/double/bool/null pros primitivos. Não é um parser JSON
// "de produção" completo (não valida todos os casos de borda), mas cobre JSON bem-formado comum.
public static class MiniJsonParser
{
    public static object Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        int indice = 0;
        return ParsearValor(json, ref indice);
    }

    private static object ParsearValor(string s, ref int i)
    {
        PularEspacos(s, ref i);
        if (i >= s.Length) return null;

        char c = s[i];
        if (c == '{') return ParsearObjeto(s, ref i);
        if (c == '[') return ParsearArray(s, ref i);
        if (c == '"') return ParsearString(s, ref i);
        if (c == 't' || c == 'f') return ParsearBool(s, ref i);
        if (c == 'n') { i += 4; return null; } // "null"
        return ParsearNumero(s, ref i);
    }

    private static Dictionary<string, object> ParsearObjeto(string s, ref int i)
    {
        var obj = new Dictionary<string, object>();
        i++; // '{'
        PularEspacos(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return obj; }

        while (i < s.Length)
        {
            PularEspacos(s, ref i);
            string chave = ParsearString(s, ref i);
            PularEspacos(s, ref i);
            if (i < s.Length && s[i] == ':') i++;
            object valor = ParsearValor(s, ref i);
            obj[chave] = valor;
            PularEspacos(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; break; }
            break;
        }
        return obj;
    }

    private static List<object> ParsearArray(string s, ref int i)
    {
        var lista = new List<object>();
        i++; // '['
        PularEspacos(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return lista; }

        while (i < s.Length)
        {
            object valor = ParsearValor(s, ref i);
            lista.Add(valor);
            PularEspacos(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; break; }
            break;
        }
        return lista;
    }

    private static string ParsearString(string s, ref int i)
    {
        if (i >= s.Length || s[i] != '"') return string.Empty;
        i++; // '"'
        var sb = new StringBuilder();
        while (i < s.Length && s[i] != '"')
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                i++;
                char esc = s[i];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        if (i + 4 < s.Length)
                        {
                            string hex = s.Substring(i + 1, 4);
                            if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                sb.Append((char)code);
                            i += 4;
                        }
                        break;
                    default: sb.Append(esc); break;
                }
                i++;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        i++; // fecha '"'
        return sb.ToString();
    }

    private static bool ParsearBool(string s, ref int i)
    {
        if (s.Length - i >= 4 && s.Substring(i, 4) == "true") { i += 4; return true; }
        if (s.Length - i >= 5 && s.Substring(i, 5) == "false") { i += 5; return false; }
        return false;
    }

    private static double ParsearNumero(string s, ref int i)
    {
        int inicio = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E'))
            i++;
        string trecho = s.Substring(inicio, i - inicio);
        return double.TryParse(trecho, NumberStyles.Float, CultureInfo.InvariantCulture, out double valor) ? valor : 0;
    }

    private static void PularEspacos(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }
}
