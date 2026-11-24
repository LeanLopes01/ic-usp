using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PainelToggleAnimado : MonoBehaviour
{
    [Header("Alvo principal")]
    [SerializeField] private GameObject painel;

    [Header("Elementos extras (opcional)")]
    [SerializeField] private GameObject[] elementosAnimados;

    [Header("Animação")]
    [SerializeField, Min(0.05f)] private float duracaoAbertura = 0.22f;
    [SerializeField, Min(0.05f)] private float duracaoFechamento = 0.16f;
    [SerializeField, Range(0.5f, 0.98f)] private float escalaInicial = 0.85f;
    [SerializeField] private bool esconderNoStart = true;

    [Header("Slide de Baixo para Cima (opcional)")]
    [SerializeField] private bool usarSlide = false;
    [SerializeField] private float slideDeslocamento = 300f;

    private struct ElementoAnimado
    {
        public GameObject go;
        public RectTransform rect;
        public CanvasGroup group;
        public Vector2 posicaoRepouso;
    }

    private readonly List<ElementoAnimado> elementos = new();
    private Coroutine rotinaAnimacao;

    private bool inicializado;
    private bool aberto;

    private void Awake()
    {
        InicializarSeNecessario();
        if (esconderNoStart)
            FecharInstantaneo();
    }

    public void Abrir()
    {
        InicializarSeNecessario();
        // Reativar GameObjects antes de iniciar a coroutine — sem isso, StartCoroutine
        // falha silenciosamente quando o componente está num GameObject desativado.
        for (int i = 0; i < elementos.Count; i++)
            elementos[i].go.SetActive(true);
        IniciarAnimacao(true);
    }

    public void Fechar()
    {
        InicializarSeNecessario();
        IniciarAnimacao(false);
    }

    public void AbrirInstantaneo()
    {
        InicializarSeNecessario();
        if (elementos.Count == 0) return;

        for (int i = 0; i < elementos.Count; i++)
        {
            ElementoAnimado elemento = elementos[i];
            elemento.go.SetActive(true);
            elemento.rect.localScale = Vector3.one;
            elemento.group.alpha = 1f;
            elemento.group.interactable = true;
            elemento.group.blocksRaycasts = true;
        }

        aberto = true;
    }

    public void FecharInstantaneo()
    {
        InicializarSeNecessario();
        if (elementos.Count == 0) return;

        for (int i = 0; i < elementos.Count; i++)
        {
            ElementoAnimado elemento = elementos[i];
            elemento.rect.localScale = Vector3.one * escalaInicial;
            elemento.group.alpha = 0f;
            elemento.group.interactable = false;
            elemento.group.blocksRaycasts = false;
            if (usarSlide)
                elemento.rect.anchoredPosition = new Vector2(elemento.posicaoRepouso.x, elemento.posicaoRepouso.y - slideDeslocamento);
            elemento.go.SetActive(false);
        }

        aberto = false;
    }

    public bool EstaAberto()
    {
        return aberto;
    }

    private void IniciarAnimacao(bool abrir)
    {
        if (rotinaAnimacao != null)
            StopCoroutine(rotinaAnimacao);

        rotinaAnimacao = StartCoroutine(Animar(abrir));
    }

    private IEnumerator Animar(bool abrir)
    {
        if (elementos.Count == 0)
        {
            rotinaAnimacao = null;
            yield break;
        }

        float duracao = abrir ? duracaoAbertura : duracaoFechamento;

        int count = elementos.Count;
        Vector3[] escalaOrigem = new Vector3[count];
        float[] alphaOrigem = new float[count];
        Vector2[] posOrigem = new Vector2[count];

        for (int i = 0; i < count; i++)
        {
            ElementoAnimado elemento = elementos[i];
            if (abrir)
            {
                if (usarSlide)
                    elemento.rect.anchoredPosition = new Vector2(elemento.posicaoRepouso.x, elemento.posicaoRepouso.y - slideDeslocamento);
                elemento.go.SetActive(true);
            }

            elemento.group.interactable = false;
            elemento.group.blocksRaycasts = false;

            escalaOrigem[i] = elemento.rect.localScale;
            alphaOrigem[i] = elemento.group.alpha;
            posOrigem[i] = elemento.rect.anchoredPosition;
        }

        Vector3 escalaDestino = abrir ? Vector3.one : Vector3.one * escalaInicial;
        float alphaDestino = abrir ? 1f : 0f;

        float elapsed = 0f;
        while (elapsed < duracao)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duracao);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            for (int i = 0; i < count; i++)
            {
                ElementoAnimado elemento = elementos[i];
                elemento.rect.localScale = Vector3.Lerp(escalaOrigem[i], escalaDestino, eased);
                elemento.group.alpha = Mathf.Lerp(alphaOrigem[i], alphaDestino, eased);
                if (usarSlide)
                {
                    Vector2 posDestino = abrir
                        ? elemento.posicaoRepouso
                        : new Vector2(elemento.posicaoRepouso.x, elemento.posicaoRepouso.y - slideDeslocamento);
                    elemento.rect.anchoredPosition = Vector2.Lerp(posOrigem[i], posDestino, eased);
                }
            }

            yield return null;
        }

        for (int i = 0; i < count; i++)
        {
            ElementoAnimado elemento = elementos[i];
            elemento.rect.localScale = escalaDestino;
            elemento.group.alpha = alphaDestino;
            if (usarSlide)
                elemento.rect.anchoredPosition = abrir
                    ? elemento.posicaoRepouso
                    : new Vector2(elemento.posicaoRepouso.x, elemento.posicaoRepouso.y - slideDeslocamento);
        }

        if (abrir)
        {
            for (int i = 0; i < count; i++)
            {
                ElementoAnimado elemento = elementos[i];
                elemento.group.interactable = true;
                elemento.group.blocksRaycasts = true;
            }

            aberto = true;
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                ElementoAnimado elemento = elementos[i];
                elemento.go.SetActive(false);
            }

            aberto = false;
        }

        rotinaAnimacao = null;
    }

    private void InicializarSeNecessario()
    {
        if (inicializado)
            return;

        if (painel == null)
            painel = gameObject;

        elementos.Clear();
        AdicionarElementoSeValido(painel);

        if (elementosAnimados != null)
        {
            for (int i = 0; i < elementosAnimados.Length; i++)
                AdicionarElementoSeValido(elementosAnimados[i]);
        }

        if (elementos.Count == 0)
            Debug.LogWarning("[PainelToggleAnimado] Nenhum elemento válido para animar.");

        aberto = false;
        for (int i = 0; i < elementos.Count; i++)
        {
            if (elementos[i].go != null && elementos[i].go.activeSelf)
            {
                aberto = true;
                break;
            }
        }

        inicializado = true;
    }

    private void AdicionarElementoSeValido(GameObject alvo)
    {
        if (alvo == null)
            return;

        for (int i = 0; i < elementos.Count; i++)
        {
            if (elementos[i].go == alvo)
                return;
        }

        RectTransform rect = alvo.GetComponent<RectTransform>();
        if (rect == null)
            rect = alvo.GetComponentInChildren<RectTransform>(true);

        if (rect == null)
        {
            Debug.LogWarning($"[PainelToggleAnimado] '{alvo.name}' precisa de RectTransform para animar escala.");
            return;
        }

        CanvasGroup group = alvo.GetComponent<CanvasGroup>();
        if (group == null)
            group = alvo.AddComponent<CanvasGroup>();

        elementos.Add(new ElementoAnimado
        {
            go = alvo,
            rect = rect,
            group = group,
            posicaoRepouso = rect.anchoredPosition
        });
    }
}