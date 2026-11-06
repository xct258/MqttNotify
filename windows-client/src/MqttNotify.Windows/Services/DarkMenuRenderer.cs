using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace MqttNotify.Windows.Services;

/// <summary>
/// 托盘右键菜单的暗色渲染器。
/// 不用系统默认菜单，统一控制背景、圆角悬停、文字垂直居中和分隔线。
/// </summary>
internal sealed class DarkMenuRenderer : ToolStripRenderer
{
    private static readonly Color Background = Color.FromArgb(15, 23, 42);
    private static readonly Color Border = Color.FromArgb(51, 65, 85);
    private static readonly Color Hover = Color.FromArgb(30, 41, 59);
    private static readonly Color Text = Color.FromArgb(226, 232, 240);
    private static readonly Color DisabledText = Color.FromArgb(100, 116, 139);

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e) =>
        e.Graphics.Clear(Background);

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
        // 不显示图标列，避免文字左侧出现一块空白
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled)
        {
            return;
        }

        var bounds = new Rectangle(4, 2, e.Item.Width - 8, e.Item.Height - 4);
        using var path = RoundedRect(bounds, 7);
        using var brush = new SolidBrush(Hover);
        var oldMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
        e.Graphics.SmoothingMode = oldMode;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        var flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;

        var rect = new Rectangle(
            e.TextRectangle.Left,
            0,
            Math.Max(0, e.TextRectangle.Width),
            e.Item.Height);

        TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.Item.Font,
            rect,
            e.Item.Enabled ? Text : DisabledText,
            flags);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        var y = e.Item.Height / 2;
        using var pen = new Pen(Border);
        e.Graphics.DrawLine(pen, 10, y, e.Item.Width - 10, y);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
