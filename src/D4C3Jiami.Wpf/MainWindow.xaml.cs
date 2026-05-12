using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using D4C3Jiami.Core.Crypto;
using Microsoft.Win32;

namespace D4C3Jiami.Wpf;

public partial class MainWindow : Window
{
    private readonly CryptoPipeline _pipeline = new();
    private readonly IReadOnlyList<AlgorithmOption> _algorithms =
    [
        new("AES-256-GCM", JiamiAlgorithm.Aes256Gcm),
        new("ChaCha20-Poly1305", JiamiAlgorithm.ChaCha20Poly1305),
        new("AES-256-CBC + HMAC-SHA256", JiamiAlgorithm.Aes256CbcHmacSha256)
    ];

    private string? _encryptFilePath;
    private string? _decryptFilePath;
    private EncryptedPackageHeader? _decryptHeader;

    public MainWindow()
    {
        InitializeComponent();
        InitializeOptions();
        UpdateLayerPanels();
        UpdateDecryptPasswordPanels(1);
    }

    private void InitializeOptions()
    {
        LayerCountCombo.ItemsSource = new[] { 1, 2, 3 };
        LayerCountCombo.SelectedIndex = 0;

        OutputExtensionCombo.ItemsSource = new[] { ".enc", ".jpg", ".txt", ".mp4" };
        OutputExtensionCombo.SelectedIndex = 0;

        foreach (var combo in new[] { Layer1AlgorithmCombo, Layer2AlgorithmCombo, Layer3AlgorithmCombo })
        {
            combo.ItemsSource = _algorithms;
            combo.DisplayMemberPath = nameof(AlgorithmOption.Name);
            combo.SelectedValuePath = nameof(AlgorithmOption.Algorithm);
        }

        Layer1AlgorithmCombo.SelectedIndex = 0;
        Layer2AlgorithmCombo.SelectedIndex = 1;
        Layer3AlgorithmCombo.SelectedIndex = 2;
    }

    private void PickEncryptFile_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFile();
        if (path is not null)
        {
            SetEncryptFile(path);
        }
    }

    private async void PickDecryptFile_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFile();
        if (path is not null)
        {
            await SetDecryptFileAsync(path);
        }
    }

    private void LayerCountCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateLayerPanels();
    }

    private async void Encrypt_Click(object sender, RoutedEventArgs e)
    {
        if (_encryptFilePath is null)
        {
            SetEncryptStatus("请先选择要加密的文件。", isError: true);
            return;
        }

        try
        {
            var layers = GetEncryptionLayers();
            var outputPath = BuildEncryptOutputPath(_encryptFilePath);

            SetBusy(isBusy: true);
            SetEncryptStatus("正在加密，请稍候...", isError: false);
            await _pipeline.EncryptFileAsync(_encryptFilePath, outputPath, layers);
            SetEncryptStatus($"已生成：{outputPath}", isError: false);
        }
        catch (Exception ex)
        {
            SetEncryptStatus(ToUserMessage(ex), isError: true);
        }
        finally
        {
            SetBusy(isBusy: false);
        }
    }

    private async void Decrypt_Click(object sender, RoutedEventArgs e)
    {
        if (_decryptFilePath is null || _decryptHeader is null)
        {
            SetDecryptStatus("请先选择要还原的加密文件。", isError: true);
            return;
        }

        try
        {
            var passwords = GetDecryptionPasswords(_decryptHeader.Layers.Count);
            var outputDirectory = Path.Combine(Path.GetDirectoryName(_decryptFilePath)!, "还原文件");

            SetBusy(isBusy: true);
            SetDecryptStatus("正在还原，请稍候...", isError: false);
            var result = await _pipeline.DecryptFileAsync(_decryptFilePath, outputDirectory, passwords);
            SetDecryptStatus($"已还原：{result.OutputFilePath}", isError: false);
        }
        catch (Exception ex)
        {
            SetDecryptStatus(ToUserMessage(ex), isError: true);
        }
        finally
        {
            SetBusy(isBusy: false);
        }
    }

    private void FileDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void EncryptDropZone_Drop(object sender, DragEventArgs e)
    {
        var path = GetDroppedFilePath(e);
        if (path is not null)
        {
            SetEncryptFile(path);
        }
    }

    private async void DecryptDropZone_Drop(object sender, DragEventArgs e)
    {
        var path = GetDroppedFilePath(e);
        if (path is not null)
        {
            await SetDecryptFileAsync(path);
        }
    }

    private static string? PickFile()
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Multiselect = false,
            Filter = "所有文件 (*.*)|*.*"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string? GetDroppedFilePath(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return null;
        }

        return (e.Data.GetData(DataFormats.FileDrop) as string[])
            ?.FirstOrDefault(File.Exists);
    }

    private void SetEncryptFile(string path)
    {
        if (!File.Exists(path))
        {
            SetEncryptStatus("请选择一个真实存在的文件。", isError: true);
            return;
        }

        _encryptFilePath = path;
        var info = new FileInfo(path);
        EncryptFileText.Text = $"{info.Name}  ({FormatBytes(info.Length)})";
        SetEncryptStatus("文件已选择，可以设置加密层。", isError: false);
    }

    private async Task SetDecryptFileAsync(string path)
    {
        if (!File.Exists(path))
        {
            SetDecryptStatus("请选择一个真实存在的文件。", isError: true);
            return;
        }

        _decryptFilePath = path;
        DecryptFileText.Text = Path.GetFileName(path);
        DecryptHeaderText.Text = "正在读取文件信息...";
        SetDecryptStatus(string.Empty, isError: false);

        try
        {
            _decryptHeader = await _pipeline.InspectHeaderAsync(path);
            DecryptHeaderText.Text =
                $"原文件：{_decryptHeader.OriginalFileName}\n" +
                $"大小：{FormatBytes(_decryptHeader.OriginalLength)}\n" +
                $"层数：{_decryptHeader.Layers.Count}\n" +
                $"算法：{string.Join(" -> ", _decryptHeader.Layers.Select(layer => GetAlgorithmDisplayName(layer.Algorithm)))}";

            UpdateDecryptPasswordPanels(_decryptHeader.Layers.Count);
            SetDecryptStatus("文件信息读取成功，请输入每层密码。", isError: false);
        }
        catch (Exception ex)
        {
            _decryptHeader = null;
            DecryptHeaderText.Text = "无法读取文件信息。";
            UpdateDecryptPasswordPanels(1);
            SetDecryptStatus(ToUserMessage(ex), isError: true);
        }
    }

    private IReadOnlyList<EncryptionLayerConfig> GetEncryptionLayers()
    {
        var layerCount = GetLayerCount();
        var layers = new List<EncryptionLayerConfig>
        {
            new(GetSelectedAlgorithm(Layer1AlgorithmCombo), GetRequiredPassword(Layer1PasswordBox, 1))
        };

        if (layerCount >= 2)
        {
            layers.Add(new EncryptionLayerConfig(GetSelectedAlgorithm(Layer2AlgorithmCombo), GetRequiredPassword(Layer2PasswordBox, 2)));
        }

        if (layerCount >= 3)
        {
            layers.Add(new EncryptionLayerConfig(GetSelectedAlgorithm(Layer3AlgorithmCombo), GetRequiredPassword(Layer3PasswordBox, 3)));
        }

        return layers;
    }

    private IReadOnlyList<string> GetDecryptionPasswords(int layerCount)
    {
        var passwords = new List<string> { GetRequiredPassword(DecryptLayer1PasswordBox, 1) };

        if (layerCount >= 2)
        {
            passwords.Add(GetRequiredPassword(DecryptLayer2PasswordBox, 2));
        }

        if (layerCount >= 3)
        {
            passwords.Add(GetRequiredPassword(DecryptLayer3PasswordBox, 3));
        }

        return passwords;
    }

    private int GetLayerCount()
    {
        return LayerCountCombo.SelectedItem is int count
            ? Math.Clamp(count, 1, CryptoPipeline.MaxLayerCount)
            : 1;
    }

    private void UpdateLayerPanels()
    {
        var layerCount = GetLayerCount();
        Layer2Panel.Visibility = layerCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
        Layer3Panel.Visibility = layerCount >= 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDecryptPasswordPanels(int layerCount)
    {
        DecryptLayer1Panel.Visibility = Visibility.Visible;
        DecryptLayer2Panel.Visibility = layerCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
        DecryptLayer3Panel.Visibility = layerCount >= 3 ? Visibility.Visible : Visibility.Collapsed;

        DecryptLayer1PasswordBox.Password = string.Empty;
        DecryptLayer2PasswordBox.Password = string.Empty;
        DecryptLayer3PasswordBox.Password = string.Empty;
    }

    private string BuildEncryptOutputPath(string inputPath)
    {
        var extension = OutputExtensionCombo.SelectedItem as string ?? ".enc";
        var inputDirectory = Path.GetDirectoryName(inputPath)!;
        var outputName = Path.GetFileNameWithoutExtension(inputPath) + extension;
        return GetAvailablePath(Path.Combine(inputDirectory, outputName));
    }

    private static string GetAvailablePath(string path)
    {
        if (!File.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);

        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("无法生成不重复的输出文件名。");
    }

    private static JiamiAlgorithm GetSelectedAlgorithm(ComboBox combo)
    {
        return combo.SelectedItem is AlgorithmOption item ? item.Algorithm : JiamiAlgorithm.Aes256Gcm;
    }

    private static string GetRequiredPassword(PasswordBox box, int layer)
    {
        if (string.IsNullOrWhiteSpace(box.Password))
        {
            throw new ArgumentException($"第 {layer} 层密码不能为空。");
        }

        return box.Password;
    }

    private void SetBusy(bool isBusy)
    {
        EncryptButton.IsEnabled = !isBusy;
        DecryptButton.IsEnabled = !isBusy;
        Cursor = isBusy ? Cursors.Wait : Cursors.Arrow;
    }

    private void SetEncryptStatus(string message, bool isError)
    {
        EncryptStatusText.Text = message;
        EncryptStatusText.Foreground = GetStatusBrush(isError);
    }

    private void SetDecryptStatus(string message, bool isError)
    {
        DecryptStatusText.Text = message;
        DecryptStatusText.Foreground = GetStatusBrush(isError);
    }

    private static Brush GetStatusBrush(bool isError)
    {
        return isError
            ? new SolidColorBrush(Color.FromRgb(180, 35, 24))
            : new SolidColorBrush(Color.FromRgb(18, 128, 92));
    }

    private static string ToUserMessage(Exception ex)
    {
        return ex switch
        {
            FileNotFoundException => "找不到文件，请重新选择。",
            UnauthorizedAccessException => "没有权限访问这个位置，请换一个文件夹或以合适权限运行。",
            PasswordVerificationException => "密码错误，或者加密文件已经损坏。",
            InvalidPackageException => "无法读取加密文件。它可能不是本软件生成的文件，或文件已经损坏。",
            ArgumentException => ex.Message,
            IOException ioEx => $"文件读写失败：{ioEx.Message}",
            _ => $"操作失败：{ex.Message}"
        };
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static string GetAlgorithmDisplayName(JiamiAlgorithm algorithm)
    {
        return algorithm switch
        {
            JiamiAlgorithm.Aes256Gcm => "AES-256-GCM",
            JiamiAlgorithm.ChaCha20Poly1305 => "ChaCha20-Poly1305",
            JiamiAlgorithm.Aes256CbcHmacSha256 => "AES-256-CBC + HMAC-SHA256",
            _ => algorithm.ToString()
        };
    }

    private sealed record AlgorithmOption(string Name, JiamiAlgorithm Algorithm);
}
