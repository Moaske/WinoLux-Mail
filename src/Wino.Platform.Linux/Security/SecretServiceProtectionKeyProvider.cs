using System.Security.Cryptography;
using Wino.Core.Domain.Interfaces;

namespace Wino.Platform.Linux.Security;

/// <summary>
/// The installation's 32-byte protection key, kept as one item in the user's default Secret Service collection
/// (GNOME Keyring, KWallet, KeePassXC) through libsecret. <see cref="Wino.Services.Security.AesGcmEnvelopeSecretProtector"/>
/// needs the raw key bytes; the keyring encrypts the item at rest and unlocks it with the user's login.
/// </summary>
/// <remarks>
/// Item attributes (stored data, never change): <c>application</c> = the application identity, <c>purpose</c> =
/// <see cref="Purpose"/>. The secret is the key in Base64.
/// Failures: no item reads as missing (null). An item that is not Base64 or not 32 bytes is corrupt
/// (<see cref="CryptographicException"/>). A missing libsecret, no running Secret Service or a refused call is
/// unavailable (<see cref="PlatformNotSupportedException"/>), as the <see cref="ISecretProtector"/> contract requires;
/// there is no plaintext fallback.
/// </remarks>
public sealed class SecretServiceProtectionKeyProvider : ISecretProtectionKeyProvider
{
    public const string Purpose = "wino-protection-v1";
    private const int KeyLength = 32;
    private static readonly Lock Gate = new();
    private readonly Dictionary<string, string> _attributes;

    public SecretServiceProtectionKeyProvider(string applicationIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationIdentity);
        ApplicationIdentity = applicationIdentity;
        _attributes = new Dictionary<string, string>
        {
            ["application"] = applicationIdentity,
            ["purpose"] = Purpose,
        };
    }

    public string ApplicationIdentity { get; }

    public byte[]? ReadKey()
    {
        lock (Gate) return ReadKeyCore();
    }

    public bool TryAddKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeyLength) throw new ArgumentException("The protection key must be 32 bytes.", nameof(key));
        lock (Gate)
        {
            var existing = ReadKeyCore();
            if (existing is not null)
            {
                CryptographicOperations.ZeroMemory(existing);
                return false;
            }
            Call(() => LibSecret.Store(_attributes, "WinoLux Mail protection key", Convert.ToBase64String(key)));
            return true;
        }
    }

    private byte[]? ReadKeyCore()
    {
        var encoded = Call(() => LibSecret.Lookup(_attributes));
        if (encoded is null) return null;

        byte[] key;
        try { key = Convert.FromBase64String(encoded); }
        catch (FormatException error) { throw new CryptographicException("The stored protection key is not valid Base64.", error); }
        if (key.Length == KeyLength) return key;
        CryptographicOperations.ZeroMemory(key);
        throw new CryptographicException("The stored protection key has an invalid length.");
    }

    private static T Call<T>(Func<T> action)
    {
        try { return action(); }
        catch (Exception error) when (error is SecretServiceException or DllNotFoundException or EntryPointNotFoundException)
        {
            throw new PlatformNotSupportedException("The Secret Service is unavailable: " + error.Message, error);
        }
    }

    private static void Call(Action action) => Call(() => { action(); return 0; });
}
