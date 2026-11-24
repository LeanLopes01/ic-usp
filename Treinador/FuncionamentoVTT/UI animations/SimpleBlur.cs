using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class MultiBlurTransitionPro : MonoBehaviour
{
    [Header("Imagens normais (sem blur)")]
    public List<Image> normalImages = new List<Image>();

    [Header("Imagens borradas (mesmas posições que as normais)")]
    public List<Image> blurredImages = new List<Image>();

    [Header("Painéis de UI")]
    public GameObject uiBemVindo;
    public GameObject uiLogin;
    public GameObject uiRegistro;

    [Header("Configuração da transição")]
    public float transitionDuration = 1f; // duração total da transição
    public float uiSlideDistance = 200f;  // distância lateral do movimento de entrada

    private Coroutine transitionRoutine;
    private GameObject painelAtual;

    void Start()
    {
        foreach (var img in blurredImages)
            img.color = new Color(img.color.r, img.color.g, img.color.b, 0);

        painelAtual = uiBemVindo;
        uiBemVindo.SetActive(true);
        uiLogin.SetActive(false);
        uiRegistro.SetActive(false);

        // Garante que todos tenham CanvasGroup
        GarantirCanvasGroup(uiBemVindo);
        GarantirCanvasGroup(uiLogin);
        GarantirCanvasGroup(uiRegistro);

        // Estado inicial: visível apenas o "Bem-vindo"
        SetUIVisibility(uiBemVindo, 1, Vector2.zero);
        SetUIVisibility(uiLogin, 0, Vector2.zero);
        SetUIVisibility(uiRegistro, 0, Vector2.zero);
    }

    public void MostrarLogin() => IniciarTransicao(uiLogin);
    public void MostrarRegistro() => IniciarTransicao(uiRegistro);
    public void VoltarBemVindo() => IniciarTransicao(uiBemVindo);

    private void IniciarTransicao(GameObject novoPainel)
    {
        if (painelAtual == novoPainel) return;
        if (transitionRoutine != null) StopCoroutine(transitionRoutine);
        transitionRoutine = StartCoroutine(FazerTransicao(novoPainel));
    }

    private IEnumerator FazerTransicao(GameObject novoPainel)
    {
        // --- 1. Preparar UI alvo ---
        var grupoAntigo = painelAtual.GetComponent<CanvasGroup>();
        var grupoNovo = novoPainel.GetComponent<CanvasGroup>();

        novoPainel.SetActive(true);
        SetUIVisibility(novoPainel, 0, new Vector2(uiSlideDistance, 0));

        // --- 2. Blur do fundo ---
        float elapsed = 0f;
        bool indoParaBlur = (novoPainel != uiBemVindo);
        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / transitionDuration;

            // Fade de blur
            float blurAlpha = Mathf.Lerp(indoParaBlur ? 0 : 1, indoParaBlur ? 1 : 0, t);
            foreach (var img in blurredImages)
            {
                var c = img.color;
                c.a = blurAlpha;
                img.color = c;
            }

            // Fade & slide da UI antiga
            if (grupoAntigo)
            {
                grupoAntigo.alpha = Mathf.Lerp(1, 0, t);
                grupoAntigo.transform.localPosition = Vector2.Lerp(Vector2.zero, new Vector2(-uiSlideDistance, 0), t);
            }

            // Fade & slide da nova UI
            if (grupoNovo)
            {
                grupoNovo.alpha = Mathf.Lerp(0, 1, t);
                grupoNovo.transform.localPosition = Vector2.Lerp(new Vector2(uiSlideDistance, 0), Vector2.zero, t);
            }

            yield return null;
        }

        // --- 3. Finalizar estados ---
        foreach (var img in blurredImages)
        {
            var c = img.color;
            c.a = indoParaBlur ? 1 : 0;
            img.color = c;
        }

        SetUIVisibility(novoPainel, 1, Vector2.zero);
        SetUIVisibility(painelAtual, 0, Vector2.zero);
        painelAtual.SetActive(false);
        painelAtual = novoPainel;
    }

    private void SetUIVisibility(GameObject go, float alpha, Vector2 position)
    {
        var cg = go.GetComponent<CanvasGroup>();
        if (cg)
        {
            cg.alpha = alpha;
            cg.transform.localPosition = position;
        }
    }

    private void GarantirCanvasGroup(GameObject go)
    {
        if (!go.GetComponent<CanvasGroup>())
            go.AddComponent<CanvasGroup>();
    }
}
