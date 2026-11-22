using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using GunlukUygulamasi.Models;

namespace GunlukUygulamasi.Services;

public class DiaryStorageService
{
    private static readonly byte[] FileMagic = "DIARY1"u8.ToArray();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true
    };

    public string DataDirectory { get; }
    public string EntriesFilePath { get; }
    public string SettingsFilePath { get; }
    public string PhotosDirectory { get; }

    public DiaryStorageService(string? dataDirectory = null)
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        DataDirectory = dataDirectory ?? Path.Combine(localAppData, "GunlukUygulamasi");
        Directory.CreateDirectory(DataDirectory);

        EntriesFilePath = Path.Combine(DataDirectory, "gunlukler.json");
        SettingsFilePath = Path.Combine(DataDirectory, "ayarlar.json");
        PhotosDirectory = Path.Combine(DataDirectory, "Fotograflar");
    }

    public List<DiaryEntry> LoadEntries(byte[] encryptionKey)
    {
        if (!File.Exists(EntriesFilePath))
        {
            return [];
        }

        var fileBytes = File.ReadAllBytes(EntriesFilePath);
        if (fileBytes.Length == 0)
        {
            return [];
        }

        if (fileBytes.AsSpan().StartsWith(FileMagic))
        {
            var clearBytes = Decrypt(fileBytes, encryptionKey);
            try
            {
                return JsonSerializer.Deserialize<List<DiaryEntry>>(clearBytes, JsonOptions) ?? [];
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clearBytes);
            }
        }

        var entries = JsonSerializer.Deserialize<List<DiaryEntry>>(fileBytes, JsonOptions) ?? [];
        SaveEntries(entries, encryptionKey);
        return entries;
    }

    public void SaveEntries(IEnumerable<DiaryEntry> entries, byte[] encryptionKey)
    {
        Directory.CreateDirectory(DataDirectory);
        var clearBytes = JsonSerializer.SerializeToUtf8Bytes(entries, JsonOptions);
        try
        {
            var encryptedBytes = Encrypt(clearBytes, encryptionKey);
            WriteAtomically(EntriesFilePath, encryptedBytes);
            CryptographicOperations.ZeroMemory(encryptedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(clearBytes);
        }
    }

    public string CopyPhotoToStorage(string sourcePath)
    {
        Directory.CreateDirectory(PhotosDirectory);
        var extension = Path.GetExtension(sourcePath);
        var destinationPath = Path.Combine(PhotosDirectory, $"{Guid.NewGuid():N}{extension}");
        File.Copy(sourcePath, destinationPath);
        return destinationPath;
    }

    public AppSettings LoadSettings()
    {
        if (!File.Exists(SettingsFilePath))
        {
            return new AppSettings();
        }

        var json = File.ReadAllText(SettingsFilePath);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new AppSettings();
        }

        return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
    }

    public void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        WriteAtomically(SettingsFilePath, Encoding.UTF8.GetBytes(json));
    }

    private static byte[] Encrypt(byte[] clearBytes, byte[] key)
    {
        ValidateKey(key);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipherText = new byte[clearBytes.Length];
        using (var aes = new AesGcm(key, tag.Length))
        {
            aes.Encrypt(nonce, clearBytes, cipherText, tag, FileMagic);
        }

        var result = new byte[FileMagic.Length + nonce.Length + tag.Length + cipherText.Length];
        FileMagic.CopyTo(result, 0);
        nonce.CopyTo(result, FileMagic.Length);
        tag.CopyTo(result, FileMagic.Length + nonce.Length);
        cipherText.CopyTo(result, FileMagic.Length + nonce.Length + tag.Length);
        CryptographicOperations.ZeroMemory(cipherText);
        return result;
    }

    private static byte[] Decrypt(byte[] encryptedBytes, byte[] key)
    {
        ValidateKey(key);
        var headerLength = FileMagic.Length + 12 + 16;
        if (encryptedBytes.Length < headerLength)
        {
            throw new CryptographicException("Günlük dosyasının şifreli biçimi geçersiz.");
        }

        var nonce = encryptedBytes.AsSpan(FileMagic.Length, 12);
        var tag = encryptedBytes.AsSpan(FileMagic.Length + 12, 16);
        var cipherText = encryptedBytes.AsSpan(headerLength);
        var clearBytes = new byte[cipherText.Length];
        using (var aes = new AesGcm(key, 16))
        {
            aes.Decrypt(nonce, cipherText, tag, clearBytes, FileMagic);
        }

        return clearBytes;
    }

    private static void ValidateKey(byte[] key)
    {
        if (key.Length != 32)
        {
            throw new CryptographicException("Günlük şifreleme anahtarı geçersiz.");
        }
    }

    private static void WriteAtomically(string path, byte[] bytes)
    {
        var tempPath = path + ".tmp";
        try
        {
            File.WriteAllBytes(tempPath, bytes);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
