package com.xct258.mqttnotify.ui

import org.junit.Assert.assertEquals
import org.junit.Assert.assertTrue
import org.junit.Test

class MarkdownTextTest {

    @Test
    fun `表格转换成保留结构的纯文本`() {
        val text = markdownToPlainText(
            """
            主机：nas-backup

            | 任务 | 状态 | 大小 |
            | --- | --- | --- |
            | docker | 成功 | 998.2 KB |
            """.trimIndent(),
        )

        assertTrue(text.contains("任务 | 状态 | 大小"))
        assertTrue(text.contains("docker | 成功 | 998.2 KB"))
        assertTrue(!text.contains("| --- | --- |"))
    }

    @Test
    fun `cron 星号不会被当成斜体`() {
        assertEquals(
            "调度：0 3 * * *（Asia/Shanghai）",
            markdownToPlainText("调度：0 3 * * *（Asia/Shanghai）"),
        )
    }

    @Test
    fun `普通斜体和粗体仍然生效`() {
        assertEquals("重要 内容", markdownToPlainText("**重要** *内容*"))
    }
}
