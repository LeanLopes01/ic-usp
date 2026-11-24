using System.Text.RegularExpressions;
using UnityEngine;

// Nem magia nem ação de monstro/ficha têm alcance/raio de área estruturado nos dados locais (mesmo
// problema já resolvido pro bônus de ataque de monstro, ver TokenStatBlockBuilder.
// ExtrairBonusAtaqueDoTexto) — melhor esforço via regex sobre o texto livre da descrição. Quem chama
// decide o fallback quando não acha nada (0 = desconhecido).
public static class AlcanceExtrator
{
    // 1 quadrado = 1.5m = 5 pés — mesma convenção de MapManager.ConverterMetrosParaMundo (regra
    // padrão de mesa da 5e).
    private const float PesPorQuadrado = 5f;
    private const float MetrosPorQuadrado = 1.5f;

    // "raio de 20 pés", "raio de 6 metros"/"raio de 9 m", "20-foot radius", "radius of 20 feet"
    private static readonly Regex RegexRaioArea = new Regex(
        @"raio\s+de\s+(?<num>\d+)\s*(?<un>pés|pes|metros|m)\b" +
        @"|(?<num>\d+)[\s-]*(?:foot|feet|ft)[\s-]*radius" +
        @"|radius\s+of\s+(?<num>\d+)\s*(?:feet|foot|ft)",
        RegexOptions.IgnoreCase);

    // "alcance de 150 pés", "alcance de 9 metros", "range 30 feet", "range of 60 ft"
    private static readonly Regex RegexAlcance = new Regex(
        @"alcance\s+de\s+(?<num>\d+)\s*(?<un>pés|pes|metros|m)\b" +
        @"|range\s+(?:of\s+)?(?<num>\d+)\s*(?:feet|foot|ft)",
        RegexOptions.IgnoreCase);

    public static int ExtrairRaioAreaQuadrados(string texto) => ExtrairQuadrados(texto, RegexRaioArea);

    public static int ExtrairAlcanceQuadrados(string texto) => ExtrairQuadrados(texto, RegexAlcance);

    // "(15-foot cone)", "(100 foot line)" — parênteses dentro de "Self (...)" no campo <range> das
    // magias novas (ver RangeInfo/ExtrairDeCampoRange).
    private static readonly Regex RegexFormaEmParenteses = new Regex(
        @"\((?<num>\d+)[\s-]*foot\s+(?<forma>cone|line)\)", RegexOptions.IgnoreCase);

    private static readonly Regex RegexNumeroComUnidade = new Regex(
        @"^(?<num>\d+)\s*(?:feet|foot|ft)\b", RegexOptions.IgnoreCase);

    /// <summary>Resultado de interpretar o campo estruturado &lt;range&gt; das magias novas (ver
    /// FichaMagiaCatalogoLoader) — bem mais confiável que regex sobre a descrição livre, quando o
    /// campo existe. <see cref="reconhecido"/> false = campo ausente/vazio/valor não reconhecido
    /// (ex: "Special") — quem chama cai no fallback de texto livre (AlcanceExtrator/
    /// FormaAreaExtrator sobre a descrição).</summary>
    public class RangeInfo
    {
        public bool reconhecido;
        public bool origemSelf;
        public int alcanceQuadrados;      // 0 = self ou desconhecido
        public FormaAreaAcao? forma;      // só preenchido se achou "(N-foot cone/line)" dentro do campo
        public int raioAreaQuadrados;     // idem, 0 se não achou
    }

    public static RangeInfo ExtrairDeCampoRange(string rangeXml)
    {
        var info = new RangeInfo();
        if (string.IsNullOrWhiteSpace(rangeXml)) return info;

        string texto = rangeXml.Trim();

        if (int.TryParse(texto, out int pesDireto))
        {
            info.reconhecido = true;
            if (pesDireto <= 0) { info.origemSelf = true; return info; }
            info.alcanceQuadrados = Mathf.Max(1, Mathf.RoundToInt(pesDireto / PesPorQuadrado));
            return info;
        }

        if (texto.Equals("touch", System.StringComparison.OrdinalIgnoreCase))
        {
            info.reconhecido = true;
            info.alcanceQuadrados = 1;
            return info;
        }

        if (texto.StartsWith("self", System.StringComparison.OrdinalIgnoreCase))
        {
            info.reconhecido = true;
            info.origemSelf = true;

            Match formaMatch = RegexFormaEmParenteses.Match(texto);
            if (formaMatch.Success && int.TryParse(formaMatch.Groups["num"].Value, out int pesForma))
            {
                info.raioAreaQuadrados = Mathf.Max(1, Mathf.RoundToInt(pesForma / PesPorQuadrado));
                info.forma = formaMatch.Groups["forma"].Value.Equals("cone", System.StringComparison.OrdinalIgnoreCase)
                    ? FormaAreaAcao.Cone : FormaAreaAcao.Linha;
            }
            return info;
        }

        Match numMatch = RegexNumeroComUnidade.Match(texto);
        if (numMatch.Success && int.TryParse(numMatch.Groups["num"].Value, out int pesComUnidade))
        {
            info.reconhecido = true;
            if (pesComUnidade <= 0) { info.origemSelf = true; return info; }
            info.alcanceQuadrados = Mathf.Max(1, Mathf.RoundToInt(pesComUnidade / PesPorQuadrado));
            return info;
        }

        // "Special" ou qualquer coisa não reconhecida — reconhecido fica false, cai no fallback.
        return info;
    }

    private static int ExtrairQuadrados(string texto, Regex regex)
    {
        if (string.IsNullOrWhiteSpace(texto)) return 0;

        Match match = regex.Match(texto);
        if (!match.Success || !int.TryParse(match.Groups["num"].Value, out int valor)) return 0;

        // Grupo "un" só existe nos ramos em português — ramos em inglês são sempre pés/feet.
        bool ehMetros = match.Groups["un"].Success && match.Groups["un"].Value.StartsWith("m", System.StringComparison.OrdinalIgnoreCase);
        float quadrados = ehMetros ? valor / MetrosPorQuadrado : valor / PesPorQuadrado;
        return Mathf.Max(1, Mathf.RoundToInt(quadrados));
    }
}
