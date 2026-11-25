using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
#if NET48
using System.Runtime.InteropServices;
#endif

namespace GlobalSCPStats.Core.Storage;

public static class StorageKey
{
    private const int KeySize = 64;
    private const string FileName = "storage.key";

    public static byte[] LoadOrCreate(string directory, string? configuredKey)
    {
        if (!string.IsNullOrWhiteSpace(configuredKey))
            return Validate(Convert.FromBase64String(configuredKey!));

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileName);
        if (File.Exists(path)) return Read(path);

        if (File.Exists(Path.Combine(directory, "enrollment.state")) ||
            File.Exists(Path.Combine(directory, "privacy.state")) ||
            Directory.EnumerateFiles(directory, "*.gss").Any())
        {
            // Honor a key created by another startup before reporting a missing key.
            if (File.Exists(path)) return Read(path);
            throw new CryptographicException("Restore the original storage key for this connector data directory.");
        }

        byte[] key = new byte[KeySize];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(key);
        string temporary = Path.Combine(directory, FileName + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            // Restrict access before writing secret bytes and publish only a complete key.
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                RestrictToCurrentAccount(temporary);
                stream.Write(key, 0, key.Length);
                stream.Flush(true);
            }
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path)) { return Read(path); }
            return key;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static byte[] Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length != KeySize)
            throw new CryptographicException("The saved connector storage key has an invalid length.");
        using var reader = new BinaryReader(stream);
        return Validate(reader.ReadBytes(KeySize));
    }

    private static byte[] Validate(byte[] key)
    {
        if (key.Length != KeySize)
            throw new CryptographicException("A 64-byte connector storage key is required.");
        return key;
    }

    private static void RestrictToCurrentAccount(string path)
    {
#if NET48
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
#else
        if (OperatingSystem.IsWindows())
#endif
        {
            using var identity = WindowsIdentity.GetCurrent();
            var security = new FileSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(
                identity.User!, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
            return;
        }

#if NET48
        const int ownerReadWrite = 0x180; // octal 0600
        if (Chmod(path, ownerReadWrite) != 0)
            throw new IOException("Could not restrict permissions on the connector storage key.");
#else
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
#endif
    }

#if NET48
    [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
    private static extern int Chmod(string path, int mode);
#endif
}
