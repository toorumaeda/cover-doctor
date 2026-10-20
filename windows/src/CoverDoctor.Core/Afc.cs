using System.Diagnostics;
using System.Text;

namespace CoverDoctor.Core;

public sealed record DeviceInfo(string Udid, string Version, string Name);

/// 補助ツール afcfs（iPhone の AFC を読み書きする小さな C のプログラム）を呼ぶ。
/// 実機との通信は、すべてこのツールを通る。書き込みの制限（Originals 配下の既存ファイルの上書きだけ）もツール側にある。
public static class Afc
{
    public static string HelperPath
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("COVERDOCTOR_AFCFS");
            if (!string.IsNullOrEmpty(env)) return env;
            return Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "afcfs.exe" : "afcfs");
        }
    }

    /// 1回の呼び出しの上限。ケーブルが抜けたときなどに、画面が止まったままにならないようにする。
    public static TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

    public static (bool Ok, string Out, string Err) Run(params string[] args)
    {
        var psi = new ProcessStartInfo(HelperPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        Process p;
        try { p = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null"); }
        catch (Exception e) { return (false, "", L.T("Could not start the helper tool: {0}", e.Message)); }
        using (p)
        {
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(Timeout))
            {
                try { p.Kill(true); } catch { }
                return (false, "", L.T("The iPhone did not respond in time. Check the USB connection and try again."));
            }
            p.WaitForExit();
            return (p.ExitCode == 0, outTask.Result, errTask.Result);
        }
    }

    public static DeviceInfo? ConnectedDevice()
    {
        var r = Run("devices");
        var line = r.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(l => l.StartsWith("DEVICE\t"));
        if (line == null) return null;
        var f = line.TrimEnd('\r').Split('\t');
        return f.Length >= 4 ? new DeviceInfo(f[1], f[2], f[3]) : null;
    }

    public static (bool Ok, string Out, string Err) Get(string udid, string remote, string local) => Run("get", udid, remote, local);
    public static (bool Ok, string Out, string Err) Put(string udid, string local, string remote) => Run("put", udid, local, remote);
}
