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
    public void 读取小驼峰配置()
    {
        var path = NewTempFile();
        try
        {
            File.WriteAllText(path, """
                {
                  "configVersion": 2,
                  "host": "192.168.1.10",
                  "port": 9001,
                  "topicBase": "taskops/todo/#",
                  "transport": "Ws",
                  "clientIdSuffix": "abcd1234"
                }
                """);

            var settings = new JsonSettingsStore(path).Load();

            Assert.Equal("192.168.1.10", settings.Host);
            Assert.Equal(9001, settings.Port);
            Assert.Equal("taskops/todo/#", settings.TopicBase);
            Assert.Equal("taskops/todo/#", settings.SubscribeTopic);
            Assert.Equal(MqttTransport.Ws, settings.Transport);
            Assert.Equal("abcd1234", settings.ClientIdSuffix);
            Assert.Equal("windows-abcd1234", settings.EffectiveClientId);
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
            Assert.Contains("\"configVersion\": 2", json);
            Assert.Contains("\"host\"", json);
            Assert.Contains("\"clientIdSuffix\"", json);
            Assert.DoesNotContain("\"Host\"", json);

            var loaded = store.Load();
            Assert.Equal("192.168.1.10", loaded.Host);
            Assert.False(string.IsNullOrWhiteSpace(loaded.ClientIdSuffix));
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
    public void 旧配置版本会回落到默认值()
    {
        var path = NewTempFile();
        try
        {
            File.WriteAllText(path, """
                {
                  "host": "old.example.com",
                  "port": 9001,
                  "transport": "Ws"
                }
                """);

            var settings = new JsonSettingsStore(path).Load();
            var defaults = new AppSettings();

            Assert.Equal(defaults.Host, settings.Host);
            Assert.Equal(defaults.Port, settings.Port);
            Assert.Equal(defaults.Transport, settings.Transport);
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
