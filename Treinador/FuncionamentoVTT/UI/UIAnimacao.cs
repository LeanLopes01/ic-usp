using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public static class UIAnimacao
{
    private const float DuracaoPadraoSegundos = 0.2f;

    // Guarda a altura configurada originalmente no prefab (antes de qualquer animação tocar no
    // LayoutElement), pra blocos com altura fixa por design (ex: detalhes de uma magia, cujos campos
    // de texto não reportam uma altura de conteúdo confiável pro layout medir sozinho).
    private class AlturaConfiguradaCache : MonoBehaviour
    {
        public float minHeight;
        public float preferredHeight;
    }

    /// <summary>
    /// Anima a altura de um bloco de conteúdo (accordion): encolhe de baixo para cima ao colapsar
    /// e cresce de cima para baixo ao expandir, em vez de aparecer/sumir de forma brusca via SetActive.
    /// </summary>
    public static Coroutine AnimarExpansao(MonoBehaviour dono, RectTransform conteudo, bool expandir,
        RectTransform layoutParaReconstruir = null, float duracaoSegundos = DuracaoPadraoSegundos, System.Action aoConcluir = null)
    {
        if (dono == null || conteudo == null || !dono.isActiveAndEnabled) return null;
        return dono.StartCoroutine(RotinaAnimarExpansao(conteudo, expandir, layoutParaReconstruir, duracaoSegundos, aoConcluir));
    }

    private static IEnumerator RotinaAnimarExpansao(RectTransform conteudo, bool expandir,
        RectTransform layoutParaReconstruir, float duracaoSegundos, System.Action aoConcluir)
    {
        // Sem isso, o conteúdo interno é desenhado no tamanho normal desde o primeiro frame e "vaza"
        // por cima do que vem depois enquanto a caixa ainda está crescendo/encolhendo — o RectMask2D
        // corta visualmente tudo que ultrapassa os limites atuais do RectTransform sendo animado.
        GarantirMascara(conteudo);

        LayoutElement layoutElement = conteudo.GetComponent<LayoutElement>();
        if (layoutElement == null) layoutElement = conteudo.gameObject.AddComponent<LayoutElement>();

        // Na primeira vez que este conteúdo é animado, guarda a altura que já estava configurada no
        // LayoutElement (definida no prefab) antes de mexermos nela — senão, ao colapsar e expandir de
        // novo, o valor original (ex: 20 no seu caso) já teria sido sobrescrito pela própria animação.
        AlturaConfiguradaCache cache = conteudo.GetComponent<AlturaConfiguradaCache>();
        if (cache == null)
        {
            cache = conteudo.gameObject.AddComponent<AlturaConfiguradaCache>();
            cache.minHeight = layoutElement.minHeight;
            cache.preferredHeight = layoutElement.preferredHeight;
        }

        bool temAlturaFixaConfigurada = cache.preferredHeight >= 0f;

        // Um ContentSizeFitter no próprio conteúdo (comum em blocos que se auto-dimensionam por
        // quantidade de itens, como o grupo de magias por nível) recalcula e sobrescreve o sizeDelta a
        // cada rebuild de layout, brigando com a altura animada — precisa ficar desligado durante a
        // transição. Não usamos essa medição dinâmica quando já existe uma altura fixa configurada.
        ContentSizeFitter fitter = temAlturaFixaConfigurada ? null : conteudo.GetComponent<ContentSizeFitter>();
        if (fitter != null) fitter.enabled = false;

        conteudo.gameObject.SetActive(true);

        float alturaTotal;
        if (temAlturaFixaConfigurada)
        {
            alturaTotal = cache.preferredHeight;
        }
        else
        {
            // Reconstrói o próprio conteúdo primeiro (ex: lista de magias dentro dele pode ter mudado
            // desde a última medição) e só então lê a altura "natural", sem travar via LayoutElement.
            layoutElement.minHeight = -1f;
            layoutElement.preferredHeight = -1f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(conteudo);
            alturaTotal = LayoutUtility.GetPreferredHeight(conteudo);
        }

        float alturaInicial = expandir ? 0f : alturaTotal;
        float alturaFinal = expandir ? alturaTotal : 0f;

        AplicarAltura(conteudo, layoutElement, alturaInicial);
        if (layoutParaReconstruir != null) LayoutRebuilder.ForceRebuildLayoutImmediate(layoutParaReconstruir);

        float tempo = 0f;
        while (tempo < duracaoSegundos)
        {
            tempo += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(tempo / duracaoSegundos));
            AplicarAltura(conteudo, layoutElement, Mathf.Lerp(alturaInicial, alturaFinal, t));
            if (layoutParaReconstruir != null) LayoutRebuilder.ForceRebuildLayoutImmediate(layoutParaReconstruir);
            yield return null;
        }

        if (expandir)
        {
            if (temAlturaFixaConfigurada)
            {
                // Restaura exatamente os valores originais do prefab (evita qualquer deriva de ponto
                // flutuante do Lerp) em vez de "soltar" para automático.
                layoutElement.minHeight = cache.minHeight;
                layoutElement.preferredHeight = cache.preferredHeight;
            }
            else
            {
                // Libera a altura travada para o conteúdo voltar a acompanhar mudanças de tamanho
                // normalmente, e devolve o controle pro ContentSizeFitter (se existir).
                layoutElement.minHeight = -1f;
                layoutElement.preferredHeight = -1f;
                if (fitter != null) fitter.enabled = true;
            }
        }
        else
        {
            AplicarAltura(conteudo, layoutElement, 0f);
            conteudo.gameObject.SetActive(false);
            // Mantém o fitter desligado enquanto colapsado — religar agora faria ele reimpor a altura cheia.
        }

        if (layoutParaReconstruir != null) LayoutRebuilder.ForceRebuildLayoutImmediate(layoutParaReconstruir);

        aoConcluir?.Invoke();
    }

    private static void GarantirMascara(RectTransform conteudo)
    {
        if (conteudo.GetComponent<RectMask2D>() == null) conteudo.gameObject.AddComponent<RectMask2D>();
    }

    // Trava minHeight e preferredHeight juntos (evita que Child Force Expand Height do pai ignore o
    // valor desejado) e também escreve o sizeDelta direto, cobrindo o caso de não haver LayoutGroup no pai.
    private static void AplicarAltura(RectTransform conteudo, LayoutElement layoutElement, float altura)
    {
        layoutElement.minHeight = altura;
        layoutElement.preferredHeight = altura;

        Vector2 size = conteudo.sizeDelta;
        size.y = altura;
        conteudo.sizeDelta = size;
    }

    /// <summary>
    /// Aplica o estado expandido/colapsado instantaneamente (sem animação) — usado ao (re)popular a UI,
    /// para não reanimar toda vez que a lista é reconstruída por causa de outro filtro/busca.
    /// </summary>
    public static void AplicarExpansaoInstantanea(RectTransform conteudo, bool expandido)
    {
        if (conteudo == null) return;

        GarantirMascara(conteudo);

        LayoutElement layoutElement = conteudo.GetComponent<LayoutElement>();
        AlturaConfiguradaCache cache = conteudo.GetComponent<AlturaConfiguradaCache>();
        bool temAlturaFixaConfigurada = cache != null && cache.preferredHeight >= 0f;
        ContentSizeFitter fitter = temAlturaFixaConfigurada ? null : conteudo.GetComponent<ContentSizeFitter>();

        if (expandido)
        {
            if (layoutElement != null)
            {
                layoutElement.minHeight = temAlturaFixaConfigurada ? cache.minHeight : -1f;
                layoutElement.preferredHeight = temAlturaFixaConfigurada ? cache.preferredHeight : -1f;
            }
            if (fitter != null) fitter.enabled = true;
        }
        else
        {
            if (fitter != null) fitter.enabled = false;
            if (layoutElement != null)
            {
                layoutElement.minHeight = 0f;
                layoutElement.preferredHeight = 0f;
                Vector2 size = conteudo.sizeDelta;
                size.y = 0f;
                conteudo.sizeDelta = size;
            }
        }

        conteudo.gameObject.SetActive(expandido);
    }
}
