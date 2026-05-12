# D4C3 Document Security

[Chinese](README.zh-CN.md) | English

D4C3 Document Security is a local Windows desktop app for encrypting files in multiple layers. It can encrypt any file with 1 to 3 independent layers, and each layer can use its own password and algorithm. During decryption, the app restores the original file name, extension, and bytes.

## Download

Use the latest package from GitHub Releases:

- Release asset: `D4C3Jiami-WPF.zip`
- Run after unzip: `D4C3Jiami.exe`

Local development builds are generated under:

```text
dist/D4C3Jiami-WPF/
```

## Features

- Encrypt any local file offline.
- Use 1, 2, or 3 encryption layers.
- Set an independent password for each layer.
- Choose a different algorithm for each layer.
- Restore the original file name, extension, and content.
- Save encrypted output with `.enc`, `.jpg`, `.txt`, or `.mp4` extensions.

## Supported Algorithms

- AES-256-GCM
- ChaCha20-Poly1305
- AES-256-CBC + HMAC-SHA256

The first version uses `PBKDF2-HMAC-SHA256` for key derivation. The encrypted package stores the metadata needed for decryption, such as salts, nonces, layer order, and algorithm parameters. It does not store plaintext passwords.

## How to Use

### Encrypt a File

1. Open `D4C3Jiami.exe`.
2. Go to the Encrypt tab.
3. Drag a file into the app, or click Select File.
4. Choose 1 to 3 encryption layers.
5. Select the algorithm and enter the password for each layer.
6. Choose the output extension.
7. Click Generate Encrypted File.

The encrypted file is created in the same folder as the original file. If a file with the same name already exists, the app automatically adds a number to the output file name.

### Decrypt a File

1. Go to the Decrypt tab.
2. Drag in an encrypted file, or click Select Encrypted File.
3. The app reads the package header and displays the original file information.
4. Enter the layer passwords in the original encryption order.
5. Click Restore File.

Restored files are written to a `还原文件` folder next to the encrypted file.

## Important Notes

- Keep your passwords safe. The app cannot recover forgotten passwords.
- The `.jpg`, `.txt`, and `.mp4` output options only disguise the extension. They are not valid image, text, or video files.
- Do not edit encrypted files manually, or decryption may fail.
- Test the full encrypt/decrypt workflow with a small file before using the app for important files.
- Keep a separate backup of important files.

## Project Structure

```text
src/D4C3Jiami.Core        Core encryption pipeline and package format
src/D4C3Jiami.Wpf         Recommended WPF desktop app
src/D4C3Jiami.App         Older WinUI app source kept for reference
tests/D4C3Jiami.Core.Tests Core encryption tests
```

## Build and Test

This workspace includes a local `.dotnet8` SDK and local NuGet cache for machines without a global .NET SDK.

```powershell
$env:DOTNET_CLI_HOME=(Join-Path (Get-Location) '.dotnet-home')
$env:APPDATA=(Join-Path (Get-Location) '.appdata')
$env:LOCALAPPDATA=(Join-Path (Get-Location) '.localappdata')
$env:NUGET_PACKAGES=(Join-Path (Get-Location) '.nuget-packages')
$env:NUGET_HTTP_CACHE_PATH=(Join-Path (Get-Location) '.nuget-http-cache')
$env:NUGET_PLUGINS_CACHE_PATH=(Join-Path (Get-Location) '.nuget-plugins-cache')

.\.dotnet8\dotnet.exe run --no-restore --project .\tests\D4C3Jiami.Core.Tests\D4C3Jiami.Core.Tests.csproj
.\.dotnet8\dotnet.exe build .\src\D4C3Jiami.Wpf\D4C3Jiami.Wpf.csproj --no-restore -c Release -p:Platform=x64
```

## Release Package

To create the Windows x64 package:

```powershell
.\.dotnet8\dotnet.exe publish .\src\D4C3Jiami.Wpf\D4C3Jiami.Wpf.csproj --no-restore -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Platform=x64 -o .\dist\D4C3Jiami-WPF
Compress-Archive -Path .\dist\D4C3Jiami-WPF\* -DestinationPath .\dist\D4C3Jiami-WPF.zip -Force
```

## License

No license has been selected yet.
