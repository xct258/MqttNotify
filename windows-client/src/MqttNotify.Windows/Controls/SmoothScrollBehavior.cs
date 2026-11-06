using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MqttNotify.Windows.Controls;

/// <summary>
/// 给 ScrollViewer / ListBox 的鼠标滚轮增加缓动动画，避免一格一格硬跳。
/// 用 CompositionTarget.Rendering 跟随显示器刷新，比固定 DispatcherTimer 更平滑；
/// 不改变 ScrollViewer 原有滚动条和键盘行为。
/// </summary>
public static class SmoothScrollBehavior
{
    private const double WheelStep = 88;
    private const double DurationMs = 200;
    private static readonly Dictionary<ScrollViewer, ScrollState> States = new();
    private static bool _renderingHooked;

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(SmoothScrollBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) =>
        (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) =>
        element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            element.PreviewMouseWheel += OnPreviewMouseWheel;
        }
        else
        {
            element.PreviewMouseWheel -= OnPreviewMouseWheel;
        }
    }

    private static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject element)
        {
            return;
        }

        var scrollViewer = element as ScrollViewer ?? FindDescendant<ScrollViewer>(element);
        if (scrollViewer is null || scrollViewer.ScrollableHeight <= 0)
        {
            return;
        }

        e.Handled = true;

        var now = Stopwatch.GetTimestamp();
        var current = scrollViewer.VerticalOffset;
        var baseTarget = States.TryGetValue(scrollViewer, out var active) && active.IsActive(now)
            ? active.To
            : current;
        var target = Math.Clamp(
            baseTarget - (e.Delta / 120.0 * WheelStep),
            0,
            scrollViewer.ScrollableHeight);

        States[scrollViewer] = new ScrollState(current, target, now);
        HookRendering();
    }

    private static void OnRendering(object? sender, EventArgs e)
    {
        if (States.Count == 0)
        {
            UnhookRendering();
            return;
        }

        var now = Stopwatch.GetTimestamp();
        var finished = new List<ScrollViewer>();

        foreach (var pair in States)
        {
            var state = pair.Value;
            var t = Math.Clamp((now - state.StartedAt) / (DurationMs / 1000.0 * Stopwatch.Frequency), 0, 1);
            var eased = 1 - Math.Pow(1 - t, 3);
            pair.Key.ScrollToVerticalOffset(state.From + ((state.To - state.From) * eased));

            if (t >= 1)
            {
                finished.Add(pair.Key);
            }
        }

        foreach (var viewer in finished)
        {
            States.Remove(viewer);
        }

        if (States.Count == 0)
        {
            UnhookRendering();
        }
    }

    private static void HookRendering()
    {
        if (_renderingHooked)
        {
            return;
        }

        CompositionTarget.Rendering += OnRendering;
        _renderingHooked = true;
    }

    private static void UnhookRendering()
    {
        if (!_renderingHooked)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRendering;
        _renderingHooked = false;
    }

    private static T? FindDescendant<T>(DependencyObject parent) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var nested = FindDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private sealed record ScrollState(double From, double To, long StartedAt)
    {
        public bool IsActive(long now) =>
            now - StartedAt < DurationMs / 1000.0 * Stopwatch.Frequency;
    }
}
