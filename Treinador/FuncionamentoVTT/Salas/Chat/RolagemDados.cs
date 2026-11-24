using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Helper mínimo de rolagem de dado — não existe nenhum sistema de dado/crítico no projeto ainda,
/// então isso é só o suficiente pra rolar 1d20 (com classificação de crítico) e uma notação simples
/// tipo "1d8+3"/"8d6"/"2d6-1" (mesmo formato que TokenStatBlockBuilder.ExtrairDanoDoTexto já extrai
/// pra AcaoStatBlock.descricao). 100% local/client-side, sem validação de servidor — mesmo modelo de
/// confiança que ChatManager já usa pro resto do chat (ServerRpc com RequireOwnership = false, sem
/// checagem anti-cheat).
/// </summary>
public static class RolagemDados
{
    public static int RolarD20() => Random.Range(1, 21);

    public static ResultadoCritico ClassificarD20(int valorNatural) => valorNatural switch
    {
        20 => ResultadoCritico.Sucesso,
        1  => ResultadoCritico.Falha,
        _  => ResultadoCritico.Normal,
    };

    // "1d20" / "1d20+7" / "1d20-1" — pra rolar o d20 de ataque/teste pelo MESMO caminho detalhado
    // que a notação de dano já usa (ver RolarNotacaoDetalhada), assim o "natural" pra classificar
    // crítico vira só dados[0].valor em vez de uma rolagem separada e descartada.
    public static string FormatarNotacaoD20(int modificador)
    {
        if (modificador == 0) return "1d20";
        return modificador > 0 ? $"1d20+{modificador}" : $"1d20{modificador}";
    }

    // Lookahead negativo (?!\s*d) impede o grupo de modificador de engolir o começo de outro termo de
    // dado (ex: em "2d6+1d4", sem o lookahead o "+1" viraria modificador do primeiro grupo e o "d4"
    // sobraria órfão) — necessário porque RolarNotacaoDetalhada agora itera todos os termos "NdM" da
    // notação, não só o primeiro (ver comentário do método).
    private static readonly Regex RegexNotacao = new Regex(@"(\d+)\s*d\s*(\d+)\s*([+-]\s*\d+(?!\s*d))?", RegexOptions.IgnoreCase);

    /// <summary>Rola uma notação de dado tipo "1d8+3" — soma o resultado de cada dado (Random.Range,
    /// 1..faces) mais o modificador fixo, se houver. Retorna 0 se a notação não bater o padrão (ex:
    /// AcaoStatBlock.descricao == "-", quando ExtrairDanoDoTexto não achou nada no texto original).</summary>
    public static int RolarNotacao(string notacao) => RolarNotacaoDetalhada(notacao).total;

    /// <summary>Mesma rolagem de RolarNotacao, mas preserva o valor de CADA dado individual (ver
    /// DadoRolado) — usado pro tooltip de detalhe de rolagem no chat (hover no total). Soma TODOS os
    /// termos "NdM" da notação (ex: "1d20+5+1d6"), não só o primeiro — antes, "1d20+5+1d6" virava só
    /// "1d20+5" e o "+1d6" era descartado em silêncio.</summary>
    public static ResultadoRolagem RolarNotacaoDetalhada(string notacao)
    {
        var resultado = new ResultadoRolagem { notacao = notacao ?? string.Empty };
        if (string.IsNullOrWhiteSpace(notacao)) return resultado;

        MatchCollection matches = RegexNotacao.Matches(notacao);
        if (matches.Count == 0) return resultado;

        int total = 0;
        int modificadorTotal = 0;
        foreach (Match match in matches)
        {
            int quantidade = int.Parse(match.Groups[1].Value);
            int faces = int.Parse(match.Groups[2].Value);
            int modificador = 0;
            if (match.Groups[3].Success)
                int.TryParse(match.Groups[3].Value.Replace(" ", ""), out modificador);

            for (int i = 0; i < quantidade; i++)
            {
                int valor = Random.Range(1, faces + 1);
                resultado.dados.Add(new DadoRolado { faces = faces, valor = valor });
                total += valor;
            }

            total += modificador;
            modificadorTotal += modificador;
        }

        resultado.total = total;
        resultado.modificador = modificadorTotal;
        return resultado;
    }
}

// Um dado individual já rolado — faces (tamanho do dado, ex: 20) + valor (o que caiu, 1..faces).
// [Serializable] pra viajar dentro de ChatAtaqueDTO/ChatTesteDTO via JsonUtility.
[System.Serializable]
public class DadoRolado
{
    public int faces;
    public int valor;
}

// Resultado completo de RolarNotacaoDetalhada — notação pronta pra exibir + cada dado + o
// modificador fixo (bônus/penalidade, separado dos dados — ver DadosRolagemTooltipUI) + o total já
// somado (mesmo total que RolarNotacao sempre devolveu).
[System.Serializable]
public class ResultadoRolagem
{
    public string notacao;
    public List<DadoRolado> dados = new List<DadoRolado>();
    public int modificador;
    public int total;
}
