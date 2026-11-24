using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Efeito de slide horizontal reutilizável pra troca de página em listas paginadas (fichas, salas,
/// campanhas, sistemas): o Content desliza pra fora na direção da navegação, o callback de
/// repopulação troca os itens enquanto ele está fora da vista, e ele desliza de volta pro centro —
/// evita o corte brusco de destruir/instanciar os itens direto na posição visível. Mesma ideia de
/// TelaInicial/Noticias/CarrosselNoticias.cs (slide + AnimationCurve), só que aqui é uma página
/// inteira de itens de cada vez (não itens individuais lado a lado em loop), então não faz sentido
/// herdar aquele componente — a única coisa em comum é a técnica de animação.
/// </summary>
public static class TransicaoDeslizantePagina
{
    private static readonly AnimationCurve CurvaPadrao = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    /// <param name="content">RectTransform onde os itens da página são instanciados (o Content da lista).</param>
    /// <param name="direcao">+1 = avançando (conteúdo sai pela esquerda, entra pela direita); -1 = voltando (inverso). 0 é tratado como +1.</param>
    /// <param name="repovoarSincrono">Limpa e recria os itens da nova página — chamado enquanto o Content está fora da tela. Síncrono: para quem já limpa/instancia direto (sem corrotina própria).</param>
    public static IEnumerator Executar(RectTransform content, int direcao, Action repovoarSincrono,
        float distancia = 70f, float duracaoPorTrecho = 0.14f, AnimationCurve curva = null)
    {
        return ExecutarAsync(content, direcao, () => { repovoarSincrono?.Invoke(); return null; }, distancia, duracaoPorTrecho, curva);
    }

    /// <summary>Mesma coisa, mas para quem já repopula via corrotina própria (ex: FichaLoaderUI.SafePopulateRoutine)
    /// — espera ela terminar antes de deslizar de volta, em vez de disparar e seguir em frente.</summary>
    public static IEnumerator ExecutarAsync(RectTransform content, int direcao, Func<IEnumerator> repovoarRotina,
        float distancia = 70f, float duracaoPorTrecho = 0.14f, AnimationCurve curva = null)
    {
        if (content == null)
        {
            if (repovoarRotina != null) yield return repovoarRotina();
            yield break;
        }
        if (curva == null) curva = CurvaPadrao;

        Vector2 posOriginal = content.anchoredPosition;
        float   sinal       = direcao < 0 ? -1f : 1f;
        Vector2 posSaida    = posOriginal + new Vector2(-distancia * sinal, 0f);
        Vector2 posEntrada  = posOriginal + new Vector2(distancia * sinal, 0f);

        // 1) desliza a página atual pra fora
        yield return AnimarPosicao(content, posOriginal, posSaida, duracaoPorTrecho, curva);

        // 2) troca os itens enquanto o Content está fora da vista
        if (repovoarRotina != null) yield return repovoarRotina();

        // 3) reposiciona instantaneamente do lado oposto (ainda fora da vista) e desliza de volta
        content.anchoredPosition = posEntrada;
        yield return AnimarPosicao(content, posEntrada, posOriginal, duracaoPorTrecho, curva);

        content.anchoredPosition = posOriginal; // garante precisão no final
    }

    private static IEnumerator AnimarPosicao(RectTransform rt, Vector2 de, Vector2 para, float duracao, AnimationCurve curva)
    {
        if (duracao <= 0f) { rt.anchoredPosition = para; yield break; }

        float t = 0f;
        while (t < duracao)
        {
            t += Time.deltaTime;
            float progresso = curva.Evaluate(Mathf.Clamp01(t / duracao));
            rt.anchoredPosition = Vector2.LerpUnclamped(de, para, progresso);
            yield return null;
        }
        rt.anchoredPosition = para;
    }
}
