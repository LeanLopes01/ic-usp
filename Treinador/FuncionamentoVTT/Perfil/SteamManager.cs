using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Steamworks;

public class SteamManager : MonoBehaviour
{
    public static SteamManager Instance { get; private set; }
    private bool initializedByThisInstance;

    [Header("UI Perfil (Opcional)")]
    [SerializeField] private TMP_Text steamNameText;
    [SerializeField] private TMP_Text steamNameText2;
    [SerializeField] private TMP_Text steamIdText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private GameObject steamAvatarObject;
    [SerializeField] private Image steamAvatarImage;
    [SerializeField] private GameObject steamAvatarObject2;
    [SerializeField] private Image steamAvatarImage2;

    public string CurrentSteamName { get; private set; } = "";
    public string CurrentSteamId   { get; private set; } = "";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        if (transform.parent == null)
            DontDestroyOnLoad(gameObject);
        else
            Debug.LogWarning("[SteamManager] Está em objeto filho. DontDestroyOnLoad ignorado para preservar hierarquia da UI.");
    }

    public event Action OnConectadoComSucesso;
    public event Action<string> OnFalhaAoConectar;

    /// <summary>Dispara a conexão com o Steam. Não é mais chamada automaticamente — quem decide
    /// QUANDO conectar é o InicializacaoJogoManager, depois que o AutoUpdater termina de checar
    /// atualizações. Se este método nunca for chamado por ninguém, o Steam nunca conecta.</summary>
    public void IniciarConexaoSteam()
    {
        try
        {
            if (!SteamClient.IsValid)
            {
                SteamClient.Init(480, true);
                initializedByThisInstance = true;
            }

            Debug.Log("<color=green>Steamworks iniciado com sucesso!</color>");
            Debug.Log($"Bem-vindo, {SteamClient.Name}. O seu Steam ID é: {SteamClient.SteamId}");

            CurrentSteamName = SteamClient.Name;
            CurrentSteamId   = SteamClient.SteamId.ToString();
            ApplyProfileTexts();
            PlayerStatsUI.RegistrarLogin();

            DataManager.Instance?.RefreshCurrentData(true);

            StartCoroutine(TryLoadAvatarRoutine());

            OnConectadoComSucesso?.Invoke();
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"Steam não está rodando. Detalhes: {e.Message}");
            OnFalhaAoConectar?.Invoke(e.Message);
        }
    }

    private void Update()
    {
        if (SteamClient.IsValid)
            SteamClient.RunCallbacks();
    }

    private void OnApplicationQuit()
    {
        if (SteamClient.IsValid)
            SteamClient.Shutdown();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (initializedByThisInstance && SteamClient.IsValid)
            SteamClient.Shutdown();
    }

    public void BindProfileUI(
        TMP_Text nameText, TMP_Text idText,
        GameObject avatarGO, Image avatarImg,
        TMP_Text status = null,
        GameObject avatarGO2 = null, Image avatarImg2 = null)
    {
        steamNameText     = nameText;
        steamIdText       = idText;
        steamAvatarObject = avatarGO;
        steamAvatarImage  = avatarImg;
        statusText        = status;
        steamAvatarObject2 = avatarGO2;
        steamAvatarImage2  = avatarImg2;
        ApplyProfileTexts();
        StartCoroutine(TryLoadAvatarRoutine());
    }

    private void ApplyProfileTexts()
    {
        if (steamNameText  != null) steamNameText.text  = CurrentSteamName;
        if (steamNameText2 != null) steamNameText2.text = CurrentSteamName;

        if (steamIdText != null)
            steamIdText.text = CurrentSteamId;

        ApplyStatus(online: SteamClient.IsValid);
    }

    public void ApplyStatus(bool online)
    {
        if (statusText == null) return;
        statusText.text  = online ? "Online" : "Offline";
        statusText.color = online
            ? new Color(0.13f, 0.78f, 0.37f)   // verde
            : new Color(0.87f, 0.22f, 0.22f);  // vermelho
    }

    private IEnumerator TryLoadAvatarRoutine()
    {
        steamAvatarObject?.SetActive(false);
        steamAvatarObject2?.SetActive(false);

        bool algumAlvo = steamAvatarImage != null || steamAvatarImage2 != null;
        if (!algumAlvo) yield break;

        var steamId = SteamClient.SteamId;
        var task    = SteamFriends.GetLargeAvatarAsync(steamId);

        while (!task.IsCompleted)
            yield return null;

        if (task.IsFaulted || !task.Result.HasValue) yield break;

        var avatar = task.Result.Value;
        if (avatar.Width <= 0 || avatar.Height <= 0 || avatar.Data == null || avatar.Data.Length == 0)
            yield break;

        Texture2D texture = new Texture2D((int)avatar.Width, (int)avatar.Height, TextureFormat.RGBA32, false);
        texture.LoadRawTextureData(avatar.Data);
        texture.Apply();

        Rect   rect   = new Rect(0, 0, texture.width, texture.height);
        Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f));

        if (steamAvatarImage != null)
        {
            steamAvatarImage.sprite = sprite;
            steamAvatarObject?.SetActive(true);
        }

        if (steamAvatarImage2 != null)
        {
            steamAvatarImage2.sprite = sprite;
            steamAvatarObject2?.SetActive(true);
        }
    }
}
