using System;
using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DevSAK.Services;

// RFC 8188 aes128gcm framing, compatible with File.Kiwi's wormhole-crypto SDK.
internal static class FileKiwiEncryption
{
    internal const int RecordSize = 65536;
    internal static long EncryptedLength(long length) => checked(21 + length + 17 * ((length - 1) / (RecordSize - 17) + 1));

    internal static async Task<byte[]> EncryptMetadataAsync(string text, byte[] key, CancellationToken ct)
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var target = new MemoryStream();
        await EncryptAsync(source, source.Length, target, key, ct);
        return target.ToArray();
    }

    internal static async Task EncryptAsync(Stream source, long length, Stream target, byte[] key,
        CancellationToken ct, Action<long>? onProgress = null, byte[]? salt = null, int recordSize = RecordSize)
    {
        if (length <= 0 || recordSize < 18) throw new ArgumentOutOfRangeException(nameof(length));
        salt ??= RandomNumberGenerator.GetBytes(16);
        if (salt.Length != 16) throw new ArgumentException("Salt inválido.", nameof(salt));
        var cek = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 16, salt, Encoding.ASCII.GetBytes("Content-Encoding: aes128gcm\0"));
        var nonceBase = HKDF.DeriveKey(HashAlgorithmName.SHA256, key, 12, salt, Encoding.ASCII.GetBytes("Content-Encoding: nonce\0"));
        var plain = new byte[recordSize - 16];
        try
        {
            var header = new byte[21];
            salt.CopyTo(header, 0);
            BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16, 4), (uint)recordSize);
            await target.WriteAsync(header, ct);
            using var aes = new AesGcm(cek, 16);
            var encrypted = new byte[recordSize];
            long processed = 0;
            uint sequence = 0;
            while (processed < length)
            {
                ct.ThrowIfCancellationRequested();
                int count = (int)Math.Min(recordSize - 17, length - processed);
                await source.ReadExactlyAsync(plain.AsMemory(0, count), ct);
                plain[count] = processed + count == length ? (byte)2 : (byte)1;
                var nonce = (byte[])nonceBase.Clone();
                var suffix = BinaryPrimitives.ReadUInt32BigEndian(nonce.AsSpan(8));
                BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), suffix ^ sequence);
                aes.Encrypt(nonce, plain.AsSpan(0, count + 1), encrypted.AsSpan(0, count + 1), encrypted.AsSpan(count + 1, 16));
                await target.WriteAsync(encrypted.AsMemory(0, count + 17), ct);
                processed += count;
                onProgress?.Invoke(processed);
                if (processed < length) sequence = checked(sequence + 1);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(cek);
            CryptographicOperations.ZeroMemory(nonceBase);
            CryptographicOperations.ZeroMemory(plain);
        }
    }
}
