# MQTT 提醒客户端（Android）

对接 [xct258/push-server](https://github.com/xct258/push-server) 的安卓端 MQTT 消息提醒客户端。
订阅下行主题，收到推送即以**系统通知**展示，并以前台服务常驻保活。

- 技术栈：Kotlin + Jetpack Compose（Material 3）+ HiveMQ MQTT Client
- 最低版本：Android 8.0（API 26）
- 连接方式：MQTT 3.1.1 over **TCP / WebSocket(WS) / WebSocket+TLS(WSS)**

> 测试方法（单元测试 / 协议联调 / 真机验收清单）见 [TESTING.md](TESTING.md)。
>
> 发布 APK 走 [GitHub Releases](https://github.com/xct258/MqttNotify/releases)；
> 当前 Release 产物是 debug 签名，可直接侧载。

---

## 1. 对接协议（摘自 push-server 的 MQTT 接口文档）

| 项 | 值 |
| --- | --- |
| 协议 | MQTT 3.1.1 / 5.0 |
| TCP 地址 | `mqtt://<broker-host>:1883` |
| WebSocket 地址 | `ws://<broker-host>:9001` |
| WebSocket + TLS | `wss://push-server.o1-1.xct258.top/mqtt`（**默认**） |
| 鉴权 | 内网匿名 |
| 订阅主题 | `{base}/#`，`base` 默认 `taskops/todo` |
| QoS | 1（至少一次） |
| Clean Session | `true` |
| Retained | `false` |
| Keep Alive | 60s |

**下行消息**：payload 为拼好的纯文本 `【待办提醒】{title}\n{message}`；主题尾段即 `task_id`。

**在线状态**：连接后向 `$presence/clients/{client_id}` 发布 retained 在线状态，60 秒心跳
（QoS0、非 retained）；异常退出由 MQTT Will 标记离线，供 push-server 管理页展示。

```
主题: taskops/todo/7
正文: 【待办提醒】交电费
      主卧电表，户号123456
      到期：2026-10-12
```

客户端不做解析拼装、不发回执，收到即展示。

> REST 侧的 `/api/push`、`/records`、`/api/tasks` 属于服务端/调用方接口，本客户端不涉及。

### 1.1 传输方式怎么选

push-server 的 Mosquitto 配置（见其 `容器构建脚本/start.sh`）是：

```
listener 1883 127.0.0.1        # 只绑本机！
    allow_anonymous true
listener 9001 0.0.0.0
    protocol websockets
    allow_anonymous true
```

所以：

| 模式 | 地址 | 适用场景 |
| --- | --- | --- |
| `TCP` | `mqtt://host:1883` | 仅当 App 与 broker **同机**（1883 绑在 `127.0.0.1`，局域网手机连不上），或你自行放开了监听 |
| `WS` | `ws://host:9001` | 仅局域网直连 Mosquitto 的 9001 |
| `WSS` | `wss://push-server.o1-1.xct258.top/mqtt` | **客户端默认值**。公网入口，任何网络环境都能用 |

WSS 用 nginx 反代 Mosquitto 的 9001 时，`location` 必须与 App 里的「WebSocket 路径」一致（默认 `/mqtt`）：

```nginx
location /mqtt {
    proxy_pass http://127.0.0.1:9001;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_read_timeout 300s;
    # ssl_certificate / ssl_certificate_key ...
}
```

> Mosquitto 自身的 websockets 监听**忽略请求路径**，所以直连 `ws://host:9001/mqtt` 或 `ws://host:9001/` 都可以；
> 但经过反代时路径会被 `location` 匹配，因此路径做成可配置项。
>
> 若 WSS 用的是自签证书，可在设置里开启「允许自签证书（跳过 TLS 校验）」（默认关闭，会降低安全性）。

---

## 2. 功能范围（首版）

- ✅ 前台服务常驻连接：断线自动重连（HiveMQ 指数退避），首次连不上本地退避重试
- ✅ 系统通知：高优先级「待办提醒」渠道 + 低优先级「连接状态」常驻渠道（Android 13+ 会请求通知权限）
- ✅ 设置页：传输方式（TCP / WS / WSS） / Broker 地址 / 端口 / WebSocket 路径 / 订阅主题 base / 服务器名称 / Client ID / 开机自启，DataStore 持久化
- ✅ 配置变更即时生效：服务监听配置流，改动后自动重连
- ✅ 主界面展示连接状态与最近提醒（仅内存，不落盘）
- ✅ 开机自启（可选开关）

暂未包含（按需求确认，可后续扩展）：

- ⬜ 离线补拉：重连后调 `GET /api/tasks?status=pending&server_name=...`
- ⬜ 提醒历史持久化（Room）
- ⬜ 免打扰 / 静默时段、通知点击跳转详情

---

## 3. 目录结构

```
app/src/main/java/com/xct258/mqttnotify/
├── MqttNotifyApp.kt            # Application：预建通知渠道
├── MainActivity.kt             # 单 Activity 入口，请求通知权限
├── data/
│   ├── AppSettings.kt          # 配置模型 + 订阅主题拼装
│   ├── MqttTransport.kt        # TCP / WS / WSS 枚举（scheme、默认端口、是否 TLS）
│   └── SettingsRepository.kt   # DataStore 持久化，settings: Flow<AppSettings>
├── mqtt/
│   ├── MqttState.kt            # 进程内状态/最近提醒总线（服务写、UI 读）
│   ├── MqttMessageParser.kt    # 下行纯文本解析（标题/正文/task_id）
│   └── InsecureSsl.kt          # WSS 自签证书用的放行型 TrustManagerFactory（纯 JDK）
├── notify/
│   └── NotificationHelper.kt   # 通知渠道、常驻通知、提醒通知
├── service/
│   ├── MqttForegroundService.kt# 核心：连接/订阅/重连/收消息
│   └── BootReceiver.kt         # 开机自启
└── ui/
    ├── MainViewModel.kt
    ├── MainScreen.kt           # 主界面（状态卡 + 设置表单 + 最近提醒）
    └── theme/Theme.kt
```

---

## 4. 构建与运行

### 4.1 环境

- Android Studio（Ladybug 或更新）或命令行 Gradle
- JDK 17
- Android SDK：compileSdk 34、build-tools 34

### 4.2 生成 Gradle Wrapper（首次）

仓库只提交了 `gradle/wrapper/gradle-wrapper.properties`，未提交 `gradle-wrapper.jar`。
用 Android Studio 打开工程会自动补全；命令行方式二选一：

```bash
# 本机已装 gradle 时
gradle wrapper --gradle-version 8.9

# 或直接改用系统 gradle 构建
gradle :app:assembleDebug
```

### 4.3 构建

```bash
./gradlew :app:assembleDebug        # 产物 app/build/outputs/apk/debug/app-debug.apk
./gradlew :app:assembleRelease      # 已开启 R8 混淆（规则见 app/proguard-rules.pro）
```

> **必须用 Gradle 8.x**（本工程是 Kotlin DSL，入口是 `settings.gradle.kts`）。
> 部分发行版自带的 `gradle` 是 2017 年的 4.4.1，它不认识 `settings.gradle.kts`，
> 会直接报 `Project 'app' not found in root project '<目录名>'`——先 `gradle --version` 确认，
> 必要时改用 `./gradlew`，或用 8.x 的绝对路径调用。

### 4.4 安装

```bash
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

### 4.5 在 Linux/arm64 上无头构建（已验证）

Google 分发的 `aapt2` 只有 x86‑64 版（`com.android.tools.build:aapt2` 的 `linux` 分类），
在 aarch64 主机上会 `Exec format error`。解决办法是用发行版提供的**原生 arm64 aapt2**：

```bash
# Debian 13 (trixie)
apt-get install -y openjdk-21-jdk-headless unzip aapt2 android-sdk-build-tools

# Android SDK：注意 cmdline-tools 必须用纯 Java 版
# 最新版（如 16111833）已改为 x86-64 原生 android CLI，在 arm64 上无法运行
curl -LO https://dl.google.com/android/repository/commandlinetools-linux-11076708_latest.zip
# 解压到 <sdk>/cmdline-tools/latest，然后
sdkmanager "platforms;android-34" "build-tools;34.0.0"

# 构建：用 -P 覆盖 AGP 从 Maven 下载的 x86-64 aapt2
gradle :app:assembleDebug -Pandroid.aapt2FromMavenOverride=/usr/bin/aapt2
```

`local.properties` 写 `sdk.dir=<android-sdk 路径>`（已 gitignore）。

> `-Pandroid.aapt2FromMavenOverride` 只在本机需要，不要写进 `gradle.properties`，
> 否则会污染其他平台的构建环境。

---

## 5. 使用步骤

1. 打开 App，授予通知权限（Android 13+）。
2. 在「连接设置」填入：
   - **传输方式**：默认 `WSS`（公网入口 `wss://push-server.o1-1.xct258.top/mqtt`）；局域网直连可选 `WS`，broker 与手机同机时才选 `TCP`
   - **Broker 地址**：默认 `push-server.o1-1.xct258.top`，也可以填内网 IP 或其它反代域名
   - **端口**：切换传输方式会自动带出默认值（`443` / `9001` / `1883`），可手改
   - **WebSocket 路径**：选 `WS`/`WSS` 时出现，默认 `/mqtt`
   - **订阅主题**：默认 `taskops/todo`（与 `PUSH_TOPIC` 一致）
   - **Client ID 前缀**：最终 ID 为 `前缀_固定随机串`；留空则为 `android_xxxxxxxx`，随机串在重装/清空数据前保持不变
3. 点「保存」，再点「启动接收」。状态卡片变为「已连接」，出现常驻通知。

> 开发期不迁移旧配置：升级后旧的 DataStore 设置会被忽略，首次打开显示上面的默认公网 WSS。
4. 服务端到点推送后，手机弹出提醒通知。
5. 需要后台常驻时，建议在系统设置里对本 App 关闭电池优化（见「已知限制」）。

调试可直接观察 topic：

```bash
# TCP
mosquitto_sub -h <broker-host> -t 'taskops/todo/#' -v

# WebSocket（需 mosquitto_sub 支持 websockets）
mosquitto_sub --url 'ws://<broker-host>:9001/mqtt' -t 'taskops/todo/#' -v
```

---

## 6. 关键实现说明

- **保活**：前台服务 + 常驻通知。Android 14+ 声明 `foregroundServiceType="specialUse"`
  （长连接消息接收不属于 `dataSync`，且 `dataSync` 在 Android 15 有 6h/24h 限制）。
- **重连**：连接建立后由 HiveMQ `automaticReconnectWithDefaultConfig()` 负责指数退避重连；
  首次连接失败由 `MqttForegroundService.connectWithRetry` 本地退避（2s→60s 封顶）重试。
- **传输方式**：`app/data/MqttTransport.kt` 定义 TCP/WS/WSS；服务里 WS/WSS 追加
  `MqttWebSocketConfig.serverPath(..)`，WSS 再叠加 `MqttClientSslConfig`（默认系统信任库，
  勾选自签证书时改用 `mqtt/InsecureSsl.kt`：纯 JDK API 的放行型 `TrustManagerFactory`
  加宽松 `HostnameVerifier`）。
  > WebSocket 传输额外依赖 `io.netty:netty-codec-http`。HiveMQ 只把 netty 的基础模块
  > 作为传递依赖，缺了它建连会抛
  > `NoClassDefFoundError: io/netty/handler/codec/http/websocketx/WebSocketVersion`，
  > 所以 `app/build.gradle.kts` 里显式声明了该依赖。
  > 顺带记一笔：HiveMQ 的 POM 里 netty 是 `<scope>runtime</scope>`，**编译期不可见**，
  > 所以不能直接引用它自带的 `InsecureTrustManagerFactory`，否则 `compileDebugKotlin` 会报
  > `Unresolved reference 'netty'`。
- **配置热更新**：服务 `collectLatest` 设置流，配置变化即拆旧连接、按新配置重连。
- **状态一致性**：`generation` 代号避免旧连接的迟到回调覆盖当前状态。
- **离线消息**：`retained=false` 不补发，需靠 REST 补拉（首版未接入）。

---

## 7. 已知限制

- **Doze / 电池优化**：系统深度休眠时可能延迟网络。设置页已提供「忽略电池优化」入口，
  点击后跳到系统授权页；首次使用建议开启。
- **划掉最近任务**：服务声明了 `stopWithTask=false`，并在 `onTaskRemoved` 里续拉前台服务，
  从最近任务划掉 App 不会主动断开连接；点击 App 内的「停止接收」仍会停止。
- **强制停止**：在系统设置里「强行停止」应用会终止服务，这是 Android 的限制，无法绕过。
- **国产品牌后台限制**：部分 ROM 会清理后台，需手动在系统设置中允许自启动 / 后台运行。
- **应用商店上架**：`specialUse` 前台服务类型需在 Play 商店声明用途；内网/自用分发不受影响。
- **明文传输**：`TCP` / `WS` 均为明文内网匿名协议，**请勿把 broker 暴露到公网**；跨公网请用 `WSS`。

---

## 8. 后续可扩展

- 离线补拉：`GET http://<host>:8383/api/tasks?status=pending&server_name=<serverName>`
- 提醒历史：Room 持久化 + 未读角标
- 用户名密码 / 客户端证书鉴权：当前 broker 为内网匿名，WSS 只解决传输加密
- 自定义 CA：目前自签证书只能「全部信任」，后续可做成导入 PEM 证书
