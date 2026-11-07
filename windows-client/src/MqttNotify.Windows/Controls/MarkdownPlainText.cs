using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MqttNotify.Windows.Controls;

/// <summary>
/// 把 Markdown 转成系统通知能显示的纯文本。
/// 托盘气泡 / Windows 通知不支持富文本，直接传原文会露出 **、#、` 等标记；
/// 这里保留标题、列表、引用、代码和表格的换行结构，只去掉格式符号。
/// </summary>
public static class MarkdownPlainText
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string ToPlainText(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return string.Empty;
        }

        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        var builder = new StringBuilder();
        AppendBlocks(document, builder);

        var text = Regex.Replace(builder.ToString(), @"\n{3,}", "\n\n");
        return text.Trim();
    }

    private static void AppendBlocks(ContainerBlock blocks, StringBuilder builder)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    AppendInlines(heading.Inline, builder);
                    builder.Append("\n\n");
                    break;

                case ParagraphBlock paragraph:
                    AppendInlines(paragraph.Inline, builder);
                    builder.Append("\n\n");
                    break;

                case ListBlock list:
                    var index = 1;
                    foreach (var item in list)
                    {
                        if (item is not ListItemBlock listItem)
                        {
                            continue;
                        }

                        builder.Append(list.IsOrdered ? $"{index}. " : "• ");
                        var first = true;
                        foreach (var child in listItem)
                        {
                            if (child is ParagraphBlock itemParagraph)
                            {
                                if (!first)
                                {
                                    builder.Append(' ');
                                }
                                AppendInlines(itemParagraph.Inline, builder);
                                first = false;
                            }
                        }
                        builder.Append('\n');
                        index++;
                    }
                    builder.Append('\n');
                    break;

                case QuoteBlock quote:
                    builder.Append("│ ");
                    AppendBlocks(quote, builder);
                    break;

                case FencedCodeBlock code:
                    builder.Append(code.Lines.ToString().TrimEnd());
                    builder.Append("\n\n");
                    break;

                case CodeBlock code:
                    builder.Append(code.Lines.ToString().TrimEnd());
                    builder.Append("\n\n");
                    break;

                case Table table:
                    AppendTable(table, builder);
                    break;

                case ThematicBreakBlock:
                    builder.Append("────────\n\n");
                    break;
            }
        }
    }

    private static void AppendTable(Table table, StringBuilder builder)
    {
        foreach (var rowBlock in table)
        {
            if (rowBlock is not TableRow row)
            {
                continue;
            }

            var cells = new List<TableCell>();
            foreach (var cellBlock in row)
            {
                if (cellBlock is TableCell cell)
                {
                    cells.Add(cell);
                }
            }

            for (var i = 0; i < cells.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(" | ");
                }

                var first = true;
                foreach (var child in cells[i])
                {
                    if (child is not ParagraphBlock paragraph)
                    {
                        continue;
                    }
                    if (!first)
                    {
                        builder.Append(' ');
                    }
                    AppendInlines(paragraph.Inline, builder);
                    first = false;
                }
            }
            builder.Append('\n');

            if (row.IsHeader)
            {
                builder.Append(new string('─', 24)).Append('\n');
            }
        }

        builder.Append('\n');
    }

    private static void AppendInlines(ContainerInline? container, StringBuilder builder)
    {
        if (container is null)
        {
            return;
        }

        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;

                case CodeInline code:
                    builder.Append(code.Content);
                    break;

                case EmphasisInline emphasis:
                    AppendInlines(emphasis, builder);
                    break;

                case LinkInline link:
                    var before = builder.Length;
                    AppendInlines(link, builder);
                    if (builder.Length == before && !string.IsNullOrWhiteSpace(link.Url))
                    {
                        builder.Append(link.Url);
                    }
                    break;

                case LineBreakInline lineBreak:
                    builder.Append(lineBreak.IsHard ? '\n' : ' ');
                    break;
            }
        }
    }
}
