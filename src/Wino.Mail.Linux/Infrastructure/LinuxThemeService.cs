using Wino.Core.Domain;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Personalization;
using Wino.Services.Themes;

namespace Wino.Mail.Linux.Infrastructure;

/// <summary>
/// The Linux theme service over the shared theme catalogue (the Android, UIKit and Mac theme services' twin). Theme ids,
/// accents and configuration keys match the Windows NewThemeService (<see cref="WinoThemeCatalog"/>), so a theme chosen
/// on one platform resolves to the same theme here. A theme sets the accent and may force light or dark; light and dark
/// apply through libadwaita's style manager. The accent, wallpapers and gradients are painted by the shell from milestone
/// 2b on, through <see cref="AppearanceChanged"/>; until then they are stored and reported but not drawn.
/// </summary>
public sealed class LinuxThemeService(IDispatcher dispatcher, IConfigurationService configuration, IPreferencesService preferences, CustomThemeFileStore store) : INewThemeService
{
    /// <summary>The iOS, Mac and Android key: Off keeps plain surfaces with the theme accent only.</summary>
    private const string BackdropEnabledKey = "MacThemeBackdropEnabled";

    /// <summary>libadwaita's default accent (blue 3), used until the system accent is read in milestone 2b.</summary>
    private const string AdwaitaAccent = "#3584e4";

    private sealed record PreviewState(CustomThemeMetadata Metadata, byte[]? Wallpaper, ApplicationElementTheme ElementTheme);

    private readonly object _sync = new();
    private List<CustomThemeMetadata> _customThemes = [];
    // The user's Element Theme choice (saved), and the light or dark mode the active predefined theme forces (not
    // saved: like Windows, leaving that theme returns to the user's choice).
    private ApplicationElementTheme _rootTheme;
    private ApplicationElementTheme? _forcedTheme;
    private string _accent = string.Empty;
    private Guid? _themeId;
    private PreviewState? _preview;
    private byte[]? _previewWallpaper;
    private bool _preferencesHooked;

    public event EventHandler<ApplicationElementTheme>? ElementThemeChanged;
    public event EventHandler<string>? AccentColorChanged;
    public event EventHandler<WindowBackdropType>? BackdropChanged;

    /// <summary>Raised on the UI thread after the theme, accent, icon style or backdrop was applied.</summary>
    public event EventHandler? AppearanceChanged;

    public bool IsCustomTheme => _themeId is { } id && FindCachedCustom(id) is not null;

    // GNOME has no window material choice.
    public WindowBackdropType CurrentBackdropType
    {
        get => WindowBackdropType.None;
        set { }
    }

    /// <summary>The wallpaper or gradient behind the panes; Off keeps plain surfaces with the theme accent only.</summary>
    public bool IsBackdropEnabled
    {
        get => configuration.Get(BackdropEnabledKey, true);
        set
        {
            configuration.Set(BackdropEnabledKey, value);
            _ = ObserveAsync(ApplyThemeToActiveWindowAsync());
        }
    }

    /// <summary>The accent the shell paints: the theme's or the user's choice, the system accent when empty.</summary>
    public string EffectiveAccentColor => (_preview?.Metadata.AccentColorHex ?? _accent) is { Length: > 0 } accent ? accent : GetSystemAccentColorHex();

    public ApplicationElementTheme RootTheme
    {
        get => _forcedTheme ?? _rootTheme;
        set
        {
            _rootTheme = value;
            configuration.Set(WinoThemeCatalog.SelectedAppThemeKey, value);
            ElementThemeChanged?.Invoke(this, value);
            _ = ObserveAsync(ApplyThemeToActiveWindowAsync());
        }
    }

    /// <summary>Setting a different id (Wino Account restore) selects that theme right away, like the Windows setter.</summary>
    public Guid? CurrentApplicationThemeId
    {
        get => _themeId;
        set
        {
            if (value == _themeId) return;
            _themeId = value;
            configuration.Set(WinoThemeCatalog.CurrentApplicationThemeKey, value ?? WinoThemeCatalog.DefaultThemeId);
            _ = ObserveAsync(ActivateStoredThemeAsync());
        }
    }

    public string AccentColor
    {
        get => _accent;
        set
        {
            _accent = value ?? string.Empty;
            configuration.Set(WinoThemeCatalog.AccentColorKey, _accent);
            AccentColorChanged?.Invoke(this, _accent);
            _ = ObserveAsync(ApplyThemeToActiveWindowAsync());
        }
    }

    public async Task InitializeAsync()
    {
        _rootTheme = configuration.Get(WinoThemeCatalog.SelectedAppThemeKey, ApplicationElementTheme.Default);
        _accent = configuration.Get(WinoThemeCatalog.AccentColorKey, string.Empty) ?? string.Empty;
        if (!_preferencesHooked)
        {
            _preferencesHooked = true;
            preferences.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(IPreferencesService.IconStyle)) _ = ObserveAsync(ApplyThemeToActiveWindowAsync());
            };
        }
        var stored = configuration.Get<Guid?>(WinoThemeCatalog.CurrentApplicationThemeKey, null);
        _themeId = WinoThemeCatalog.DefaultThemeId;
        if (stored is { } id)
        {
            // Like Windows, custom themes are only read at launch when one is in use.
            if (WinoThemeCatalog.IsPredefined(id)) _themeId = id;
            else if ((await RefreshCustomThemesAsync()).Any(theme => theme.Id == id)) _themeId = id;
        }
        _forcedTheme = ForcedThemeOf(WinoThemeCatalog.Find(_themeId.Value));
        await ApplyThemeToActiveWindowAsync();
    }

    public async Task<List<AppThemeBase>> GetAvailableThemesAsync()
    {
        var themes = new List<AppThemeBase>(WinoThemeCatalog.PredefinedThemes.Select(static theme => new PredefinedTheme(theme)));
        themes.AddRange((await RefreshCustomThemesAsync()).Select(metadata => new CustomTheme(metadata, store.PreviewPath(metadata.Id))));
        foreach (var theme in themes) await theme.LoadPreviewImageAsync();
        return themes;
    }

    public async Task SelectThemeAsync(Guid themeId, bool forceReapply = false)
    {
        var predefined = WinoThemeCatalog.Find(themeId);
        var custom = predefined is null ? await FindCustomAsync(themeId) : null;
        if (predefined is null && custom is null)
            throw new InvalidOperationException($"Theme '{themeId}' is not available.");
        if (!forceReapply && themeId == _themeId && _preview is null) return;

        var previous = CaptureRuntimeState();
        try
        {
            _preview = null;
            _themeId = themeId;
            configuration.Set(WinoThemeCatalog.CurrentApplicationThemeKey, themeId);
            ApplyThemeChoice(predefined, custom);
            await ApplyThemeToActiveWindowAsync();
        }
        catch
        {
            await RestoreRuntimeStateAsync(previous);
            throw;
        }
    }

    /// <summary>A theme's accent (empty means the system accent); a predefined theme may force light or dark.</summary>
    private void ApplyThemeChoice(WinoThemeDefinition? predefined, CustomThemeMetadata? custom)
    {
        _accent = predefined?.AccentColor ?? custom?.AccentColorHex ?? string.Empty;
        configuration.Set(WinoThemeCatalog.AccentColorKey, _accent);
        AccentColorChanged?.Invoke(this, _accent);
        var forced = ForcedThemeOf(predefined);
        if (forced == _forcedTheme) return;
        _forcedTheme = forced;
        ElementThemeChanged?.Invoke(this, RootTheme);
    }

    private static ApplicationElementTheme? ForcedThemeOf(WinoThemeDefinition? predefined)
        => predefined is { ForcedElementTheme: not ApplicationElementTheme.Default and var forced } ? forced : null;

    private async Task ActivateStoredThemeAsync()
    {
        var id = _themeId ?? WinoThemeCatalog.DefaultThemeId;
        var predefined = WinoThemeCatalog.Find(id);
        var custom = predefined is null ? await FindCustomAsync(id) : null;
        if (predefined is not null || custom is not null) ApplyThemeChoice(predefined, custom);
        await ApplyThemeToActiveWindowAsync();
    }

    public Task SetAccentColorAsync(string hexColor, bool preserveTheme = true)
    {
        AccentColor = hexColor;
        return Task.CompletedTask;
    }

    public string GetSystemAccentColorHex() => AdwaitaAccent;

    /// <summary>The single apply point: light, dark or the system appearance through libadwaita, on the UI thread.</summary>
    public Task ApplyThemeToActiveWindowAsync()
    {
        var root = _preview?.ElementTheme ?? RootTheme;
        return dispatcher.ExecuteOnUIThread(() =>
        {
            Adw.StyleManager.GetDefault().ColorScheme = root switch
            {
                ApplicationElementTheme.Dark => Adw.ColorScheme.ForceDark,
                ApplicationElementTheme.Light => Adw.ColorScheme.ForceLight,
                _ => Adw.ColorScheme.Default
            };
            AppearanceChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    public ThemeRuntimeState CaptureRuntimeState() => new(_themeId, _themeId ?? WinoThemeCatalog.DefaultThemeId, _accent, _rootTheme);

    public async Task RestoreRuntimeStateAsync(ThemeRuntimeState state)
    {
        _preview = null;
        _previewWallpaper = null;
        _themeId = state.ThemeId;
        configuration.Set(WinoThemeCatalog.CurrentApplicationThemeKey, state.ThemeId ?? WinoThemeCatalog.DefaultThemeId);
        _accent = state.AccentColor ?? string.Empty;
        _rootTheme = state.ElementTheme;
        _forcedTheme = state.ThemeId is { } restored ? ForcedThemeOf(WinoThemeCatalog.Find(restored)) : null;
        if (state.ThemeId is { } id && !WinoThemeCatalog.IsPredefined(id) && FindCachedCustom(id) is null)
            await RefreshCustomThemesAsync();
        AccentColorChanged?.Invoke(this, _accent);
        ElementThemeChanged?.Invoke(this, RootTheme);
        await ApplyThemeToActiveWindowAsync();
    }

    public List<string> GetAvailableAccountColors() =>
        ["#e74c3c", "#c0392b", "#e53935", "#d81b60", "#e91e63", "#ec407a", "#ff4081", "#9b59b6", "#8e44ad", "#673ab7", "#3f51b5", "#3498db", "#2980b9", "#03a9f4", "#00bcd4", "#009688", "#1abc9c", "#16a085", "#2ecc71", "#27ae60", "#4caf50", "#8bc34a", "#cddc39", "#f1c40f", "#f39c12", "#ff9800", "#e67e22", "#d35400", "#795548", "#607d8b"];

    public List<BackdropTypeWrapper> GetAvailableBackdropTypes() =>
        [new(WindowBackdropType.None, Translator.MacOS_Personalization_WindowMaterialDefault)];

    public void ApplyBackdrop(WindowBackdropType backdropType) => BackdropChanged?.Invoke(this, WindowBackdropType.None);

    // Windows-only presentation hooks: the icon style follows the preference in the apply point.
    public void ApplyIconStyle() => _ = ObserveAsync(ApplyThemeToActiveWindowAsync());
    public void UpdateSystemCaptionButtonColors() { }

    public Task ApplyCustomThemeAsync(bool isInitializing) => ApplyThemeToActiveWindowAsync();

    public async Task<CustomThemeMetadata?> GetCustomThemeAsync(Guid themeId)
        => (await RefreshCustomThemesAsync()).FirstOrDefault(theme => theme.Id == themeId);

    public Task<List<CustomThemeMetadata>> GetCurrentCustomThemesAsync() => RefreshCustomThemesAsync();

    /// <summary>
    /// Saving needs the wallpaper decoded into a preview thumbnail, which arrives with the Personalization page; custom
    /// themes made on Windows or the Mac still load, show and delete.
    /// </summary>
    public Task<CustomThemeMetadata> SaveCustomThemeAsync(CustomThemeSaveRequest request)
        => Task.FromException<CustomThemeMetadata>(new PlatformNotSupportedException("Creating custom themes is not available on Linux yet."));

    public async Task<bool> DeleteCustomThemeAsync(Guid themeId)
    {
        if (await store.GetAsync(themeId) is null) return false;
        if (_themeId == themeId) await SelectThemeAsync(WinoThemeCatalog.DefaultThemeId, forceReapply: true);
        try
        {
            return await Task.Run(() => store.DeleteAsync(themeId));
        }
        finally
        {
            await RefreshCustomThemesAsync();
        }
    }

    /// <summary>Shows a theme being edited without saving it; the editor sends the picked wallpaper bytes once.</summary>
    public async Task PreviewCustomThemeAsync(CustomThemeMetadata metadata, byte[]? wallpaperData, ApplicationElementTheme elementTheme)
    {
        if (wallpaperData is { Length: > 0 }) _previewWallpaper = wallpaperData;
        _preview = new PreviewState(metadata, wallpaperData is { Length: > 0 } ? wallpaperData : metadata.Id == Guid.Empty ? _previewWallpaper : null, elementTheme);
        await ApplyThemeToActiveWindowAsync();
    }

    private async Task<List<CustomThemeMetadata>> RefreshCustomThemesAsync()
    {
        var themes = await Task.Run(() => store.ListAsync());
        themes = themes.OrderBy(theme => theme.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        lock (_sync) _customThemes = themes;
        return new List<CustomThemeMetadata>(themes);
    }

    private async Task<CustomThemeMetadata?> FindCustomAsync(Guid themeId)
        => FindCachedCustom(themeId) ?? (await RefreshCustomThemesAsync()).FirstOrDefault(theme => theme.Id == themeId);

    private CustomThemeMetadata? FindCachedCustom(Guid themeId)
    {
        lock (_sync) return _customThemes.FirstOrDefault(theme => theme.Id == themeId);
    }

    private static async Task ObserveAsync(Task task)
    {
        try { await task; }
        catch (Exception exception) { Serilog.Log.Warning(exception, "Applying the theme failed."); }
    }

    private sealed class PredefinedTheme : AppThemeBase
    {
        public PredefinedTheme(WinoThemeDefinition definition) : base(definition.Name, definition.Id)
        {
            AccentColor = definition.AccentColor;
            ForceElementTheme = definition.ForcedElementTheme;
            Compatibility = definition.Compatibility;
        }

        public override AppThemeType AppThemeType => Id == WinoThemeCatalog.DefaultThemeId ? AppThemeType.System : AppThemeType.PreDefined;
        public override Task<string> GetThemeResourceDictionaryContentAsync() => Task.FromResult(string.Empty);

        // The theme gallery arrives with the Personalization page.
        protected override Task<string> GetPreviewImagePathAsync() => Task.FromResult(string.Empty);
    }

    private sealed class CustomTheme : AppThemeBase
    {
        private readonly string _previewPath;

        public CustomTheme(CustomThemeMetadata metadata, string previewPath) : base(metadata.Name, metadata.Id)
        {
            _previewPath = previewPath;
            AccentColor = metadata.AccentColorHex;
            ForceElementTheme = ApplicationElementTheme.Default;
            Compatibility = ThemeCompatibility.Both;
        }

        public override AppThemeType AppThemeType => AppThemeType.Custom;
        public override Task<string> GetThemeResourceDictionaryContentAsync() => Task.FromResult(string.Empty);
        protected override Task<string> GetPreviewImagePathAsync() => Task.FromResult(File.Exists(_previewPath) ? _previewPath : string.Empty);
    }
}

/// <summary>Whether the system appearance is dark (libadwaita's style manager, read on the UI thread).</summary>
public sealed class LinuxUnderlyingThemeService : IUnderlyingThemeService
{
    public bool IsUnderlyingThemeDark() => Adw.StyleManager.GetDefault().Dark;
}
