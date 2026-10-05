# Yiqth 音效器

Windows 桌面音效板：把真实麦克风、音效和音乐实时混音，通过 WASAPI 输出到已安装的 Voicemeeter。游戏、Discord、OBS 等软件使用 Voicemeeter Out B1 接收混音。

## 运行

从源码发布后，打开 `dist/Yiqth-音效器-win-x64/Yiqth 音效器.exe`。发布产物为 Windows x64 自包含版本，无需单独安装 .NET 运行时。

1. 打开 Voicemeeter，然后启动 Yiqth 音效器。
2. 选择真实麦克风，将“混音输出到”设为 `Voicemeeter Input (VB-Audio Voicemeeter VAIO)`。
3. 在 Voicemeeter 的对应虚拟输入通道打开 **B1**。
4. 在游戏或语音软件中把麦克风设为 **Voicemeeter Out B1**。
5. 添加本地音效或音乐，点击播放。

不要把 Voicemeeter Out B1 再选作本程序的真实麦克风，以免声音回授。若不想监听自己的声音，可关闭本程序的 Mic Monitor 及 Voicemeeter 对应输入通道的 A1。

## 功能

- 深色界面，蓝底白字下拉框，独立程序图标。
- WAV、MP3、FLAC、OGG、M4A 音效导入、预加载、多实例播放、停止、音量、编辑和删除。
- 全局快捷键、重复及冲突提示、Stop All。
- 音乐流式播放、暂停、停止、切歌、进度、单曲与列表循环。
- Mic / Soundboard / Music / Master 音量、静音、电平与软限幅。
- 麦克风设备选择、监听、设备通知刷新；请求混音周期可选 5/10/20/40 ms。
- 可选本地 WAV 录制、缓冲诊断、配置保存和日志。

配置继续保存在 `%AppData%/GameSoundboard/config.json`，保留此前的设备、音效、快捷键和窗口设置。日志位于 `%AppData%/GameSoundboard/logs/`，不记录实际音频内容。

## 工程和环境

| 路径 | 用途 |
| --- | --- |
| `GameSoundboard.sln` | 当前 .NET 解决方案 |
| `src/GameSoundboard.App/` | WPF 界面、全局快捷键、设备控制；输出程序名为 Yiqth 音效器 |
| `src/GameSoundboard.Audio/` | WASAPI、解码、重采样、混音、限幅、Voicemeeter 输出 |
| `src/GameSoundboard.Core/` | 模型、契约、配置、日志 |
| `tests/Core.Tests/`、`tests/Audio.Tests/` | 配置与音频逻辑测试 |
| `.dotnet/`、`.packages/` | 本地 SDK 与 NuGet 依赖缓存，不纳入 Git |
| `.appdata/`、`.localappdata/`、`.tools/` | 本地构建和检查工具，不纳入 Git |
| `scripts/build-app-icon.ps1` | 将图标 PNG 转换为多尺寸 ICO |
| `dist/Yiqth-音效器-win-x64/` | 本地发布产物，不纳入 Git |

目标平台为 Windows x64，项目使用 `net10.0-windows10.0.19041.0`、NAudio.Wasapi 3.1.0 和 NAudio.Vorbis 3.0.0。构建需要 .NET 10 SDK。内部项目命名维持 GameSoundboard，避免影响现有配置和构建路径。

图标由内置 imagegen 生成：深蓝圆角底，青蓝色麦克风与递增音量柱，小尺寸清晰、无文字。PNG 和 ICO 保存在 `src/GameSoundboard.App/Assets/`。

## 构建与测试

在安装 .NET 10 SDK 的 Windows 环境中，于项目根目录运行 PowerShell：

```powershell
dotnet restore '.\GameSoundboard.sln' --configfile '.\NuGet.Config'
dotnet build '.\GameSoundboard.sln' --no-restore --configuration Debug --nologo
dotnet test '.\GameSoundboard.sln' --no-restore --configuration Debug --nologo
```

首次恢复依赖需要联网。本地离线构建可按需使用缓存的 SDK 和依赖，并为 restore 添加 `-p:NuGetAudit=false`。

## 发布

在同一项目目录运行：

```powershell
dotnet restore '.\src\GameSoundboard.App\GameSoundboard.App.csproj' --runtime win-x64 --configfile '.\NuGet.Config'
dotnet publish '.\src\GameSoundboard.App\GameSoundboard.App.csproj' --configuration Release --runtime win-x64 --self-contained true --no-restore --output '.\dist\Yiqth-音效器-win-x64' -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

## 已验证与待验证

2026-10-05：工程清理后重新恢复依赖、从头构建和发布成功；Debug 构建 0 警告、0 错误，Core 1 项与 Audio 14 项测试全部通过。发布文件为上方列出的当前 EXE。

本机曾验证五种格式解码、真实麦克风与音效同时进入 WAV、Voicemeeter Input 连接、程序启动及蓝色控件显示。4 个下拉框的展开/收起通过 Windows UI Automation 检查。

最近的长运行探针停止于 35 分钟：210,008 个混音周期、12 次迟到、3 次输入欠载、4 次输入过载，麦克风仍在采集，错误为 none。完整一小时稳定性、实际热插拔/睡眠恢复、游戏与语音软件通话效果及 Windows 10/11 多机兼容性仍需实测。

后续实现以 [当前架构](docs/architecture.md) 和本 README 为准。
