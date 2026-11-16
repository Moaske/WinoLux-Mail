using System.Runtime.InteropServices;

namespace Wino.Platform.Linux.Security;

/// <summary>
/// The few libsecret and GLib calls the protection key needs. The <c>v</c> variants take the attributes as a GHashTable
/// instead of C varargs, which P/Invoke cannot express. A null schema stores and matches the attributes exactly.
/// </summary>
internal static unsafe partial class LibSecret
{
    private const string SecretLibrary = "libsecret-1.so.0";
    private const string GLibLibrary = "libglib-2.0.so.0";

    [LibraryImport(SecretLibrary, EntryPoint = "secret_password_lookupv_sync")]
    private static partial IntPtr LookupNative(IntPtr schema, IntPtr attributes, IntPtr cancellable, out IntPtr error);

    [LibraryImport(SecretLibrary, EntryPoint = "secret_password_storev_sync", StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial int StoreNative(IntPtr schema, IntPtr attributes, string? collection, string label, IntPtr password, IntPtr cancellable, out IntPtr error);

    [LibraryImport(SecretLibrary, EntryPoint = "secret_password_free")]
    private static partial void PasswordFree(IntPtr password);

    [LibraryImport(GLibLibrary, EntryPoint = "g_hash_table_new_full")]
    private static partial IntPtr HashTableNewFull(IntPtr hash, IntPtr equal, IntPtr keyDestroy, IntPtr valueDestroy);

    [LibraryImport(GLibLibrary, EntryPoint = "g_hash_table_insert")]
    [return: MarshalAs(UnmanagedType.I4)]
    private static partial int HashTableInsert(IntPtr table, IntPtr key, IntPtr value);

    [LibraryImport(GLibLibrary, EntryPoint = "g_hash_table_unref")]
    private static partial void HashTableUnref(IntPtr table);

    [LibraryImport(GLibLibrary, EntryPoint = "g_strdup", StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr StrDup(string value);

    [LibraryImport(GLibLibrary, EntryPoint = "g_error_free")]
    private static partial void ErrorFree(IntPtr error);

    private static readonly Lazy<(IntPtr Hash, IntPtr Equal, IntPtr Free)> HashFunctions = new(() =>
    {
        var glib = NativeLibrary.Load(GLibLibrary);
        return (NativeLibrary.GetExport(glib, "g_str_hash"), NativeLibrary.GetExport(glib, "g_str_equal"), NativeLibrary.GetExport(glib, "g_free"));
    });

    /// <summary>The stored secret, or null when no item matches. Throws <see cref="SecretServiceException"/> when the service fails.</summary>
    public static string? Lookup(IReadOnlyDictionary<string, string> attributes)
    {
        var table = CreateTable(attributes);
        try
        {
            var secret = LookupNative(IntPtr.Zero, table, IntPtr.Zero, out var error);
            ThrowOnError(error);
            if (secret == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUTF8(secret); }
            finally { PasswordFree(secret); }
        }
        finally { HashTableUnref(table); }
    }

    /// <summary>Stores <paramref name="secret"/> in the default collection, replacing an item with the same attributes.</summary>
    public static void Store(IReadOnlyDictionary<string, string> attributes, string label, string secret)
    {
        var table = CreateTable(attributes);
        var password = Marshal.StringToCoTaskMemUTF8(secret);
        try
        {
            var stored = StoreNative(IntPtr.Zero, table, null, label, password, IntPtr.Zero, out var error);
            ThrowOnError(error);
            if (stored == 0) throw new SecretServiceException("The Secret Service did not store the item.");
        }
        finally
        {
            // The secret's bytes are cleared before the buffer is released.
            new Span<byte>((void*)password, System.Text.Encoding.UTF8.GetByteCount(secret)).Clear();
            Marshal.FreeCoTaskMem(password);
            HashTableUnref(table);
        }
    }

    private static IntPtr CreateTable(IReadOnlyDictionary<string, string> attributes)
    {
        var (hash, equal, free) = HashFunctions.Value;
        var table = HashTableNewFull(hash, equal, free, free);
        foreach (var (key, value) in attributes) HashTableInsert(table, StrDup(key), StrDup(value));
        return table;
    }

    private static void ThrowOnError(IntPtr error)
    {
        if (error == IntPtr.Zero) return;
        // GError: guint32 domain, gint code, gchar* message.
        var message = Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(error, 8)) ?? "Unknown Secret Service error.";
        ErrorFree(error);
        throw new SecretServiceException(message);
    }
}

/// <summary>The Secret Service rejected or could not complete a call (no service running, locked and not unlocked, D-Bus error).</summary>
public sealed class SecretServiceException(string message) : Exception(message);
