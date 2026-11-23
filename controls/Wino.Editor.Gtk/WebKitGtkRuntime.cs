namespace Wino.Editor.Gtk;

/// <summary>
/// Loads WebKitGTK 6.0 once. It is a system library (Ubuntu package <c>libwebkitgtk-6.0-4</c>), so it may be missing;
/// callers show <see cref="PackageName"/> instead of a mail view when <see cref="TryInitialize"/> fails.
/// </summary>
public static class WebKitGtkRuntime
{
    public const string PackageName = "libwebkitgtk-6.0-4";
    private static readonly Lazy<Exception?> Initialization = new(() =>
    {
        try
        {
            WebKit.Module.Initialize();
            return null;
        }
        catch (Exception exception) when (exception is DllNotFoundException or TypeInitializationException or EntryPointNotFoundException)
        {
            return exception;
        }
    });

    /// <summary>True when WebKitGTK is loaded; otherwise <paramref name="error"/> says why. UI thread.</summary>
    public static bool TryInitialize(out Exception? error)
    {
        error = Initialization.Value;
        return error is null;
    }
}
