using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class BarraLateralFichaDnd : MonoBehaviour
{
    [Header("Botões e Prefabs (botão[i] corresponde à prefab[i])")]
    public Button[] botoes;
    public GameObject[] prefabs;

    [Header("Slider")]
    public GameObject painelSlider;
    public Transform conteudoContainer; // ex: content de um ScrollView dentro de painelSlider
    public float duracaoAnimacao = 0.35f;

    private RectTransform painelRect;
    private Vector2 posicaoAberta;
    private Vector2 posicaoFechada;
    private bool aberto = false;
    private bool animando = false;
    private int indiceAtual = -1;
    private int indicePendente = -1;
    private GameObject conteudoInstanciado;
    private Coroutine animacaoEmAndamento;

    void Awake()
    {
        if (painelSlider == null) return;

        painelRect = painelSlider.GetComponent<RectTransform>();
        if (painelRect == null) return;

        posicaoAberta = painelRect.anchoredPosition;
        posicaoFechada = posicaoAberta + new Vector2(painelRect.rect.width, 0f);

        painelRect.anchoredPosition = posicaoFechada;
        painelSlider.SetActive(false);
    }

    void OnEnable()
    {
        if (botoes == null) return;

        for (int i = 0; i < botoes.Length; i++)
        {
            if (botoes[i] == null) continue;
            int indice = i;
            botoes[i].onClick.AddListener(() => OnClickBotao(indice));
        }
    }

    void OnDisable()
    {
        if (botoes == null) return;

        for (int i = 0; i < botoes.Length; i++)
            if (botoes[i] != null) botoes[i].onClick.RemoveAllListeners();
    }

    private void OnClickBotao(int indice)
    {
        if (prefabs == null || indice < 0 || indice >= prefabs.Length || animando) return;

        if (aberto && indiceAtual == indice)
        {
            FecharSlider();
            return;
        }

        if (aberto)
        {
            // Fecha o painel atual e, ao terminar, reabre já com o novo conteúdo.
            indicePendente = indice;
            FecharSlider();
            return;
        }

        MostrarConteudo(indice);
        AbrirSlider();
    }

    private void MostrarConteudo(int indice)
    {
        if (conteudoInstanciado != null)
        {
            Destroy(conteudoInstanciado);
            conteudoInstanciado = null;
        }

        GameObject prefab = prefabs[indice];
        if (prefab != null && conteudoContainer != null)
        {
            conteudoInstanciado = UIUtils.Spawn(prefab, conteudoContainer);
            conteudoInstanciado.SetActive(true);
        }

        indiceAtual = indice;
    }

    public void AbrirSlider()
    {
        if (painelRect == null || animando || aberto) return;

        if (animacaoEmAndamento != null) StopCoroutine(animacaoEmAndamento);
        animacaoEmAndamento = StartCoroutine(Animar(posicaoFechada, posicaoAberta, aoTerminar: () => aberto = true));
    }

    public void FecharSlider()
    {
        if (painelRect == null || animando || !aberto) return;

        if (animacaoEmAndamento != null) StopCoroutine(animacaoEmAndamento);
        animacaoEmAndamento = StartCoroutine(Animar(posicaoAberta, posicaoFechada, aoTerminar: () =>
        {
            aberto = false;
            indiceAtual = -1;

            if (conteudoInstanciado != null)
            {
                Destroy(conteudoInstanciado);
                conteudoInstanciado = null;
            }

            painelSlider.SetActive(false);

            if (indicePendente >= 0)
            {
                int proximo = indicePendente;
                indicePendente = -1;
                MostrarConteudo(proximo);
                AbrirSlider();
            }
        }));
    }

    private IEnumerator Animar(Vector2 inicio, Vector2 fim, Action aoTerminar)
    {
        animando = true;
        painelSlider.SetActive(true);

        float tempo = 0f;
        while (tempo < duracaoAnimacao)
        {
            tempo += Time.unscaledDeltaTime;
            float t = Mathf.SmoothStep(0f, 1f, tempo / duracaoAnimacao);
            painelRect.anchoredPosition = Vector2.Lerp(inicio, fim, t);
            yield return null;
        }

        painelRect.anchoredPosition = fim;
        animando = false;
        aoTerminar?.Invoke();
    }
}
