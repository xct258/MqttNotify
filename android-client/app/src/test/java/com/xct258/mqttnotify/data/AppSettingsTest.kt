package com.xct258.mqttnotify.data

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class AppSettingsTest {

    @Test
    fun `默认使用公网 WSS 入口`() {
        val s = AppSettings()
        assertEquals("push-server.o1-1.xct258.top", s.host)
        assertEquals(443, s.port)
        assertEquals("/mqtt", s.effectiveWsPath)
        assertEquals("taskops/todo", s.topicBase)
        assertEquals("taskops/todo/#", s.subscribeTopic)
        assertEquals(MqttTransport.WSS, s.transport)
        assertTrue(s.isValid)
        assertEquals(
            "wss://push-server.o1-1.xct258.top/mqtt",
            s.transport.displayUrl(s.host, s.port, s.effectiveWsPath),
        )
    }

    @Test
    fun `订阅主题拼接通配符并去掉多余斜杠`() {
        assertEquals("a/b/#", AppSettings(topicBase = "a/b").subscribeTopic)
        assertEquals("a/b/#", AppSettings(topicBase = "a/b/").subscribeTopic)
        assertEquals("a/b/#", AppSettings(topicBase = "  a/b//  ").subscribeTopic)
        // 直接粘贴完整通配主题时，不能二次拼接成非法的 a/b/#/#
        assertEquals("a/b/#", AppSettings(topicBase = "a/b/#").subscribeTopic)
        assertEquals("#", AppSettings(topicBase = "#").subscribeTopic)
        assertEquals("#", AppSettings(topicBase = "  #  ").subscribeTopic)
    }

    @Test
    fun `normalized 会去掉用户粘贴的通配符尾缀`() {
        assertEquals("a/b", AppSettings(topicBase = "a/b/#").normalized().topicBase)
        assertEquals("#", AppSettings(topicBase = " # ").normalized().topicBase)
    }

    @Test
    fun `展示地址按传输方式拼装`() {
        assertEquals("mqtt://h:1883", MqttTransport.TCP.displayUrl("h", 1883, "/mqtt"))
        assertEquals("ws://h:9001/mqtt", MqttTransport.WS.displayUrl("h", 9001, "/mqtt"))
        assertEquals("wss://h/mqtt", MqttTransport.WSS.displayUrl("h", 443, "/mqtt"))
        assertEquals("wss://h:8443/mqtt", MqttTransport.WSS.displayUrl("h", 8443, "/mqtt"))
    }

    @Test
    fun `host 为空时配置无效`() {
        assertFalse(AppSettings(host = "").isValid)
        assertTrue(AppSettings(host = "192.168.1.10").isValid)
    }

    @Test
    fun `端口越界时配置无效`() {
        assertFalse(AppSettings(host = "h", port = 0).isValid)
        assertFalse(AppSettings(host = "h", port = 70000).isValid)
        assertTrue(AppSettings(host = "h", port = 1).isValid)
        assertTrue(AppSettings(host = "h", port = 65535).isValid)
    }

    @Test
    fun `主题为空时配置无效`() {
        assertFalse(AppSettings(host = "h", topicBase = "  ").isValid)
    }

    @Test
    fun `normalized 会去掉首尾空白`() {
        val s = AppSettings(
            host = "  10.0.0.1 ",
            topicBase = " taskops/todo/ ",
            serverName = " taskops ",
            clientId = " cid ",
        ).normalized()
        assertEquals("10.0.0.1", s.host)
        assertEquals("taskops/todo", s.topicBase)
        assertEquals("taskops", s.serverName)
        assertEquals("cid", s.clientId)
    }
}
