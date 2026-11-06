# MqttNotify

对接 [push-server](https://github.com/xct258/push-server) 下行提醒的多端客户端合集。

push-server 把待办提醒 / 服务通知通过 MQTT 下行发布，这里的客户端常驻后台订阅该主题，**
收到即弹系统通知**。本仓库同时收录 Android 与 Windows 两个端——它们共用同一套协议、
同一份解析契约，因此放在一起维护，跨端改动可以一次提交完成。

## 仓库结构

```
.
├── android-client/      Android 客户端（Kotlin + Jetpack Compose）
├── windows-client/      Windows 客户端（C# + WPF，.NET 10）
└── .github/workflows/   GitHub Actions：打 tag 后自动构建并发布 Release
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
| 端口 | MQTT over TCP `1883`、MQTT over WebSocket `9001`、WSS `443`、FastAPI REST `8383` |
| 下行订阅 | `{base}/#`，`base` 默认 `taskops/todo` |
| task_id | 主题的最后一段（尾段） |
| 消息分类 | push-server API 必填 `category`：`service`（服务通知）/ `todo`（待办提醒）；以后可扩展新值 |
| payload | `todo`：`【待办提醒】{title}\n{message}`；`service`：`【服务通知】{server}\n【服务】{title}\n【状态】{status}\n\n{message}` |
| 订阅参数 | QoS 1、`retained = false`、KeepAlive 60s、匿名（无账号密码）；Android 使用持久会话（`cleanSession=false`）以接收离线队列，Windows 仍为 clean session |
| 在线状态 | 连接后向 `$presence/clients/{client_id}` 发布 retained JSON；60s QoS1 心跳。Android 不用 Will（异常断线靠服务端 TTL，默认 180s，主动停止实时上报离线），Windows 仍用 Will 标记离线 |
| Client ID | `{自定义前缀或 windows/android}_{安装期固定随机 8 位}`；后缀是设备唯一标识，清空数据/重装后才会变化 |

解析口径以两端的 `MqttMessageParser` 为准（Android 为 `.kt`，Windows 为 `.cs`），
两边是同名类、同名测试用例，改一处必须改另一处。

> 注意 push-server 自带的 Mosquitto 把 TCP `1883` 绑在 `127.0.0.1`（局域网访问不到），
> 而 WebSocket `9001` 绑在 `0.0.0.0`。**两端客户端现在默认都走公网 WSS
> `wss://push-server.o1-1.xct258.top/mqtt`**，局域网内可以手工切回 WebSocket 9001。

## 下载与发布

正式版本走 **GitHub Releases**，不再把二进制提交进仓库：

https://github.com/xct258/MqttNotify/releases

发布由 `.github/workflows/release.yml` 自动完成：

- 推送 `v*` tag（例如 `v1.0.0`），或手动运行 Actions 的 **Release** workflow
- 自动构建并上传：
  - `MqttNotify-Windows-x64.exe`：依赖框架单文件，需装 .NET 10 Desktop Runtime
  - `MqttNotify-Windows-x64-self-contained.exe`：自包含，免装运行时，体积较大
  - `MqttNotify-Android-debug.apk`：debug 签名，可直接侧载
  - `SHA256SUMS.txt`：文件校验和

本地构建产物请放 `dist/` 或 `artifacts/`，两者都已加入 `.gitignore`。

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
- 首版范围：常驻连接 + 系统通知 + 设置页 + 最近提醒（本地落盘）；**不含**离线 REST 拉取。
- Android 端已叠加网络切换重连、15 分钟看门狗、持久会话和 Doze 白名单引导；
  但用户在系统设置里「强行停止」应用后，系统会清除闹钟，任何第三方应用都无法自恢复。
- 下发提醒依赖 push-server 与移动/桌面端的中间可达性；局域网内请用 WebSocket 9001。
