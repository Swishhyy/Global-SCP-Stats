using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace GlobalSCPStats.Core.Storage;

public sealed class EncryptedFileStore
{
    private static readonly byte[] Magic = [71, 83, 83, 49]; // GSS1
    private readonly byte[] encryptionKey;
    private readonly byte[] authenticationKey;

    public EncryptedFileStore(byte[] key)
    {
        if (key == null || key.Length != 64) throw new CryptographicException("A 64-byte storage key is required.");
        encryptionKey = [.. key.Take(32)];
        authenticationKey = [.. key.Skip(32)];
    }

    public void WriteNew(string path, byte[] plaintext) => Write(path, plaintext, false);

    public void Replace(string path, byte[] plaintext) => Write(path, plaintext, true);

    private void Write(string path, byte[] plaintext, bool replace)
    {
        if (plaintext.Length > 16 * 1024 * 1024 - 80)
            throw new IOException("The encrypted document exceeds the size limit.");
        using var aes = Aes.Create();
        aes.Key = encryptionKey;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();
        using var encryptor = aes.CreateEncryptor();
        byte[] ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
        byte[] payload = [.. Magic.Concat(aes.IV).Concat(ciphertext)];
        using var hmac = new HMACSHA256(authenticationKey);
        byte[] envelope = [.. payload.Concat(hmac.ComputeHash(payload))];
        string temporary = Path.Combine(Path.GetDirectoryName(path)!,
            Path.GetFileNameWithoutExtension(path) + "." + Guid.NewGuid().ToString("N") + Path.GetExtension(path) + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                file.Write(envelope, 0, envelope.Length);
                file.Flush(true);
            }
            if (replace && File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    public byte[] Read(string path)
    {
        if (new FileInfo(path).Length > 16 * 1024 * 1024)
            throw new CryptographicException("The encrypted outbox file exceeds the size limit.");
        byte[] envelope = File.ReadAllBytes(path);
        if (envelope.Length < 68 || !envelope.Take(4).SequenceEqual(Magic))
            throw new CryptographicException("The encrypted outbox file is invalid.");

        int payloadLength = envelope.Length - 32;
        using var hmac = new HMACSHA256(authenticationKey);
        byte[] expected = hmac.ComputeHash(envelope, 0, payloadLength);
        int difference = 0;
        for (int i = 0; i < expected.Length; i++) difference |= expected[i] ^ envelope[payloadLength + i];
        if (difference != 0) throw new CryptographicException("Outbox authentication failed. The file has been preserved.");

        using var aes = Aes.Create();
        aes.Key = encryptionKey;
        aes.IV = [.. envelope.Skip(4).Take(16)];
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(envelope, 20, payloadLength - 20);
    }

}
