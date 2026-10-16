package com.xct258.mqttnotify.mqtt

import com.hivemq.client.mqtt.MqttClient
import com.hivemq.client.mqtt.datatypes.MqttQos
import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

/**
 * 真实 broker 的端到端联调测试（**默认跳过**）。
 *
 * 验证的是客户端最核心的链路：用与 App **相同的 HiveMQ 客户端、相同的连接参数**
 * （QoS1 / cleanSession / keepAlive 60 / 订阅 `{base}/#`）连到真的 Mosquitto，
 * 然后模拟 push-server 到点 publish（主题 `{base}/{task_id}`、正文为纯文本），
 * 断言 App 的解析结果能还原出正确的通知标题与正文。
 *
 * 覆盖不到的部分：Android 前台服务保活、系统通知渲染——那些只能在真机上验。
 *
 * 运行方式（未指定 broker 时测试会被 assumeTrue 跳过，不影响 CI）：
 * ```
 * gradle :app:testDebugUnitTest \
 *   -Dmqtt.broker.host=127.0.0.1 -Dmqtt.broker.port=1883 \
 *   --tests '*MqttBrokerIntegrationTest*'
 * ```
 * 注意：Gradle 需把系统属性透传给测试 JVM，见 app/build.gradle.kts 的 testOptions。
 */
class MqttBrokerIntegrationTest {

    @Test
    fun `订阅下行主题并还原出正确的通知标题与正文`() {
        val host = System.getProperty("mqtt.broker.host")
        if (host.isNullOrBlank()) {
            assumeTrue("未指定 -Dmqtt.broker.host，跳过联调测试", false)
            return
        }
        val port = System.getProperty("mqtt.broker.port")?.toIntOrNull() ?: 1883

        val base = "taskops/todo"
        val taskId = 7
        val title = "交电费"
        val body = "主卧电表，户号123456\n到期：2026-10-12"
        // 与 push-server downlink_text() 的输出格式一致
        val payloadText = "${MqttMessageParser.MARKER}$title\n$body"
        val topic = "$base/$taskId"

        val received = AtomicReference<Pair<String, String>>()
        val latch = CountDownLatch(1)

        val client = MqttClient.builder()
            .useMqttVersion3()
            .identifier("it-" + System.nanoTime())
            .serverHost(host)
            .serverPort(port)
            .buildAsync()

        try {
            client.connectWith()
                .cleanSession(true)
                .keepAlive(60)
                .send()
                .get(10, TimeUnit.SECONDS)

            client.subscribeWith()
                .topicFilter("$base/#")
                .qos(MqttQos.AT_LEAST_ONCE)
                .callback { publish ->
                    received.set(
                        publish.topic.toString() to publish.payloadAsBytes.toString(Charsets.UTF_8)
                    )
                    latch.countDown()
                }
                .send()
                .get(10, TimeUnit.SECONDS)

            // 模拟服务端到点推送
            client.publishWith()
                .topic(topic)
                .payload(payloadText.toByteArray(Charsets.UTF_8))
                .qos(MqttQos.AT_LEAST_ONCE)
                .send()
                .get(10, TimeUnit.SECONDS)

            assertTrue("10s 内未收到下行消息", latch.await(10, TimeUnit.SECONDS))

            val (receivedTopic, receivedText) = received.get()
            assertEquals(topic, receivedTopic)

            val alert = MqttMessageParser.parse(
                receivedTopic,
                receivedText.toByteArray(Charsets.UTF_8),
                "新提醒",
            )
            assertEquals(taskId.toLong(), alert.taskId!!.toLong())
            assertEquals(title, alert.title)
            assertEquals(body, alert.body)
        } finally {
            runCatching { client.disconnect().get(5, TimeUnit.SECONDS) }
        }
    }
}
