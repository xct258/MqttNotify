using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ProjectRecorder.Models;

namespace ProjectRecorder.Services;

/// <summary>
/// 极简 xlsx 导出（无第三方依赖）：汇总 / 每日汇总 / 明细。
/// 工序顺序与工作量界面一致（项目按左侧顺序，工序按 Order → CreatedTime）；
/// 表格带标题、表头填色、斑马纹、边框、冻结表头、筛选和打印适配。
/// xlsx 本质是 zip 包，这里用 inline 字符串直写，避免 sharedStrings 表。
/// </summary>
public static class ExcelExporter
{
    private sealed class SheetData
    {
        public string Name = string.Empty;
        public string Title = string.Empty;
        public string[] Headers = Array.Empty<string>();
        public List<string[]> Rows { get; } = new();
        public List<bool> RowShaded { get; } = new();
        public bool[] Numeric = Array.Empty<bool>();
        public double[] Caps = Array.Empty<double>();
        public List<string> Merges { get; } = new();
        public int TotalRowIndex = -1;
        public string TabColor = "FF2563EB";
    }

    // ---------- 单日导出（默认本班次或指定日期） ----------

    public static int ExportWorkload(
        string path,
        DateTime day,
        List<WorkloadRecord> records,
        string? shift = null,
        IReadOnlyList<WorkloadProcess>? processes = null,
        IReadOnlyList<string>? projectOrder = null)
    {
        var processList = processes ?? Array.Empty<WorkloadProcess>();
        var summary = WorkloadExportBuilder.BuildSummary(records, processList, projectOrder);
        var details = WorkloadExportBuilder.OrderDetails(records, processList, projectOrder);

        string title = shift == null
            ? $"工作量统计（{day:yyyy-MM-dd}）"
            : $"工作量统计（{day:yyyy-MM-dd} {shift}）";

        WriteWorkbook(path, new[]
        {
            BuildSummarySheet(title, summary),
            BuildDetailSheet(title, details)
        });
        return summary.Count;
    }

    // ---------- 批量导出（日期范围，生成 汇总 / 每日汇总 / 明细） ----------

    public static int ExportWorkloadBatch(
        string path,
        DateTime start,
        DateTime end,
        string? shift,
        List<WorkloadRecord> records,
        IReadOnlyList<WorkloadProcess>? processes = null,
        IReadOnlyList<string>? projectOrder = null)
    {
        DateTime from = start.Date;
        DateTime to = end.Date;
        if (from > to) (from, to) = (to, from);

        var filtered = records
            .Where(x => x.WorkDate.Date >= from && x.WorkDate.Date <= to)
            .Where(x => shift == null || x.Shift == shift)
            .ToList();

        var processList = processes ?? Array.Empty<WorkloadProcess>();
        var summary = WorkloadExportBuilder.BuildSummary(filtered, processList, projectOrder);
        var daily = WorkloadExportBuilder.BuildDaily(filtered, processList, projectOrder);
        var details = WorkloadExportBuilder.OrderDetails(filtered, processList, projectOrder);

        string range = from == to ? $"{from:yyyy-MM-dd}" : $"{from:yyyy-MM-dd} ~ {to:yyyy-MM-dd}";
        if (!string.IsNullOrEmpty(shift)) range += $" {shift}";
        string title = $"工作量统计（{range}）";

        WriteWorkbook(path, new[]
        {
            BuildSummarySheet(title, summary),
            BuildDailySheet(title, daily),
            BuildDetailSheet(title, details)
        });
        return summary.Count;
    }

    // ---------- 三张表的行数据 ----------

    private static SheetData BuildSummarySheet(string title, List<WorkloadSummaryRow> summary)
    {
        var sheet = new SheetData
        {
            Name = "汇总",
            Title = title,
            Headers = new[] { "项目", "工序", "数量" },
            Numeric = new[] { false, false, true },
            Caps = new[] { 50d, 50d, 12d },
            TabColor = "FF2563EB"
        };

        // 同一项目只写一次项目名，并纵向合并；斑马纹按项目分组交替，合并单元格颜色才一致
        bool shade = false;
        int i = 0;
        while (i < summary.Count)
        {
            int j = i;
            while (j + 1 < summary.Count &&
                   string.Equals(summary[j + 1].ProjectName, summary[i].ProjectName, StringComparison.Ordinal))
                j++;

            for (int k = i; k <= j; k++)
            {
                sheet.Rows.Add(new[]
                {
                    k == i ? summary[k].ProjectName : string.Empty,
                    summary[k].ProcessName,
                    summary[k].Quantity.ToString(CultureInfo.InvariantCulture)
                });
                sheet.RowShaded.Add(shade);
            }
            if (j > i) sheet.Merges.Add($"A{i + 3}:A{j + 3}");

            i = j + 1;
            shade = !shade;
        }

        int total = summary.Sum(x => x.Quantity);
        sheet.Rows.Add(new[] { "合计", string.Empty, total.ToString(CultureInfo.InvariantCulture) });
        sheet.RowShaded.Add(false);
        sheet.TotalRowIndex = sheet.Rows.Count - 1;
        sheet.Merges.Add($"A{sheet.TotalRowIndex + 3}:B{sheet.TotalRowIndex + 3}");
        return sheet;
    }

    private static SheetData BuildDailySheet(string title, List<WorkloadDailyRow> daily)
    {
        var sheet = new SheetData
        {
            Name = "每日汇总",
            Title = title + "（每日汇总）",
            Headers = new[] { "日期", "班次", "项目", "工序", "数量" },
            Numeric = new[] { false, false, false, false, true },
            Caps = new[] { 14d, 10d, 50d, 50d, 12d },
            TabColor = "FF0EA5E9"
        };

        int row = 0;
        foreach (var d in daily)
        {
            sheet.Rows.Add(new[]
            {
                d.WorkDate.ToString("yyyy-MM-dd"),
                d.Shift,
                d.ProjectName,
                d.ProcessName,
                d.Quantity.ToString(CultureInfo.InvariantCulture)
            });
            sheet.RowShaded.Add(row++ % 2 == 1);
        }

        int total = daily.Sum(x => x.Quantity);
        sheet.Rows.Add(new[] { "合计", string.Empty, string.Empty, string.Empty, total.ToString(CultureInfo.InvariantCulture) });
        sheet.RowShaded.Add(false);
        sheet.TotalRowIndex = sheet.Rows.Count - 1;
        sheet.Merges.Add($"A{sheet.TotalRowIndex + 3}:D{sheet.TotalRowIndex + 3}");
        return sheet;
    }

    private static SheetData BuildDetailSheet(string title, List<WorkloadRecord> details)
    {
        var sheet = new SheetData
        {
            Name = "明细",
            Title = title + "（执行明细）",
            Headers = new[] { "项目", "工序", "日期", "班次", "数量", "执行时间" },
            Numeric = new[] { false, false, false, false, true, false },
            Caps = new[] { 50d, 50d, 14d, 10d, 12d, 22d },
            TabColor = "FF10B981"
        };

        int row = 0;
        foreach (var d in details)
        {
            sheet.Rows.Add(new[]
            {
                d.ProjectName ?? string.Empty,
                d.ProcessName ?? string.Empty,
                d.WorkDate.ToString("yyyy-MM-dd"),
                d.Shift ?? string.Empty,
                d.Quantity.ToString(CultureInfo.InvariantCulture),
                d.IsBackfill ? "补录" : d.CreatedTime.ToString("yyyy-MM-dd HH:mm:ss")
            });
            sheet.RowShaded.Add(row++ % 2 == 1);
        }
        return sheet;
    }

    // ---------- 工作表 XML ----------

    private static string BuildSheetXml(SheetData sheet)
    {
        int cols = sheet.Headers.Length;
        string lastCol = ColumnLetter(cols);
        int lastRow = sheet.Rows.Count + 2;

        double[] widths = CalcWidths(sheet);

        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sb.Append($"<sheetPr><tabColor rgb=\"{sheet.TabColor}\"/><pageSetUpPr fitToPage=\"1\"/></sheetPr>");
        sb.Append($"<dimension ref=\"A1:{lastCol}{lastRow}\"/>");
        sb.Append("<sheetViews><sheetView workbookViewId=\"0\">");
        sb.Append("<pane ySplit=\"2\" topLeftCell=\"A3\" activePane=\"bottomLeft\" state=\"frozen\"/>");
        sb.Append("</sheetView></sheetViews>");
        sb.Append("<sheetFormatPr defaultRowHeight=\"18\"/>");

        sb.Append("<cols>");
        for (int c = 0; c < cols; c++)
            sb.Append($"<col min=\"{c + 1}\" max=\"{c + 1}\" width=\"{widths[c].ToString("0.#", CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>");
        sb.Append("</cols>");

        sb.Append("<sheetData>");
        // 标题
        sb.Append("<row r=\"1\" ht=\"28\" customHeight=\"1\">");
        sb.Append(TextCell("A1", sheet.Title, 1));
        sb.Append("</row>");

        // 表头
        sb.Append("<row r=\"2\" ht=\"22\" customHeight=\"1\">");
        for (int c = 0; c < cols; c++)
            sb.Append(TextCell($"{ColumnLetter(c + 1)}2", sheet.Headers[c], 2));
        sb.Append("</row>");

        // 数据行
        for (int i = 0; i < sheet.Rows.Count; i++)
        {
            int r = i + 3;
            bool isTotal = i == sheet.TotalRowIndex;
            bool shaded = !isTotal && i < sheet.RowShaded.Count && sheet.RowShaded[i];
            sb.Append($"<row r=\"{r}\" ht=\"20\" customHeight=\"1\">");

            for (int c = 0; c < cols; c++)
            {
                string cellRef = $"{ColumnLetter(c + 1)}{r}";
                string value = c < sheet.Rows[i].Length ? sheet.Rows[i][c] : string.Empty;

                if (sheet.Numeric[c])
                {
                    int style = isTotal ? 8 : shaded ? 6 : 4;
                    sb.Append($"<c r=\"{cellRef}\" s=\"{style}\"><v>{value}</v></c>");
                }
                else
                {
                    int style = isTotal ? 7 : shaded ? 5 : 3;
                    sb.Append(TextCell(cellRef, value, style));
                }
            }
            sb.Append("</row>");
        }
        sb.Append("</sheetData>");

        // 筛选（不含合计行）
        int filterRows = sheet.Rows.Count - (sheet.TotalRowIndex >= 0 ? 1 : 0);
        if (filterRows > 0)
            sb.Append($"<autoFilter ref=\"A2:{lastCol}{filterRows + 2}\"/>");

        var merges = new List<string> { $"A1:{lastCol}1" };
        merges.AddRange(sheet.Merges);
        if (merges.Count > 0)
        {
            sb.Append($"<mergeCells count=\"{merges.Count}\">");
            foreach (string m in merges) sb.Append($"<mergeCell ref=\"{m}\"/>");
            sb.Append("</mergeCells>");
        }

        sb.Append("<pageMargins left=\"0.4\" right=\"0.4\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>");
        sb.Append("<pageSetup paperSize=\"9\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
        sb.Append("</worksheet>");
        return sb.ToString();
    }

    private static double[] CalcWidths(SheetData sheet)
    {
        int cols = sheet.Headers.Length;
        double[] w = new double[cols];
        for (int c = 0; c < cols; c++)
            w[c] = DisplayWidth(sheet.Headers[c]);

        foreach (var row in sheet.Rows)
            for (int c = 0; c < cols && c < row.Length; c++)
                w[c] = Math.Max(w[c], DisplayWidth(row[c]));

        for (int c = 0; c < cols; c++)
            w[c] = Math.Min(w[c] + 2, sheet.Caps[c]);

        // 标题合并后总宽不足会被 Excel 截断：把缺口均摊到各列
        double titleWidth = DisplayWidth(sheet.Title) + 2;
        double totalWidth = w.Sum();
        if (totalWidth < titleWidth)
        {
            double add = (titleWidth - totalWidth) / cols + 0.1;
            for (int c = 0; c < cols; c++) w[c] += add;
        }
        return w;
    }

    // ---------- 工作簿打包 ----------

    private static void WriteWorkbook(string path, IReadOnlyList<SheetData> sheets)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8);

        var contentTypes = new StringBuilder();
        contentTypes.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        contentTypes.Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        contentTypes.Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>");
        contentTypes.Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>");
        contentTypes.Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>");
        contentTypes.Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>");
        for (int i = 0; i < sheets.Count; i++)
            contentTypes.Append($"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
        contentTypes.Append("</Types>");
        WriteEntry(zip, "[Content_Types].xml", contentTypes.ToString());

        WriteEntry(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");

        var workbook = new StringBuilder();
        workbook.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        workbook.Append("<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        workbook.Append("<sheets>");
        for (int i = 0; i < sheets.Count; i++)
            workbook.Append($"<sheet name=\"{Escape(sheets[i].Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>");
        workbook.Append("</sheets></workbook>");
        WriteEntry(zip, "xl/workbook.xml", workbook.ToString());

        var rels = new StringBuilder();
        rels.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        rels.Append("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        for (int i = 0; i < sheets.Count; i++)
            rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>");
        rels.Append($"<Relationship Id=\"rId{sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");
        rels.Append("</Relationships>");
        WriteEntry(zip, "xl/_rels/workbook.xml.rels", rels.ToString());

        WriteEntry(zip, "xl/styles.xml", BuildStyles());

        for (int i = 0; i < sheets.Count; i++)
            WriteEntry(zip, $"xl/worksheets/sheet{i + 1}.xml", BuildSheetXml(sheets[i]));
    }

    // ---------- 样式 ----------

    private static string BuildStyles()
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"4\">" +
            "<font><sz val=\"11\"/><name val=\"等线\"/></font>" +
            "<font><b/><sz val=\"16\"/><color rgb=\"FF1F4E79\"/><name val=\"等线\"/></font>" +
            "<font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"等线\"/></font>" +
            "<font><b/><sz val=\"11\"/><name val=\"等线\"/></font>" +
            "</fonts>" +
            "<fills count=\"5\">" +
            "<fill><patternFill patternType=\"none\"/></fill>" +
            "<fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF2563EB\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF8FAFC\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDBEAFE\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "</fills>" +
            "<borders count=\"2\">" +
            "<border><left/><right/><top/><bottom/><diagonal/></border>" +
            "<border>" +
            "<left style=\"thin\"><color rgb=\"FFD1D5DB\"/></left>" +
            "<right style=\"thin\"><color rgb=\"FFD1D5DB\"/></right>" +
            "<top style=\"thin\"><color rgb=\"FFD1D5DB\"/></top>" +
            "<bottom style=\"thin\"><color rgb=\"FFD1D5DB\"/></bottom>" +
            "<diagonal/></border>" +
            "</borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"9\">" +
            // 0 默认
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            // 1 标题
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            // 2 表头
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\" wrapText=\"1\"/></xf>" +
            // 3 正文
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" +
            // 4 数字
            "<xf numFmtId=\"1\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            // 5 斑马正文
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" +
            // 6 斑马数字
            "<xf numFmtId=\"1\" fontId=\"0\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            // 7 合计文字
            "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            // 8 合计数字
            "<xf numFmtId=\"1\" fontId=\"3\" fillId=\"4\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"center\" vertical=\"center\"/></xf>" +
            "</cellXfs>" +
            "</styleSheet>";
    }

    // ---------- 通用工具 ----------

    private static string TextCell(string reference, string? text, int style)
        => $"<c r=\"{reference}\" s=\"{style}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Escape(text)}</t></is></c>";

    private static string ColumnLetter(int index)
    {
        var result = string.Empty;
        while (index > 0)
        {
            index--;
            result = (char)('A' + index % 26) + result;
            index /= 26;
        }
        return result;
    }

    // 估算文本显示宽度：半角 1，全角/中文 2
    private static double DisplayWidth(string? s)
    {
        double w = 0;
        foreach (char c in s ?? string.Empty)
            w += c <= 0x7F ? 1 : 2;
        return w;
    }

    private static void WriteEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s!.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&apos;");
    }
}
