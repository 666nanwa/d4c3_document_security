using D4C3Jiami.Core.Crypto;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace D4C3Jiami.App;

public sealed partial class MainWindow : Window
{
    private readonly CryptoPipeline _pipeline = new();
    private string? _encryptFilePath;
    private string? _decryptFilePath;
    private EncryptedPackageHeader? _decryptHeader;

    public MainWindow()
    {
        InitializeComponent();
        PopulateAlgorithmBoxes();
        UpdateLayerPanels();
    }

    private async void PickEncryptFile_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickSingleFileAsync();
        if (file is not null)
        {
            SetEncryptFile(file.Path);
        }
    }

    private async void PickDecryptFile_Click(object sender, RoutedEventArgs e)
    {
        var file = await PickSingleFileAsync();
        if (file is not null)
        {
            await SetDecryptFileAsync(file.Path);
        }
    }

    private void LayerCountBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        UpdateLayerPanels();
    }

    private async void Encrypt_Click(object sender, RoutedEventArgs e)
    {
        if (_encryptFilePath is null)
        {
            ShowInfo(EncryptInfoBar, InfoBarSeverity.Warning, "请先选择要加密的文件。");
            return;
        }

        try
        {
            var layers = GetEncryptionLayers();
            var extension = GetSelectedExtension();
            var inputDirectory = Path.GetDirectoryName(_encryptFilePath)!;
            var outputName = Path.GetFileNameWithoutExtension(_encryptFilePath) + extension;
            var outputPath = GetAvailablePath(Path.Combine(inputDirectory, outputName));

            ShowInfo(EncryptInfoBar, InfoBarSeverity.Informational, "正在加密，请稍候。");
            await _pipeline.EncryptFileAsync(_encryptFilePath, outputPath, layers);

            ShowInfo(EncryptInfoBar, InfoBarSeverity.Success, $"已生成：{outputPath}");
        }
        catch (Exception ex)
        {
            ShowInfo(EncryptInfoBar, InfoBarSeverity.Error, ToUserMessage(ex));
        }
    }

    private async void Decrypt_Click(object sender, RoutedEventArgs e)
    {
        if (_decryptFilePath is null || _decryptHeader is null)
        {
            ShowInfo(DecryptInfoBar, InfoBarSeverity.Warning, "请先选择要解密的文件。");
            return;
        }

        try
        {
            var passwords = GetDecryptionPasswords(_decryptHeader.Layers.Count);
            var outputDirectory = Path.Combine(Path.GetDirectoryName(_decryptFilePath)!, "还原文件");

            ShowInfo(DecryptInfoBar, InfoBarSeverity.Informational, "正在解密，请稍候。");
            var result = await _pipeline.DecryptFileAsync(_decryptFilePath, outputDirectory, passwords);

            ShowInfo(DecryptInfoBar, InfoBarSeverity.Success, $"已还原：{result.OutputFilePath}");
        }
        catch (Exception ex)
        {
            ShowInfo(DecryptInfoBar, InfoBarSeverity.Error, ToUserMessage(ex));
        }
    }

    private void EncryptDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void EncryptDropZone_Drop(object sender, DragEventArgs e)
    {
        var path = await GetDroppedFilePathAsync(e);
        if (path is not null)
        {
            SetEncryptFile(path);
        }
    }

    private void DecryptDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
    }

    private async void DecryptDropZone_Drop(object sender, DragEventArgs e)
    {
        var path = await GetDroppedFilePathAsync(e);
        if (path is not null)
        {
            await SetDecryptFileAsync(path);
        }
    }

    private async Task<StorageFile?> PickSingleFileAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");

        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        return await picker.PickSingleFileAsync();
    }

    private static async Task<string?> GetDroppedFilePathAsync(DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return null;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        return items.OfType<StorageFile>().FirstOrDefault()?.Path;
    }

    private void SetEncryptFile(string path)
    {
        _encryptFilePath = path;
        var info = new FileInfo(path);
        EncryptFileText.Text = $"{info.Name}  ({FormatBytes(info.Length)})";
        ShowInfo(EncryptInfoBar, InfoBarSeverity.Informational, "文件已选择，可以设置加密层。");
    }

    private async Task SetDecryptFileAsync(string path)
    {
        _decryptFilePath = path;
        DecryptFileText.Text = Path.GetFileName(path);

        try
        {
            _decryptHeader = await _pipeline.InspectHeaderAsync(path);
            DecryptHeaderText.Text =
                $"原文件：{_decryptHeader.OriginalFileName}\n" +
                $"大小：{FormatBytes(_decryptHeader.OriginalLength)}\n" +
                $"层数：{_decryptHeader.Layers.Count}\n" +
                $"算法：{string.Join(" -> ", _decryptHeader.Layers.Select(l => GetAlgorithmDisplayName(l.Algorithm)))}";

            UpdateDecryptPasswordBoxes(_decryptHeader.Layers.Count);
            ShowInfo(DecryptInfoBar, InfoBarSeverity.Informational, "文件信息读取成功，请输入每层密码。");
        }
        catch (Exception ex)
        {
            _decryptHeader = null;
            DecryptHeaderText.Text = "无法读取文件信息。";
            ShowInfo(DecryptInfoBar, InfoBarSeverity.Error, ToUserMessage(ex));
        }
    }

    private IReadOnlyList<EncryptionLayerConfig> GetEncryptionLayers()
    {
        var layerCount = GetLayerCount();
        var configs = new List<EncryptionLayerConfig>
        {
            new(GetSelectedAlgorithm(Layer1AlgorithmBox), Layer1PasswordBox.Password)
        };

        if (layerCount >= 2)
        {
            configs.Add(new EncryptionLayerConfig(GetSelectedAlgorithm(Layer2AlgorithmBox), Layer2PasswordBox.Password));
        }

        if (layerCount >= 3)
        {
            configs.Add(new EncryptionLayerConfig(GetSelectedAlgorithm(Layer3AlgorithmBox), Layer3PasswordBox.Password));
        }

        return configs;
    }

    private IReadOnlyList<string> GetDecryptionPasswords(int layerCount)
    {
        var passwords = new List<string> { DecryptLayer1PasswordBox.Password };
        if (layerCount >= 2)
        {
            passwords.Add(DecryptLayer2PasswordBox.Password);
        }

        if (layerCount >= 3)
        {
            passwords.Add(DecryptLayer3PasswordBox.Password);
        }

        if (passwords.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("每一层密码都不能为空。");
        }

        return passwords;
    }

    private int GetLayerCount()
    {
        if (double.IsNaN(LayerCountBox.Value))
        {
            return 1;
        }

        return Math.Clamp((int)LayerCountBox.Value, 1, CryptoPipeline.MaxLayerCount);
    }

    private void UpdateLayerPanels()
    {
        var layerCount = GetLayerCount();
        Layer2Panel.Visibility = layerCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
        Layer3Panel.Visibility = layerCount >= 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateDecryptPasswordBoxes(int layerCount)
    {
        DecryptLayer1PasswordBox.Visibility = Visibility.Visible;
        DecryptLayer2PasswordBox.Visibility = layerCount >= 2 ? Visibility.Visible : Visibility.Collapsed;
        DecryptLayer3PasswordBox.Visibility = layerCount >= 3 ? Visibility.Visible : Visibility.Collapsed;
        DecryptLayer1PasswordBox.Password = string.Empty;
        DecryptLayer2PasswordBox.Password = string.Empty;
        DecryptLayer3PasswordBox.Password = string.Empty;
    }

    private void PopulateAlgorithmBoxes()
    {
        var items = new[]
        {
            new AlgorithmItem("AES-256-GCM", JiamiAlgorithm.Aes256Gcm),
            new AlgorithmItem("ChaCha20-Poly1305", JiamiAlgorithm.ChaCha20Poly1305),
            new AlgorithmItem("AES-256-CBC + HMAC-SHA256", JiamiAlgorithm.Aes256CbcHmacSha256)
        };

        foreach (var box in new[] { Layer1AlgorithmBox, Layer2AlgorithmBox, Layer3AlgorithmBox })
        {
            box.ItemsSource = items;
            box.DisplayMemberPath = nameof(AlgorithmItem.Name);
            box.SelectedValuePath = nameof(AlgorithmItem.Algorithm);
        }

        Layer1AlgorithmBox.SelectedIndex = 0;
        Layer2AlgorithmBox.SelectedIndex = 1;
        Layer3AlgorithmBox.SelectedIndex = 2;
    }

    private static JiamiAlgorithm GetSelectedAlgorithm(ComboBox box)
    {
        return box.SelectedItem is AlgorithmItem item ? item.Algorithm : JiamiAlgorithm.Aes256Gcm;
    }

    private string GetSelectedExtension()
    {
        return OutputExtensionBox.SelectedItem is ComboBoxItem item &&
               item.Content is string extension
            ? extension
            : ".enc";
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

    private static string ToUserMessage(Exception ex)
    {
        return ex switch
        {
            FileNotFoundException => "找不到文件，请重新选择。",
            UnauthorizedAccessException => "没有权限访问这个位置，请换一个文件夹或以合适权限运行。",
            InvalidPackageException or PasswordVerificationException or ArgumentException => ex.Message,
            IOException ioEx => $"文件读写失败：{ioEx.Message}",
            _ => $"操作失败：{ex.Message}"
        };
    }

    private static void ShowInfo(InfoBar infoBar, InfoBarSeverity severity, string message)
    {
        infoBar.Severity = severity;
        infoBar.Title = severity switch
        {
            InfoBarSeverity.Success => "完成",
            InfoBarSeverity.Warning => "需要处理",
            InfoBarSeverity.Error => "出错了",
            _ => "提示"
        };
        infoBar.Message = message;
        infoBar.IsOpen = true;
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

    private sealed record AlgorithmItem(string Name, JiamiAlgorithm Algorithm);
}
