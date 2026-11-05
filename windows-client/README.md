# MQTT 提醒 · Windows 客户端

对接 [xct258/push-server](https://github.com/xct258/push-server) 的 Windows 提醒客户端：
常驻托盘，订阅服务端的 MQTT 下行主题，收到待办提醒就弹系统通知。

与手机端（同作者的 Android 版 `MqttNotify`）共用同一套协议、同一份解析契约，
两端对同一条 payload 的解析结果保证一致。

- **技术栈**：C# / .NET 10 / WPF（托盘图标用 WinForms 的 `NotifyIcon`）
- **MQTT 库**：MQTTnet 4.3.7（client 与 server 同包，便于测试起进程内 broker）
- **形态**：托盘常驻 + 主窗口设置页，可开机自启、可关闭即最小化到托盘

---

## 1. 协议约定

服务端（`push-server`）的 Mosquitto 监听（见其 `容器构建脚本/start.sh`）：

| 监听 | 地址 | 说明 |
| --- | --- | --- |
| TCP 1883 | `127.0.0.1` | **只绑本机**，跨机连不上（重要） |
| WebSocket 9001 | `0.0.0.0` | 局域网直连用 |
| WSS 443 | `push-server.o1-1.xct258.top` | 公网入口（TLS 终止后转发到 9001），**客户端默认地址** |
| FastAPI REST 8383 | `0.0.0.0` | 本版本未使用（离线补拉不在首版范围内） |

本客户端的行为：

- 订阅 `{base}/#`，`base` 默认 `taskops/todo` → 实际订阅 `taskops/todo/#`
- QoS 1、`clean session = true`、KeepAlive 60s、匿名认证、`retained = false`
- 主题最后一段是 `task_id`（解析不出数字就为 `null`，仅用于展示）
- payload 为纯文本：`【待办提醒】{title}\n{message}`；没有该前缀时整段当正文，标题用默认值
- 连接后向 `$presence/clients/{client_id}` 发布 retained 在线状态，60 秒心跳
  （QoS0、非 retained）；异常退出由 MQTT Will 标记离线，供 push-server 管理页展示

解析口径由 `MqttMessageParser` 唯一实现，且被两端共享的测试用例钉死（见 `TESTING.md`）。

---

## 2. 快速开始

### 2.1 运行

GitHub Releases 提供**依赖框架单文件**版（约 800 KB）和**自包含版**（约 72 MB）。
依赖框架版目标机器需要先安装
[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)（Windows x64）：

```bash
dotnet publish src/MqttNotify.Windows -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true \
  -o artifacts/win-x64
```

产物是 `artifacts/win-x64/MqttNotify.exe`（Windows x64），拷到目标机器双击即可运行。
首次启动请点托盘图标打开主窗口填写 Broker 地址，然后「保存并重连」。

如果目标机器不能装运行时，也可以改成自包含发布（体积大很多）：

```bash
dotnet publish src/MqttNotify.Windows -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -o artifacts/win-x64
```

三种发布方式的实测体积（本仓库的 Release 配置）：

| 方式 | 体积 | 目标机器要求 | 说明 |
| --- | --- | --- | --- |
| 依赖框架 | **约 800 KB** | 需装 .NET 10 桌面运行时 | **Release 默认提供**，体积最小 |
| 自包含 + 压缩 | 约 72 MB | 无 | **Release 同时提供**；启动时需解压，首启略慢 |
| 自包含 | 约 166 MB | 无 | 启动最快，体积最大 |

注意 WPF **不支持** `PublishTrimmed`，别指望靠裁剪把自包含体积压下去。

> 托盘图标与 exe 图标是同一个 `Assets/MqttNotify.ico`，会随程序一起拷到输出目录。

### 2.2 配置

配置写在 `%APPDATA%\MqttNotify\settings.json`（写入走「临时文件 + 原子替换」，崩溃不会写坏）。
也可以在界面里改，「保存并重连」会立即生效——不需要重启程序。

| 字段 | 默认值 | 说明 |
| --- | --- | --- |
| `configVersion` | `2` | 配置格式版本；版本不一致会直接重置为默认配置 |
| `host` | `push-server.o1-1.xct258.top` | Broker 主机名或 IP |
| `port` | `443` | 端口；切换传输方式时若仍是旧默认值会自动跟随 |
| `transport` | `Wss` | `Tcp` / `Ws` / `Wss`；默认公网 WSS |
| `wsPath` | `/mqtt` | WS/WSS 的路径；缺前导 `/` 会自动补 |
| `topicBase` | `taskops/todo` | 下行主题 base，实际订阅 `{base}/#` |
| `serverName` | `taskops` | 来源服务名（预留给后续 REST 补拉） |
| `clientId` | 空 | 自定义 ID 前缀；最终 ID 为 `{前缀}_{固定随机后缀}`，留空则为 `windows_xxxxxxxx` |
| `clientIdSuffix` | 首次启动生成 | 安装期固定随机后缀；清空配置/重装后才会变化 |
| `allowInsecureTls` | `false` | 仅 WSS：跳过证书链与主机名校验（内网自签证书用） |
| `autoStartOnBoot` | `true` | 写 `HKCU` 的 Run 项，不需要管理员权限 |

> 配置文件使用小驼峰字段名，不兼容旧配置格式（仍在开发中）；旧文件会被忽略并回落到默认值。
> `host` 只填主机名或 IP，不要带 `wss://`、端口或 `/mqtt` 路径；端口和路径分别由 `port`、`wsPath` 控制。
>
> `topicBase` 只填 base（如 `taskops/todo`）；也接受直接粘贴完整主题 `taskops/todo/#`，
> 不会重复拼成 `#/#`。填 `#` 表示订阅所有主题。

### 2.3 传输方式怎么选

| 传输 | 何时用 | 地址形如 |
| --- | --- | --- |
| **WebSocket + TLS** | **默认**。公网入口，任何网络环境都能用 | `wss://push-server.o1-1.xct258.top/mqtt` |
| WebSocket | 仅局域网直连 Mosquitto 的 9001 | `ws://192.168.1.10:9001/mqtt` |
| TCP | 仅在 broker 与本机同机、或你已把 1883 改绑 `0.0.0.0` 时可用 | `mqtt://192.168.1.10:1883` |

> **最容易踩的坑**：服务端默认的 TCP 1883 监听在 `127.0.0.1`，所以「直连 `mqtt://<内网IP>:1883`」
> 一定会连接失败。客户端默认值已经是公网 WSS，正常不会踩到；只有手工改成 TCP 才会。
> 也可以把服务端配置改成 `listener 1883 0.0.0.0`。

WSS 通常是 nginx 终止 TLS 之后再转发到 Mosquitto 的 WS 端口：

```nginx
server {
    listen 443 ssl;
    server_name notify.example.com;

    ssl_certificate     /etc/nginx/certs/notify.crt;
    ssl_certificate_key /etc/nginx/certs/notify.key;

    location /mqtt {
        proxy_pass http://127.0.0.1:9001;
        proxy_http_version 1.1;

        # WebSocket 升级必须转发的头，少了会 400
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;

        # MQTT 是长连接，别让 nginx 提前掐掉
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }
}
```

客户端默认配置就是：传输方式 `WebSocket + TLS`、地址 `push-server.o1-1.xct258.top`、
端口 `443`、WS 路径 `/mqtt`。自签证书场景再勾上「允许不安全 TLS」。

---

## 3. 工程结构

```
MqttNotify.sln
├── src/MqttNotify.Core/          平台无关核心库（net10.0，可在 Linux/macOS 上跑测试）
│   ├── AppSettings.cs            配置模型 + 订阅主题/路径规范化
│   ├── MqttTransport.cs          TCP / WS / WSS 及其默认端口、展示地址
│   ├── MqttMessageParser.cs      下行 payload 解析（与手机端同契约）
│   ├── MqttOptionsFactory.cs     AppSettings → MQTTnet 连接参数
│   ├── MqttConnectionService.cs  连接 / 订阅 / 断线重连 / 消息回调
│   ├── ConnectionState.cs        连接状态与提醒记录模型
│   └── SettingsStore.cs          JSON 持久化（原子写入 + 损坏降级）
├── src/MqttNotify.Windows/       WPF 界面层（net10.0-windows）
│   ├── App.xaml(.cs)             单实例守卫 + 依赖装配 + 托盘生命周期
│   ├── MainWindow.xaml(.cs)      状态卡片 / 设置表单 / 最近提醒列表
│   ├── ViewModels/MainViewModel.cs
│   ├── Services/TrayIconService.cs     托盘图标、右键菜单、气泡通知
│   ├── Services/AutostartService.cs    开机自启（HKCU Run）
│   └── Assets/MqttNotify.ico
├── tests/MqttNotify.Core.Tests/  xunit 测试（含进程内 broker 的端到端）
└── tools/icon-gen/               图标生成器（手写 ICO，见 §6）
```

把「连接逻辑」放在 `Core` 而不是 WPF 工程里，是为了它能在 Linux 上被测试覆盖——
Windows 工程本身在非 Windows 上跑不起来，但它的业务逻辑全部可测。

---

## 4. 构建

需要 .NET 10 SDK。仓库**不含** `gradlew` 之类的包装脚本，直接用 `dotnet`：

```bash
dotnet build MqttNotify.sln -c Release
dotnet test  tests/MqttNotify.Core.Tests -c Release
```

### 4.1 在 Linux / macOS 上交叉编译

本仓库的 `MqttNotify.Windows.csproj` 里带了 `<EnableWindowsTargeting>true</EnableWindowsTargeting>`，
因此在 Linux/macOS 上也能编译出 Windows 的 exe（WPF 与 WinForms 都验证过）。

> 这只影响**编译**：产物仍然只能在 Windows 上运行，界面也无法在 Linux 上冒烟测试。
> 该属性在 Windows 上是无害的，无需删改。

---

## 5. 已知限制

- **通知形式是托盘气泡（`NotifyIcon.ShowBalloonTip`），不是「真正的」Toast。**
  Win10/11 会把它渲染成右下角通知，但它不进操作中心、也不支持按钮/点击回调。
  升级路径是用 `CommunityToolkit` 的 Toast（需要注册 AppUserModelID / 开始菜单快捷方式），
  首版为保持零第三方 UI 依赖没有引入。
- **最近提醒只在内存里**（最多 100 条），退出即清空；不落盘、不做历史列表。
- **不做离线补拉**：断线期间服务端产生的提醒不会补发（REST 8383 未接入）。
- 自签证书场景需要手动勾选「允许不安全 TLS」，这是有意的显式操作，不是默认行为。
- 未做单次通知的「点击跳转」——payload 里没有可跳转的目标。

---

## 6. 图标

`Assets/MqttNotify.ico` 由 `tools/icon-gen` 生成。之所以手写字节而不是用图形库：
本仓库在 Linux 上交叉编译，而 `System.Drawing.Common` 从 .NET 6 起只在 Windows 上可用。

生成器按 ICO 规范直接拼字节，用 4×4 超采样自绘抗锯齿（圆角蓝色方块 + 白色铃铛），
输出 16/32/48/64/128 五种尺寸，并在写完后回读校验长度与条目数。

重新生成：

```bash
dotnet run --project tools/icon-gen -c Release -- src/MqttNotify.Windows/Assets/MqttNotify.ico
```

该工程刻意**不加入** `MqttNotify.sln`：日常 `build` / `test` 不该为它付出代价。
