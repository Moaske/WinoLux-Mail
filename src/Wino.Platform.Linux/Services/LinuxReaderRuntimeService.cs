using System.Runtime.InteropServices;
using Wino.Core.Domain.Interfaces;

namespace Wino.Platform.Linux.Services;

/// <summary>The reader and the composer need WebKitGTK 6.0; it is available when its library loads.</summary>
public sealed class LinuxReaderRuntimeService : IReaderRuntimeService
{
    private const string WebKitLibrary = "libwebkitgtk-6.0.so.4";
    private static readonly Lazy<bool> Available = new(() =>
    {
        if (!NativeLibrary.TryLoad(WebKitLibrary, out var handle)) return false;
        NativeLibrary.Free(handle);
        return true;
    });

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Available.Value);
    }
}
