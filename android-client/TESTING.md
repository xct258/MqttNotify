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

- `MqttMessageParserTest` — 两种 payload 的解析契约：
  - 待办 `【待办提醒】{title}\n{message}`：带/不带标题、多行正文、空 payload、CRLF 换行、
    主题尾段非数字（野消息）、标题为空时回退默认标题；
  - 服务 `【服务通知】{server}\n【服务】{title}\n【状态】{status}\n\n{message}`：
    服务器名/服务名/状态、缺状态行、缺空行、正文以 `【服务】` 开头、未知格式兜底。
- `AppSettingsTest` — 订阅主题拼接（`{base}/#`、去尾斜杠）、`host/port/topic` 合法性、`normalized()`

> 这两层是纯 Kotlin 逻辑，不碰 Android API，所以能在 JVM 上直接跑。
> `SettingsRepository`(DataStore) 和 Service 依赖 `android.*`，得用 Robolectric 或仪器测试，暂未覆盖。

---

## 2. 协议联调测试（真实 broker，无需设备）

验证「客户端真的能连上 Mosquitto、订阅 `{base}/#`、收到到点推送并还原成通知文案」。
用的是**与 App 相同的 HiveMQ 客户端与核心连接参数**（QoS 1 / keepAlive 60 / 订阅 `{base}/#`；
联调测试为了不在 broker 上留下会话，使用临时 client id + cleanSession=true，App 本身用持久会话）。

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
# TCP（push-server 的 1883 只绑 127.0.0.1，适合同机测试）
./gradlew :app:testDebugUnitTest \
  -Dmqtt.broker.host=127.0.0.1 -Dmqtt.broker.port=1883 \
  --tests '*MqttBrokerIntegrationTest*'

# WebSocket（局域网直连 9001；客户端默认已改为公网 WSS）
./gradlew :app:testDebugUnitTest \
  -Dmqtt.broker.host=127.0.0.1 -Dmqtt.broker.port=9001 \
  -Dmqtt.broker.transport=ws \
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
    "title": "交电费",
    "message": "【待办提醒】交电费\n主卧电表，户号123456\n到期：2026-10-12",
    "status": "info",
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
| 5 | 进程被杀 | `adb shell am kill <pkg>`（模拟系统回收，不要用 force-stop）后等 15 分钟再推 | 看门狗把服务拉起并重连，离线期间的消息补收 |
| 6 | 开机自启 | 重启手机（开关已开） | 无需打开 App，通知栏即出现常驻通知 |
| 7 | 权限 | 关掉通知权限 | 不崩溃；连接照常，只是不弹通知 |
| 8 | 息屏/Doze | 息屏放置 10 分钟后再推 | 通知能到（务必开启忽略电池优化） |
| 9 | 网络切换 | 推送前在 Wi-Fi / 移动数据之间切换 | 3 秒内状态回到「已连接」，切换后推送正常收到 |
| 10 | 离线补收 | 关掉网络 2 分钟，期间发 2 条推送，再恢复网络 | 重连后两条通知都到（持久会话排队）；顺序可能按 broker 队列 |
| 11 | 假死自愈 | 用防火墙丢弃 broker 流量 3 分钟（或飞行模式后恢复） | 应用主动重连，不必等 TCP KeepAlive 超时 |
| 12 | 停止接收 | 点「停止接收」后等 15 分钟 | 服务保持停止，不被看门狗重新拉起 |
| 13 | 覆盖安装 | `adb install -r` 新包（之前正在接收） | 安装完成后自动恢复接收 |
| 14 | 精确闹钟 | Android 12+ 关闭精确闹钟权限 | 看门狗仍工作（退化为非精确）；授权后 Doze 唤醒更准时 |
| 15 | 在线状态 | 保持连接 5 分钟，观察 push 管理页「客户端」 | 始终显示在线，最后心跳不超过 60 秒；发一条消息后 last_seen 立即刷新 |
| 16 | 重连后的在线状态 | 断开网络 30 秒再恢复，观察管理页 | 重连成功后 retained 状态恢复为 online=true，不会一直停在离线 |

> 看门狗是 15 分钟一次，测试时可以临时把 `MqttWatchdog.INTERVAL_MS` 调小，
> 或用 `adb shell dumpsys alarm | grep mqttnotify` 确认闹钟已注册。
> 强制停止（`am force-stop` / 系统设置里「强行停止」）会清除所有闹钟，任何应用都无法自恢复。

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
3. **Client ID 冲突**：两个客户端用同一个前缀会互相顶掉。最终 ID 是 `前缀_固定随机串`，留空前缀则自动使用 `android_xxxxxxxx`。
4. **连不上 broker**：内网不通 / 端口不是 1883 / 手机与 broker 不同网段。
5. **连上了收不到**：QoS 1 正常；确认消息是 `retained=false` 的实时推送，
   离线期间的消息本来就**不会补发**（要靠 REST 补拉，首版未接入）。
6. **后台被杀 / 延迟**：MIUI/EMUI 等需手动允许自启动与后台运行；关闭电池优化。
7. **管理页显示离线但手机能收消息**：先确认两边 client id 一致，再旁路观察心跳：
   ```bash
   mosquitto_sub -h <host> -t '$presence/clients/#' -v
   ```
   正常情况下，Android 客户端上线、每 60 秒心跳、收到消息、MQTT 重连、亮屏/解锁时都会
   发一条 retained 在线消息。若完全没有心跳，检查服务是否被 ROM 冻结、通知/自启动权限是否给足。

**看日志**：

```bash
adb logcat -s MqttService:* BootReceiver:*
```

---

## 5. 构建环境（本仓库机器专属）

见 README 4.5：aarch64 主机需要用发行版的原生 `aapt2`，
并通过 `-Pandroid.aapt2FromMavenOverride=/usr/bin/aapt2` 覆盖 AGP 默认下载的 x86-64 版。
