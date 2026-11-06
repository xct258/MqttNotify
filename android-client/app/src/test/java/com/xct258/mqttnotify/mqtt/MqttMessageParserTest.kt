package com.xct258.mqttnotify.mqtt

import com.xct258.mqttnotify.mqtt.MqttMessageParser.Kind
import com.xct258.mqttnotify.mqtt.MqttMessageParser.MARKER
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * 下行 payload 解析的契约测试。
 *
 * - 待办：`【待办提醒】{title}\n{message}`
 * - 服务：`【服务通知】{server}\n【服务】{title}\n【状态】{status}\n\n{message}`
 * - 主题尾段为 task_id。
 */
class MqttMessageParserTest {

    private val fallback = "新提醒"

    private fun parse(topic: String, payload: String) =
        MqttMessageParser.parse(topic, payload.toByteArray(Charsets.UTF_8), fallback)

    @Test
    fun `待办：带标题与多行正文`() {
        val alert = parse(
            "taskops/todo/7",
            "【待办提醒】交电费\n主卧电表，户号123456\n到期：2026-10-12",
        )
        assertEquals(Kind.TODO, alert.kind)
        assertEquals(7, alert.taskId)
        assertEquals("交电费", alert.title)
        assertEquals("主卧电表，户号123456\n到期：2026-10-12", alert.body)
    }

    @Test
    fun `待办：只有标题无正文`() {
        val alert = parse("taskops/todo/8", "【待办提醒】交电费")
        assertEquals(Kind.TODO, alert.kind)
        assertEquals("交电费", alert.title)
        assertEquals("", alert.body)
    }

    @Test
    fun `待办：标题为空时回退到默认标题`() {
        val alert = parse("taskops/todo/9", "【待办提醒】\n正文在这里")
        assertEquals(Kind.TODO, alert.kind)
        assertEquals(fallback, alert.title)
        assertEquals("正文在这里", alert.body)
    }

    @Test
    fun `服务：完整头 + Markdown 正文`() {
        val alert = parse(
            "server/khx-live",
            "【服务通知】o1-debian-1\n【服务】录播备份上传脚本\n【状态】ok\n\n上传成功\n\n- 文件 A\n- 文件 B",
        )
        assertEquals(Kind.SERVICE, alert.kind)
        assertEquals("o1-debian-1", alert.serverName)
        assertEquals("录播备份上传脚本", alert.serviceName)
        assertEquals("ok", alert.status)
        assertEquals("o1-debian-1", alert.title)
        assertEquals("上传成功\n\n- 文件 A\n- 文件 B", alert.body)
        assertNull(alert.taskId)
    }

    @Test
    fun `服务：没有状态行也能解析`() {
        val alert = parse(
            "server/khx-live",
            "【服务通知】nas\n【服务】备份\n\n完成",
        )
        assertEquals(Kind.SERVICE, alert.kind)
        assertEquals("nas", alert.serverName)
        assertEquals("备份", alert.serviceName)
        assertNull(alert.status)
        assertEquals("完成", alert.body)
    }

    @Test
    fun `服务：正文以字段样式开头也不会被吞掉`() {
        val alert = parse(
            "server/khx-live",
            "【服务通知】nas\n【服务】备份\n\n【服务】这行属于正文",
        )
        assertEquals("【服务】这行属于正文", alert.body)
    }

    @Test
    fun `服务：缺少空行时遇到正文立即停止解析头`() {
        val alert = parse(
            "server/khx-live",
            "【服务通知】nas\n【服务】备份\n上传成功",
        )
        assertEquals("nas", alert.serverName)
        assertEquals("备份", alert.serviceName)
        assertEquals("上传成功", alert.body)
    }

    @Test
    fun `未知格式：整段作为正文`() {
        val alert = parse("taskops/todo/10", "服务端只发了正文")
        assertEquals(Kind.UNKNOWN, alert.kind)
        assertEquals(fallback, alert.title)
        assertEquals("服务端只发了正文", alert.body)
    }

    @Test
    fun `空 payload 不崩溃`() {
        val alert = parse("taskops/todo/11", "")
        assertEquals(Kind.UNKNOWN, alert.kind)
        assertEquals(fallback, alert.title)
        assertEquals("", alert.body)
    }

    @Test
    fun `CRLF 换行也能正确解析`() {
        val alert = parse("taskops/todo/12", "【待办提醒】交电费\r\n第一行\r\n第二行")
        assertEquals(Kind.TODO, alert.kind)
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
        assertEquals("【服务通知】", MqttMessageParser.SERVICE_MARKER)
    }
}
