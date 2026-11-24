using UnityEngine;

/// <summary>
/// Sistema centralizado de logs do DiceVTT.
/// Desative módulos individuais via toggle sem tocar no código que os usa.
/// Warnings e Errors são sempre emitidos, independente dos toggles.
/// </summary>
public static class VTTLog
{
    // ── Toggles por módulo ──────────────────────────────────────────────────
    public static bool ativoSheet   = true;
    public static bool ativoLoad    = true;
    public static bool ativoSave    = true;
    public static bool ativoParser  = true;
    public static bool ativoNetwork = true;
    public static bool ativoUI      = false;  // verboso demais por padrão
    public static bool ativoRules   = true;
    public static bool ativoDice    = true;
    public static bool ativoUpdater = true;
    public static bool ativoCore    = true;
    public static bool ativoBoard   = true;  // TEMP: ligado pra investigar bug do marcador de luz (canvas/câmera sumindo). volte pra false depois
    public static bool ativoToken   = true;   // TEMP: ligado pra investigar Bug E (drag de token) — volte pra false depois
    public static bool ativoForja   = true;
    public static bool ativoAudio   = true;
    public static bool ativoSystem  = true;

    // ── Cores Rich Text (visíveis apenas no Console do Unity Editor) ────────
    const string COR_SHEET   = "#4FC3F7";
    const string COR_LOAD    = "#81C784";
    const string COR_SAVE    = "#AED581";
    const string COR_PARSER  = "#FFB74D";
    const string COR_NETWORK = "#CE93D8";
    const string COR_UI      = "#80CBC4";
    const string COR_RULES   = "#F48FB1";
    const string COR_DICE    = "#FFF176";
    const string COR_UPDATER = "#90CAF9";
    const string COR_CORE    = "#B0BEC5";
    const string COR_BOARD   = "#A5D6A7";
    const string COR_TOKEN   = "#FFCC80";
    const string COR_FORJA   = "#EF9A9A";
    const string COR_AUDIO   = "#B39DDB";
    const string COR_SYSTEM  = "#80DEEA";

    static string Fmt(string tag, string cor, string msg) =>
        $"<b><color={cor}>[{tag}]</color></b> {msg}";

    // ── Logs informativos (controlados por toggle) ──────────────────────────
    public static void Sheet(string msg)   { if (ativoSheet)   Debug.Log(Fmt("Ficha",   COR_SHEET,   msg)); }
    public static void Load(string msg)    { if (ativoLoad)    Debug.Log(Fmt("Load",    COR_LOAD,    msg)); }
    public static void Save(string msg)    { if (ativoSave)    Debug.Log(Fmt("Save",    COR_SAVE,    msg)); }
    public static void Parser(string msg)  { if (ativoParser)  Debug.Log(Fmt("Parser",  COR_PARSER,  msg)); }
    public static void Network(string msg) { if (ativoNetwork) Debug.Log(Fmt("Network", COR_NETWORK, msg)); }
    public static void UI(string msg)      { if (ativoUI)      Debug.Log(Fmt("UI",      COR_UI,      msg)); }
    public static void Rules(string msg)   { if (ativoRules)   Debug.Log(Fmt("Rules",   COR_RULES,   msg)); }
    public static void Dice(string msg)    { if (ativoDice)    Debug.Log(Fmt("Dice",    COR_DICE,    msg)); }
    public static void Updater(string msg) { if (ativoUpdater) Debug.Log(Fmt("Updater", COR_UPDATER, msg)); }
    public static void Core(string msg)    { if (ativoCore)    Debug.Log(Fmt("Core",    COR_CORE,    msg)); }
    public static void Board(string msg)   { if (ativoBoard)   Debug.Log(Fmt("Board",   COR_BOARD,   msg)); }
    public static void Token(string msg)   { if (ativoToken)   Debug.Log(Fmt("Token",   COR_TOKEN,   msg)); }
    public static void Forja(string msg)   { if (ativoForja)   Debug.Log(Fmt("Forja",   COR_FORJA,   msg)); }
    public static void Audio(string msg)   { if (ativoAudio)   Debug.Log(Fmt("Audio",   COR_AUDIO,   msg)); }
    public static void System(string msg)  { if (ativoSystem)  Debug.Log(Fmt("System",  COR_SYSTEM,  msg)); }

    // ── Warnings (sempre emitidos) ──────────────────────────────────────────
    public static void SheetWarn(string msg)   => Debug.LogWarning($"[Ficha] {msg}");
    public static void LoadWarn(string msg)    => Debug.LogWarning($"[Load] {msg}");
    public static void SaveWarn(string msg)    => Debug.LogWarning($"[Save] {msg}");
    public static void ParserWarn(string msg)  => Debug.LogWarning($"[Parser] {msg}");
    public static void NetworkWarn(string msg) => Debug.LogWarning($"[Network] {msg}");
    public static void UIWarn(string msg)      => Debug.LogWarning($"[UI] {msg}");
    public static void RulesWarn(string msg)   => Debug.LogWarning($"[Rules] {msg}");
    public static void BoardWarn(string msg)   => Debug.LogWarning($"[Board] {msg}");
    public static void TokenWarn(string msg)   => Debug.LogWarning($"[Token] {msg}");
    public static void SystemWarn(string msg)  => Debug.LogWarning($"[System] {msg}");
    public static void UpdaterWarn(string msg) => Debug.LogWarning($"[Updater] {msg}");
    public static void DiceWarn(string msg)    => Debug.LogWarning($"[Dice] {msg}");

    // ── Errors (sempre emitidos) ────────────────────────────────────────────
    public static void SheetError(string msg)   => Debug.LogError($"[Ficha] {msg}");
    public static void LoadError(string msg)    => Debug.LogError($"[Load] {msg}");
    public static void SaveError(string msg)    => Debug.LogError($"[Save] {msg}");
    public static void ParserError(string msg)  => Debug.LogError($"[Parser] {msg}");
    public static void NetworkError(string msg) => Debug.LogError($"[Network] {msg}");
    public static void UIError(string msg)      => Debug.LogError($"[UI] {msg}");
    public static void RulesError(string msg)   => Debug.LogError($"[Rules] {msg}");
    public static void UpdaterError(string msg) => Debug.LogError($"[Updater] {msg}");
    public static void BoardError(string msg)   => Debug.LogError($"[Board] {msg}");
    public static void TokenError(string msg)   => Debug.LogError($"[Token] {msg}");
    public static void ForjaError(string msg)   => Debug.LogError($"[Forja] {msg}");
    public static void SystemError(string msg)  => Debug.LogError($"[System] {msg}");
    public static void DiceError(string msg)    => Debug.LogError($"[Dice] {msg}");
    public static void CoreError(string msg)    => Debug.LogError($"[Core] {msg}");
    public static void AudioError(string msg)   => Debug.LogError($"[Audio] {msg}");
}
