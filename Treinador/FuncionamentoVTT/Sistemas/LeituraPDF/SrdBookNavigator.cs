using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class SrdBookNavigator : MonoBehaviour
{
    [Serializable]
    public class SrdPageEvent : UnityEvent<int, string> { }

    [Header("Índice de Referências")]
    public SrdReferenceIndex referenceIndex;

    [Header("Arquivo PDF (opcional)")]
    [Tooltip("Caminho relativo dentro de StreamingAssets. Ex: Livros/srd-5e-ptbr.pdf")]
    public string pdfRelativePath = "Livros/srd-5e-ptbr.pdf";
    public bool openExternalPdf = true;
    [Tooltip("Alguns viewers usam página base 0.")]
    public bool useZeroBasedPageForExternalViewer;

    [Header("Mapa Dinâmico (JSON em StreamingAssets)")]
    [Tooltip("Ex: Livros/srd-index.json")]
    public string indexRelativePath = "Livros/srd-index.json";
    public bool loadRuntimeIndexOnAwake = true;

    [Header("UI de Feedback (opcional)")]
    public TMP_Text feedbackText;

    [Header("Leitor no Content (opcional)")]
    public SrdBookContentReader contentReader;

    [Header("Eventos")]
    public SrdPageEvent OnPageRequested;

    [Serializable]
    private class RuntimeEntry
    {
        public string term;
        public int page = 1;
        public string section;
        public string[] aliases;
    }

    [Serializable]
    private class RuntimeEntryList
    {
        public RuntimeEntry[] entries;
    }

    private readonly Dictionary<string, RuntimeEntry> runtimeMap = new Dictionary<string, RuntimeEntry>(StringComparer.OrdinalIgnoreCase);

    private void Awake()
    {
        if (loadRuntimeIndexOnAwake)
        {
            CarregarMapaRuntime();
        }
    }

    public void CarregarMapaRuntime()
    {
        runtimeMap.Clear();

        string path = Path.Combine(Application.streamingAssetsPath, indexRelativePath ?? string.Empty);
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            RuntimeEntryList payload = JsonUtility.FromJson<RuntimeEntryList>(json);
            if (payload?.entries == null) return;

            foreach (var entry in payload.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.term)) continue;
                RegistrarRuntime(entry.term, entry);

                if (entry.aliases == null) continue;
                foreach (var alias in entry.aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    RegistrarRuntime(alias, entry);
                }
            }
        }
        catch (Exception ex)
        {
            VTTLog.LoadWarn($"SrdBookNavigator: falha ao carregar index dinâmico: {ex.Message}");
        }
    }

    public void MostrarPorTermo(string termo)
    {
        if (TryGetRuntimeEntry(termo, out var runtime))
        {
            string secaoRuntime = string.IsNullOrWhiteSpace(runtime.section) ? runtime.term : runtime.section;
            MostrarPagina(runtime.page, secaoRuntime);
            return;
        }

        if (referenceIndex == null)
        {
            DefinirFeedback("Índice SRD não configurado.");
            return;
        }

        if (!referenceIndex.TryGetEntry(termo, out var entry) || entry == null)
        {
            DefinirFeedback($"Referência não encontrada: {termo}");
            return;
        }

        MostrarPagina(entry.pagina, string.IsNullOrWhiteSpace(entry.secao) ? entry.termo : entry.secao);
    }

    public void MostrarPagina(int pagina, string secao = "")
    {
        int paginaFinal = Mathf.Max(1, pagina);
        string tituloSecao = string.IsNullOrWhiteSpace(secao) ? "SRD" : secao;

        if (contentReader != null)
        {
            contentReader.IrParaPagina(paginaFinal);
        }

        OnPageRequested?.Invoke(paginaFinal, tituloSecao);
        DefinirFeedback($"{tituloSecao} - página {paginaFinal}");

        if (openExternalPdf)
        {
            AbrirPdfExterno(paginaFinal);
        }
    }

    public void MostrarPorTextoSelecionavel(string textoUI)
    {
        if (string.IsNullOrWhiteSpace(textoUI)) return;

        string termo = textoUI.Trim();
        int idxPipe = termo.IndexOf('|');
        if (idxPipe >= 0 && idxPipe < termo.Length - 1)
        {
            termo = termo.Substring(idxPipe + 1).Trim();
        }

        MostrarPorTermo(termo);
    }

    private void AbrirPdfExterno(int pagina)
    {
        string caminhoCompleto = Path.Combine(Application.streamingAssetsPath, pdfRelativePath ?? string.Empty);
        if (!File.Exists(caminhoCompleto))
        {
            VTTLog.LoadWarn($"SrdBookNavigator: PDF não encontrado: {caminhoCompleto}");
            return;
        }

        int paginaViewer = useZeroBasedPageForExternalViewer ? Mathf.Max(0, pagina - 1) : pagina;
        string uri = "file:///" + caminhoCompleto.Replace("\\", "/") + "#page=" + paginaViewer;
        Application.OpenURL(uri);
    }

    private void DefinirFeedback(string texto)
    {
        if (feedbackText != null)
        {
            feedbackText.text = texto;
        }
    }

    private bool TryGetRuntimeEntry(string termo, out RuntimeEntry entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(termo)) return false;

        string chave = Normalizar(termo);
        if (runtimeMap.TryGetValue(chave, out var found))
        {
            entry = found;
            return true;
        }

        foreach (var kvp in runtimeMap)
        {
            if (kvp.Key.Contains(chave) || chave.Contains(kvp.Key))
            {
                entry = kvp.Value;
                return true;
            }
        }

        return false;
    }

    private void RegistrarRuntime(string termo, RuntimeEntry entry)
    {
        string chave = Normalizar(termo);
        if (string.IsNullOrWhiteSpace(chave)) return;
        runtimeMap[chave] = entry;
    }

    private static string Normalizar(string texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return string.Empty;

        string lower = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        char[] chars = new char[lower.Length];
        int idx = 0;
        for (int i = 0; i < lower.Length; i++)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(lower[i]);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                chars[idx++] = lower[i];
            }
        }

        return new string(chars, 0, idx).Normalize(NormalizationForm.FormC);
    }
}