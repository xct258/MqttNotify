package com.xct258.mqttnotify.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.remember
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * 提醒正文的轻量 Markdown 渲染。
 *
 * 支持：标题、段落、无序/有序列表、引用、围栏代码块，以及行内粗体、斜体、
 * 行内代码和链接文本。目标是覆盖 push-server 常见的 Markdown 消息，不追求
 * 完整 CommonMark，也不额外引入渲染库。
 */
@Composable
fun MarkdownText(markdown: String, modifier: Modifier = Modifier) {
    val blocks = remember(markdown) { parseMarkdownBlocks(markdown) }
    val accent = MaterialTheme.colorScheme.primary
    val muted = MaterialTheme.colorScheme.onSurfaceVariant
    val codeBackground = MaterialTheme.colorScheme.surfaceVariant.copy(alpha = 0.55f)

    Column(modifier = modifier, verticalArrangement = Arrangement.spacedBy(4.dp)) {
        blocks.forEach { block ->
            when (block) {
                is MdBlock.Heading -> Text(
                    text = buildInline(block.text, accent, codeBackground),
                    style = MaterialTheme.typography.bodyMedium.copy(
                        fontSize = when (block.level) {
                            1 -> 19.sp
                            2 -> 17.sp
                            else -> 15.sp
                        },
                        fontWeight = FontWeight.Bold,
                    ),
                )

                is MdBlock.Paragraph -> Text(
                    text = buildInline(block.text, accent, codeBackground),
                    style = MaterialTheme.typography.bodyMedium,
                )

                is MdBlock.Bullet -> Row {
                    Text("•", modifier = Modifier.width(16.dp), color = accent)
                    Text(
                        text = buildInline(block.text, accent, codeBackground),
                        style = MaterialTheme.typography.bodyMedium,
                    )
                }

                is MdBlock.Numbered -> Row {
                    Text("${block.number}.", modifier = Modifier.width(24.dp), color = accent)
                    Text(
                        text = buildInline(block.text, accent, codeBackground),
                        style = MaterialTheme.typography.bodyMedium,
                    )
                }

                is MdBlock.Quote -> Text(
                    text = buildInline(block.text, accent, codeBackground),
                    style = MaterialTheme.typography.bodyMedium,
                    color = muted,
                    modifier = Modifier.padding(start = 8.dp),
                )

                is MdBlock.Code -> Text(
                    text = block.text,
                    fontFamily = FontFamily.Monospace,
                    fontSize = 12.sp,
                    modifier = Modifier
                        .fillMaxWidth()
                        .background(codeBackground, RoundedCornerShape(6.dp))
                        .padding(8.dp),
                )
            }
        }
    }
}

private sealed interface MdBlock {
    data class Heading(val level: Int, val text: String) : MdBlock
    data class Paragraph(val text: String) : MdBlock
    data class Bullet(val text: String) : MdBlock
    data class Numbered(val number: Int, val text: String) : MdBlock
    data class Quote(val text: String) : MdBlock
    data class Code(val text: String) : MdBlock
}

private val inlinePattern = Regex(
    """(\*\*[^*]+\*\*|__[^_]+__|\*[^*]+\*|_[^_]+_|`[^`]+`|\[[^\]]+]\([^)]+\))"""
)

private fun buildInline(text: String, accent: Color, codeBackground: Color): AnnotatedString =
    buildAnnotatedString {
        var last = 0
        for (match in inlinePattern.findAll(text)) {
            append(text.substring(last, match.range.first))
            val token = match.value
            when {
                token.startsWith("**") || token.startsWith("__") ->
                    withStyle(SpanStyle(fontWeight = FontWeight.Bold)) {
                        append(token.substring(2, token.length - 2))
                    }

                token.startsWith("`") ->
                    withStyle(SpanStyle(fontFamily = FontFamily.Monospace, background = codeBackground)) {
                        append(token.substring(1, token.length - 1))
                    }

                token.startsWith("[") -> {
                    val close = token.indexOf(']')
                    val label = if (close > 1) token.substring(1, close) else token
                    withStyle(SpanStyle(color = accent, textDecoration = TextDecoration.Underline)) {
                        append(label)
                    }
                }

                else ->
                    withStyle(SpanStyle(fontStyle = FontStyle.Italic)) {
                        append(token.substring(1, token.length - 1))
                    }
            }
            last = match.range.last + 1
        }
        append(text.substring(last))
    }

private fun parseMarkdownBlocks(markdown: String): List<MdBlock> {
    val lines = markdown.replace("\r\n", "\n").split('\n')
    val blocks = mutableListOf<MdBlock>()
    var i = 0

    while (i < lines.size) {
        val line = lines[i]
        val trimmed = line.trimStart()

        when {
            trimmed.isBlank() -> i++

            trimmed.startsWith("```") -> {
                i++
                val code = mutableListOf<String>()
                while (i < lines.size && !lines[i].trimStart().startsWith("```")) {
                    code.add(lines[i])
                    i++
                }
                if (i < lines.size) i++
                blocks.add(MdBlock.Code(code.joinToString("\n")))
            }

            Regex("^#{1,6}\\s+").containsMatchIn(trimmed) -> {
                val level = trimmed.takeWhile { it == '#' }.length
                blocks.add(MdBlock.Heading(level, trimmed.substring(level).trimStart()))
                i++
            }

            trimmed.startsWith(">") -> {
                val quote = mutableListOf<String>()
                while (i < lines.size && lines[i].trimStart().startsWith(">")) {
                    quote.add(lines[i].trimStart().removePrefix(">").trimStart())
                    i++
                }
                blocks.add(MdBlock.Quote(quote.joinToString("\n")))
            }

            Regex("^[-*+]\\s+").containsMatchIn(trimmed) -> {
                blocks.add(MdBlock.Bullet(trimmed.replaceFirst(Regex("^[-*+]\\s+"), "")))
                i++
            }

            Regex("^\\d+[.)]\\s+").containsMatchIn(trimmed) -> {
                val number = trimmed.takeWhile { it.isDigit() }.toIntOrNull() ?: 1
                blocks.add(MdBlock.Numbered(number, trimmed.replaceFirst(Regex("^\\d+[.)]\\s+"), "")))
                i++
            }

            else -> {
                val paragraph = mutableListOf<String>()
                while (i < lines.size && lines[i].isNotBlank() && !isBlockStart(lines[i])) {
                    paragraph.add(lines[i])
                    i++
                }
                if (paragraph.isEmpty()) {
                    paragraph.add(lines[i])
                    i++
                }
                blocks.add(MdBlock.Paragraph(paragraph.joinToString("\n")))
            }
        }
    }

    return blocks
}

private fun isBlockStart(line: String): Boolean {
    val trimmed = line.trimStart()
    return trimmed.startsWith("```") ||
        trimmed.startsWith(">") ||
        Regex("^#{1,6}\\s+").containsMatchIn(trimmed) ||
        Regex("^[-*+]\\s+").containsMatchIn(trimmed) ||
        Regex("^\\d+[.)]\\s+").containsMatchIn(trimmed)
}

/** 系统通知不支持 Markdown 富文本，转换成保留结构的纯文本。 */
fun markdownToPlainText(markdown: String): String {
    if (markdown.isBlank()) return ""

    val builder = StringBuilder()
    parseMarkdownBlocks(markdown).forEach { block ->
        when (block) {
            is MdBlock.Heading -> builder.append(stripInline(block.text)).append("\n\n")
            is MdBlock.Paragraph -> builder.append(stripInline(block.text)).append("\n\n")
            is MdBlock.Bullet -> builder.append("• ").append(stripInline(block.text)).append('\n')
            is MdBlock.Numbered -> builder.append("${block.number}. ").append(stripInline(block.text)).append('\n')
            is MdBlock.Quote -> builder.append("│ ").append(stripInline(block.text)).append('\n')
            is MdBlock.Code -> builder.append(block.text).append("\n\n")
        }
    }

    return builder.toString()
        .replace(Regex("\\n{3,}"), "\n\n")
        .trim()
}

private fun stripInline(text: String): String {
    var value = text
    value = Regex("""\[([^\]]+)]\([^)]+\)""").replace(value, "$1")
    value = Regex("""\*\*([^*]+)\*\*""").replace(value, "$1")
    value = Regex("""__([^_]+)__""").replace(value, "$1")
    value = Regex("""`([^`]+)`""").replace(value, "$1")
    value = Regex("""\*([^*]+)\*""").replace(value, "$1")
    value = Regex("""_([^_]+)_""").replace(value, "$1")
    return value
}
