using System;
using System.IO;
using Xunit;

namespace MqttNotify.Core.Tests;

/// <summary>
/// 配置文件读写的兼容性测试：既要知道程序自己写出的格式，也要容忍手写 JSON
/// （小驼峰、注释、尾逗号）——用户经常直接编辑 settings.json。
/// </summary>
public class SettingsStoreTests
{
    [Fact]
    public void 读取手写小驼峰配置并接受注释和尾逗号()
    {
        var path = NewTempFile();
        try
        {
            File.WriteAllText(path, """
                {
                  // 用户手写：小驼峰字段名
                  "host": "192.168.1.10",
                  "port": 9001,
                  "topicBase": "taskops/todo/#",
                  "transport": "Ws",
                }
                """);

            var settings = new JsonSettingsStore(path).Load();

            Assert.Equal("192.168.1.10", settings.Host);
            Assert.Equal(9001, settings.Port);
            Assert.Equal("taskops/todo/#", settings.TopicBase);
            Assert.Equal("taskops/todo/#", settings.SubscribeTopic);
            Assert.Equal(MqttTransport.Ws, settings.Transport);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 保存使用小驼峰并且可以往返()
    {
        var path = NewTempFile();
        try
        {
            var store = new JsonSettingsStore(path);
            store.Save(new AppSettings
            {
                Host = " 192.168.1.10 ",
                TopicBase = "a/b/#",
                Transport = MqttTransport.Wss,
                AllowInsecureTls = true,
            });

            var json = File.ReadAllText(path);
            Assert.Contains("\"host\"", json);
            Assert.DoesNotContain("\"Host\"", json);

            var loaded = store.Load();
            Assert.Equal("192.168.1.10", loaded.Host);
            Assert.Equal("a/b", loaded.TopicBase);
            Assert.Equal("a/b/#", loaded.SubscribeTopic);
            Assert.Equal(MqttTransport.Wss, loaded.Transport);
            Assert.True(loaded.AllowInsecureTls);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 兼容旧版大驼峰配置()
    {
        var path = NewTempFile();
        try
        {
            File.WriteAllText(path, """
                {
                  "Host": "10.0.0.2",
                  "Port": 9001,
                  "TopicBase": "taskops/todo"
                }
                """);

            var settings = new JsonSettingsStore(path).Load();

            Assert.Equal("10.0.0.2", settings.Host);
            Assert.Equal("taskops/todo", settings.TopicBase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string NewTempFile() => Path.Combine(
        Path.GetTempPath(),
        $"mqttnotify-settings-{Guid.NewGuid():N}.json");
}
