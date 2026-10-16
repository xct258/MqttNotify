# MqttNotify

对接 [push-server](https://github.com/xct258/push-server) 下行提醒的多端客户端合集。

push-server 把待办提醒通过 MQTT 下行发布，这里的客户端常驻后台订阅该主题，**
收到即弹系统通知**。本仓库同时收录 Android 与 Windows 两个端——它们共用同一套协议、
同一份解析契约，因此放在一起维护，跨端改动可以一次提交完成。

## 仓库结构

```
.
├── android-client/      Android 客户端（Kotlin + Jetpack Compose）
├── windows-client/      Windows 客户端（C# + WPF，.NET 8）
└── dist/                预编译产物（刻意提交，方便直接取用，见下）
```

两端各自的说明与验收清单在各自目录下：

| 目录 | 说明文档 | 测试/验收 |
| --- | --- | --- |
| `android-client/` | [README](android-client/README.md) | [TESTING](android-client/TESTING.md) |
| `windows-client/` | [README](windows-client/README.md) | [TESTING](windows-client/TESTING.md) |

## 共享的协议契约

这是两端必须保持一致的部分，任何一端改动都要同步另一端。

| 项 | 约定 |
| --- | --- |
| 端口 | MQTT over TCP `1883`、MQTT over WebSocket `9001`、FastAPI REST `8383` |
| 下行订阅 | `{base}/#`，`base` 默认 `taskops/todo` |
| task_id | 主题的最后一段（尾段） |
| payload | 明文 `【待办提醒】{title}\n{message}` |
| 订阅参数 | QoS 1、`retained = false`、clean session、KeepAlive 60s、匿名（无账号密码） |

解析口径以两端的 `MqttMessageParser` 为准（Android 为 `.kt`，Windows 为 `.cs`），
两边是同名类、同名测试用例，改一处必须改另一处。

> 注意 push-server 自带的 Mosquitto 把 TCP `1883` 绑在 `127.0.0.1`（局域网访问不到），
> 而 WebSocket `9001` 绑在 `0.0.0.0`。所以**局域网直连默认走 WebSocket 9001**，
> 具体见 `windows-client/README.md` §2.3。

## 预编译产物

`dist/` 下的文件是从本仓库源码构建出来的，**为了能直接取用而刻意提交进仓库**
（不随 `git clone` 之外的机制分发）：

| 文件 | 大小 | 说明 |
| --- | --- | --- |
| `dist/MqttNotify-Android-debug.apk` | 约 19 MB | debug 签名，可直接侧载安装 |
| `dist/MqttNotify-Windows-x64.exe` | 约 69 MB | 自包含单文件，**免装 .NET 运行时**，双击即用 |

产物是**快照**：源码更新后需要重新构建才会同步。不希望仓库里带大二进制的话，
删掉 `dist/` 并改走 GitHub Releases 挂附件即可。

## 从源码构建

**Windows**（需要 Windows；WPF 无法在 Linux/macOS 上运行）：

```bash
cd windows-client
dotnet build MqttNotify.sln -c Release
dotnet test tests/MqttNotify.Core.Tests -c Release
```

**Android**（`gradle` 用 8.9；仓库未提交 `gradlew`）：

```bash
cd android-client
gradle :app:assembleDebug
```

> 在 **aarch64** 主机上构建安卓时，Google 分发的 `aapt2` 只有 x86-64 版，会报
> `Exec format error`，需要用发行版的原生 aapt2 覆盖：
> `gradle :app:assembleDebug -Pandroid.aapt2FromMavenOverride=/usr/bin/aapt2`。
> 详见 `android-client/README.md` §4.5。

## 已知限制

- Windows 端需要 Windows 才能运行（WPF / 托盘通知 / 自启动注册表项均未在跨平台环境验证过）。
- 首版范围只含：常驻连接 + 系统通知 + 设置页。**不含**离线 REST 拉取、历史消息列表。
- 下发提醒依赖 push-server 与移动/桌面端的中间可达性；局域网内请用 WebSocket 9001。
