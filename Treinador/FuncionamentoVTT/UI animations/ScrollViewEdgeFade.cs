using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ScrollViewEdgeFade : MonoBehaviour
{
    [Header("Referências")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private CanvasGroup topFade;
    [SerializeField] private CanvasGroup bottomFade;

    [Header("Configuração")]
    [SerializeField, Min(0.001f)] private float fadeRangeNormalized = 0.1f;
    [SerializeField, Min(0f)] private float maxAlpha = 1f;
    [SerializeField, Min(0f)] private float smoothSpeed = 12f;

    private float targetTopAlpha;
    private float targetBottomAlpha;

    private void Awake()
    {
        if (scrollRect == null)
        {
            scrollRect = GetComponent<ScrollRect>();
        }

        UpdateTargets();
        ApplyImmediate();
    }

    private void OnEnable()
    {
        if (scrollRect != null)
        {
            scrollRect.onValueChanged.AddListener(OnScrollChanged);
        }

        UpdateTargets();
        ApplyImmediate();
    }

    private void OnDisable()
    {
        if (scrollRect != null)
        {
            scrollRect.onValueChanged.RemoveListener(OnScrollChanged);
        }
    }

    private void LateUpdate()
    {
        UpdateTargets();
        SmoothApply();
    }

    private void OnScrollChanged(Vector2 _)
    {
        UpdateTargets();
    }

    private void UpdateTargets()
    {
        if (scrollRect == null || topFade == null || bottomFade == null)
        {
            return;
        }

        if (!IsScrollable())
        {
            targetTopAlpha = 0f;
            targetBottomAlpha = 0f;
            return;
        }

        float vertical = Mathf.Clamp01(scrollRect.verticalNormalizedPosition);
        float topAmount = Mathf.InverseLerp(0f, fadeRangeNormalized, 1f - vertical);
        float bottomAmount = Mathf.InverseLerp(0f, fadeRangeNormalized, vertical);

        targetTopAlpha = topAmount * maxAlpha;
        targetBottomAlpha = bottomAmount * maxAlpha;
    }

    private bool IsScrollable()
    {
        if (scrollRect.content == null || scrollRect.viewport == null)
        {
            return false;
        }

        float contentHeight = scrollRect.content.rect.height;
        float viewportHeight = scrollRect.viewport.rect.height;
        return contentHeight > viewportHeight + 0.5f;
    }

    private void ApplyImmediate()
    {
        if (topFade != null)
        {
            topFade.alpha = targetTopAlpha;
        }

        if (bottomFade != null)
        {
            bottomFade.alpha = targetBottomAlpha;
        }
    }

    private void SmoothApply()
    {
        if (topFade != null)
        {
            topFade.alpha = Mathf.Lerp(topFade.alpha, targetTopAlpha, Time.unscaledDeltaTime * smoothSpeed);
        }

        if (bottomFade != null)
        {
            bottomFade.alpha = Mathf.Lerp(bottomFade.alpha, targetBottomAlpha, Time.unscaledDeltaTime * smoothSpeed);
        }
    }
}