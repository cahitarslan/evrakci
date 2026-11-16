using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Evrakci.Core.Cases;

/// <summary>Values of one case, saved to a .evrak file.</summary>
public sealed class CaseData
{
    public int FormatVersion { get; set; } = CaseFile.CurrentFormatVersion;
    public string App { get; set; } = "Evrakçı";
    public string? AppVersion { get; set; }
    public string? SetName { get; set; }
    public string? SetFolder { get; set; }
    public DateTimeOffset SavedAt { get; set; }
    public Dictionary<string, CaseValue> Values { get; set; } = new();
    /// <summary>Template file → occurrence index (as text) → suffix.</summary>
    public Dictionary<string, Dictionary<string, string>>? SuffixOverrides { get; set; }
    /// <summary>Templates chosen for this case; null means the set's defaults.</summary>
    public List<string>? Documents { get; set; }
    /// <summary>Name shown in the file list, e.g. "AYŞE YILMAZ – Kira".</summary>
    public string? Title { get; set; }
    /// <summary>The user named the file; it is no longer derived from the party's name.</summary>
    public bool TitleIsCustom { get; set; }
    /// <summary>When documents were last produced from this case.</summary>
    public DateTimeOffset? GeneratedAt { get; set; }
    /// <summary>
    /// Files of the set changed for this case only (templates, set.json): file name → base64 content.
    /// They replace the set's own files whenever this case is opened.
    /// </summary>
    public Dictionary<string, string>? TemplateFiles { get; set; }
}

public sealed class CaseValue
{
    public string? Value { get; set; }
    public bool Blank { get; set; }
    public string? Currency { get; set; }
}

/// <summary>
/// Plain JSON, or — when a password is given — a JSON envelope with the same JSON encrypted using
/// AES-256-GCM and a PBKDF2-SHA256 derived key.
/// </summary>
public static class CaseFile
{
    public const int CurrentFormatVersion = 1;
    public const string Extension = ".evrak";
    const int Iterations = 600_000;
    const long MaxBytes = 10 * 1024 * 1024;

    public static void Save(string path, CaseData data, string? password)
    {
        data.FormatVersion = CurrentFormatVersion;
        var plain = JsonSerializer.SerializeToUtf8Bytes(data, Sets.SetDefinition.JsonOptions);
        byte[] bytes = string.IsNullOrEmpty(password) ? plain : Encrypt(plain, password);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>The case as plain JSON bytes (for callers that store it in their own envelope).</summary>
    public static byte[] Serialize(CaseData data)
    {
        data.FormatVersion = CurrentFormatVersion;
        return JsonSerializer.SerializeToUtf8Bytes(data, Sets.SetDefinition.JsonOptions);
    }

    /// <summary>Reads plain case JSON produced by <see cref="Serialize"/>.</summary>
    public static CaseData Parse(byte[] json)
    {
        try
        {
            var data = JsonSerializer.Deserialize<CaseData>(json, Sets.SetDefinition.JsonOptions) ?? throw new EvrakciException("Dosya boş.");
            if (data.FormatVersion > CurrentFormatVersion)
                throw new EvrakciException("Bu dosya Evrakçı'nın daha yeni bir sürümüyle kaydedilmiş.");
            return data;
        }
        catch (JsonException) { throw new EvrakciException("Dosya okunamadı; biçimi bozuk."); }
    }

    public static bool IsEncrypted(string path)
    {
        try
        {
            var node = ReadNode(path);
            return node?["encrypted"]?.GetValue<bool>() == true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new EvrakciException("Vaka dosyası okunamadı; biçimi bozuk.");
        }
    }

    public static CaseData Load(string path, string? password)
    {
        JsonNode? node;
        try { node = ReadNode(path); }
        catch (JsonException) { throw new EvrakciException("Vaka dosyası okunamadı; biçimi bozuk."); }
        if (node is null) throw new EvrakciException("Vaka dosyası boş.");

        var version = node["formatVersion"]?.GetValue<int>() ?? 0;
        if (version > CurrentFormatVersion)
            throw new EvrakciException("Bu vaka dosyası Evrakçı'nın daha yeni bir sürümüyle kaydedilmiş.");

        byte[] plain;
        if (node["encrypted"]?.GetValue<bool>() == true)
        {
            if (string.IsNullOrEmpty(password)) throw new EvrakciException("Bu vaka dosyası şifreli; şifre gerekli.");
            plain = Decrypt(node, password);
        }
        else plain = Encoding.UTF8.GetBytes(node.ToJsonString());

        try
        {
            return JsonSerializer.Deserialize<CaseData>(plain, Sets.SetDefinition.JsonOptions)
                   ?? throw new EvrakciException("Vaka dosyası boş.");
        }
        catch (JsonException) { throw new EvrakciException("Vaka dosyası okunamadı; biçimi bozuk."); }
    }

    static JsonNode? ReadNode(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new EvrakciException("Vaka dosyası bulunamadı.");
        if (info.Length > MaxBytes) throw new EvrakciException("Vaka dosyası çok büyük.");
        return JsonNode.Parse(File.ReadAllBytes(path), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true });
    }

    static byte[] Encrypt(byte[] plain, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, plain, cipher, tag);
        CryptographicOperations.ZeroMemory(key);
        var envelope = new JsonObject
        {
            ["formatVersion"] = CurrentFormatVersion,
            ["app"] = "Evrakçı",
            ["encrypted"] = true,
            ["kdf"] = "PBKDF2-SHA256",
            ["iterations"] = Iterations,
            ["cipher"] = "AES-256-GCM",
            ["salt"] = Convert.ToBase64String(salt),
            ["nonce"] = Convert.ToBase64String(nonce),
            ["tag"] = Convert.ToBase64String(tag),
            ["data"] = Convert.ToBase64String(cipher),
        };
        return Encoding.UTF8.GetBytes(envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    static byte[] Decrypt(JsonNode node, string password)
    {
        try
        {
            var iterations = node["iterations"]!.GetValue<int>();
            if (iterations < 10_000 || iterations > 10_000_000) throw new FormatException();
            var salt = Convert.FromBase64String(node["salt"]!.GetValue<string>());
            var nonce = Convert.FromBase64String(node["nonce"]!.GetValue<string>());
            var tag = Convert.FromBase64String(node["tag"]!.GetValue<string>());
            var cipher = Convert.FromBase64String(node["data"]!.GetValue<string>());
            var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(key, 16)) aes.Decrypt(nonce, cipher, tag, plain);
            CryptographicOperations.ZeroMemory(key);
            return plain;
        }
        catch (AuthenticationTagMismatchException)
        {
            throw new EvrakciException("Şifre yanlış ya da dosya bozulmuş.");
        }
        catch (Exception ex) when (ex is FormatException or NullReferenceException or InvalidOperationException or ArgumentException or CryptographicException)
        {
            throw new EvrakciException("Şifreli vaka dosyası bozuk.");
        }
    }
}
