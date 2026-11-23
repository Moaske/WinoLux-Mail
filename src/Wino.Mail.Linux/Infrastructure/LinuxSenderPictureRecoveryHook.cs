using Wino.Core.Domain.Entities.Shared;
using Wino.Core.Domain.Interfaces;
using Wino.Mail.ViewModels.Lifecycle;
using Wino.Services;

namespace Wino.Mail.Linux.Infrastructure;

/// <summary>
/// One-time repair for installations that ran before SkiaSharp's Linux library shipped (milestone 2d). Every picture
/// download then failed, and the shared thumbnail service stored each sender as "no picture", an answer it never asks
/// again. This forgets those answers once, before the first page shows, so the senders are looked up again; senders
/// with a picture keep it.
/// </summary>
internal sealed class LinuxSenderPictureRecoveryHook(IDatabaseService database, IConfigurationService configuration) : IApplicationLifecycleHook
{
    private const string CompletedKey = "Linux.SenderPictureMissesCleared.v1";

    public async Task ServicesReadyAsync()
    {
        if (configuration.Get(CompletedKey, false)) return;
        try
        {
            var cleared = await database.Connection.ExecuteAsync(
                $"DELETE FROM {nameof(Thumbnail)} WHERE IFNULL({nameof(Thumbnail.GravatarFileName)}, '') = '' AND IFNULL({nameof(Thumbnail.FaviconFileName)}, '') = ''");
            configuration.Set(CompletedKey, true);
            Serilog.Log.Information("Forgot {Count} stored 'no picture' answers for sender pictures.", cleared);
        }
        catch (Exception exception)
        {
            Serilog.Log.Warning(exception, "Clearing stored 'no picture' answers failed; it runs again at the next start.");
        }
    }

    public Task StoppingAsync(bool runtimeStarted) => Task.CompletedTask;
}
