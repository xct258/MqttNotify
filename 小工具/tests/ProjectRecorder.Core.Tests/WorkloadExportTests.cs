using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using ProjectRecorder.Models;
using ProjectRecorder.Services;
using Xunit;

namespace ProjectRecorder.Core.Tests;

public class WorkloadExportBuilderTests
{
    private static WorkloadProcess Proc(string project, string process, int order, DateTime? created = null)
        => new()
        {
            ProjectName = project,
            ProcessName = process,
            Order = order,
            CreatedTime = created ?? new DateTime(2026, 1, 1)
        };

    private static WorkloadRecord Rec(
        string project, string process, DateTime date, string shift, int qty,
        DateTime? created = null)
        => new()
        {
            ProjectName = project,
            ProcessName = process,
            WorkDate = date,
            Shift = shift,
            Quantity = qty,
            CreatedTime = created ?? date.AddHours(9)
        };

    [Fact]
    public void Summary_FollowsUiProcessOrder_NotAlphabetical()
    {
        var processes = new[]
        {
            Proc("P", "乙", 2),
            Proc("P", "甲", 1),
            Proc("P", "丙", 3)
        };
        var records = new[]
        {
            Rec("P", "丙", DateTime.Today, WorkloadRecord.DayShift, 3),
            Rec("P", "甲", DateTime.Today, WorkloadRecord.DayShift, 1),
            Rec("P", "乙", DateTime.Today, WorkloadRecord.DayShift, 2)
        };

        var rows = WorkloadExportBuilder.BuildSummary(records, processes, new[] { "P" });

        Assert.Equal(new[] { "甲", "乙", "丙" }, rows.Select(x => x.ProcessName));
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(x => x.Quantity));
    }

    [Fact]
    public void Summary_TieBreaksByCreatedTime()
    {
        var t = new DateTime(2026, 1, 1);
        var processes = new[]
        {
            Proc("P", "后建", 1, t.AddDays(1)),
            Proc("P", "先建", 1, t)
        };
        var records = new[]
        {
            Rec("P", "后建", t, WorkloadRecord.DayShift, 1),
            Rec("P", "先建", t, WorkloadRecord.DayShift, 1)
        };

        var rows = WorkloadExportBuilder.BuildSummary(records, processes, new[] { "P" });

        Assert.Equal(new[] { "先建", "后建" }, rows.Select(x => x.ProcessName));
    }

    [Fact]
    public void Summary_FollowsProjectOrderFromUi()
    {
        var processes = new[]
        {
            Proc("A", "工序", 1),
            Proc("B", "工序", 1),
            Proc("通用", "工序", 1)
        };
        var records = new[]
        {
            Rec("A", "工序", DateTime.Today, WorkloadRecord.DayShift, 1),
            Rec("B", "工序", DateTime.Today, WorkloadRecord.DayShift, 1),
            Rec("通用", "工序", DateTime.Today, WorkloadRecord.DayShift, 1)
        };

        var rows = WorkloadExportBuilder.BuildSummary(
            records, processes, new[] { "通用", "B", "A" });

        Assert.Equal(new[] { "通用", "B", "A" }, rows.Select(x => x.ProjectName));
    }

    [Fact]
    public void Summary_UnknownProcessGoesLast()
    {
        var processes = new[] { Proc("P", "已登记", 1) };
        var records = new[]
        {
            Rec("P", "已删除旧工序", DateTime.Today, WorkloadRecord.DayShift, 1),
            Rec("P", "已登记", DateTime.Today, WorkloadRecord.DayShift, 2)
        };

        var rows = WorkloadExportBuilder.BuildSummary(records, processes, new[] { "P" });

        Assert.Equal("已登记", rows[0].ProcessName);
        Assert.Equal("已删除旧工序", rows[1].ProcessName);
    }

    [Fact]
    public void Daily_GroupsByDateShiftAndKeepsUiOrder()
    {
        var processes = new[]
        {
            Proc("P", "乙", 2),
            Proc("P", "甲", 1)
        };
        var d1 = new DateTime(2026, 10, 20);
        var d2 = new DateTime(2026, 10, 21);
        var records = new[]
        {
            Rec("P", "乙", d2, WorkloadRecord.DayShift, 2),
            Rec("P", "甲", d2, WorkloadRecord.DayShift, 1),
            Rec("P", "乙", d2, WorkloadRecord.DayShift, 3),
            Rec("P", "乙", d1, WorkloadRecord.NightShift, 5)
        };

        var rows = WorkloadExportBuilder.BuildDaily(records, processes, new[] { "P" });

        Assert.Equal(3, rows.Count);
        Assert.Equal(d1, rows[0].WorkDate);
        Assert.Equal(5, rows[0].Quantity);
        Assert.Equal(d2, rows[1].WorkDate);
        Assert.Equal("甲", rows[1].ProcessName);
        Assert.Equal(1, rows[1].Quantity);
        Assert.Equal("乙", rows[2].ProcessName);
        Assert.Equal(5, rows[2].Quantity);
    }

    [Fact]
    public void Details_FollowUiOrderWithinDate()
    {
        var processes = new[]
        {
            Proc("P", "乙", 2),
            Proc("P", "甲", 1)
        };
        var d = new DateTime(2026, 10, 22);
        var records = new[]
        {
            Rec("P", "乙", d, WorkloadRecord.DayShift, 1, d.AddHours(10)),
            Rec("P", "甲", d, WorkloadRecord.DayShift, 1, d.AddHours(11))
        };

        var ordered = WorkloadExportBuilder.OrderDetails(records, processes, new[] { "P" });

        Assert.Equal(new[] { "甲", "乙" }, ordered.Select(x => x.ProcessName));
    }
}

public class ExcelExporterTests
{
    private static readonly XNamespace Ns =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static string ReadEntry(ZipArchive zip, string name)
    {
        using var reader = new StreamReader(zip.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static string? CellValue(XDocument sheet, string reference)
    {
        var cell = sheet.Descendants(Ns + "c")
            .FirstOrDefault(c => (string?)c.Attribute("r") == reference);
        if (cell == null) return null;
        return cell.Element(Ns + "v")?.Value
               ?? cell.Descendants(Ns + "t").FirstOrDefault()?.Value;
    }

    private static List<string> SheetNames(ZipArchive zip)
    {
        var wb = XDocument.Parse(ReadEntry(zip, "xl/workbook.xml"));
        return wb.Descendants(Ns + "sheet")
            .Select(x => (string?)x.Attribute("name") ?? string.Empty)
            .ToList();
    }

    [Fact]
    public void ExportWorkload_WritesStyledWorkbook_InUiOrder()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pr_export_{Guid.NewGuid():N}.xlsx");
        try
        {
            var processes = new[]
            {
                new WorkloadProcess { ProjectName = "P", ProcessName = "乙", Order = 2 },
                new WorkloadProcess { ProjectName = "P", ProcessName = "甲", Order = 1 }
            };
            var day = new DateTime(2026, 10, 22);
            var records = new List<WorkloadRecord>
            {
                new() { ProjectName = "P", ProcessName = "乙", WorkDate = day, Shift = WorkloadRecord.DayShift, Quantity = 2 },
                new() { ProjectName = "P", ProcessName = "甲", WorkDate = day, Shift = WorkloadRecord.DayShift, Quantity = 1 }
            };

            int count = ExcelExporter.ExportWorkload(
                path, day, records, null, processes, new[] { "P" });

            Assert.Equal(2, count);
            using var zip = ZipFile.OpenRead(path);
            Assert.NotNull(zip.GetEntry("xl/styles.xml"));
            Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
            Assert.NotNull(zip.GetEntry("xl/worksheets/sheet2.xml"));

            var styles = XDocument.Parse(ReadEntry(zip, "xl/styles.xml"));
            var cellXfs = styles.Descendants(Ns + "cellXfs").First();
            Assert.Equal("9", (string?)cellXfs.Attribute("count"));

            var sheet1 = XDocument.Parse(ReadEntry(zip, "xl/worksheets/sheet1.xml"));
            Assert.Equal("甲", CellValue(sheet1, "B3"));
            Assert.Equal("乙", CellValue(sheet1, "B4"));
            Assert.Equal("1", CellValue(sheet1, "C3"));
            Assert.NotNull(sheet1.Descendants(Ns + "pane").FirstOrDefault());
            Assert.NotNull(sheet1.Descendants(Ns + "autoFilter").FirstOrDefault());
            Assert.NotNull(sheet1.Descendants(Ns + "mergeCell")
                .FirstOrDefault(x => (string?)x.Attribute("ref") == "A1:C1"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExportWorkloadBatch_WritesSummaryDailyAndDetailSheets()
    {
        string path = Path.Combine(Path.GetTempPath(), $"pr_batch_{Guid.NewGuid():N}.xlsx");
        try
        {
            var processes = new[]
            {
                new WorkloadProcess { ProjectName = "P", ProcessName = "乙", Order = 2 },
                new WorkloadProcess { ProjectName = "P", ProcessName = "甲", Order = 1 }
            };
            var d1 = new DateTime(2026, 10, 20);
            var d2 = new DateTime(2026, 10, 21);
            var records = new List<WorkloadRecord>
            {
                new() { ProjectName = "P", ProcessName = "甲", WorkDate = d1, Shift = WorkloadRecord.DayShift, Quantity = 1, CreatedTime = d1.AddHours(9) },
                new() { ProjectName = "P", ProcessName = "乙", WorkDate = d1, Shift = WorkloadRecord.DayShift, Quantity = 2, CreatedTime = d1.AddHours(10) },
                new() { ProjectName = "P", ProcessName = "甲", WorkDate = d2, Shift = WorkloadRecord.NightShift, Quantity = 3, IsBackfill = true }
            };

            int count = ExcelExporter.ExportWorkloadBatch(
                path, d1, d2, null, records, processes, new[] { "P" });

            Assert.Equal(2, count);
            using var zip = ZipFile.OpenRead(path);
            Assert.Equal(new[] { "汇总", "每日汇总", "明细" }, SheetNames(zip));
            Assert.NotNull(zip.GetEntry("xl/worksheets/sheet3.xml"));

            var daily = XDocument.Parse(ReadEntry(zip, "xl/worksheets/sheet2.xml"));
            Assert.Equal("2026-10-20", CellValue(daily, "A3"));
            Assert.Equal("2026-10-21", CellValue(daily, "A5"));

            var detail = XDocument.Parse(ReadEntry(zip, "xl/worksheets/sheet3.xml"));
            // 按日期/班次/UI 工序顺序：d1 甲、d1 乙、d2 甲
            Assert.Equal("甲", CellValue(detail, "B3"));
            Assert.Equal("乙", CellValue(detail, "B4"));
            Assert.Equal("补录", CellValue(detail, "F5"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
