using Wino.Core.Domain.Enums;
using Wino.Core.Domain.Interfaces;

namespace Wino.Platform.Linux.Services;

/// <summary>
/// The Microsoft Store does not exist on Linux and WinoLux sells nothing, so there are no purchases or store updates.
/// The review page is the WinoLux repository.
/// </summary>
public sealed class LinuxMicrosoftStoreService(IExternalLauncher launcher) : IMicrosoftStoreService
{
    public static readonly Uri RepositoryUri = new("https://github.com/Moaske/WinoLux-Mail");

    public bool HasAvailableUpdate => false;
    public Task<bool> HasProductAsync(WinoAddOnProductType productType) => Task.FromResult(false);
    public Task<StorePurchaseResult> PurchaseAsync(WinoAddOnProductType productType) => Task.FromException<StorePurchaseResult>(new PlatformNotSupportedException("Microsoft Store is not available on Linux."));
    public Task<string?> GetCustomerCollectionsIdAsync(string serviceTicket, string publisherUserId) => Task.FromResult<string?>(null);
    public Task<bool> RefreshAvailabilityAsync() => Task.FromResult(false);
    public Task<bool> StartUpdateAsync(IProgress<double>? progress = null) => Task.FromResult(false);
    public Task PromptRatingDialogAsync() => Task.CompletedTask;

    public async Task LaunchStorePageForReviewAsync()
    {
        var result = await launcher.LaunchUriAsync(RepositoryUri).ConfigureAwait(false);
        if (result.Status != PlatformOperationStatus.Succeeded)
            Serilog.Log.Warning("Could not open the WinoLux repository page: {Error}", result.ErrorMessage);
    }
}
