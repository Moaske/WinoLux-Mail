using System.Diagnostics;
using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Platform;

namespace Wino.Platform.Linux.Services;

/// <summary>
/// Opens links, files and folders with the user's default apps through <c>xdg-open</c>, which the desktop and the
/// Flatpak sandbox route through the OpenURI portal. Only http, https and mailto links leave the app.
/// </summary>
public sealed class LinuxExternalLauncher : IExternalLauncher
{
    private const string Opener = "xdg-open";

    public Task<PlatformOperationResult> LaunchUriAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("https" or "http" or "mailto"))
            return Task.FromResult(new PlatformOperationResult(PlatformOperationStatus.Failed, "Unsupported external URL scheme."));
        return OpenAsync(uri.AbsoluteUri, cancellationToken);
    }

    public Task<PlatformOperationResult> LaunchFileAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) return Task.FromResult(new PlatformOperationResult(PlatformOperationStatus.Failed, "The file does not exist."));
        return OpenAsync(new Uri(fullPath).AbsoluteUri, cancellationToken);
    }

    public Task<PlatformOperationResult> LaunchFolderAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath)) return Task.FromResult(new PlatformOperationResult(PlatformOperationStatus.Failed, "The folder does not exist."));
        return OpenAsync(new Uri(fullPath).AbsoluteUri, cancellationToken);
    }

    private static async Task<PlatformOperationResult> OpenAsync(string target, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(Opener) { UseShellExecute = false, RedirectStandardError = true };
        start.ArgumentList.Add(target);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return new PlatformOperationResult(PlatformOperationStatus.Unavailable, "xdg-open could not be started.");
            var error = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0
                ? new PlatformOperationResult(PlatformOperationStatus.Succeeded)
                : new PlatformOperationResult(PlatformOperationStatus.Failed, error.Trim() is { Length: > 0 } message ? message : $"xdg-open exited with code {process.ExitCode}.");
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            return new PlatformOperationResult(PlatformOperationStatus.Unavailable, "xdg-open is not installed: " + error.Message);
        }
    }
}
