using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MqttNotify.Windows.Controls;

/// <summary>
/// 轻量 Markdown 渲染控件：把常见的标题、段落、列表、引用、代码和行内格式
/// 转成 TextBlock 的 Inlines，避免为提醒正文引入浏览器 / WebView。
/// </summary>
public sealed class MarkdownTextBlock : TextBlock
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private static readonly Brush CodeBackground = Freeze("#1E293B");
    private static readonly Brush QuoteForeground = Freeze("#94A3B8");
    private static readonly Brush LinkForeground = Freeze("#A5B4FC");

    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownTextBlock),
        new PropertyMetadata(string.Empty, OnMarkdownChanged));

    public MarkdownTextBlock()
    {
        TextWrapping = TextWrapping.Wrap;
    }

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private static void OnMarkdownChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MarkdownTextBlock)d).Render();

    private void Render()
    {
        Inlines.Clear();

        var source = Markdown;
        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        var document = Markdig.Markdown.Parse(source, Pipeline);
        foreach (var block in document)
        {
            AppendBlock(block);
        }
    }

    private void AppendBlock(Markdig.Syntax.Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                AppendInlines(heading.Inline);
                AddLineBreak();
                break;

            case ParagraphBlock paragraph:
                AppendInlines(paragraph.Inline);
                AddLineBreak();
                break;

            case ListBlock list:
                AppendList(list);
                break;

            case QuoteBlock quote:
                foreach (var child in quote)
                {
                    if (child is ParagraphBlock quoteParagraph)
                    {
                        var prefix = new Run("│ ") { Foreground = QuoteForeground };
                        Inlines.Add(prefix);
                        AppendInlines(quoteParagraph.Inline, QuoteForeground);
                        AddLineBreak();
                    }
                }
                break;

            case FencedCodeBlock code:
                AppendCode(code.Lines.ToString());
                break;

            case CodeBlock code:
                AppendCode(code.Lines.ToString());
                break;

            case ThematicBreakBlock:
                Inlines.Add(new Run("────────") { Foreground = QuoteForeground });
                AddLineBreak();
                break;
        }
    }

    private void AppendList(ListBlock list)
    {
        var index = 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock listItem)
            {
                continue;
            }

            var marker = list.IsOrdered ? $"{index}. " : "• ";
            Inlines.Add(new Run(marker) { Foreground = LinkForeground });

            var first = true;
            foreach (var child in listItem)
            {
                if (child is ParagraphBlock paragraph)
                {
                    if (!first)
                    {
                        AddLineBreak();
                    }
                    AppendInlines(paragraph.Inline);
                    first = false;
                }
            }
            AddLineBreak();
            index++;
        }
    }

    private void AppendInlines(ContainerInline? container, Brush? foreground = null)
    {
        if (container is not null)
        {
            BuildInlines(container, Inlines, foreground);
        }
    }

    private void BuildInlines(ContainerInline container, InlineCollection target, Brush? foreground)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    var run = new Run(literal.Content.ToString());
                    if (foreground is not null)
                    {
                        run.Foreground = foreground;
                    }
                    target.Add(run);
                    break;

                case CodeInline code:
                    target.Add(new Run(code.Content)
                    {
                        FontFamily = new FontFamily("Consolas, Cascadia Mono, Microsoft YaHei UI"),
                        Background = CodeBackground,
                        Foreground = Brushes.White,
                    });
                    break;

                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterCount >= 2)
                    {
                        span.FontWeight = FontWeights.SemiBold;
                    }
                    else
                    {
                        span.FontStyle = FontStyles.Italic;
                    }
                    BuildInlines(emphasis, span.Inlines, foreground);
                    target.Add(span);
                    break;

                case LinkInline link:
                    var hyperlink = new Hyperlink
                    {
                        Foreground = LinkForeground,
                        TextDecorations = System.Windows.TextDecorations.Underline,
                    };
                    if (!string.IsNullOrWhiteSpace(link.Url))
                    {
                        hyperlink.NavigateUri = new Uri(link.Url, UriKind.RelativeOrAbsolute);
                        hyperlink.RequestNavigate += OnLinkNavigate;
                    }
                    BuildInlines(link, hyperlink.Inlines, foreground);
                    target.Add(hyperlink);
                    break;

                case LineBreakInline:
                    target.Add(new LineBreak());
                    break;
            }
        }
    }

    private void AppendCode(string code)
    {
        Inlines.Add(new Run(code.TrimEnd())
        {
            FontFamily = new FontFamily("Consolas, Cascadia Mono, Microsoft YaHei UI"),
            Background = CodeBackground,
            Foreground = Brushes.White,
        });
        AddLineBreak();
    }

    private void AddLineBreak() =>
        Inlines.Add(new LineBreak());

    private void OnLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 打不开链接不影响消息展示
        }
        e.Handled = true;
    }

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
