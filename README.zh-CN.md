# D4C3 文件加密

中文 | [English](README.md)

D4C3 文件加密是一款本地离线 Windows 桌面工具。它可以把任意文件做 1 到 3 层加密，每层使用独立密码和独立算法，并在解密时恢复原文件名、原扩展名和原始内容。

## 下载

请从 GitHub Releases 下载最新版本：

- 下载文件：`D4C3Jiami-WPF.zip`
- 解压后运行：`D4C3Jiami.exe`

本地开发构建会生成到：

```text
dist/D4C3Jiami-WPF/
```

## 功能

- 本地离线加密任意文件。
- 可选择 1、2 或 3 层加密。
- 每一层都可以设置独立密码。
- 每一层都可以选择不同算法。
- 解密时恢复原文件名、原扩展名和原始内容。
- 加密文件可输出为 `.enc`、`.jpg`、`.txt` 或 `.mp4` 扩展名。

## 支持的算法

- AES-256-GCM
- ChaCha20-Poly1305
- AES-256-CBC + HMAC-SHA256

第一版使用 `PBKDF2-HMAC-SHA256` 从密码派生密钥。加密包会保存解密所需的盐、随机数、层顺序和算法参数，但不会保存明文密码。

## 使用方法

### 加密文件

1. 打开 `D4C3Jiami.exe`。
2. 进入“加密”页。
3. 拖入文件，或点击“选择文件”。
4. 选择 1 到 3 层加密。
5. 为每一层选择算法并输入密码。
6. 选择输出扩展名。
7. 点击“生成加密文件”。

生成的加密文件会保存在原文件同一目录下。如果同名文件已存在，软件会自动追加编号。

### 解密文件

1. 进入“解密还原”页。
2. 拖入加密文件，或点击“选择加密文件”。
3. 软件会读取文件头并显示原文件信息。
4. 按加密时的层顺序输入每一层密码。
5. 点击“还原文件”。

还原后的文件会输出到加密文件所在目录下的 `还原文件` 文件夹中。

## 注意事项

- 请妥善保存密码。软件无法找回忘记的密码。
- `.jpg`、`.txt`、`.mp4` 输出选项只是伪装扩展名，不代表它们是真正的图片、文本或视频文件。
- 不要手动修改加密文件内容，否则可能无法解密。
- 处理重要文件前，建议先用小文件完整测试一次加密和解密流程。
- 重要文件建议保留一份独立备份。

## 项目结构

```text
src/D4C3Jiami.Core         核心加密流程和加密包格式
src/D4C3Jiami.Wpf          当前推荐使用的 WPF 桌面应用
src/D4C3Jiami.App          旧 WinUI 版本源码，仅保留参考
tests/D4C3Jiami.Core.Tests 核心加密测试
```

## 构建和测试

当前工作区包含本地 `.dotnet8` SDK 和本地 NuGet 缓存，可用于没有全局 .NET SDK 的机器。

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

## 发布包

生成 Windows x64 发布包：

```powershell
.\.dotnet8\dotnet.exe publish .\src\D4C3Jiami.Wpf\D4C3Jiami.Wpf.csproj --no-restore -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:Platform=x64 -o .\dist\D4C3Jiami-WPF
Compress-Archive -Path .\dist\D4C3Jiami-WPF\* -DestinationPath .\dist\D4C3Jiami-WPF.zip -Force
```

## 许可证

暂未选择许可证。
