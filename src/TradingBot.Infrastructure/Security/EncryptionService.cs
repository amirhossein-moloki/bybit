using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using TradingBot.Application.Interfaces;
using TradingBot.Infrastructure.Configuration;

namespace TradingBot.Infrastructure.Security;

public class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;

    public EncryptionService(TradingBotSettings settings)
    {
        var keyString = settings.Security?.EncryptionKey;
        if (string.IsNullOrWhiteSpace(keyString))
        {
            keyString = "FallbackSecureDefaultEncryptionKeyChangeInProd!";
        }

        // Standardize key to 32 bytes using SHA256
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(keyString));
    }

    public string Encrypt(string plainText)
    {
        if (plainText == null) return null!;
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor(aes.Key, iv);
        using var ms = new MemoryStream();

        // Write IV first
        ms.Write(iv, 0, iv.Length);

        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        using (var sw = new StreamWriter(cs, Encoding.UTF8))
        {
            sw.Write(plainText);
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    public string Decrypt(string cipherText)
    {
        if (cipherText == null) return null!;
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;

        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = _key;

        var iv = new byte[aes.BlockSize / 8];
        if (fullCipher.Length < iv.Length)
        {
            throw new CryptographicException("Invalid cipher text length.");
        }

        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream(fullCipher, iv.Length, fullCipher.Length - iv.Length);
        using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
        using var sr = new StreamReader(cs, Encoding.UTF8);

        return sr.ReadToEnd();
    }
}
