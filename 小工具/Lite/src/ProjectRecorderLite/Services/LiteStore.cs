using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using ProjectRecorderLite.Models;

namespace ProjectRecorderLite.Services;

/// <summary>Lite 版的全部数据：项目列表 + 快捷路径。</summary>
[DataContract]
public sealed class LiteData
{
    [DataMember] public List<string> Projects { get; set; } = new();
    [DataMember] public List<PathShortcut> Shortcuts { get; set; } = new();
}

/// <summary>
/// 明文 JSON 数据存储（无登录、无加密）。
/// 文件位置：exe 同级 Data/data.json。
/// 增删改在内存中完成并立即落盘，失败时回滚内存改动。
/// </summary>
public static class LiteStore
{
    public const string DataFolderName = "Data";
    public const string DataFileName = "data.json";

    private static LiteData? _data;

    public static string AppDir => AppDomain.CurrentDomain.BaseDirectory;
    public static string DataDir => Path.Combine(AppDir, DataFolderName);
    public static string DataPath => Path.Combine(DataDir, DataFileName);

    /// <summary>最近一次保存失败的原因；成功后清空。</summary>
    public static string? LastError { get; private set; }

    /// <summary>启动时数据文件损坏等提示（只用于第一次加载）。</summary>
    public static string? StartupWarning { get; private set; }

    private static LiteData Data => _data ??= LoadFromDisk();

    /// <summary>项目列表（内部真实引用，按显示顺序）。</summary>
    public static List<string> Projects => Data.Projects;

    public static List<PathShortcut> GetShortcuts(string project)
    {
        if (string.IsNullOrWhiteSpace(project)) return new List<PathShortcut>();
        return Data.Shortcuts
            .Where(x => string.Equals(x.ProjectName, project, StringComparison.Ordinal))
            .ToList();
    }

    // ---------- 项目 ----------

    public static bool AddProject(string name)
    {
        name = (name ?? string.Empty).Trim();
        if (name.Length == 0) return false;
        if (Data.Projects.Contains(name)) return true;

        Data.Projects.Add(name);
        if (TrySave()) return true;
        Data.Projects.Remove(name);
        return false;
    }

    public static bool RemoveProject(string name)
    {
        int index = Data.Projects.IndexOf(name);
        if (index < 0) return false;

        var removedShortcuts = Data.Shortcuts
            .Where(x => string.Equals(x.ProjectName, name, StringComparison.Ordinal))
            .ToList();

        Data.Projects.RemoveAt(index);
        Data.Shortcuts.RemoveAll(x => string.Equals(x.ProjectName, name, StringComparison.Ordinal));

        if (TrySave()) return true;

        // 回滚
        Data.Projects.Insert(Math.Min(index, Data.Projects.Count), name);
        Data.Shortcuts.AddRange(removedShortcuts);
        return false;
    }

    public static bool MoveProject(string name, int delta)
    {
        int i = Data.Projects.IndexOf(name);
        int j = i + delta;
        if (i < 0 || j < 0 || j >= Data.Projects.Count) return true;

        (Data.Projects[i], Data.Projects[j]) = (Data.Projects[j], Data.Projects[i]);
        if (TrySave()) return true;
        (Data.Projects[i], Data.Projects[j]) = (Data.Projects[j], Data.Projects[i]);
        return false;
    }

    // ---------- 快捷路径 ----------

    public static bool AddShortcut(PathShortcut shortcut)
    {
        if (shortcut == null) return false;
        shortcut.Id = string.IsNullOrEmpty(shortcut.Id) ? Guid.NewGuid().ToString() : shortcut.Id;
        shortcut.CreatedTime = shortcut.CreatedTime == default ? DateTime.Now : shortcut.CreatedTime;

        Data.Shortcuts.Add(shortcut);
        if (TrySave()) return true;
        Data.Shortcuts.Remove(shortcut);
        return false;
    }

    public static bool RemoveShortcut(string id)
    {
        int index = Data.Shortcuts.FindIndex(x => x.Id == id);
        if (index < 0) return false;

        var removed = Data.Shortcuts[index];
        Data.Shortcuts.RemoveAt(index);
        if (TrySave()) return true;
        Data.Shortcuts.Insert(Math.Min(index, Data.Shortcuts.Count), removed);
        return false;
    }

    // ---------- 落盘 ----------

    private static bool TrySave()
    {
        try
        {
            SaveCore();
            LastError = null;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return false;
        }
    }

    private static void SaveCore()
    {
        Directory.CreateDirectory(DataDir);

        var serializer = new DataContractJsonSerializer(typeof(LiteData));
        using var ms = new MemoryStream();
        serializer.WriteObject(ms, Data);
        string json = Encoding.UTF8.GetString(ms.ToArray());

        // 先写临时文件再覆盖，尽量避免中途崩溃把原文件写坏
        string tmp = DataPath + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Copy(tmp, DataPath, overwrite: true);
        try { File.Delete(tmp); } catch { /* 临时文件删不掉不影响数据 */ }
    }

    private static LiteData LoadFromDisk()
    {
        if (!File.Exists(DataPath))
            return new LiteData();

        try
        {
            var serializer = new DataContractJsonSerializer(typeof(LiteData));
            using var fs = File.OpenRead(DataPath);
            var data = serializer.ReadObject(fs) as LiteData ?? new LiteData();
            Normalize(data);
            return data;
        }
        catch (Exception ex)
        {
            // 损坏时先备份，避免用户数据被无声覆盖
            try
            {
                Directory.CreateDirectory(DataDir);
                string backup = Path.Combine(DataDir, $"data.corrupt-{DateTime.Now:yyyyMMddHHmmss}.json");
                File.Copy(DataPath, backup, overwrite: true);
            }
            catch
            {
                // 备份失败不阻断启动
            }

            StartupWarning = "数据文件读取失败，已按空数据启动（原文件已尝试备份）。\n" + ex.Message;
            return new LiteData();
        }
    }

    private static void Normalize(LiteData data)
    {
        data.Projects ??= new List<string>();
        data.Shortcuts ??= new List<PathShortcut>();

        data.Projects = data.Projects
            .Select(p => (p ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .Distinct()
            .ToList();

        var projectSet = new HashSet<string>(data.Projects, StringComparer.Ordinal);

        var shortcuts = new List<PathShortcut>();
        foreach (var sc in data.Shortcuts)
        {
            if (sc == null) continue;

            sc.Id = string.IsNullOrEmpty(sc.Id) ? Guid.NewGuid().ToString() : sc.Id;
            sc.ProjectName = (sc.ProjectName ?? string.Empty).Trim();
            sc.Kind = string.IsNullOrEmpty(sc.Kind) ? PathShortcut.KindFolder : sc.Kind;
            sc.Name = sc.Name ?? string.Empty;
            sc.FolderPath = sc.FolderPath ?? string.Empty;
            if (sc.CreatedTime == default) sc.CreatedTime = DateTime.Now;

            // 丢弃找不到归属项目的脏数据
            if (!projectSet.Contains(sc.ProjectName)) continue;
            shortcuts.Add(sc);
        }
        data.Shortcuts = shortcuts;
    }
}
