# Yiqth 音效器架构

## 组件

- **App**：WPF 界面与 ViewModel、全局快捷键、设备选择和状态呈现；依赖 Core 与 Audio。
- **Core**：音频与设备契约、模型、配置持久化、异步日志；不依赖界面和 NAudio。
- **Audio**：WASAPI 输入/输出、文件解码、重采样、混音、软限幅、音量和电平；依赖 Core。
- **Core.Tests / Audio.Tests**：配置、缓冲、混音、播放与输出等逻辑验证。

## 音频路径

```text
真实麦克风 -> WASAPI Capture -> 格式转换/重采样 -> Mic Bus ┐
短音效 -> 后台解码/预加载 -> 多实例播放 -> Soundboard Bus ├
音乐 -> 后台流式解码/预取 -> Music Bus -------------------┘
  -> 各 Bus 音量 / 静音 / 电平
  -> 48 kHz、float32、Stereo 混音
  -> Master 音量 / 软限幅 / 电平
  -> RecordableOutputSink
       ├─ 可选异步 WAV 录制
       └─ WasapiRenderSink -> Voicemeeter Input
                              -> Voicemeeter B1
                              -> Voicemeeter Out B1
                              -> 游戏 / Discord / OBS
```

`IAudioOutputSink` 接收 48 kHz 双声道浮点采样。`WasapiRenderSink` 使用有界缓冲，将混音交给选定的 Windows 播放端点；设备打开在后台任务执行，混音写入在设备切换期间不等待资源锁。输出积压过多时清除陈旧音频，避免延迟持续增加。输出播放当前请求 40 ms；界面中的 5/10/20/40 ms 是混音与采集的请求周期，不是端到端延迟保证。

输入统一重采样到内部格式。Mic 缓冲容量为 1 秒；消费端在积压超过 100 ms 时保留约 50 ms 的最新音频。音效在后台解码后缓存，音乐使用流式预取。软限幅将 Master 输出保持在安全采样范围内。

## 线程和状态

- WPF 线程处理交互、刷新电平与诊断，快捷键通过窗口的 RegisterHotKey 消息触发播放。
- 麦克风采集与混音在独立的音频执行路径中工作。
- 文件解码、音乐预取、WAV 写盘和日志写盘不在 UI 或混音循环中执行。
- 音量、静音、音源和播放命令通过线程安全状态或有界队列传递。
- 设备选择使用稳定的 Windows endpoint ID，通知回调触发去抖刷新；麦克风失效时显示断开状态。

Mic Monitor 将麦克风送到默认扬声器，是可选的独立监听。使用 Voicemeeter 时，源麦克风应选择物理设备，避免将 B1 输出重新送回混音输入。

## 数据保存

设置位于 `%AppData%/GameSoundboard/config.json`，包括设备 ID、音量/静音、音效路径、快捷键、音乐列表、循环、缓冲和窗口尺寸。程序名改变后继续使用原目录，以保留用户配置。

日志位于 `%AppData%/GameSoundboard/logs/`，记录启动、设备、引擎、异常和缓冲统计，不记录实际音频内容。只有用户主动选择本地 WAV 录制时才保存混音音频。

## 验证边界

单元测试验证配置、混音、限幅、缓冲和播放逻辑；实际设备连接与程序界面另做本机检查。最终使用效果取决于 Voicemeeter 的通道与 B1 路由。游戏兼容性、完整一小时运行、热插拔与睡眠恢复需要真实环境验收。
