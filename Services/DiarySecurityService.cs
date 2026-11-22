using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GunlukUygulamasi.Services;

public sealed class DiarySecurityService
{
    private const int Pbkdf2Iterations = 600_000;
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GunlukUygulamasi.Security.v1");
    private static readonly byte[] WrapAssociatedData = Encoding.UTF8.GetBytes("GunlukUygulamasi.DataKey.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _securityFilePath;
    private SecurityState? _state;

    public DiarySecurityService(string dataDirectory)
    {
        _securityFilePath = Path.Combine(dataDirectory, "guvenlik.json");
    }

    public bool HasConfiguration => File.Exists(_securityFilePath);
    public bool PasswordEnabled => LoadState().PasswordEnabled;

    public byte[] InitializeWithoutPassword()
    {
        if (HasConfiguration)
        {
            throw new InvalidOperationException("Güvenlik kurulumu zaten tamamlanmış.");
        }

        var dataKey = RandomNumberGenerator.GetBytes(32);
        SaveState(new SecurityState
        {
            PasswordEnabled = false,
            ProtectedDataKey = Convert.ToBase64String(ProtectedData.Protect(
                dataKey, Entropy, DataProtectionScope.CurrentUser))
        });
        return dataKey;
    }

    public byte[] InitializeWithPassword(string password)
    {
        if (HasConfiguration)
        {
            throw new InvalidOperationException("Güvenlik kurulumu zaten tamamlanmış.");
        }

        var dataKey = RandomNumberGenerator.GetBytes(32);
        SetPassword(dataKey, password);
        return dataKey;
    }

    public byte[] UnlockWithoutPassword()
    {
        var state = LoadState();
        if (state.PasswordEnabled)
        {
            throw new InvalidOperationException("Bu günlük parola ile korunuyor.");
        }

        return ProtectedData.Unprotect(
            Convert.FromBase64String(state.ProtectedDataKey ?? throw new CryptographicException("Korunan günlük anahtarı eksik.")),
            Entropy, DataProtectionScope.CurrentUser);
    }

    public bool TryUnlock(string password, out byte[]? dataKey)
    {
        var state = LoadState();
        if (!state.PasswordEnabled)
        {
            dataKey = UnlockWithoutPassword();
            return true;
        }

        var salt = Convert.FromBase64String(state.Salt ?? throw new CryptographicException("Parola bilgisi eksik."));
        var expectedVerifier = Convert.FromBase64String(state.PasswordVerifier ?? throw new CryptographicException("Parola doğrulaması eksik."));
        var keyMaterial = DeriveKeyMaterial(password, salt);
        try
        {
            var verifier = CreateVerifier(keyMaterial.AsSpan(32, 32));
            if (!CryptographicOperations.FixedTimeEquals(verifier, expectedVerifier))
            {
                dataKey = null;
                return false;
            }

            dataKey = UnwrapDataKey(keyMaterial.AsSpan(0, 32), state.WrappedDataKey);
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterial);
        }
    }

    public void SetPassword(byte[] dataKey, string? newPassword)
    {
        if (dataKey.Length != 32)
        {
            throw new CryptographicException("Günlük şifreleme anahtarı geçersiz.");
        }

        if (newPassword is null)
        {
            SaveState(new SecurityState
            {
                PasswordEnabled = false,
                ProtectedDataKey = Convert.ToBase64String(ProtectedData.Protect(
                    dataKey, Entropy, DataProtectionScope.CurrentUser))
            });
            return;
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var keyMaterial = DeriveKeyMaterial(newPassword, salt);
        try
        {
            SaveState(new SecurityState
            {
                PasswordEnabled = true,
                Salt = Convert.ToBase64String(salt),
                PasswordVerifier = Convert.ToBase64String(CreateVerifier(keyMaterial.AsSpan(32, 32))),
                WrappedDataKey = Convert.ToBase64String(WrapDataKey(keyMaterial.AsSpan(0, 32), dataKey))
            });
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterial);
        }
    }

    private static byte[] DeriveKeyMaterial(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, 64);

    private static byte[] CreateVerifier(ReadOnlySpan<byte> key) =>
        HMACSHA256.HashData(key, "GunlukUygulamasi.PasswordCheck.v1"u8);

    private static byte[] WrapDataKey(ReadOnlySpan<byte> wrappingKey, byte[] dataKey)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var wrappedKey = new byte[dataKey.Length];
        using (var aes = new AesGcm(wrappingKey, tag.Length))
        {
            aes.Encrypt(nonce, dataKey, wrappedKey, tag, WrapAssociatedData);
        }

        var result = new byte[nonce.Length + tag.Length + wrappedKey.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, nonce.Length);
        wrappedKey.CopyTo(result, nonce.Length + tag.Length);
        CryptographicOperations.ZeroMemory(wrappedKey);
        return result;
    }

    private static byte[] UnwrapDataKey(ReadOnlySpan<byte> wrappingKey, string? wrappedDataKey)
    {
        var wrapped = Convert.FromBase64String(wrappedDataKey ?? throw new CryptographicException("Şifreli günlük anahtarı eksik."));
        if (wrapped.Length != 60)
        {
            throw new CryptographicException("Şifreli günlük anahtarı geçersiz.");
        }

        var dataKey = new byte[32];
        using (var aes = new AesGcm(wrappingKey, 16))
        {
            aes.Decrypt(wrapped.AsSpan(0, 12), wrapped.AsSpan(28, 32), wrapped.AsSpan(12, 16),
                dataKey, WrapAssociatedData);
        }

        return dataKey;
    }

    private SecurityState LoadState()
    {
        if (_state is not null)
        {
            return _state;
        }

        if (!File.Exists(_securityFilePath))
        {
            throw new InvalidOperationException("Güvenlik yapılandırması bulunamadı.");
        }

        var json = File.ReadAllText(_securityFilePath);
        _state = JsonSerializer.Deserialize<SecurityState>(json, JsonOptions)
            ?? throw new InvalidDataException("Güvenlik yapılandırması okunamadı.");
        return _state;
    }

    private void SaveState(SecurityState state)
    {
        var tempPath = _securityFilePath + ".tmp";
        var json = JsonSerializer.Serialize(state, JsonOptions);
        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _securityFilePath, overwrite: true);
            _state = state;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private sealed class SecurityState
    {
        public bool PasswordEnabled { get; set; }
        public string? Salt { get; set; }
        public string? PasswordVerifier { get; set; }
        public string? WrappedDataKey { get; set; }
        public string? ProtectedDataKey { get; set; }
    }
}
