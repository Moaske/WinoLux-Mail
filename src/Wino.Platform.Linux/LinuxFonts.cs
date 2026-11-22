using System.Runtime.InteropServices;

namespace Wino.Platform.Linux;

/// <summary>
/// Fonts the app ships, made visible to Pango through fontconfig for this process only. Registration must run before
/// GTK creates its font map, so <c>Program</c> calls it first.
/// </summary>
public static partial class LinuxFonts
{
    [LibraryImport("libfontconfig.so.1", EntryPoint = "FcConfigAppFontAddFile", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int AppFontAddFile(IntPtr config, string file);

    /// <summary>Adds <paramref name="path"/> to the current fontconfig configuration. False when the file or fontconfig is missing.</summary>
    public static bool AddApplicationFont(string path)
    {
        if (!File.Exists(path)) return false;
        try { return AppFontAddFile(IntPtr.Zero, path) != 0; }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
}
