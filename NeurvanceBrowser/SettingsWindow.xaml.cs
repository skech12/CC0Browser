using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace NeurvanceBrowser;

public partial class SettingsWindow : Window
{
    public string ResultApiKey { get; private set; } = "";

    private readonly string? _dotEnvPath;
    private string _plainText = "";
    private bool _syncing;

    public SettingsWindow(string currentKey, string? dotEnvPath)
    {
        InitializeComponent();
        _dotEnvPath = dotEnvPath;
        _plainText = currentKey;
        ApiKeyBox.Password = currentKey;
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _plainText = ApiKeyBox.Password;
    }

    private void RevealCheck_Changed(object sender, RoutedEventArgs e)
    {
        _syncing = true;
        if (RevealCheck.IsChecked == true)
        {
            ApiKeyBox.Password = _plainText;
        }
        else
        {
            ApiKeyBox.Password = _plainText;
        }
        _syncing = false;
    }

    private void LoadFromEnv_Click(object sender, RoutedEventArgs e)
    {
        if (_dotEnvPath is null || !File.Exists(_dotEnvPath))
        {
            MessageBox.Show(
                "No .env file found next to rag.py.\n\nCreate a .env file with:\nCC0_CONTENT_API_KEY=your_key_here",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dict = MainWindow.ParseDotEnv(_dotEnvPath);
        if (!dict.TryGetValue("CC0_CONTENT_API_KEY", out var key) || string.IsNullOrEmpty(key))
        {
            MessageBox.Show(
                ".env was found but CC0_CONTENT_API_KEY is missing or empty.",
                "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _syncing = true;
        _plainText = key;
        ApiKeyBox.Password = key;
        _syncing = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ResultApiKey = _plainText;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
