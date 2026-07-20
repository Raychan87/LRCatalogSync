using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace LRCatalogSync.Infrastructure;

/// <summary>
/// Statische Klasse für AES-256-Verschlüsselung und -Entschlüsselung
/// Wird verwendet, um Samba-Passwörter sicher in der Konfigurationsdatei zu speichern.
/// </summary>
public static class AesEncryptor
{
    // Statische Passphrase für Schlüsselgenerierung (32 Bytes für AES-256)
    // Hinweis: Für höhere Sicherheit könnte dieser Wert in einer Umgebungsvariable oder separaten Datei gespeichert werden
    private const string PASSPHRASE = "LightroomSync2024SecureKey!";

    /// <summary>
    /// Verschlüsselt einen Text mit AES-256
    /// </summary>
    /// <param name="plainText">Der zu verschlüsselnde Text</param>
    /// <returns>Der verschlüsselte Text als Base64-String (IV + verschlüsselter Daten)</returns>
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            throw new ArgumentException("Plain text cannot be null or empty", nameof(plainText));

        // Schlüssel aus Passphrase generieren (32 Bytes für AES-256)
        byte[] key = GenerateKey(PASSPHRASE);

        using (Aes aes = Aes.Create())
        {
            aes.Key = key;
            aes.GenerateIV(); // Zufälligen IV generieren

            ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV);

            using (MemoryStream ms = new MemoryStream())
            {
                // IV am Anfang des Streams speichern (16 Bytes)
                ms.Write(aes.IV, 0, aes.IV.Length);

                using (CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                {
                    byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                    cs.Write(plainBytes, 0, plainBytes.Length);
                }

                // Verschlüsselte Daten zurückgeben als Base64-String
                return Convert.ToBase64String(ms.ToArray());
            }
        }
    }

    /// <summary>
    /// Entschlüsselt einen AES-256-verschlüsselten Text
    /// </summary>
    /// <param name="cipherText">Der verschlüsselte Text als Base64-String</param>
    /// <returns>Der entschlüsselte Text</returns>
    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            throw new ArgumentException("Cipher text cannot be null or empty", nameof(cipherText));

        // Schlüssel aus Passphrase generieren (32 Bytes für AES-256)
        byte[] key = GenerateKey(PASSPHRASE);

        try
        {
            byte[] fullCipher = Convert.FromBase64String(cipherText);

            using (Aes aes = Aes.Create())
            {
                aes.Key = key;

                // IV aus den ersten 16 Bytes extrahieren
                byte[] iv = new byte[16];
                Array.Copy(fullCipher, 0, iv, 0, iv.Length);
                aes.IV = iv;

                // Restliche Bytes als verschlüsselten Text behandeln
                byte[] cipherBytes = new byte[fullCipher.Length - iv.Length];
                Array.Copy(fullCipher, iv.Length, cipherBytes, 0, cipherBytes.Length);

                ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                using (MemoryStream ms = new MemoryStream(cipherBytes))
                using (CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read))
                using (StreamReader reader = new StreamReader(cs))
                {
                    return reader.ReadToEnd();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"AesEncryptor: Entschlüsselung fehlgeschlagen: {ex.Message}");
            throw new InvalidOperationException("Failed to decrypt password. The password may be corrupted or the key may be incorrect.", ex);
        }
    }

    /// <summary>
    /// Generiert einen 32-Byte-Schlüssel aus einer Passphrase mittels SHA-256
    /// </summary>
    /// <param name="passphrase">Die Passphrase</param>
    /// <returns>32-Byte-Schlüssel für AES-256</returns>
    private static byte[] GenerateKey(string passphrase)
    {
        using (SHA256 sha256 = SHA256.Create())
        {
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(passphrase));
        }
    }
}