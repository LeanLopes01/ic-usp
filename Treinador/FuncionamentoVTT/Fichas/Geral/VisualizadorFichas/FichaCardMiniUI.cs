using System;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class FichaCardMiniUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI")]
    public TMP_Text nomeText;
    public TMP_Text classeText;
    public Image    fotoImage;

    private int    _generalId;
    private int    _dndId;
    private string _nome;
    private int    _level;

    // generalId, dndId, nome, level, fichaIncompleta
    private Action<int, int, string, int, bool> _onClique;

    void Awake() => AutoBind();

    public void SetFicha(string nome, string classe, int generalId, int dndId,
                         int level, string imagemPath,
                         Action<int, int, string, int, bool> onClique)
    {
        _nome      = nome;
        _generalId = generalId;
        _dndId     = dndId;
        _level     = level;
        _onClique  = onClique;

        if (nomeText   != null) nomeText.text   = nome   ?? "<sem nome>";
        if (classeText != null) classeText.text = PrimeiraClasse(classe);

        if (!string.IsNullOrWhiteSpace(imagemPath) && fotoImage != null)
            CarregarImagem(imagemPath);
    }

    public void OnPointerClick(PointerEventData _)
    {
        int    pid = DataManager.Instance != null ? DataManager.Instance.GetCurrentId()      : 0;
        string sid = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";
        bool incompleta = LocalFichaStorage.IsFichaIncompleta(sid, pid, _generalId, _dndId);
        _onClique?.Invoke(_generalId, _dndId, _nome, _level, incompleta);
    }

    private static string PrimeiraClasse(string classe)
    {
        if (string.IsNullOrWhiteSpace(classe)) return "-";
        int barra = classe.IndexOf('/');
        return barra >= 0 ? classe.Substring(0, barra).Trim() : classe.Trim();
    }

    private void CarregarImagem(string path)
    {
        try
        {
            string full = Path.IsPathRooted(path) ? path : Path.Combine(Application.dataPath, path);
            if (!File.Exists(full)) return;

            byte[]    data = File.ReadAllBytes(full);
            Texture2D tex  = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(data);

            fotoImage.sprite  = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), Vector2.one * 0.5f);
            fotoImage.enabled = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[FichaCardMiniUI] Erro ao carregar imagem: {ex.Message}");
        }
    }

    private void AutoBind()
    {
        if (nomeText == null || classeText == null)
        {
            foreach (TMP_Text t in GetComponentsInChildren<TMP_Text>(true))
            {
                string n = t.gameObject.name.ToLowerInvariant();
                if (nomeText   == null && (n.Contains("nome") || n.Contains("name")))   nomeText   = t;
                else if (classeText == null && (n.Contains("class") || n.Contains("classe"))) classeText = t;
            }
        }

        if (fotoImage == null)
        {
            foreach (Image img in GetComponentsInChildren<Image>(true))
            {
                string n = img.gameObject.name.ToLowerInvariant();
                if (n.Contains("foto") || n.Contains("image") || n.Contains("token") || n.Contains("photo"))
                { fotoImage = img; break; }
            }
        }
    }
}
