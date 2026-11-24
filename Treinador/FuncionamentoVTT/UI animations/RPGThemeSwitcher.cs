using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public enum RPGSystemTheme
{
    Default,
    DND,
    Cyberpunk,
    Tormenta,
    QuintaEdicao
}

public enum ThemeColorRole
{
    Primary,
    Secondary,
    Accent,
    Background,
    Text,
    TextSecondary,
    Border
}

[System.Serializable]
public class RPGThemePalette
{
    public RPGSystemTheme system;
    public Color primary = new Color32(229, 27, 36, 255);      // #E51B24
    public Color secondary = new Color32(26, 26, 26, 255);     // #1A1A1A (Surface)
    public Color accent = new Color32(200, 164, 93, 255);      // #C8A45D
    public Color background = new Color32(15, 15, 15, 255);    // #0F0F0F (Foundation)
    public Color text = new Color32(255, 255, 255, 255);       // #FFFFFF
    public Color textSecondary = new Color32(160, 160, 160, 255); // #A0A0A0
    public Color border = new Color32(45, 45, 45, 255);        // #2D2D2D

    public Color GetColor(ThemeColorRole role)
    {
        switch (role)
        {
            case ThemeColorRole.Primary:
                return primary;
            case ThemeColorRole.Secondary:
                return secondary;
            case ThemeColorRole.Accent:
                return accent;
            case ThemeColorRole.Background:
                return background;
            case ThemeColorRole.Text:
                return text;
            case ThemeColorRole.TextSecondary:
                return textSecondary;
            case ThemeColorRole.Border:
                return border;
            default:
                return Color.white;
        }
    }
}

[System.Serializable]
public class ThemeGraphicTarget
{
    public ThemeColorRole role;
    public Graphic graphic;
    public TMP_Text tmpText;
    public bool preserveAlpha = true;

    public void ApplyColor(Color newColor)
    {
        if (graphic != null)
        {
            Color final = preserveAlpha
                ? new Color(newColor.r, newColor.g, newColor.b, graphic.color.a)
                : newColor;
            graphic.color = final;
        }

        if (tmpText != null)
        {
            Color final = preserveAlpha
                ? new Color(newColor.r, newColor.g, newColor.b, tmpText.color.a)
                : newColor;
            tmpText.color = final;
        }
    }
}

[System.Serializable]
public class ThemeObjectTarget
{
    public Graphic graphic;
    public TMP_Text tmpText;
    public bool preserveAlpha = true;

    public void ApplyColor(Color newColor)
    {
        if (graphic != null)
        {
            Color final = preserveAlpha
                ? new Color(newColor.r, newColor.g, newColor.b, graphic.color.a)
                : newColor;
            graphic.color = final;
        }

        if (tmpText != null)
        {
            Color final = preserveAlpha
                ? new Color(newColor.r, newColor.g, newColor.b, tmpText.color.a)
                : newColor;
            tmpText.color = final;
        }
    }

    public bool TryGetCurrentColor(out Color color)
    {
        if (graphic != null)
        {
            color = graphic.color;
            return true;
        }

        if (tmpText != null)
        {
            color = tmpText.color;
            return true;
        }

        color = Color.white;
        return false;
    }

    public Color BuildTargetColor(Color newColor)
    {
        if (!preserveAlpha)
        {
            return newColor;
        }

        if (graphic != null)
        {
            return new Color(newColor.r, newColor.g, newColor.b, graphic.color.a);
        }

        if (tmpText != null)
        {
            return new Color(newColor.r, newColor.g, newColor.b, tmpText.color.a);
        }

        return newColor;
    }
}

[System.Serializable]
public class ThemePaletteTargets
{
    public RPGSystemTheme system;

    [Header("Foundation (#0F0F0F)")]
    public List<ThemeObjectTarget> foundationTargets = new List<ThemeObjectTarget>();

    [Header("Surface (#1A1A1A)")]
    public List<ThemeObjectTarget> surfaceTargets = new List<ThemeObjectTarget>();

    [Header("Primary (#E51B24)")]
    public List<ThemeObjectTarget> primaryTargets = new List<ThemeObjectTarget>();

    [Header("Accent / Gold (#C8A45D)")]
    public List<ThemeObjectTarget> accentTargets = new List<ThemeObjectTarget>();

    [Header("Text Primary (#FFFFFF)")]
    public List<ThemeObjectTarget> textPrimaryTargets = new List<ThemeObjectTarget>();

    [Header("Text Secondary (#A0A0A0)")]
    public List<ThemeObjectTarget> textSecondaryTargets = new List<ThemeObjectTarget>();

    [Header("Border (#2D2D2D)")]
    public List<ThemeObjectTarget> borderTargets = new List<ThemeObjectTarget>();
}

[DisallowMultipleComponent]
public class RPGThemeSwitcher : MonoBehaviour
{
    private static readonly Color32 FoundationColor = new Color32(15, 15, 15, 255);   // #0F0F0F
    private static readonly Color32 SurfaceColor = new Color32(26, 26, 26, 255);       // #1A1A1A
    private static readonly Color32 PrimaryColor = new Color32(229, 27, 36, 255);      // #E51B24
    private static readonly Color32 AccentColor = new Color32(200, 164, 93, 255);      // #C8A45D
    private static readonly Color32 TextPrimaryColor = new Color32(255, 255, 255, 255);// #FFFFFF
    private static readonly Color32 TextSecondaryColor = new Color32(160, 160, 160, 255);// #A0A0A0
    private static readonly Color32 BorderColor = new Color32(45, 45, 45, 255);        // #2D2D2D

    [Header("Tema")]
    [SerializeField] private RPGSystemTheme currentSystem = RPGSystemTheme.Default;
    [SerializeField] private List<RPGThemePalette> palettes = new List<RPGThemePalette>();

    [Header("Preset global de cores")]
    [SerializeField] private bool applyGlobalPalettePreset = false;

    [Header("Alvos por paleta (recomendado)")]
    [SerializeField] private List<ThemePaletteTargets> paletteTargets = new List<ThemePaletteTargets>();

    [Header("Alvos para trocar cor (legado)")]
    [SerializeField] private List<ThemeGraphicTarget> targets = new List<ThemeGraphicTarget>();

    [Header("Aplicação automática")]
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private bool smoothTransition = true;
    [SerializeField, Min(0.01f)] private float transitionDuration = 0.25f;

    [Header("Telas com transição diferente")]
    [SerializeField] private GameObject telaCriarFicha;
    [SerializeField] private GameObject telaVisualizacaoFicha;
    [SerializeField, Min(0.01f)] private float transitionDurationFichas = 0.6f;

    [Header("Auto detecção por ficha")]
    [SerializeField] private bool autoDetectThemeByFicha = true;
    [SerializeField] private bool useDefaultWhenNoFicha = true;

    private readonly Dictionary<RPGSystemTheme, RPGThemePalette> paletteMap = new Dictionary<RPGSystemTheme, RPGThemePalette>();
    private readonly Dictionary<RPGSystemTheme, ThemePaletteTargets> paletteTargetsMap = new Dictionary<RPGSystemTheme, ThemePaletteTargets>();
    private readonly List<ColorTransitionItem> activeTransitions = new List<ColorTransitionItem>();
    private readonly List<ResolvedThemeTarget> resolvedTargetsBuffer = new List<ResolvedThemeTarget>();
    private Coroutine transitionRoutine;
    private RPGSystemTheme? lastAutoDetectedTheme;

    private enum TargetSource
    {
        Legacy,
        PaletteGroup
    }

    private struct ResolvedThemeTarget
    {
        public TargetSource source;
        public ThemeColorRole role;
        public ThemeGraphicTarget legacyTarget;
        public ThemeObjectTarget groupedTarget;
    }

    private struct ColorTransitionItem
    {
        public ResolvedThemeTarget target;
        public Color from;
        public Color to;
    }

    private void Awake()
    {
        if (applyGlobalPalettePreset)
        {
            ApplyGlobalPalettePresetToAll();
        }

        BuildPaletteMap();
        BuildPaletteTargetsMap();
    }

    private void Start()
    {
        if (applyOnStart)
        {
            ApplyTheme(RPGSystemTheme.Default);
            currentSystem = RPGSystemTheme.Default;
        }

        if (autoDetectThemeByFicha)
            ApplyThemeFromFichaContext(force: true);
    }

    private void OnEnable()
    {
        if (autoDetectThemeByFicha)
            ApplyThemeFromFichaContext(force: true);
    }

    private void Update()
    {
        if (autoDetectThemeByFicha)
            ApplyThemeFromFichaContext(force: false);
    }

    public void SetDNDTheme()
    {
        ApplyTheme(RPGSystemTheme.DND);
    }

    public void SetDefaultTheme()
    {
        ApplyTheme(RPGSystemTheme.Default);
    }

    public void SetCyberpunkTheme()
    {
        ApplyTheme(RPGSystemTheme.Cyberpunk);
    }

    public void SetTormentaTheme()
    {
        ApplyTheme(RPGSystemTheme.Tormenta);
    }

    public void ApplyTheme(RPGSystemTheme system)
    {
        if (!paletteMap.TryGetValue(system, out RPGThemePalette palette))
        {
            Debug.LogWarning($"Paleta não encontrada para o sistema: {system}");
            return;
        }

        currentSystem = system;

        if (transitionRoutine != null)
        {
            StopCoroutine(transitionRoutine);
            transitionRoutine = null;
        }

        if (!smoothTransition)
        {
            ApplyThemeImmediate(palette);
            return;
        }

        BuildTransitions(palette);

        if (activeTransitions.Count == 0)
        {
            return;
        }

        transitionRoutine = StartCoroutine(AnimateThemeTransition());
    }

    private void ApplyThemeImmediate(RPGThemePalette palette)
    {
        ResolveTargets(currentSystem, resolvedTargetsBuffer);

        for (int i = 0; i < resolvedTargetsBuffer.Count; i++)
        {
            ResolvedThemeTarget target = resolvedTargetsBuffer[i];
            if (!IsValidResolvedTarget(target))
            {
                continue;
            }

            ApplyColor(target, palette.GetColor(target.role));
        }
    }

    private void BuildTransitions(RPGThemePalette palette)
    {
        activeTransitions.Clear();
        ResolveTargets(currentSystem, resolvedTargetsBuffer);

        for (int i = 0; i < resolvedTargetsBuffer.Count; i++)
        {
            ResolvedThemeTarget target = resolvedTargetsBuffer[i];
            if (!IsValidResolvedTarget(target))
            {
                continue;
            }

            Color from;
            bool hasCurrent = TryGetCurrentColor(target, out from);
            Color to = BuildTargetColor(target, palette.GetColor(target.role));

            if (!hasCurrent)
            {
                ApplyColor(target, to);
                continue;
            }

            activeTransitions.Add(new ColorTransitionItem
            {
                target = target,
                from = from,
                to = to
            });
        }
    }

    private float GetCurrentTransitionDuration()
    {
        bool fichaAtiva = (telaCriarFicha != null && telaCriarFicha.activeInHierarchy)
                       || (telaVisualizacaoFicha != null && telaVisualizacaoFicha.activeInHierarchy);
        return fichaAtiva ? transitionDurationFichas : transitionDuration;
    }

    private IEnumerator AnimateThemeTransition()
    {
        float duration = GetCurrentTransitionDuration();
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = Mathf.SmoothStep(0f, 1f, t);

            for (int i = 0; i < activeTransitions.Count; i++)
            {
                ColorTransitionItem item = activeTransitions[i];
                Color lerped = Color.Lerp(item.from, item.to, eased);
                ApplyColor(item.target, lerped);
            }

            yield return null;
        }

        for (int i = 0; i < activeTransitions.Count; i++)
        {
            ColorTransitionItem item = activeTransitions[i];
            ApplyColor(item.target, item.to);
        }

        activeTransitions.Clear();
        transitionRoutine = null;
    }

    public void ApplyThemeByName(string systemName)
    {
        RPGSystemTheme mapped = MapThemeByName(systemName);
        ApplyTheme(mapped);
    }

    private void ApplyThemeFromFichaContext(bool force)
    {
        RPGSystemTheme detected = DetectThemeFromGlobalFichaData();
        if (!force && lastAutoDetectedTheme.HasValue && lastAutoDetectedTheme.Value == detected)
            return;

        lastAutoDetectedTheme = detected;
        ApplyTheme(detected);
    }

    private RPGSystemTheme DetectThemeFromGlobalFichaData()
    {
        bool hasFichaAberta = GlobalFichaData.fichasGeneralId > 0 || GlobalFichaData.fichasDndId > 0;
        if (!hasFichaAberta)
            return RPGSystemTheme.Default;

        if (GlobalFichaData.fichasDndId > 0)
            return RPGSystemTheme.DND;

        RPGSystemTheme byName = MapThemeByName(GlobalFichaData.sistemaNome);
        if (byName != RPGSystemTheme.Default)
            return byName;

        return useDefaultWhenNoFicha ? RPGSystemTheme.Default : currentSystem;
    }

    private RPGSystemTheme MapThemeByName(string systemName)
    {
        if (string.IsNullOrWhiteSpace(systemName))
            return RPGSystemTheme.Default;

        string clean = systemName.Trim().ToLowerInvariant();

        if (clean.Contains("quinta") || clean.Contains("5e") || clean.Contains("dnd") || clean.Contains("d&d") || clean.Contains("dungeons") || clean.Contains("dragon"))
            return RPGSystemTheme.DND;

        if (clean.Contains("tormenta") || clean.Contains("t20"))
            return RPGSystemTheme.Tormenta;

        if (clean.Contains("cyber"))
            return RPGSystemTheme.Cyberpunk;

        if (System.Enum.TryParse(systemName, true, out RPGSystemTheme parsed))
            return parsed;

        return RPGSystemTheme.Default;
    }

    private void BuildPaletteMap()
    {
        paletteMap.Clear();

        for (int i = 0; i < palettes.Count; i++)
        {
            RPGThemePalette palette = palettes[i];
            if (palette == null)
            {
                continue;
            }

            paletteMap[palette.system] = palette;
        }
    }

    private void BuildPaletteTargetsMap()
    {
        paletteTargetsMap.Clear();

        for (int i = 0; i < paletteTargets.Count; i++)
        {
            ThemePaletteTargets item = paletteTargets[i];
            if (item == null)
            {
                continue;
            }

            paletteTargetsMap[item.system] = item;
        }
    }

    private void ApplyGlobalPalettePresetToAll()
    {
        for (int i = 0; i < palettes.Count; i++)
        {
            RPGThemePalette palette = palettes[i];
            if (palette == null)
            {
                continue;
            }

            palette.background = FoundationColor;
            palette.secondary = SurfaceColor;
            palette.primary = PrimaryColor;
            palette.accent = AccentColor;
            palette.text = TextPrimaryColor;
            palette.textSecondary = TextSecondaryColor;
            palette.border = BorderColor;
        }
    }

    private void ResolveTargets(RPGSystemTheme system, List<ResolvedThemeTarget> output)
    {
        output.Clear();

        if (paletteTargetsMap.TryGetValue(system, out ThemePaletteTargets grouped) && grouped != null)
        {
            AddGroupedTargets(output, grouped.foundationTargets, ThemeColorRole.Background);
            AddGroupedTargets(output, grouped.surfaceTargets, ThemeColorRole.Secondary);
            AddGroupedTargets(output, grouped.primaryTargets, ThemeColorRole.Primary);
            AddGroupedTargets(output, grouped.accentTargets, ThemeColorRole.Accent);
            AddGroupedTargets(output, grouped.textPrimaryTargets, ThemeColorRole.Text);
            AddGroupedTargets(output, grouped.textSecondaryTargets, ThemeColorRole.TextSecondary);
            AddGroupedTargets(output, grouped.borderTargets, ThemeColorRole.Border);

            if (output.Count > 0)
            {
                return;
            }
        }

        for (int i = 0; i < targets.Count; i++)
        {
            ThemeGraphicTarget legacy = targets[i];
            if (legacy == null)
            {
                continue;
            }

            output.Add(new ResolvedThemeTarget
            {
                source = TargetSource.Legacy,
                role = legacy.role,
                legacyTarget = legacy,
                groupedTarget = null
            });
        }
    }

    private void AddGroupedTargets(List<ResolvedThemeTarget> output, List<ThemeObjectTarget> list, ThemeColorRole role)
    {
        if (list == null)
        {
            return;
        }

        for (int i = 0; i < list.Count; i++)
        {
            ThemeObjectTarget grouped = list[i];
            if (grouped == null)
            {
                continue;
            }

            output.Add(new ResolvedThemeTarget
            {
                source = TargetSource.PaletteGroup,
                role = role,
                legacyTarget = null,
                groupedTarget = grouped
            });
        }
    }

    private bool IsValidResolvedTarget(ResolvedThemeTarget target)
    {
        switch (target.source)
        {
            case TargetSource.Legacy:
                return target.legacyTarget != null;
            case TargetSource.PaletteGroup:
                return target.groupedTarget != null;
            default:
                return false;
        }
    }

    private bool TryGetCurrentColor(ResolvedThemeTarget target, out Color color)
    {
        switch (target.source)
        {
            case TargetSource.Legacy:
                if (target.legacyTarget != null)
                {
                    return TryGetCurrentColor(target.legacyTarget, out color);
                }
                break;
            case TargetSource.PaletteGroup:
                if (target.groupedTarget != null)
                {
                    return target.groupedTarget.TryGetCurrentColor(out color);
                }
                break;
        }

        color = Color.white;
        return false;
    }

    private bool TryGetCurrentColor(ThemeGraphicTarget target, out Color color)
    {
        if (target.graphic != null)
        {
            color = target.graphic.color;
            return true;
        }

        if (target.tmpText != null)
        {
            color = target.tmpText.color;
            return true;
        }

        color = Color.white;
        return false;
    }

    private Color BuildTargetColor(ResolvedThemeTarget target, Color newColor)
    {
        switch (target.source)
        {
            case TargetSource.Legacy:
                if (target.legacyTarget != null)
                {
                    return BuildTargetColor(target.legacyTarget, newColor);
                }
                break;
            case TargetSource.PaletteGroup:
                if (target.groupedTarget != null)
                {
                    return target.groupedTarget.BuildTargetColor(newColor);
                }
                break;
        }

        return newColor;
    }

    private Color BuildTargetColor(ThemeGraphicTarget target, Color newColor)
    {
        if (!target.preserveAlpha)
        {
            return newColor;
        }

        if (target.graphic != null)
        {
            return new Color(newColor.r, newColor.g, newColor.b, target.graphic.color.a);
        }

        if (target.tmpText != null)
        {
            return new Color(newColor.r, newColor.g, newColor.b, target.tmpText.color.a);
        }

        return newColor;
    }

    private void ApplyColor(ResolvedThemeTarget target, Color color)
    {
        switch (target.source)
        {
            case TargetSource.Legacy:
                if (target.legacyTarget != null)
                {
                    target.legacyTarget.ApplyColor(color);
                }
                break;
            case TargetSource.PaletteGroup:
                if (target.groupedTarget != null)
                {
                    target.groupedTarget.ApplyColor(color);
                }
                break;
        }
    }
}
