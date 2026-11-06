using System;
using System.IO;
using Xunit;

namespace MqttNotify.Core.Tests;

public class AlertStoreTests
{
    [Fact]
    public void 只保留最近三十天并按时间倒序()
    {
        var path = TempFile();
        try
        {
            var store = new AlertStore(path);
            store.Save(new[]
            {
                Alert("旧消息", DateTimeOffset.Now.AddDays(-31)),
                Alert("新消息", DateTimeOffset.Now.AddMinutes(-1)),
                Alert("中间消息", DateTimeOffset.Now.AddDays(-3)),
            });

            var loaded = store.Load();

            Assert.Equal(2, loaded.Count);
            Assert.Equal("新消息", loaded[0].Title);
            Assert.Equal("中间消息", loaded[1].Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 服务通知字段可以完整落盘并恢复()
    {
        var path = TempFile();
        try
        {
            var store = new AlertStore(path);
            store.Save(new[]
            {
                new AlertRecord(
                    AlertKind.Service,
                    null,
                    "o1-debian-1",
                    "录播备份上传脚本",
                    "ok",
                    "server/khx-live",
                    "o1-debian-1",
                    "上传成功",
                    DateTimeOffset.Now),
            });

            var record = Assert.Single(store.Load());
            Assert.Equal(AlertKind.Service, record.Kind);
            Assert.Equal("o1-debian-1", record.ServerName);
            Assert.Equal("录播备份上传脚本", record.ServiceName);
            Assert.Equal("ok", record.Status);
            Assert.Equal("o1-debian-1", record.DisplayTitle);
            Assert.Equal("录播备份上传脚本", record.DisplayService);
            Assert.True(record.HasService);
            Assert.True(record.HasStatus);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 损坏文件回退为空历史()
    {
        var path = TempFile();
        try
        {
            File.WriteAllText(path, "{ not json");
            Assert.Empty(new AlertStore(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static AlertRecord Alert(string title, DateTimeOffset receivedAt) =>
        new(AlertKind.Todo, null, null, null, null,
            "taskops/todo/1", title, "body", receivedAt);

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"mqttnotify-alerts-{Guid.NewGuid():N}.json");
}
