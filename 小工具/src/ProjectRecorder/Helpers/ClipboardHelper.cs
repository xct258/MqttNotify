using System.Threading;
using System.Windows;

namespace ProjectRecorder.Helpers;

/// <summary>
/// 剪贴板复制（单次尝试）：剪贴板 API 在被其它程序占用时会阻塞不返回，
/// 因此放到独立 STA 线程执行并带 700ms 超时，保证 UI 不卡死。
/// </summary>
public static class ClipboardHelper
{
    public static bool Copy(string text)
    {
        var finished = new ManualResetEventSlim(false);
        bool ok = false;
        var thread = new Thread(() =>
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                ok = true;
            }
            catch
            {
                // 失败（如剪贴板被锁定）直接返回，由调用方打开手动复制弹窗
            }
            finally
            {
                finished.Set();
            }
        });
        try
        {
            thread.SetApartmentState(ApartmentState.STA); // 剪贴板 API 要求 STA 线程
            thread.IsBackground = true;
            thread.Start();
        }
        catch
        {
            return false;
        }
        return finished.Wait(700) && ok;
    }
}
