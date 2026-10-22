using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProjectRecorder.Models;
using ProjectRecorder.Services;

namespace ProjectRecorder.Views;

/// <summary>
/// 通用模块「导出」页：默认导出本班次，也可选历史日期导出当天合计（xlsx）。
/// </summary>
public partial class ExportView : UserControl
{
    private DataService? _data;
    private InactivityMonitor? _monitor;
    private DateTime _exportDate = DateTime.Today;
    private bool _exportToday = true;

    public ExportView()
    {
        InitializeComponent();
    }

    /// <summary>由 MainWindow 在构造后注入会话依赖。</summary>
    public void Initialize(DataService data, InactivityMonitor monitor)
    {
        _data = data;
        _monitor = monitor;
    }

    /// <summary>进入导出页：重置为“今日（本班次）”。</summary>
    public void Reset()
    {
        _exportToday = true;
        _exportDate = WorkloadRecord.GetShiftDate(DateTime.Now);
        RefreshDateButton();
    }

    // 通用「导出」入口：默认导出本班次；选日期后导出该日期全天合计（xlsx，冻结标题行）
    private void BtnExport_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (_data == null) return;

        DateTime day;
        string? shift = null;
        string label;
        string fileName;
        if (_exportToday)
        {
            var now = DateTime.Now;
            day = WorkloadRecord.GetShiftDate(now);
            shift = WorkloadRecord.GetShiftName(now);
            label = $"{day:yyyy-MM-dd} {shift}";
            fileName = $"工作量_{day:yyyy-MM-dd}_{shift}.xlsx";
        }
        else
        {
            day = _exportDate.Date;
            label = $"{day:yyyy-MM-dd}";
            fileName = $"工作量_{day:yyyy-MM-dd}.xlsx";
        }

        var all = _data.LoadWorkload();
        if (all == null) return;
        var list = all
            .Where(x => x.WorkDate.Date == day.Date && (shift == null || x.Shift == shift))
            .ToList();

        if (list.Count == 0)
        {
            MessageBox.Show($"{label} 暂无工作量记录。", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var fileDlg = new SaveFileDialog
        {
            Title = "导出工作量",
            Filter = "Excel 文件|*.xlsx",
            FileName = fileName
        };
        var owner = Window.GetWindow(this);
        bool? picked = owner == null ? fileDlg.ShowDialog() : fileDlg.ShowDialog(owner);
        if (picked != true) return;

        try
        {
            int rowCount = ExcelExporter.ExportWorkload(fileDlg.FileName, day, list, shift);
            MessageBox.Show($"导出成功，共 {rowCount} 项合计：\n{fileDlg.FileName}", "导出",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // 导出页：默认“今日”（本班次日期），上限封顶到班次今日，不能选未来
    private void RefreshDateButton()
    {
        DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
        if (!_exportToday && _exportDate.Date >= today.Date)
        {
            _exportToday = true;
            _exportDate = today;
        }
        ExpPickDate.Content = _exportToday ? "今日" : _exportDate.ToString("yyyy-MM-dd");
        ExpNextDay.IsEnabled = !_exportToday;
    }

    private void ExpPrevDay_Click(object sender, RoutedEventArgs e) => ChangeDate(-1);
    private void ExpNextDay_Click(object sender, RoutedEventArgs e) => ChangeDate(1);

    private void ChangeDate(int days)
    {
        _monitor?.NotifyActivity();
        DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
        DateTime baseDay = _exportToday ? today : _exportDate.Date;
        if (baseDay > today) baseDay = today;
        DateTime next = baseDay.AddDays(days);
        if (next >= today.Date)
        {
            _exportToday = true;
            _exportDate = today;
        }
        else
        {
            _exportToday = false;
            _exportDate = next;
        }
        RefreshDateButton();
    }

    private void ExpPickDate_Click(object sender, RoutedEventArgs e)
    {
        _monitor?.NotifyActivity();
        DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
        if (!_exportToday && _exportDate.Date > today.Date)
        {
            _exportToday = true;
            _exportDate = today;
            RefreshDateButton();
        }
        try
        {
            ExpCal.DisplayDateStart = null;
            ExpCal.DisplayDateEnd = today;
            ExpCal.BlackoutDates.Clear();
            if (today.Date < new DateTime(9999, 12, 31))
                ExpCal.BlackoutDates.Add(new CalendarDateRange(today.Date.AddDays(1), new DateTime(9999, 12, 31)));
        }
        catch
        {
            // 日历限制设置失败不影响打开
        }
        ExpCal.SelectedDate = _exportToday ? today : _exportDate;
        ExpCal.DisplayDate = ExpCal.SelectedDate ?? today;
        if (ExpCal.DisplayDate > today) ExpCal.DisplayDate = today;
        ExpPopCalendar.IsOpen = true;
    }

    private void ExpCal_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        _monitor?.NotifyActivity();
        if (ExpCal.SelectedDate.HasValue)
        {
            DateTime today = WorkloadRecord.GetShiftDate(DateTime.Now);
            DateTime picked = ExpCal.SelectedDate.Value.Date;
            if (picked >= today.Date)
            {
                _exportToday = true;
                _exportDate = today;
            }
            else
            {
                _exportDate = picked;
                _exportToday = false;
            }
            RefreshDateButton();
        }
        ExpPopCalendar.IsOpen = false;
    }
}
