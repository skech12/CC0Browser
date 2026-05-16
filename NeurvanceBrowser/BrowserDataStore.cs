using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NeurvanceBrowser;

internal static class BrowserDataStore
{
    private static readonly byte[] Magic = "NVB1"u8.ToArray();
    private static readonly byte[] Entropy = "NeurvanceBrowser.v1"u8.ToArray();

    public static bool TryLoad(string path, JsonSerializerOptions opts, out MainWindow.BrowserData data)
    {
        data = new MainWindow.BrowserData();

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < Magic.Length)
            {
                return false;
            }

            for (var i = 0; i < Magic.Length; i++)
            {
                if (bytes[i] != Magic[i])
                {
                    return false;
                }
            }

            var ciphertext = new byte[bytes.Length - Magic.Length];
            Buffer.BlockCopy(bytes, Magic.Length, ciphertext, 0, ciphertext.Length);

            var plaintext = ProtectedData.Unprotect(ciphertext, Entropy, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plaintext);
            var loaded = JsonSerializer.Deserialize<MainWindow.BrowserData>(json, opts);
            if (loaded is null)
            {
                return false;
            }

            loaded.History ??= [];
            loaded.Bookmarks ??= [];
            loaded.Settings ??= new MainWindow.AppSettings();
            data = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Save(string path, MainWindow.BrowserData data, JsonSerializerOptions opts)
    {
        var json = JsonSerializer.Serialize(data, opts);
        var plaintext = Encoding.UTF8.GetBytes(json);
        var ciphertext = ProtectedData.Protect(plaintext, Entropy, DataProtectionScope.CurrentUser);

        var output = new byte[Magic.Length + ciphertext.Length];
        Buffer.BlockCopy(Magic, 0, output, 0, Magic.Length);
        Buffer.BlockCopy(ciphertext, 0, output, Magic.Length, ciphertext.Length);

        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, output);
        File.Move(tmp, path, overwrite: true);
    }

    public static bool TryLoadLegacyJson(string path, JsonSerializerOptions opts, out MainWindow.BrowserData data)
    {
        data = new MainWindow.BrowserData();

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<MainWindow.BrowserData>(json, opts);
            if (loaded is null)
            {
                return false;
            }

            loaded.History ??= [];
            loaded.Bookmarks ??= [];
            loaded.Settings ??= new MainWindow.AppSettings();
            data = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
