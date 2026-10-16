# 测试说明

本项目的测试分三层。之所以这么分，是因为开发环境（Linux 沙箱）**跑不起 WPF 界面，也没有 Windows**，
所以：

- 能自动化的部分全部自动化（第 1、2 层，`dotnet test` 一条命令跑完）；
- 只有必须在 Windows 上肉眼确认的部分，才留给人（第 3 层）。

```bash
dotnet test tests/MqttNotify.Core.Tests -c Release
```

---

## 第 1 层：单元测试（纯逻辑，毫秒级）

### `MqttMessageParserTests` —— 跨端解析契约

这是**唯一**定义下行 payload 解析口径的地方，Android 端有对应的一组同名用例。
两端必须同时通过，否则「同一条消息在手机和电脑上显示不一样」。

覆盖：带标题+多行正文、只有标题、标题为空回退默认、无前缀整段当正文、空 payload、
CRLF 换行归一化、尾段非数字/带空白/小数时 `taskId` 为 `null`、主题不含 `/` 时取整段。

`【待办提醒】` 这个前缀常量也单独钉了一条用例——它是两端约定的字面量。

### `AppSettingsTests` —— 配置模型

覆盖默认值（与 push-server 文档一致）、订阅主题拼接与去多余斜杠、`IsValid` 的边界
（host 空 / 端口 0 与 70000 / 主题全空白）、`Normalized()` 的去空白、
`wsPath` 的空值与前导斜杠补齐、以及「切到非 WSS 时清掉 `allowInsecureTls`」。
传输方式的默认端口（1883 / 9001 / 443）与展示地址拼装也在这里钉死。

### `MqttOptionsFactoryTests` —— 传输方式矩阵

**这一层是不可替代的**：WSS 要端到端跑通需要真实的 TLS 反代，沙箱里搭不出来
（MQTTnet 的内置 broker 只支持 TCP/TLS 端点，不支持 WebSocket 端点）。
所以「TCP / WS / WSS × 是否信任自签证书」这个矩阵必须靠单测把参数拼装钉死：

- TCP → `MqttClientTcpOptions`，`RemoteEndpoint` 是 `DnsEndPoint` 且 host/port 正确，未启用 TLS
- WS → `MqttClientWebSocketOptions`，URI 为 `ws://host:9001/mqtt`，未启用 TLS
- WSS → URI 为 `wss://...`，`TlsOptions.UseTls == true`
- WSS 默认严格校验：`AllowUntrustedCertificates` / `IgnoreCertificateChainErrors` /
  `IgnoreCertificateRevocationErrors` 全为 `false`，且未设置自定义校验回调
- WSS + 允许不安全 TLS：四项全部放宽
- WebSocket 路径规范化：`/mqtt`、`/custom/path`、`custom`、空白 四种输入
- 通用参数：client id、协议版本 V311、clean session、KeepAlive 60s、连接超时

> 顺带说明：`MqttClientTcpOptions.Server` / `.Port` 在 MQTTnet 4.3.7 已标记过时，
> 实际承载值是 `RemoteEndpoint`。测试里断言的是后者。

---

## 第 2 层：进程内 broker 的端到端测试（秒级）

`MqttConnectionServiceIntegrationTests` 用 MQTTnet **自带的 broker** 起一个进程内服务端，
因此不需要外部 Mosquitto 或 docker——这是 Windows 端相比手机端在可测性上的最大优势。

| 用例 | 验证什么 |
| --- | --- |
| 端到端_连上broker后收到下行消息会抛出提醒 | 连接 → 订阅 → 收到消息 → 解析出 `taskId/title/body/topic` 全链路 |
| 主题不在订阅范围内时不会收到提醒 | `taskops/todo/#` 不会命中 `other/topic/1`（确认订阅真的生效） |
| broker不可达时进入失败态并持续重试 | 连不上时状态为 `Failed`，且**在持续重试**（2 秒内 ≥2 次，用极短退避加速） |
| broker重启后能自动重连 | 停掉 broker → 同端口重启 → 客户端自行恢复连接（自己实现的退避重连循环） |
| 掉线后状态不再停留在已连接 | 掉线**立刻**切到 `Reconnecting`；否则整个退避等待期间界面会一直显示「已连接」 |
| 未配置broker地址时直接失败 | 配置不合法时立即置 `Failed`，不进入重试循环 |
| 停止后状态回到Stopped | `StopAsync()` 能干净地收尾 |

broker 端口由「先占一个临时端口再释放」得到，测试之间互不冲突。

---

## 第 3 层：Windows 上的人工验收（必须人工）

以下内容在 Linux 沙箱里**无法验证**，需要在一台真实 Windows 上按清单过一遍。

### 3.1 先准备一个可用的服务端

三种选择，任选其一：

1. **真实 push-server**：在服务器上跑起来，客户端用 `WebSocket` + `9001` 连接
   （注意其 TCP 1883 绑在 `127.0.0.1`，跨机连不上）。
2. **本地 Mosquitto**：`listener 1883 0.0.0.0` + `allow_anonymous true`，客户端走 `TCP`，
   然后用 `mosquitto_pub` 投递测试消息：
   ```bash
   mosquitto_pub -h <broker> -p 1883 -t taskops/todo/1 \
     -m '【待办提醒】交电费
   主卧电表，户号123456'
   ```
3. **公共 broker 验传输链路**（最适合验 WS/WSS，不用自己搭）：
   - `ws://broker.emqx.io:8083/mqtt`
   - `wss://broker.emqx.io:8084/mqtt`

   把客户端指过去、订阅 `taskops/todo/#`，再从该 broker 的在线 WebSocket 客户端发布同一条消息。
   **公共 broker 是开放的，别发敏感内容。**

### 3.2 验收清单

| # | 场景 | 预期 | ✅ |
| --- | --- | --- | --- |
| 1 | 首次启动，未配置 | 主窗口弹出，状态灯灰色「已停止」，提示尚未配置 | |
| 2 | 填错地址后保存 | 状态灯变红、显示失败原因，且日志/状态里能看到它在反复重试 | |
| 3 | 填对地址（WS 9001）后保存 | 状态灯变绿「已连接」，副标题显示地址与 `taskops/todo/#` | |
| 4 | 服务端投递一条带标题的提醒 | 右下角弹出通知，标题/正文与 payload 一致；右侧列表新增一条（时间、`#taskId`、主题） | |
| 5 | 投递无 `【待办提醒】` 前缀的正文 | 标题显示为「新提醒」，正文为原文 | |
| 6 | 投递含 `\r\n` 换行的 payload | 正文换行正常，不出现多余空行 | |
| 7 | 投递超长正文 | 气泡不抛异常（超长会被截断到 255 字符）；完整内容仍能在列表里看到 | |
| 8 | 关闭 broker 进程 | 状态在数秒内变为「重连中 / 连接失败」，并持续重试 | |
| 9 | 重新启动 broker | 不重启客户端即自动恢复「已连接」 | |
| 10 | 点窗口的 X | 窗口消失、**进程仍在**、托盘图标还在，并首次提示「已收进托盘」 | |
| 11 | 双击托盘图标 | 窗口重新出现并置于前台 | |
| 12 | 托盘右键 → 停止接收 / 启动接收 | 状态随之切换 | |
| 13 | 勾选「开机自动启动」并保存 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 出现 `MqttNotify`，值为 `"<路径>" --minimized`；取消勾选后该项消失 | |
| 14 | 注销/重启后（或手动跑 `MqttNotify.exe --minimized`） | 直接进托盘、不弹主窗口，且已自动连上 | |
| 15 | 再次双击 exe（已有实例在跑） | 第二个进程直接退出，托盘只有一个图标、不会重复弹通知 | |
| 16 | 托盘右键 → 退出 | 进程真正退出（任务管理器中消失），图标消失，注册表自启项保留 | |
| 17 | WSS + 受信证书（如公共 `broker.emqx.io:8084`） | 能连上并收到消息 | |
| 18 | WSS + 自签证书，**不勾**「允许不安全 TLS」 | 连接失败（这是期望行为，不能静默放行） | |
| 19 | 同上，**勾上**「允许不安全 TLS」 | 能连上 | |
| 20 | 多显示器不同缩放 | 界面不糊、不错位 | |
| 21 | 改配置并保存 | 立即按新配置重连（不需要重启程序） | |
| 22 | 把 `%APPDATA%\MqttNotify\settings.json` 改成非法 JSON 后启动 | 程序正常启动并回落到默认配置（不崩溃） | |

### 3.3 单文件发布的额外确认

若用 `--self-contained -p:PublishSingleFile=true` 发布，额外确认：

- 目标机器**未安装 .NET** 时也能直接双击运行；
- exe 图标是蓝色铃铛（说明 Win32 图标资源写入成功）；
- 托盘图标同样是蓝色铃铛（说明 `Assets/MqttNotify.ico` 拷到了输出目录且能被 `IconLoader` 读到；
  读不到会静默回退成系统默认图标——看到默认图标就说明这一步有问题）。

---

## 已知未覆盖

- WPF 界面的自动化测试（无 UI 测试框架接入，靠第 3 层人工验收）。
- WSS 握手本身（沙箱内没有 WSS broker；参数拼装由第 1 层覆盖，实际握手靠第 3 层第 17–19 项）。
- 通知气泡的**外观**（尺寸、截断策略由 Windows 决定，无法在本仓库内断言）。
- 高并发/长时间运行的资源占用与内存泄漏（未做压测）。
