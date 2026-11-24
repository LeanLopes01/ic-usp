using UnityEngine;
using System;
using System.Collections.Generic;

[Serializable]
public class AtributosData
{
    public int forca;
    public int destreza;
    public int constituicao;
    public int inteligencia;
    public int sabedoria;
    public int carisma;
}

[Serializable]
public class FichaData
{
    // Campos base (mantidos para compatibilidade com FichaUI.SetFicha e demais UI)
    public string name_pc;
    public int    level_pc;
    public string level_pc_detalhe; // "Bruxo 3 / Druida 3" — populado em multiclasse
    public string race_pc;          // nome da raça para display (fallback e compat)
    public string class_pc;         // nome/detalhe da classe para display (fallback e compat)
    public int    fichas_general_id; // necessário para o sistema de Sala
    public int    fichas_dnd_id;     // necessário para o sistema de Sala

    // Novos campos estruturados
    public RacaData         raca    = new RacaData();
    public List<ClasseData> classes = new List<ClasseData>();
}

public class DataManager : MonoBehaviour
{
    public static DataManager Instance { get; private set; }

    [Header("Config Local")]
    public bool fetchOnStart = true;

    [Header("Debug (apenas Editor)")]
    public int    debugPlayerId   = 1;
    public string debugPlayerName = "Mestre de Teste";

    [TextArea(4, 8)]
    public string lastJson = "";

    private FichaData[] fichas = new FichaData[0];

    public bool HasData    => fichas != null && fichas.Length > 0;
    public bool IsFetching { get; private set; } = false;

    public event Action<FichaData[]> OnDataUpdated;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (fetchOnStart)
        {
            int pid = GetCurrentId();
            if (pid > 0) StartFetch(pid);
        }
    }

    public int GetCurrentId()
    {
        string steamId = GetCurrentSteamId();
        if (!string.IsNullOrWhiteSpace(steamId) && steamId != "0")
            return StablePositiveHash(steamId);

#if UNITY_EDITOR
        Debug.Log("[DataManager] Modo Editor sem Steam. Retornando ID de debug: " + debugPlayerId);
        return Mathf.Max(1, debugPlayerId);
#else
        return 0; // Steam ainda não inicializou; SteamManager.Start() chama RefreshCurrentData após o Init.
#endif
    }

    public string GetCurrentSteamId()
    {
        if (SteamManager.Instance != null &&
            !string.IsNullOrWhiteSpace(SteamManager.Instance.CurrentSteamId) &&
            SteamManager.Instance.CurrentSteamId != "0")
        {
            return SteamManager.Instance.CurrentSteamId;
        }

#if UNITY_EDITOR
        return $"editor_{Mathf.Max(1, debugPlayerId)}";
#else
        return "0";
#endif
    }

    public void StartFetch(int playerId, bool force = false)
    {
        if (IsFetching && !force) return;

        IsFetching = true;
        try
        {
            FetchFromLocal(playerId);
        }
        finally
        {
            IsFetching = false;
        }
    }

    public void RefreshCurrentData(bool force = true)
    {
        int pid = GetCurrentId();
        if (pid > 0)
            StartFetch(pid, force);
    }

    public FichaData[] GetFichasCopy()
    {
        if (fichas == null) return new FichaData[0];
        var copy = new FichaData[fichas.Length];
        Array.Copy(fichas, copy, fichas.Length);
        return copy;
    }

    public string GetPlayerName()
    {
        if (SteamManager.Instance != null && !string.IsNullOrWhiteSpace(SteamManager.Instance.CurrentSteamName))
            return SteamManager.Instance.CurrentSteamName;

#if UNITY_EDITOR
        return debugPlayerName;
#else
        return "Jogador";
#endif
    }

    private void FetchFromLocal(int playerId)
    {
        try
        {
            string steamId = GetCurrentSteamId();
            fichas   = LocalFichaStorage.LoadDndFichasForSteamId(steamId, playerId);
            lastJson = $"local://{LocalFichaStorage.BasePath}";
            Debug.Log($"[DataManager] LocalStorage: {fichas.Length} fichas carregadas | steam_id={steamId} | player_hash={playerId}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[DataManager] Erro ao carregar fichas locais: {ex.Message}");
            fichas = new FichaData[0];
        }

        DispararOnDataUpdated(fichas);
    }

    // Invoca cada assinante de OnDataUpdated isoladamente — um assinante que lança exceção não pode
    // impedir os demais de rodar (Action?.Invoke() é uma cadeia só: se o primeiro assinante lançar,
    // os seguintes na lista simplesmente nunca são chamados). Como telas que fecham um popup de
    // carregamento compartilhado (ex: FichaLoaderUI.OnDataUpdated) dependem de serem notificadas
    // aqui, um assinante quebrado em qualquer outro lugar não pode deixar esse popup preso.
    private void DispararOnDataUpdated(FichaData[] dados)
    {
        if (OnDataUpdated == null) return;

        foreach (Delegate d in OnDataUpdated.GetInvocationList())
        {
            try
            {
                ((Action<FichaData[]>)d).Invoke(dados);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DataManager] Assinante de OnDataUpdated lançou exceção: {ex}");
            }
        }
    }

    private int StablePositiveHash(string value)
    {
        unchecked
        {
            int hash = 23;
            for (int i = 0; i < value.Length; i++)
                hash = (hash * 31) + value[i];

            if (hash == int.MinValue) return int.MaxValue;
            hash = Mathf.Abs(hash);
            return Mathf.Max(1, hash);
        }
    }
}
