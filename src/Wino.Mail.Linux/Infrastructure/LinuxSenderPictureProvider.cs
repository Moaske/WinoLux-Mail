using CommunityToolkit.Mvvm.Messaging;
using Wino.Core.Domain.Interfaces;
using Wino.Messaging.UI;

namespace Wino.Mail.Linux.Infrastructure;

/// <summary>
/// Sender pictures for mail rows (the AndroidSenderPictureProvider twin): looks each address up through
/// <see cref="IThumbnailService"/> once, loads the cached file as a texture and raises <see cref="PictureChanged"/> so
/// visible rows re-map. A new thumbnail (<see cref="ThumbnailAdded"/>) reloads that address; picture preferences reset
/// the cache. UI thread.
/// </summary>
public sealed class LinuxSenderPictureProvider : IRecipient<ThumbnailAdded>
{
    private readonly IThumbnailService _thumbnails;
    private readonly IDispatcher _dispatcher;
    private readonly Dictionary<string, Gdk.Texture?> _images = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object> _loading = new(StringComparer.Ordinal);

    public LinuxSenderPictureProvider(IThumbnailService thumbnails, IPreferencesService preferences, IDispatcher dispatcher)
    {
        _thumbnails = thumbnails;
        _dispatcher = dispatcher;
        preferences.PreferenceChanged += PreferenceChanged;
        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>Raised with the address whose picture changed, or null when every picture was reset.</summary>
    public event EventHandler<string?>? PictureChanged;

    /// <summary>The cached picture for <paramref name="address"/>, or null while unknown or when there is none.</summary>
    public Gdk.Texture? Get(string? address)
    {
        if (Normalize(address) is not { } key) return null;
        if (_images.TryGetValue(key, out var image)) return image;
        if (!_loading.ContainsKey(key)) Load(key);
        return null;
    }

    public void Receive(ThumbnailAdded message)
    {
        if (Normalize(message.Email) is not { } key) return;
        _ = _dispatcher.ExecuteOnUIThread(() => Load(key));
    }

    private void PreferenceChanged(object? sender, string name)
    {
        if (name is not (nameof(IPreferencesService.IsShowSenderPicturesEnabled)
            or nameof(IPreferencesService.IsGravatarEnabled)
            or nameof(IPreferencesService.IsFaviconEnabled))) return;
        _ = _dispatcher.ExecuteOnUIThread(() =>
        {
            _images.Clear();
            _loading.Clear();
            PictureChanged?.Invoke(this, null);
        });
    }

    private async void Load(string key)
    {
        var token = new object();
        _loading[key] = token;
        string? path = null;
        try { path = (await _thumbnails.GetThumbnailAsync(key).ConfigureAwait(false))?.FilePath; }
        catch (Exception exception) { Serilog.Log.Debug(exception, "The sender picture lookup failed."); }
        await _dispatcher.ExecuteOnUIThread(() =>
        {
            if (!_loading.TryGetValue(key, out var current) || !ReferenceEquals(current, token)) return;
            _loading.Remove(key);
            Gdk.Texture? image = null;
            if (path is not null && File.Exists(path))
            {
                try { image = Gdk.Texture.NewFromFilename(path); }
                catch (Exception exception) { Serilog.Log.Debug(exception, "The sender picture could not be decoded."); }
            }
            _images[key] = image;
            if (image is not null) PictureChanged?.Invoke(this, key);
        });
    }

    private static string? Normalize(string? address)
        => string.IsNullOrWhiteSpace(address) ? null : address.Trim().ToLowerInvariant();
}
