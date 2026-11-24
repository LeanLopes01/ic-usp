using System;

// Nem ficha nem monstro têm campo estruturado de forma/origem de área — só o catálogo de magias
// (FichaMagiaCatalogoLoader) ganha os elementos <area>/<origem> no XML (ver plano). Pra tudo mais
// (e pras próprias magias sem o campo preenchido ainda), melhor esforço via palavra-chave no texto
// livre — mesmo espírito de AlcanceExtrator.
public static class FormaAreaExtrator
{
    public static FormaAreaAcao ExtrairForma(string textoLivre, int raioAreaQuadrados)
    {
        if (!string.IsNullOrWhiteSpace(textoLivre))
        {
            if (Contem(textoLivre, "cone")) return FormaAreaAcao.Cone;
            if (Contem(textoLivre, "linha", "line")) return FormaAreaAcao.Linha;
        }

        // "raio de X pés/metros" (já reconhecido por AlcanceExtrator) é, na esmagadora maioria das
        // magias, uma esfera/cilindro — melhor default que quadrado quando existe um raio de área.
        if (raioAreaQuadrados > 0) return FormaAreaAcao.Circulo;

        return FormaAreaAcao.Quadrado;
    }

    public static bool ExtrairOrigemSelf(string textoLivre, FormaAreaAcao forma)
    {
        // Cone e linha são sempre "self" por regra da 5e (RAW) — não existe cone/linha "à distância".
        if (forma == FormaAreaAcao.Cone || forma == FormaAreaAcao.Linha) return true;

        if (string.IsNullOrWhiteSpace(textoLivre)) return false;
        return Contem(textoLivre, "centrada em você", "centrado em você", "emana de você", "você é a origem", "self (");
    }

    private static bool Contem(string texto, params string[] palavras)
    {
        foreach (string palavra in palavras)
            if (texto.IndexOf(palavra, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }
}
