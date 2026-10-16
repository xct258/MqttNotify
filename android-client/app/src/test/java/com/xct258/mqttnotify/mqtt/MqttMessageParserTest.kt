package com.xct258.mqttnotify.mqtt

import com.xct258.mqttnotify.mqtt.MqttMessageParser.MARKER
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * 下行 payload 解析的契约测试。
 *
 * 对应 push-server 的 `downlink_text()`：
 *   有标题 -> `【待办提醒】{title}\n{message}`；无标题 -> 只发正文。
 * 主题尾段为 task_id。
 */
class MqttMessageParserTest {

    private val fallback = "新提醒"

    private fun parse(topic: String, payload: String) =
        MqttMessageParser.parse(topic, payload.toByteArray(Charsets.UTF_8), fallback)

    @Test
    fun `带标题与多行正文`() {
        val alert = parse(
            "taskops/todo/7",
            "【待办提醒】交电费\n主卧电表，户号123456\n到期：2026-10-12",
        )
        assertEquals(7, alert.taskId?.toLong()?.toInt())
        assertEquals("交电费", alert.title)
        assertEquals("主卧电表，户号123456\n到期：2026-10-12", alert.body)
    }

    @Test
    fun `只有标题无正文`() {
        val alert = parse("taskops/todo/8", "【待办提醒】交电费")
        assertEquals("交电费", alert.title)
        assertEquals("", alert.body)
    }

    @Test
    fun `标题为空时回退到默认标题`() {
        val alert = parse("taskops/todo/9", "【待办提醒】\n正文在这里")
        assertEquals(fallback, alert.title)
        assertEquals("正文在这里", alert.body)
    }

    @Test
    fun `无前缀时整段作为正文`() {
        val alert = parse("taskops/todo/10", "服务端只发了正文")
        assertEquals(fallback, alert.title)
        assertEquals("服务端只发了正文", alert.body)
    }

    @Test
    fun `空 payload 不崩溃`() {
        val alert = parse("taskops/todo/11", "")
        assertEquals(fallback, alert.title)
        assertEquals("", alert.body)
    }

    @Test
    fun `CRLF 换行也能正确解析`() {
        val alert = parse("taskops/todo/12", "【待办提醒】交电费\r\n第一行\r\n第二行")
        assertEquals("交电费", alert.title)
        assertEquals("第一行\n第二行", alert.body)
    }

    @Test
    fun `主题尾段非数字时 taskId 为 null（野消息）`() {
        val alert = parse("taskops/todo/not-a-number", "【待办提醒】x")
        assertNull(alert.taskId)
        assertEquals("x", alert.title)
    }

    @Test
    fun `marker 常量与 push-server 一致`() {
        assertEquals("【待办提醒】", MARKER)
    }
}
