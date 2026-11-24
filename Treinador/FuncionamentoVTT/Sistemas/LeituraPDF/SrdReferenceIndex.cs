using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

[CreateAssetMenu(fileName = "SrdReferenceIndex", menuName = "D&D/SRD/Reference Index")]
public class SrdReferenceIndex : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string termo;
        public int pagina = 1;
        public string secao;
        public List<string> aliases = new List<string>();
    }

    public List<Entry> entradas = new List<Entry>();

    private readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private bool cachePronto;

    public bool TryGetEntry(string termo, out Entry entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(termo)) return false;

        GarantirCache();
        string chave = Normalizar(termo);

        if (cache.TryGetValue(chave, out var encontrado))
        {
            entry = encontrado;
            return true;
        }

        foreach (var kvp in cache)
        {
            if (kvp.Key.Contains(chave) || chave.Contains(kvp.Key))
            {
                entry = kvp.Value;
                return true;
            }
        }

        return false;
    }

    public void RebuildCache()
    {
        cache.Clear();
        cachePronto = false;
        GarantirCache();
    }

    private void GarantirCache()
    {
        if (cachePronto) return;

        cache.Clear();
        if (entradas != null)
        {
            foreach (var e in entradas)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.termo)) continue;

                string termoPrincipal = Normalizar(e.termo);
                if (!cache.ContainsKey(termoPrincipal)) cache.Add(termoPrincipal, e);

                if (e.aliases == null) continue;
                foreach (var alias in e.aliases)
                {
                    if (string.IsNullOrWhiteSpace(alias)) continue;
                    string a = Normalizar(alias);
                    if (!cache.ContainsKey(a)) cache.Add(a, e);
                }
            }
        }

        cachePronto = true;
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