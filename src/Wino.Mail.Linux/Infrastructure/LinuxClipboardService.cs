using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models.Platform;

namespace Wino.Mail.Linux.Infrastructure;

/// <summary>Copies text to the display's clipboard on the UI thread.</summary>
public sealed class LinuxClipboardService(IDispatcher dispatcher) : IClipboardService
{
    public async Task<PlatformOperationResult> CopyTextAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        var result = new PlatformOperationResult(PlatformOperationStatus.Unavailable, "No display is available.");
        await dispatcher.ExecuteOnUIThread(() =>
        {
            if (Gdk.Display.GetDefault() is not { } display) return;
            display.GetClipboard().SetText(text);
            result = new PlatformOperationResult(PlatformOperationStatus.Succeeded);
        }).ConfigureAwait(false);
        return result;
    }
}
