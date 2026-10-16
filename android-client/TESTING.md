# 测试指南

分三层测。越往下越接近真实，成本也越高。

| 层 | 覆盖 | 需要设备 | 能否在本仓库机器跑 |
| --- | --- | --- | --- |
| 1. JVM 单元测试 | 下行 payload 解析、配置校验 | 否 | ✅ |
| 2. 协议联调测试 | 真实 Mosquitto + HiveMQ 客户端 + 订阅/收包/解析 | 否 | ✅ |
| 3. 真机端到端 | 前台服务保活、系统通知、重连、开机自启 | **是** | ❌ |

前两层跑不起来的只有「Android 运行时行为」，协议链路本身能测得很实。

---

## 1. JVM 单元测试（无需设备）

```bash
./gradlew :app:testDebugUnitTest
# 报告：app/build/reports/tests/testDebugUnitTest/index.html
```

覆盖：

- `MqttMessageParserTest` — `【待办提醒】{title}\n{message}` 的解析契约：带/不带标题、
  多行正文、空 payload、CRLF 换行、主题尾段非数字（野消息）、标题为空时回退默认标题
- `AppSettingsTest` — 订阅主题拼接（`{base}/#`、去尾斜杠）、`host/port/topic` 合法性、`normalized()`

> 这两层是纯 Kotlin 逻辑，不碰 Android API，所以能在 JVM 上直接跑。
> `SettingsRepository`(DataStore) 和 Service 依赖 `android.*`，得用 Robolectric 或仪器测试，暂未覆盖。

---

## 2. 协议联调测试（真实 broker，无需设备）

验证「客户端真的能连上 Mosquitto、订阅 `{base}/#`、收到到点推送并还原成通知文案」。
用的是**与 App 完全相同的 HiveMQ 客户端与连接参数**（QoS 1 / cleanSession / keepAlive 60）。

### 2.1 起一个本地 broker

本地已有 Mosquitto 时跳过；没有就：

```bash
# 方式 A：用 push-server 自带的（会同时拉起 FastAPI）
cd push-server && docker compose up -d        # Mosquitto 在 1883

# 方式 B：只起裸 broker（内网匿名，与 push-server 的 Mosquitto 配置一致）
cat > /tmp/mosquitto.conf <<'EOF'
listener 1883 0.0.0.0
allow_anonymous true
persistence false
EOF
mosquitto -c /tmp/mosquitto.conf -d
```

自检（确认 broker 活着、通配订阅有效）：

```bash
mosquitto_sub -h 127.0.0.1 -t 'taskops/todo/#' -C 1 -v &
mosquitto_pub -h 127.0.0.1 -t 'taskops/todo/7' -m 'selftest'
# 期望输出：taskops/todo/7 selftest
```

### 2.2 跑联调测试

```bash
./gradlew :app:testDebugUnitTest \
  -Dmqtt.broker.host=127.0.0.1 -Dmqtt.broker.port=1883 \
  --tests '*MqttBrokerIntegrationTest*'
```

**未指定 `-Dmqtt.broker.host` 时该测试会被 `assumeTrue` 自动跳过**，所以放在 CI 里不会红。
（Gradle 不会自动把 `-D` 透传给测试 JVM，透传逻辑写在 `app/build.gradle.kts` 的 `testOptions`。）

它做的事：连 broker → 订阅 `taskops/todo/#` → 发布 `taskops/todo/7` + 纯文本 payload
→ 断言收到的 topic 与解析出的 `taskId/title/body` 与 push-server 的 `downlink_topic()`
/`downlink_text()` 输出一致。

**测不到**：前台服务是否被系统杀死、通知长什么样、Doze 下的延迟——必须真机。

---

## 3. 真机端到端（完整验收）

### 3.1 准备服务端

在跑 push-server 的机器上（或本机 `docker compose up -d`）：

```bash
# 健康检查
curl http://<host>:8383/api/health          # {"status":"running"}
```

确认 App 里填的 `host/port/topic` 与服务的 `PUSH_TOPIC` 一致（默认 `taskops/todo`）。

> ⚠️ broker 是**内网匿名**的，别暴露公网。手机要与 broker 网络互通
> （同一内网，或端口转发/反代）。先确认手机能连：手机浏览器开 `http://<host>:8383` 看能否打开管理页。

### 3.2 安装

```bash
adb install -r app/build/outputs/apk/debug/app-debug.apk
```

release 包未签名，需先 `apksigner sign`（见 README 4 节）。

### 3.3 配置 App

填 Broker 地址 / 端口 / 订阅主题 → **保存** → **启动接收**。
状态卡应变为「已连接」，通知栏出现常驻通知。给通知权限（Android 13+）。

### 3.4 触发一次推送

**方式 A：REST 直接推**（最快）

```bash
curl -X POST http://<host>:8383/api/push \
  -H 'Content-Type: application/json' \
  -d '{
    "server_name": "taskops",
    "topic": "taskops/todo/7",
    "message": "【待办提醒】交电费\n主卧电表，户号123456\n到期：2026-10-12",
    "push_to_mqtt": true,
    "msg_type": "markdown",
    "mode": "append"
  }'
```

> `push_to_mqtt` 必须是**不带引号的布尔值**，服务端会校验。

**方式 B：定时任务到点推**（更贴近真实链路）

```bash
# 建一个 1 分钟后到点的任务
curl -X POST http://<host>:8383/api/task \
  -H 'Content-Type: application/json' \
  -d '{"server_name":"taskops","client_ref":"todo-test-1","title":"交电费",
       "message":"主卧电表，户号123456","schedule_at":"2026-10-16T21:00:00+08:00"}'
```

**方式 C：绕过服务端，直接发**

```bash
mosquitto_pub -h <host> -t 'taskops/todo/7' -q 1 \
  -m '【待办提醒】交电费
主卧电表，户号123456'
```

### 3.5 验收清单

| # | 场景 | 操作 | 期望 |
| --- | --- | --- | --- |
| 1 | 正常收信 | 3.4 任一方式 | 弹出「交电费」通知，正文为多行内容 |
| 2 | 主题解析 | 发到 `taskops/todo/42` | 通知副标题是 topic，详情含 `#42` |
| 3 | 断线重连 | 停掉 broker 30s 再启动 | 状态变「重连中」→「已连接」，之后推送正常收到（指数退避） |
| 4 | 配置热更新 | 改 topic 后保存 | 服务自动重连，无需手动重启 |
| 5 | 进程被杀 | `adb shell am force-stop <pkg>` 后重新推 | 系统按 `START_STICKY` 重启服务；若未重启则手动打开 App |
| 6 | 开机自启 | 重启手机（开关已开） | 无需打开 App，通知栏即出现常驻通知 |
| 7 | 权限 | 关掉通知权限 | 不崩溃；连接照常，只是不弹通知 |
| 8 | 息屏/Doze | 息屏放置 10 分钟后再推 | 通知能到（必要时把 App 加入电池优化白名单） |

包名：debug 是 `com.xct258.mqttnotify.debug`，release 是 `com.xct258.mqttnotify`。

---

## 4. 排错

**收不到消息**，按顺序排查：

1. **旁路监听**，确认服务端到底发没发：
   ```bash
   mosquitto_sub -h <host> -t 'taskops/todo/#' -v
   ```
   收不到 → 是服务端/主题的问题，不是客户端。
2. **主题对不上**：App 里「订阅主题」要等于服务端 `PUSH_TOPIC`（默认 `taskops/todo`），
   实际订阅的是 `<主题>/#`。填成 `taskops/todo/7` 就收不到了。
3. **Client ID 冲突**：两个客户端用同一个 ID 会互相顶掉。留空即自动生成。
4. **连不上 broker**：内网不通 / 端口不是 1883 / 手机与 broker 不同网段。
5. **连上了收不到**：QoS 1 正常；确认消息是 `retained=false` 的实时推送，
   离线期间的消息本来就**不会补发**（要靠 REST 补拉，首版未接入）。
6. **后台被杀 / 延迟**：MIUI/EMUI 等需手动允许自启动与后台运行；关闭电池优化。

**看日志**：

```bash
adb logcat -s MqttService:* BootReceiver:*
```

---

## 5. 构建环境（本仓库机器专属）

见 README 4.5：aarch64 主机需要用发行版的原生 `aapt2`，
并通过 `-Pandroid.aapt2FromMavenOverride=/usr/bin/aapt2` 覆盖 AGP 默认下载的 x86-64 版。
