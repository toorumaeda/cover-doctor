using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CoverDoctor.Core;

/// 画面の状態（Mac 版 App.swift の Model に対応）。画面の部品（WPF）に依存しないので、開発機でも自己テストできる。
/// 状態の変更は、post に渡した関数（画面のスレッドで実行する仕組み）を通して行う。
public sealed class AppModel : INotifyPropertyChanged
{
    public enum PhaseKind { Idle, Diagnosing, Diagnosed, Fixing, Fixed, Restored }

    readonly Action<Action> post;
    CancellationTokenSource? cts;
    Task? poller;

    public AppModel(Action<Action> post) { this.post = post; }

    public event PropertyChangedEventHandler? PropertyChanged;
    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Busy)));
    }

    DeviceInfo? device; public DeviceInfo? Device { get => device; private set => Set(ref device, value); }
    PhaseKind phase; public PhaseKind Phase { get => phase; private set => Set(ref phase, value); }
    Progress progress = new("", 0, 0); public Progress Progress { get => progress; private set => Set(ref progress, value); }
    Plan? plan; public Plan? Plan { get => plan; private set => Set(ref plan, value); }
    string? errorMessage; public string? ErrorMessage { get => errorMessage; private set => Set(ref errorMessage, value); }
    bool hasUndo; public bool HasUndo { get => hasUndo; private set => Set(ref hasUndo, value); }
    bool muxdMissing; public bool MuxdMissing { get => muxdMissing; private set => Set(ref muxdMissing, value); }
    FixResult? lastFix; public FixResult? LastFix { get => lastFix; private set => Set(ref lastFix, value); }
    int restoredCount; public int RestoredCount { get => restoredCount; private set => Set(ref restoredCount, value); }

    public string? Work => Device == null ? null : Engine.DefaultWorkDir(Device.Udid);
    public bool Busy => Phase is PhaseKind.Diagnosing or PhaseKind.Fixing;
    public int FixCount => Plan?.Entries.Count(e => e.Status == "wrong") ?? 0;

    /// 2秒ごとに、つながっている iPhone を調べる。
    public void StartPolling()
    {
        if (poller != null) return;
        poller = Task.Run(async () =>
        {
            while (true)
            {
                var r = Afc.Run("devices");
                bool missing = r.Err.Contains("MUXD\tUNAVAILABLE");
                DeviceInfo? d = null;
                if (r.Ok)
                {
                    var line = r.Out.Split('\n').FirstOrDefault(l => l.StartsWith("DEVICE\t"));
                    var f = line?.TrimEnd('\r').Split('\t');
                    if (f != null && f.Length >= 4) d = new DeviceInfo(f[1], f[2], f[3]);
                }
                post(() =>
                {
                    MuxdMissing = missing;
                    if (d != Device)
                    {
                        Device = d;
                        if (d == null && !Busy) { Phase = PhaseKind.Idle; Plan = null; }
                        if (d != null) HasUndo = Engine.HasUndo(Engine.DefaultWorkDir(d.Udid));
                    }
                });
                await Task.Delay(2000);
            }
        });
    }

    IProgress<Progress> Handler() => new PostProgress(p => post(() => Progress = p));
    sealed class PostProgress(Action<Progress> a) : IProgress<Progress> { public void Report(Progress p) => a(p); }

    public void Diagnose(bool reuse = true)
    {
        var work = Work;
        if (work == null || Busy) return;
        ErrorMessage = null; Plan = null; Phase = PhaseKind.Diagnosing;
        Progress = new Progress(L.T("Preparing…"), 0, 0);
        cts = new CancellationTokenSource();
        var ct = cts.Token; var h = Handler();
        Task.Run(() =>
        {
            try
            {
                Directory.CreateDirectory(work);
                var p = Engine.Diagnose(work, reuse, h, ct);
                post(() => { Plan = p; Phase = PhaseKind.Diagnosed; });
            }
            catch (OperationCanceledException) { post(() => Phase = PhaseKind.Idle); }
            catch (Exception e) { post(() => { ErrorMessage = e.Message; Phase = PhaseKind.Idle; }); }
        });
    }

    public void Fix()
    {
        var work = Work; var p = Plan;
        if (work == null || p == null || Busy) return;
        ErrorMessage = null; Phase = PhaseKind.Fixing;
        Progress = new Progress(L.T("Preparing…"), 0, 0);
        cts = new CancellationTokenSource();
        var ct = cts.Token; var h = Handler();
        Task.Run(() =>
        {
            try
            {
                var r = Engine.Fix(work, p, h, ct);
                post(() => { LastFix = r; Phase = PhaseKind.Fixed; HasUndo = Engine.HasUndo(work); });
            }
            catch (OperationCanceledException) { post(() => { Phase = PhaseKind.Diagnosed; HasUndo = Engine.HasUndo(work); }); }
            catch (Exception e) { post(() => { ErrorMessage = e.Message; Phase = PhaseKind.Diagnosed; HasUndo = Engine.HasUndo(work); }); }
        });
    }

    public void Restore()
    {
        var work = Work;
        if (work == null || Busy) return;
        ErrorMessage = null; Phase = PhaseKind.Fixing;
        Progress = new Progress(L.T("Restoring…"), 0, 0);
        var h = Handler();
        Task.Run(() =>
        {
            try
            {
                int n = Engine.Restore(work, h);
                post(() => { RestoredCount = n; Phase = PhaseKind.Restored; HasUndo = Engine.HasUndo(work); Plan = null; });
            }
            catch (Exception e) { post(() => { ErrorMessage = e.Message; Phase = PhaseKind.Idle; }); }
        });
    }

    public void Cancel() => cts?.Cancel();

    // ---- 自己テスト（開発用。環境変数 COVERDOCTOR_SELFTEST_OUT があるときだけ、画面の起動時に実行する）----

    static async Task<bool> WaitUntil(double seconds, Func<bool> cond)
    {
        var end = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < end) { if (cond()) return true; await Task.Delay(100); }
        return false;
    }

    /// 画面のボタンと同じ経路（Diagnose / Fix / Restore）を順に呼び、結果を文字列で返す。失敗があれば先頭が "FAIL"。
    public async Task<string> SelfTest()
    {
        var log = new List<string>();
        string Fail(string m) => "FAIL: " + m + "\n" + string.Join("\n", log);
        if (!await WaitUntil(30, () => Device != null)) return Fail("device not found");
        log.Add($"device: {Device!.Name}");
        Diagnose(reuse: false);
        if (!await WaitUntil(300, () => Phase == PhaseKind.Diagnosed || ErrorMessage != null) || Phase != PhaseKind.Diagnosed || Plan == null)
            return Fail($"diagnose failed: {ErrorMessage ?? "timeout"}");
        int wrong = FixCount;
        log.Add($"diagnosed: total={Plan.Entries.Count} wrong={wrong}");
        if (wrong == 0) return Fail("expected fixable albums, got 0");
        Fix();
        if (!await WaitUntil(300, () => Phase == PhaseKind.Fixed || ErrorMessage != null) || Phase != PhaseKind.Fixed || LastFix == null)
            return Fail($"fix failed: {ErrorMessage ?? "timeout"}");
        log.Add($"fixed: ok={LastFix.Ok} failed={LastFix.Failed.Count}");
        if (LastFix.Ok != wrong || LastFix.Failed.Count != 0) return Fail("fix result mismatch");
        if (!HasUndo) return Fail("undo history missing");
        Diagnose(reuse: true);
        if (!await WaitUntil(300, () => Phase == PhaseKind.Diagnosed) || Plan == null) return Fail("re-diagnose failed");
        log.Add($"re-diagnosed: wrong={FixCount}");
        if (FixCount != 0) return Fail($"still fixable after fix: {FixCount}");
        Restore();
        if (!await WaitUntil(300, () => Phase == PhaseKind.Restored || ErrorMessage != null) || Phase != PhaseKind.Restored)
            return Fail($"restore failed: {ErrorMessage ?? "timeout"}");
        log.Add($"restored: {RestoredCount}");
        if (RestoredCount != wrong || HasUndo) return Fail("restore result mismatch");
        return "PASS\n" + string.Join("\n", log);
    }
}
