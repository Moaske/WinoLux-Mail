namespace Wino.Platform.Linux;

/// <summary>
/// The app's folders under the XDG base directories. Data (database, logs, pictures) and secure items live under
/// <c>$XDG_DATA_HOME/WinoLux</c>, the configuration file under <c>$XDG_CONFIG_HOME/WinoLux</c>, temporary files under
/// <c>$XDG_CACHE_HOME/WinoLux</c>. Inside the Flatpak sandbox the same variables point into the app's own folders.
/// </summary>
public sealed class LinuxPaths
{
    public const string FolderName = "WinoLux";

    public LinuxPaths(string dataHome, string configHome, string cacheHome, string packagedResourceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(configHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagedResourceRoot);
        var dataRoot = Path.Combine(dataHome, FolderName);
        ApplicationDataRoot = Path.Combine(dataRoot, "data");
        SecureItemsRoot = Path.Combine(dataRoot, "secure");
        ConfigurationRoot = Path.Combine(configHome, FolderName);
        TemporaryRoot = Path.Combine(cacheHome, FolderName, "temp");
        PackagedResourceRoot = packagedResourceRoot;
    }

    /// <summary>The XDG folders of the current user; the packaged resources sit next to the program.</summary>
    public static LinuxPaths FromEnvironment()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) throw new InvalidOperationException("The home folder is unavailable.");
        return new LinuxPaths(
            XdgFolder("XDG_DATA_HOME", Path.Combine(home, ".local", "share")),
            XdgFolder("XDG_CONFIG_HOME", Path.Combine(home, ".config")),
            XdgFolder("XDG_CACHE_HOME", Path.Combine(home, ".cache")),
            Path.Combine(AppContext.BaseDirectory, "PackagedResources"));
    }

    /// <summary><c>IApplicationConfiguration.ApplicationDataFolderPath</c>: database, logs, pictures.</summary>
    public string ApplicationDataRoot { get; }

    /// <summary>Protected secure item files (<see cref="Wino.Services.Security.ProtectedFileSecureItemStore"/>).</summary>
    public string SecureItemsRoot { get; }

    /// <summary>The JSON configuration file's folder.</summary>
    public string ConfigurationRoot { get; }

    public string TemporaryRoot { get; }

    /// <summary>Files the shared services read as packaged resources (<c>ms-appx:///</c> paths), shipped with the program.</summary>
    public string PackagedResourceRoot { get; }

    /// <summary>True when no data or secure item exists yet: the only time a new protection key is created.</summary>
    public bool IsVerifiedNewInstallation => !Exists(ApplicationDataRoot) && !Exists(SecureItemsRoot);

    /// <summary>
    /// Creates the folders, owner-only. The default umask leaves new folders readable by every local user, and the
    /// database holds the user's mail; the owner-only mode on each WinoLux root also covers everything inside it,
    /// including folders an earlier version created.
    /// </summary>
    public void CreateDirectories()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("WinoLux paths are Linux folders.");
        foreach (var root in new[] { Path.GetDirectoryName(ApplicationDataRoot), ConfigurationRoot, Path.GetDirectoryName(TemporaryRoot) })
        {
            if (string.IsNullOrEmpty(root)) continue;
            Directory.CreateDirectory(root, OwnerOnly);
            File.SetUnixFileMode(root, OwnerOnly);
        }
        Directory.CreateDirectory(ApplicationDataRoot, OwnerOnly);
        Directory.CreateDirectory(SecureItemsRoot, OwnerOnly);
        Directory.CreateDirectory(TemporaryRoot, OwnerOnly);
    }

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    // The XDG specification ignores relative values.
    private static string XdgFolder(string variable, string fallback)
        => Environment.GetEnvironmentVariable(variable) is { Length: > 0 } value && Path.IsPathRooted(value) ? value : fallback;

    private static bool Exists(string path) => Directory.Exists(path) || File.Exists(path);
}
