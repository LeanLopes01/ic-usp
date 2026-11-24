using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class SmoothScreenTransition : MonoBehaviour
{
    [Header("Telas disponíveis")]
    [SerializeField] private List<GameObject> screens = new List<GameObject>();
    [SerializeField] private GameObject initialScreen;

    [Header("Animação")]
    [SerializeField, Min(0.05f)] private float duration = 0.25f;
    [SerializeField, Min(0.9f)] private float enterStartScale = 0.98f;
    [SerializeField, Min(1f)] private float exitEndScale = 1.01f;

    private GameObject currentScreen;
    private Coroutine transitionRoutine;

    private void Start()
    {
        InitializeScreens();
    }

    public void ShowScreen(GameObject nextScreen)
    {
        if (nextScreen == null)
        {
            return;
        }

        if (currentScreen == nextScreen)
        {
            return;
        }

        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
        }

        transitionRoutine = StartCoroutine(TransitionRoutine(nextScreen));
    }

    public void ShowScreenByIndex(int index)
    {
        if (index < 0 || index >= screens.Count)
        {
            Debug.LogWarning($"Índice de tela inválido: {index}");
            return;
        }

        ShowScreen(screens[index]);
    }

    public void ShowScreenByName(string screenName)
    {
        GameObject next = screens.Find(s => s != null && s.name == screenName);
        if (next == null)
        {
            Debug.LogWarning($"Tela não encontrada: {screenName}");
            return;
        }

        ShowScreen(next);
    }

    private void InitializeScreens()
    {
        if (initialScreen == null)
        {
            initialScreen = FindFirstActiveScreen();
        }

        currentScreen = initialScreen;

        for (int i = 0; i < screens.Count; i++)
        {
            GameObject screen = screens[i];
            if (screen == null)
            {
                continue;
            }

            CanvasGroup group = EnsureCanvasGroup(screen);
            bool isCurrent = screen == currentScreen;

            screen.SetActive(isCurrent);
            group.alpha = isCurrent ? 1f : 0f;
            group.interactable = isCurrent;
            group.blocksRaycasts = isCurrent;

            RectTransform rect = screen.transform as RectTransform;
            if (rect != null)
            {
                rect.localScale = Vector3.one;
            }
        }
    }

    private IEnumerator TransitionRoutine(GameObject nextScreen)
    {
        CanvasGroup nextGroup = EnsureCanvasGroup(nextScreen);
        RectTransform nextRect = nextScreen.transform as RectTransform;

        CanvasGroup currentGroup = null;
        RectTransform currentRect = null;

        if (currentScreen != null)
        {
            currentGroup = EnsureCanvasGroup(currentScreen);
            currentRect = currentScreen.transform as RectTransform;
        }

        nextScreen.SetActive(true);
        nextGroup.alpha = 0f;
        nextGroup.interactable = false;
        nextGroup.blocksRaycasts = false;

        if (nextRect != null)
        {
            nextRect.localScale = Vector3.one * enterStartScale;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            if (currentGroup != null)
            {
                currentGroup.alpha = 1f - eased;
            }

            nextGroup.alpha = eased;

            if (currentRect != null)
            {
                currentRect.localScale = Vector3.Lerp(Vector3.one, Vector3.one * exitEndScale, eased);
            }

            if (nextRect != null)
            {
                nextRect.localScale = Vector3.Lerp(Vector3.one * enterStartScale, Vector3.one, eased);
            }

            yield return null;
        }

        if (currentScreen != null)
        {
            currentGroup.alpha = 0f;
            currentGroup.interactable = false;
            currentGroup.blocksRaycasts = false;

            if (currentRect != null)
            {
                currentRect.localScale = Vector3.one;
            }

            currentScreen.SetActive(false);
        }

        nextGroup.alpha = 1f;
        nextGroup.interactable = true;
        nextGroup.blocksRaycasts = true;

        if (nextRect != null)
        {
            nextRect.localScale = Vector3.one;
        }

        currentScreen = nextScreen;
        transitionRoutine = null;
    }

    private GameObject FindFirstActiveScreen()
    {
        for (int i = 0; i < screens.Count; i++)
        {
            if (screens[i] != null && screens[i].activeSelf)
            {
                return screens[i];
            }
        }

        return screens.Count > 0 ? screens[0] : null;
    }

    private CanvasGroup EnsureCanvasGroup(GameObject target)
    {
        CanvasGroup group = target.GetComponent<CanvasGroup>();
        if (group == null)
        {
            group = target.AddComponent<CanvasGroup>();
        }

        return group;
    }
}