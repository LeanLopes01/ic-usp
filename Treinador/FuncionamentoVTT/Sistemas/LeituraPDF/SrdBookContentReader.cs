using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class SrdBookContentReader : MonoBehaviour
{
    [Header("ScrollView")]
    public ScrollRect scrollRect;
    public RectTransform contentRoot;

    [Header("Fonte das Páginas (imagens)")]
    [Tooltip("Pasta relativa dentro de StreamingAssets. Ex: Livros/srd-pages")]
    public string pagesRelativeFolder = "Livros/srd-pages";
    public string pageFilePrefix = "page_";
    public string pageFileExtension = ".png";
    public int pageNumberDigits = 3;
    public int firstPage = 1;
    public int lastPage = 400;

    [Header("Layout")]
    public float spacing = 18f;
    public Vector2 fallbackPageSize = new Vector2(1000f, 1400f);

    [Header("Comportamento")]
    public bool loadOnEnable = true;
    public bool clearContentBeforeBuild = true;

    [Header("UI (Opcional)")]
    public TMP_Text statusText;

    private readonly List<RectTransform> pageRects = new List<RectTransform>();
    private readonly Dictionary<int, int> pageToIndex = new Dictionary<int, int>();
    private bool isLoading;
    private bool isBuilt;
    private int pendingPage = -1;

    private void OnEnable()
    {
        if (loadOnEnable)
        {
            EnsureLoaded();
        }
    }

    public void EnsureLoaded()
    {
        if (isBuilt || isLoading) return;
        StartCoroutine(BuildPagesRoutine());
    }

    public void IrParaPagina(int page)
    {
        int targetPage = Mathf.Max(1, page);
        if (!isBuilt)
        {
            pendingPage = targetPage;
            EnsureLoaded();
            return;
        }

        if (!pageToIndex.TryGetValue(targetPage, out int index))
        {
            DefinirStatus($"Página {targetPage} não encontrada nas imagens.");
            return;
        }

        if (pageRects.Count <= 1)
        {
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;
            return;
        }

        float t = (float)index / (pageRects.Count - 1);
        float normalized = 1f - Mathf.Clamp01(t);

        if (scrollRect != null)
        {
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = normalized;
        }

        DefinirStatus($"Livro aberto na página {targetPage}.");
    }

    private IEnumerator BuildPagesRoutine()
    {
        isLoading = true;
        DefinirStatus("Carregando livro...");

        if (scrollRect == null) scrollRect = GetComponentInParent<ScrollRect>();
        if (contentRoot == null && scrollRect != null) contentRoot = scrollRect.content;

        if (contentRoot == null)
        {
            DefinirStatus("Content do ScrollView não configurado.");
            isLoading = false;
            yield break;
        }

        PrepararLayoutContent(contentRoot);

        if (clearContentBeforeBuild)
        {
            for (int i = contentRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(contentRoot.GetChild(i).gameObject);
            }
            yield return null;
        }

        pageRects.Clear();
        pageToIndex.Clear();

        int loaded = 0;
        int missesAfterFirst = 0;

        for (int page = firstPage; page <= lastPage; page++)
        {
            string file = pageFilePrefix + page.ToString("D" + pageNumberDigits) + pageFileExtension;
            string fullPath = Path.Combine(Application.streamingAssetsPath, pagesRelativeFolder ?? string.Empty, file);

            if (!File.Exists(fullPath))
            {
                if (loaded > 0)
                {
                    missesAfterFirst++;
                    if (missesAfterFirst >= 15) break;
                }
                continue;
            }

            missesAfterFirst = 0;
            string uri = "file:///" + fullPath.Replace("\\", "/");
            using (UnityWebRequest req = UnityWebRequestTexture.GetTexture(uri))
            {
                yield return req.SendWebRequest();
                if (req.result != UnityWebRequest.Result.Success)
                {
                    continue;
                }

                Texture2D tex = DownloadHandlerTexture.GetContent(req);
                if (tex == null) continue;

                RectTransform pageRect = CriarPaginaUI(page, tex);
                pageToIndex[page] = pageRects.Count;
                pageRects.Add(pageRect);
                loaded++;
            }

            if (loaded % 8 == 0)
            {
                DefinirStatus($"Carregando livro... {loaded} páginas");
                yield return null;
            }
        }

        isLoading = false;
        isBuilt = loaded > 0;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);

        if (!isBuilt)
        {
            DefinirStatus("Nenhuma página encontrada. Exporte o PDF para PNG em StreamingAssets/Livros/srd-pages.");
            yield break;
        }

        DefinirStatus($"Livro carregado ({loaded} páginas).");

        if (pendingPage > 0)
        {
            int alvo = pendingPage;
            pendingPage = -1;
            IrParaPagina(alvo);
        }
    }

    private RectTransform CriarPaginaUI(int page, Texture2D tex)
    {
        GameObject go = new GameObject($"Page_{page}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        go.transform.SetParent(contentRoot, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        Image img = go.GetComponent<Image>();
        LayoutElement le = go.GetComponent<LayoutElement>();

        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        img.sprite = sprite;
        img.preserveAspect = true;

        float viewportWidth = 0f;
        if (scrollRect != null && scrollRect.viewport != null)
            viewportWidth = Mathf.Max(0f, scrollRect.viewport.rect.width - 24f);

        float width = viewportWidth > 10f ? viewportWidth : fallbackPageSize.x;
        float height = tex.width > 0 ? width * ((float)tex.height / tex.width) : fallbackPageSize.y;

        le.preferredWidth = width;
        le.preferredHeight = height;
        le.minHeight = height;

        return rt;
    }

    private static void PrepararLayoutContent(RectTransform content)
    {
        VerticalLayoutGroup vlg = content.GetComponent<VerticalLayoutGroup>();
        if (vlg == null) vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();

        vlg.childControlHeight = true;
        vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.spacing = 18f;
        vlg.padding = new RectOffset(10, 10, 10, 10);

        ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = content.gameObject.AddComponent<ContentSizeFitter>();

        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void DefinirStatus(string txt)
    {
        if (statusText != null)
        {
            statusText.text = txt;
        }
    }
}