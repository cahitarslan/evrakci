using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Evrakci.Core.Cases;
using Evrakci.Core.Text;

namespace Evrakci.App.Services;

/// <summary>
/// Values the user ticked "Hatırla" for. They pre-fill the same field (by name) in every new case of
/// every set. Stored in %APPDATA%\Evrakci\hatirlanan.dat, encrypted for the current Windows user (DPAPI).
/// </summary>
public sealed class RememberedValues
{
    sealed class Entry
    {
        public string Raw { get; set; } = "";
        public bool Blank { get; set; }
        public string? Currency { get; set; }
    }

    static readonly byte[] Entropy = "Evrakçı hatırlanan değerler"u8.ToArray();
    static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Evrakci", "hatirlanan.dat");

    readonly Dictionary<string, Entry> _values;

    RememberedValues(Dictionary<string, Entry> values) => _values = values;

    public static RememberedValues Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
                var map = JsonSerializer.Deserialize<Dictionary<string, Entry>>(plain);
                if (map is not null) return new RememberedValues(map);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or CryptographicException or UnauthorizedAccessException) { }
        return new RememberedValues(new());
    }

    public int Count => _values.Count;

    public bool Has(string name) => _values.ContainsKey(Tr.Key(name));

    public FieldInput? Get(string name) =>
        _values.TryGetValue(Tr.Key(name), out var e) ? new FieldInput { Raw = e.Raw, Blank = e.Blank, Currency = e.Currency } : null;

    public void Set(string name, FieldInput input)
    {
        _values[Tr.Key(name)] = new Entry { Raw = input.Raw, Blank = input.Blank, Currency = input.Currency };
        Save();
    }

    public void Remove(string name)
    {
        if (_values.Remove(Tr.Key(name))) Save();
    }

    public void Clear()
    {
        _values.Clear();
        Save();
    }

    void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var bytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(_values), Entropy, DataProtectionScope.CurrentUser);
            var tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException) { }
    }
}
