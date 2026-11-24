using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Interpretador mínimo de fórmulas matemáticas usado por PropriedadePersonagem.formula (ver
/// dicevtt-motor-de-efeitos.md §3, EstadoPersonagem.ObterValor) e pelas condições de grau do motor de
/// dados (ver Mds/SistemasModulares/MotorDeDados, DefinicaoGrauResultado.condicao). Suporta +, -, *,
/// /, parênteses, negativo unário, comparações (&lt;=, &gt;=, ==, !=, &lt;, &gt;, não encadeáveis) e
/// as funções floor/ceil/round/abs/max — o suficiente pra regras de D&D 5e tipo
/// "floor(({for}-10)/2)" (modificador de atributo) e pra condições de grau tipo "{total} &gt;= {cd}".
/// Mesmo espírito de MiniJsonParser: um parser recursivo pequeno e independente, não uma calculadora
/// de expressões "de produção" completa.
///
/// Referências {idPropriedade} são substituídas pelo valor atual ANTES do parse (via
/// EstadoPersonagem.ObterValor), então o parser em si nunca vê o texto "{for}", só o número já
/// resolvido — evita duplicar a lógica de resolução de propriedade aqui dentro.
/// </summary>
public static class AvaliadorDeExpressao
{
    private static readonly Regex PadraoReferencia = new Regex(@"\{([a-zA-Z0-9_]+)\}", RegexOptions.Compiled);

    // Tolerância pras comparações — valores passam por round-trip string em {prop} (ver Avaliar), e
    // fórmulas encadeadas acumulam erro de ponto flutuante, então "==" exato quebraria em casos óbvios.
    private const float Epsilon = 0.0001f;

    public static float Avaliar(string formula, EstadoPersonagem estado)
    {
        if (string.IsNullOrWhiteSpace(formula)) return 0f;

        string substituida = PadraoReferencia.Replace(formula, m =>
            estado.ObterValor(m.Groups[1].Value).ToString(CultureInfo.InvariantCulture));

        int posicao = 0;
        return AnalisarComparacao(substituida, ref posicao);
    }

    /// <summary>Atalho pra condições booleanas (graus de resultado do motor de dados, ex:
    /// "{d1} == 20") — mesma avaliação de Avaliar, só interpretando o resultado numérico (1f/0f)
    /// como bool.</summary>
    public static bool AvaliarCondicao(string formula, EstadoPersonagem estado) => Avaliar(formula, estado) != 0f;

    // Comparacao := Expressao ( OpComparacao Expressao )?
    // Não encadeável de propósito ("a>=b==c" não é suportado) — condição de grau de RPG sempre é uma
    // comparação única (ex: "{total} >= {cd}"), então não vale a pena inventar associatividade aqui.
    private static float AnalisarComparacao(string s, ref int i)
    {
        float esquerda = AnalisarExpressao(s, ref i);

        PularEspacos(s, ref i);
        string operador = LerOperadorComparacao(s, ref i);
        if (operador == null) return esquerda;

        float direita = AnalisarExpressao(s, ref i);
        return AplicarComparacao(operador, esquerda, direita) ? 1f : 0f;
    }

    // Testa os operadores de 2 caracteres ANTES dos de 1 — sem isso "<=" seria lido como "<" e
    // sobraria um "=" solto que o resto do parser não sabe tratar.
    private static string LerOperadorComparacao(string s, ref int i)
    {
        if (i + 1 < s.Length)
        {
            string doisChars = s.Substring(i, 2);
            if (doisChars == "<=" || doisChars == ">=" || doisChars == "==" || doisChars == "!=")
            {
                i += 2;
                return doisChars;
            }
        }

        if (i < s.Length && (s[i] == '<' || s[i] == '>'))
        {
            string umChar = s[i].ToString();
            i++;
            return umChar;
        }

        return null;
    }

    private static bool AplicarComparacao(string operador, float a, float b) => operador switch
    {
        "<=" => a <= b + Epsilon,
        ">=" => a >= b - Epsilon,
        "==" => Mathf.Abs(a - b) < Epsilon,
        "!=" => Mathf.Abs(a - b) >= Epsilon,
        "<"  => a < b - Epsilon,
        ">"  => a > b + Epsilon,
        _    => false,
    };

    // Expressao := Termo (('+'|'-') Termo)*
    private static float AnalisarExpressao(string s, ref int i)
    {
        float valor = AnalisarTermo(s, ref i);
        while (true)
        {
            PularEspacos(s, ref i);
            if (i < s.Length && s[i] == '+') { i++; valor += AnalisarTermo(s, ref i); }
            else if (i < s.Length && s[i] == '-') { i++; valor -= AnalisarTermo(s, ref i); }
            else break;
        }
        return valor;
    }

    // Termo := Fator (('*'|'/') Fator)*
    private static float AnalisarTermo(string s, ref int i)
    {
        float valor = AnalisarFator(s, ref i);
        while (true)
        {
            PularEspacos(s, ref i);
            if (i < s.Length && s[i] == '*') { i++; valor *= AnalisarFator(s, ref i); }
            else if (i < s.Length && s[i] == '/')
            {
                i++;
                float divisor = AnalisarFator(s, ref i);
                valor = divisor != 0f ? valor / divisor : 0f;
            }
            else break;
        }
        return valor;
    }

    // Fator := '-' Fator | '(' Comparacao ')' | NomeFuncao '(' ListaArgumentos ')' | Numero
    private static float AnalisarFator(string s, ref int i)
    {
        PularEspacos(s, ref i);
        if (i < s.Length && s[i] == '-') { i++; return -AnalisarFator(s, ref i); }

        if (i < s.Length && s[i] == '(')
        {
            i++;
            float valor = AnalisarComparacao(s, ref i);
            PularEspacos(s, ref i);
            if (i < s.Length && s[i] == ')') i++;
            return valor;
        }

        int inicio = i;
        while (i < s.Length && char.IsLetter(s[i])) i++;
        if (i > inicio)
        {
            string nomeFuncao = s.Substring(inicio, i - inicio).ToLowerInvariant();
            PularEspacos(s, ref i);
            if (i < s.Length && s[i] == '(')
            {
                i++;
                List<float> argumentos = AnalisarListaArgumentos(s, ref i);
                PularEspacos(s, ref i);
                if (i < s.Length && s[i] == ')') i++;
                return AplicarFuncao(nomeFuncao, argumentos);
            }
            i = inicio; // não era função (ex: nome de propriedade não substituído) — devolve e tenta número
        }

        return AnalisarNumero(s, ref i);
    }

    // ListaArgumentos := Comparacao (',' Comparacao)* — vazio permitido (ex: "pi()", mesmo que não
    // exista nenhuma função de aridade zero hoje).
    private static List<float> AnalisarListaArgumentos(string s, ref int i)
    {
        var argumentos = new List<float>();

        PularEspacos(s, ref i);
        if (i < s.Length && s[i] == ')') return argumentos;

        argumentos.Add(AnalisarComparacao(s, ref i));
        PularEspacos(s, ref i);
        while (i < s.Length && s[i] == ',')
        {
            i++;
            argumentos.Add(AnalisarComparacao(s, ref i));
            PularEspacos(s, ref i);
        }

        return argumentos;
    }

    // Nome de função desconhecido = passthrough silencioso do primeiro argumento (mesmo
    // comportamento tolerante de antes), inclusive sem nenhum argumento (retorna 0).
    private static float AplicarFuncao(string nome, List<float> argumentos)
    {
        float primeiro = argumentos.Count > 0 ? argumentos[0] : 0f;
        return nome switch
        {
            "floor" => Mathf.Floor(primeiro),
            "ceil"  => Mathf.Ceil(primeiro),
            "round" => Mathf.Round(primeiro),
            "abs"   => Mathf.Abs(primeiro),
            "max"   => argumentos.Count >= 2 ? Mathf.Max(primeiro, argumentos[1]) : primeiro,
            _       => primeiro,
        };
    }

    private static float AnalisarNumero(string s, ref int i)
    {
        PularEspacos(s, ref i);
        int inicio = i;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.')) i++;
        if (i == inicio) return 0f;

        return float.TryParse(s.Substring(inicio, i - inicio), NumberStyles.Float, CultureInfo.InvariantCulture, out float valor)
            ? valor : 0f;
    }

    private static void PularEspacos(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }
}
