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
        new(null, "taskops/todo/1", title, "body", receivedAt);

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"mqttnotify-alerts-{Guid.NewGuid():N}.json");
}
