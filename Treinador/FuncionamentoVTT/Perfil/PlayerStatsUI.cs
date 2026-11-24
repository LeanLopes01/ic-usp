using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Exibe estatísticas do jogador e expõe métodos estáticos para
/// rastrear eventos (login, sala, comunidade) via PlayerPrefs.
/// </summary>
public class PlayerStatsUI : MonoBehaviour
{
    // ── Chaves PlayerPrefs ────────────────────────────────────────────────
    private const string KEY_LOGINS         = "stats_logins";
    private const string KEY_SALAS          = "stats_salas";
    private const string KEY_COMUNIDADES    = "stats_comunidades";
    private const string KEY_TEMPO_TOTAL    = "stats_tempo_total_seg";
    private const string KEY_PRIMEIRA_SESSAO = "stats_primeira_sessao";
    private const string KEY_ULTIMA_SESSAO  = "stats_ultima_sessao";

    // ── Campos UI ─────────────────────────────────────────────────────────
    [Header("Textos de Estatísticas")]
    [SerializeField] private TMP_Text fichasText;
    [SerializeField] private TMP_Text salasText;
    [SerializeField] private TMP_Text comunidadesText;
    [SerializeField] private TMP_Text loginsText;
    [SerializeField] private TMP_Text tempoTotalText;
    [SerializeField] private TMP_Text primeiraSessaoText;
    [SerializeField] private TMP_Text ultimaSessaoText;
    [SerializeField] private TMP_Text sessaoAtualText;

    [Header("Atualização em tempo real")]
    [SerializeField] private float intervaloAtualizacao = 30f;

    private float sessionStart;
    private Coroutine updateRoutine;

    // ── Ciclo de vida ─────────────────────────────────────────────────────

    private void OnEnable()
    {
        sessionStart = Time.realtimeSinceStartup;

        if (DataManager.Instance != null)
            DataManager.Instance.OnDataUpdated += OnFichasAtualizadas;

        Refresh();
        if (updateRoutine != null) StopCoroutine(updateRoutine);
        updateRoutine = StartCoroutine(LoopAtualizacao());
    }

    private void OnDisable()
    {
        if (DataManager.Instance != null)
            DataManager.Instance.OnDataUpdated -= OnFichasAtualizadas;

        if (updateRoutine != null)
        {
            StopCoroutine(updateRoutine);
            updateRoutine = null;
        }

        SalvarTempoSessao();
    }

    // ── API pública de rastreamento (chamada por outros scripts) ──────────

    public static void RegistrarLogin()
    {
        int logins = PlayerPrefs.GetInt(KEY_LOGINS, 0) + 1;
        PlayerPrefs.SetInt(KEY_LOGINS, logins);

        string hoje = System.DateTime.Now.ToString("dd/MM/yyyy HH:mm");
        if (!PlayerPrefs.HasKey(KEY_PRIMEIRA_SESSAO))
            PlayerPrefs.SetString(KEY_PRIMEIRA_SESSAO, hoje);
        PlayerPrefs.SetString(KEY_ULTIMA_SESSAO, hoje);

        PlayerPrefs.Save();
    }

    public static void RegistrarEntradaNaSala()
    {
        int salas = PlayerPrefs.GetInt(KEY_SALAS, 0) + 1;
        PlayerPrefs.SetInt(KEY_SALAS, salas);
        PlayerPrefs.Save();
    }

    public static void RegistrarEntradaNaComunidade()
    {
        int coms = PlayerPrefs.GetInt(KEY_COMUNIDADES, 0) + 1;
        PlayerPrefs.SetInt(KEY_COMUNIDADES, coms);
        PlayerPrefs.Save();
    }

    // ── Atualização da UI ─────────────────────────────────────────────────

    public void Refresh()
    {
        AtualizarFichas();
        AtualizarContadores();
        AtualizarTempo();
        AtualizarDatas();
    }

    private void OnFichasAtualizadas(FichaData[] _) => AtualizarFichas();

    private void AtualizarFichas()
    {
        if (fichasText == null) return;
        int total = DataManager.Instance != null
            ? DataManager.Instance.GetFichasCopy().Length
            : 0;
        fichasText.text = $"Fichas Criadas: {total}";
    }

    private void AtualizarContadores()
    {
        if (salasText != null)
            salasText.text = $"Salas Visitadas: {PlayerPrefs.GetInt(KEY_SALAS, 0)}";

        if (loginsText != null)
            loginsText.text = $"Logins Realizados: {PlayerPrefs.GetInt(KEY_LOGINS, 0)}";

        if (comunidadesText != null)
        {
            int coms = PlayerPrefs.GetInt(KEY_COMUNIDADES, 0);
            comunidadesText.text = $"Comunidades: {(coms > 0 ? coms.ToString() : "—")}";
        }
    }

    private void AtualizarTempo()
    {
        float acumulado = PlayerPrefs.GetFloat(KEY_TEMPO_TOTAL, 0f);
        float sessao    = Time.realtimeSinceStartup - sessionStart;

        if (tempoTotalText != null)
            tempoTotalText.text = $"Tempo Total: {FormatarTempo(acumulado + sessao)}";

        if (sessaoAtualText != null)
            sessaoAtualText.text = $"Sessão Atual: {FormatarTempo(sessao)}";
    }

    private void AtualizarDatas()
    {
        if (primeiraSessaoText != null)
            primeiraSessaoText.text = $"Primeira Sessão: {PlayerPrefs.GetString(KEY_PRIMEIRA_SESSAO, "—")}";

        if (ultimaSessaoText != null)
            ultimaSessaoText.text = $"Último Login: {PlayerPrefs.GetString(KEY_ULTIMA_SESSAO, "—")}";
    }

    private void SalvarTempoSessao()
    {
        float acumulado = PlayerPrefs.GetFloat(KEY_TEMPO_TOTAL, 0f);
        float sessao    = Time.realtimeSinceStartup - sessionStart;
        PlayerPrefs.SetFloat(KEY_TEMPO_TOTAL, acumulado + sessao);
        PlayerPrefs.Save();
        sessionStart = Time.realtimeSinceStartup;
    }

    private IEnumerator LoopAtualizacao()
    {
        var wait = new WaitForSecondsRealtime(intervaloAtualizacao);
        while (true)
        {
            yield return wait;
            AtualizarTempo();
        }
    }

    private static string FormatarTempo(float segundos)
    {
        int h = (int)(segundos / 3600);
        int m = (int)(segundos % 3600 / 60);
        return h > 0 ? $"{h}h {m:D2}min" : $"{m}min";
    }
}
