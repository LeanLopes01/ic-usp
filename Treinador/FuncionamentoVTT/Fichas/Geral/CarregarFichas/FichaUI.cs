using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
using System;
using System.IO;

public class FichaUI : MonoBehaviour, IPointerClickHandler
{
    [Header("Visuais")]
    [FormerlySerializedAs("fichaImage")]
    public Image tokenImage;

    [Header("Textos Principais")]
    [FormerlySerializedAs("nameText")]
    public TMP_Text nomeText;
    [FormerlySerializedAs("levelText")]
    public TMP_Text levelTotalText; // Ex: "10"

    [Header("Classes")]
    public TMP_Text classePrincipalText; // Ex: "Guerreiro 6"
    public TMP_Text classeSecundariaText; // Ex: "Bárbaro 4" (multiclasse)

    [Header("Metadados")]
    public TMP_Text campanhaText;
    public TMP_Text sistemaText;
    public TMP_Text dataCriacaoText;

    [Header("Config")]
    public bool isClickable = true;

    // IDs da ficha para edição
    private int fichasGeneralId;
    private int fichasDndId;
    private string nomePersonagem;
    private int levelPersonagem;
    private string racePersonagem;
    private string classePersonagem;
    private Action<FichaData> customClickHandler;
    private Action<FichaData> doubleClickHandler;

    private float _ultimoCliqueTime = -1f;
    private const float DoubleClickInterval = 0.3f;

    void Awake()
    {
        AutoBindTextRefsIfNeeded();
        AutoBindImageRefIfNeeded();

        VTTLog.UI($"FichaUI.Awake '{gameObject.name}' | nomeText={(nomeText!=null)} levelTotalText={(levelTotalText!=null)} tokenImage={(tokenImage!=null)}");

        // Se não tiver Button component, adicione um para ficar clicável
        if (GetComponent<Button>() == null && isClickable)
        {
            var button = gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(OnFichaClicked);
        }
    }

    public void SetCustomClickHandler(Action<FichaData> handler)
    {
        customClickHandler = handler;
    }

    public void SetDoubleClickHandler(Action<FichaData> handler)
    {
        doubleClickHandler = handler;
    }

    public FichaData GetFichaData()
    {
        return new FichaData
        {
            name_pc = nomePersonagem,
            level_pc = levelPersonagem,
            race_pc = racePersonagem ?? string.Empty,
            class_pc = classePersonagem ?? string.Empty,
            fichas_general_id = fichasGeneralId,
            fichas_dnd_id = fichasDndId
        };
    }

    public void SetFicha(string name, int level, string race, string classe, int generalId = 0, int dndId = 0, string imagemPath = "")
    {
        VTTLog.Sheet($"SetFicha name='{name}' lvl={level} race='{race}' class='{classe}' generalId={generalId} dndId={dndId} imagemPath='{imagemPath}' on '{gameObject.name}'");

        if (nomeText != null) nomeText.text = name ?? "<sem nome>";
        if (levelTotalText != null) levelTotalText.text = level.ToString();

        // Carregar imagem se o caminho for fornecido
        if (!string.IsNullOrWhiteSpace(imagemPath) && tokenImage != null)
        {
            CarregarImagem(imagemPath);
        }

        // Armazenar dados para edição
        nomePersonagem = name;
        levelPersonagem = level;
        racePersonagem = race;
        classePersonagem = classe;
        fichasGeneralId = generalId;
        fichasDndId = dndId;

        AtualizarClassesESubclasses(classe, level);
        AtualizarMetadados(generalId, dndId);
    }

    // Preenche classePrincipalText/classeSecundariaText a partir do snapshot local (multiclasse
    // estruturada em LocalFichaData.classes). Sem snapshot, cai no par classe/level já recebido
    // pelo FichaLoaderUI (sem detalhe de subclasse nem multiclasse).
    private void AtualizarClassesESubclasses(string classeFallback, int levelFallback)
    {
        if (TryObterSnapshot(fichasGeneralId, fichasDndId, out LocalFichaData snap) && snap.classes != null && snap.classes.Count > 0)
        {
            ClasseData principal = snap.classes.PrimaryClass();
            ClasseData secundaria = null;

            foreach (var c in snap.classes)
            {
                if (c != null && c != principal) { secundaria = c; break; }
            }

            if (classePrincipalText != null)
                classePrincipalText.text = FormatarClasse(principal, classeFallback, levelFallback);

            if (classeSecundariaText != null)
            {
                bool temSecundaria = secundaria != null;
                classeSecundariaText.gameObject.SetActive(temSecundaria);
                if (temSecundaria)
                    classeSecundariaText.text = FormatarClasse(secundaria, secundaria.class_name, secundaria.level);
            }
        }
        else
        {
            if (classePrincipalText != null)
                classePrincipalText.text = FormatarClasse(null, classeFallback, levelFallback);

            if (classeSecundariaText != null)
                classeSecundariaText.gameObject.SetActive(false);
        }
    }

    private static string FormatarClasse(ClasseData dados, string nomeFallback, int levelFallback)
    {
        string nome = dados != null ? dados.class_name : nomeFallback;
        int level = dados != null ? dados.level : levelFallback;

        string texto = string.IsNullOrEmpty(nome) ? "-" : nome;
        return $"{texto} {level}";
    }

    // Sistema e data de criação vêm do snapshot local; campanha ainda não existe como conceito
    // no projeto (não há sistema de Campanhas implementado), então fica como placeholder.
    private void AtualizarMetadados(int generalId, int dndId)
    {
        if (campanhaText != null)
            campanhaText.text = "Sem Campanha";

        if (!TryObterSnapshot(generalId, dndId, out LocalFichaData snap))
        {
            if (sistemaText != null) sistemaText.text = "Sistema não definido";
            if (dataCriacaoText != null) dataCriacaoText.text = "-";
            return;
        }

        if (sistemaText != null)
            sistemaText.text = string.IsNullOrWhiteSpace(snap.sistema_nome) ? "Sistema não definido" : snap.sistema_nome;

        if (dataCriacaoText != null)
        {
            dataCriacaoText.text = DateTime.TryParse(snap.created_at_utc, out DateTime dataCriacao)
                ? $"Adicionado {dataCriacao.ToString("dd/MM/yyyy")}"
                : "-";
        }
    }

    private static bool TryObterSnapshot(int generalId, int dndId, out LocalFichaData snapshot)
    {
        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        return LocalFichaStorage.TryGetFichaSnapshot(steamId, playerId, generalId, dndId, out snapshot);
    }

    private void CarregarImagem(string caminhoImagem)
    {
        if (tokenImage == null || string.IsNullOrWhiteSpace(caminhoImagem)) return;

        try
        {
            // Trata tanto caminhos absolutos quanto relativos
            string caminhoCompleto = caminhoImagem;
            if (!Path.IsPathRooted(caminhoImagem))
            {
                caminhoCompleto = Path.Combine(Application.dataPath, caminhoImagem);
            }

            if (!File.Exists(caminhoCompleto))
            {
                VTTLog.LoadWarn($"FichaUI: imagem não encontrada: {caminhoCompleto}");
                return;
            }

            byte[] imageData = File.ReadAllBytes(caminhoCompleto);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.LoadImage(imageData);

            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f);
            tokenImage.sprite = sprite;
            tokenImage.enabled = true;

            VTTLog.Load($"FichaUI: imagem carregada de '{caminhoCompleto}'");
        }
        catch (System.Exception ex)
        {
            VTTLog.LoadError($"FichaUI: erro ao carregar imagem '{caminhoImagem}': {ex.Message}");
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (!isClickable) return;

        float agora = Time.unscaledTime;
        bool duploCli = doubleClickHandler != null && (agora - _ultimoCliqueTime) <= DoubleClickInterval;
        _ultimoCliqueTime = agora;

        if (duploCli)
        {
            _ultimoCliqueTime = -1f; // reset para não contar triplo
            doubleClickHandler.Invoke(GetFichaData());
            return;
        }

        OnFichaClicked();
    }

    private void OnFichaClicked()
    {
        if (customClickHandler != null)
        {
            customClickHandler.Invoke(GetFichaData());
            return;
        }

        int playerId = DataManager.Instance != null ? DataManager.Instance.GetCurrentId() : 0;
        string steamId = DataManager.Instance != null ? DataManager.Instance.GetCurrentSteamId() : "0";

        bool fichaIncompleta = LocalFichaStorage.IsFichaIncompleta(steamId, playerId, fichasGeneralId, fichasDndId);

        FichaLoaderUI loader = FindObjectOfType<FichaLoaderUI>();
        if (loader != null)
            loader.TratarCliqueFicha(fichasGeneralId, fichasDndId, nomePersonagem, levelPersonagem, fichaIncompleta);
        else
            VTTLog.UIWarn("FichaLoaderUI não encontrado na cena!");
    }

    private void AutoBindTextRefsIfNeeded()
    {
        if (nomeText != null && levelTotalText != null)
            return;

        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text t = texts[i];
            if (t == null) continue;

            string n = t.gameObject.name.ToLowerInvariant();

            if (nomeText == null && (n.Contains("name") || n.Contains("nome")))
                nomeText = t;
            else if (levelTotalText == null && (n.Contains("level") || n.Contains("nível") || n.Contains("nivel")))
                levelTotalText = t;
        }

        if (nomeText == null && texts.Length > 0) nomeText = texts[0];
        if (levelTotalText == null && texts.Length > 1) levelTotalText = texts[1];
    }

    private void AutoBindImageRefIfNeeded()
    {
        if (tokenImage != null) return;

        Image[] images = GetComponentsInChildren<Image>(true);
        for (int i = 0; i < images.Length; i++)
        {
            Image img = images[i];
            if (img == null) continue;

            string n = img.gameObject.name.ToLowerInvariant();
            if (n.Contains("image") || n.Contains("ficha") || n.Contains("foto") || n.Contains("token"))
            {
                tokenImage = img;
                break;
            }
        }
    }
}
