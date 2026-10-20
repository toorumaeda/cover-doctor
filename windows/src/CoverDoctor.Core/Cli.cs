namespace CoverDoctor.Core;

// コマンド版（CoverDoctor.exe に引数を付けて起動。開発機では coverdoctor）:
// CoverDoctor.exe diagnose [--reuse] [--work DIR]   読み取りだけ。結果は plan.json に保存
// CoverDoctor.exe fix      [--work DIR] --yes       画像を置き換える
// CoverDoctor.exe restore  [--work DIR] --yes       置き換えを取り消す
// CoverDoctor.exe testswap [--count N] [--playlists M] --yes  書き込みの試験: 正しいアルバムを N 件（プレイリストを M 件）、わざと入れ替える（testswap-undo --yes で戻す）
// CoverDoctor.exe version                         版番号を表示
// CoverDoctor.exe report   [--work DIR]          診断結果の件数と距離の分布（名前は出さない）を report.txt に保存
// CoverDoctor.exe compare A B                       2つの画像の見た目の距離（開発用）
// CoverDoctor.exe jpeg IN OUT                       曲の画像の JPEG 化（長辺1200・品質0.9）を試す（開発用）
// CoverDoctor.exe hash FILE...                  画像ごとの dHash（しきい値の測定用。tools/measure_threshold.py）
// CoverDoctor.exe art SONG OUT                      曲ファイルの埋め込みジャケットを取り出す（開発用）

public static class Cli
{
    /// 版番号（テスト版の「-test1」なども含む。ビルドの識別子「+…」は除く）
    public static string AppVersion =>
        (System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(Cli).Assembly)?.InformationalVersion ?? "?").Split('+')[0];

    /// コマンド版の入口。CoverDoctor.exe に引数を付けて起動したときと、開発用の coverdoctor（Linux）の両方から呼ぶ。
    public static int Run(string[] args, Action<string> Say)
    {
        string? Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        string? Work()
        {
            var w = Arg("--work");
            if (w != null) return Path.GetFullPath(w);
            var dev = Afc.ConnectedDevice();
            if (dev == null) { Say(L.T("No iPhone found.")); return null; }
            return Engine.DefaultWorkDir(dev.Udid);
        }

        IProgress<Progress> sync = new SyncProgress(p => { if (p.Total > 0 && ((p.Done + 1) % 20 == 0 || p.Done == p.Total)) Say($"  {p.Done}/{p.Total}"); });

        string cmd = args.Length > 0 ? args[0] : "";
        int code = 0;
        try
        {
            switch (cmd)
            {
                case "diagnose":
                {
                    var work = Work(); if (work == null) return 1;
                    Directory.CreateDirectory(work);
                    var plan = Engine.Diagnose(work, args.Contains("--reuse"), sync);
                    var wrong = plan.Entries.Where(e => e.Status == "wrong").ToList();
                    if (plan.LibraryAlbumCount is int total && plan.NotDiagnosedCount is int skipped)
                        Say(L.T("Albums in the library: {0} · checked: {1} · no cover record: {2}", total, total - skipped, skipped));
                    Say($"{L.T("Correct")} {plan.Entries.Count(e => e.Status == "same")} / {L.T("Fixable")} {wrong.Count} / {L.T("Not fixable")} {plan.Entries.Count(e => e.Status == "noEmbeddedArt")}");
                    foreach (var e in wrong.Take(15)) Say($"  -> {(e.IsPlaylist ? "[" + L.T("Playlist") + "] " : "")}{e.Album}");
                    if (wrong.Count > 15) Say($"  … +{wrong.Count - 15}");
                    var custom = plan.Entries.Where(e => e.Status == "custom").ToList();
                    if (custom.Count > 0) Say(L.T("Playlist covers that may be custom (left alone): {0}", custom.Count));
                    break;
                }
                case "fix":
                {
                    if (!args.Contains("--yes")) { Say("--yes"); return 2; }
                    var work = Work(); var plan = work == null ? null : Engine.LoadPlan(work);
                    if (work == null || plan == null) { Say("diagnose first"); return 1; }
                    var r = Engine.Fix(work, plan, sync);
                    Say($"{L.Plural(r.Ok, "Fixed {0} cover", "Fixed {0} covers")} / {L.T("Could not fix: {0}", r.Failed.Count)}");
                    if (r.ChangedSinceDiagnosis.Count > 0) Say(L.T("Changed on the iPhone after the diagnosis (not written; diagnose again): {0}", r.ChangedSinceDiagnosis.Count));
                    Say(L.T("Fully quit the Music app (swipe it up in the app switcher) and open it again."));
                    if (r.Failed.Count > 0 || r.ChangedSinceDiagnosis.Count > 0) code = 1;
                    break;
                }
                case "restore":
                {
                    if (!args.Contains("--yes")) { Say("--yes"); return 2; }
                    var work = Work(); if (work == null) return 1;
                    int n = Engine.Restore(work, sync);
                    Say(L.Plural(n, "Restored {0} cover", "Restored {0} covers"));
                    break;
                }
                case "compare" when args.Length >= 3:
                {
                    var h1 = Imaging.DHash(File.ReadAllBytes(args[1])); var h2 = Imaging.DHash(File.ReadAllBytes(args[2]));
                    if (h1 == null || h2 == null) { Say("?"); return 1; }
                    Say(Imaging.Hamming(h1, h2).ToString());
                    break;
                }
                case "selftest":
                {
                    // 画面の状態（AppModel）を、ボタンと同じ順に動かす（Mac 版の COVERDOCTOR_SELFTEST と同じ内容）
                    var model = new AppModel(a => { lock (args) a(); });
                    model.StartPolling();
                    var result = model.SelfTest().GetAwaiter().GetResult();
                    Say(result);
                    code = result.StartsWith("PASS") ? 0 : 1;
                    break;
                }
#if TESTSWAP
                case "testswap":
                {
                    // 書き込みの試験: 正しいアルバムを N 件、わざと別のアルバムの画像にする（--count N、既定 10）
                    if (!args.Contains("--yes")) { Say("--yes"); return 2; }
                    var work = Work(); if (work == null) return 1;
                    int n = int.TryParse(Arg("--count"), out var c) ? c : 10;
                    int pl = int.TryParse(Arg("--playlists"), out var c2) ? c2 : 0;   // プレイリストも入れ替える数（既定 0）
                    var names = Engine.TestSwap(work, n, sync, pl);
                    Say($"swapped: {names.Count}");
                    foreach (var a in names) Say("  " + a);
                    break;
                }
                case "testswap-undo":
                {
                    if (!args.Contains("--yes")) { Say("--yes"); return 2; }
                    var work = Work(); if (work == null) return 1;
                    Say($"restored: {Engine.TestSwapUndo(work)}");
                    break;
                }
#endif
                case "report":
                {
                    // 実機での確認用: 診断結果の件数と、見た目の距離の分布だけを出す（アルバム名などは出さない）
                    var work = Work(); var plan = work == null ? null : Engine.LoadPlan(work);
                    if (work == null || plan == null) { Say("diagnose first"); return 1; }
                    var lines = new List<string>
                    {
                        $"Cover Doctor {AppVersion} report",
                        $"OS: {Environment.OSVersion.VersionString}",
                        $"iOS: {plan.IosVersion}",
                        $"created: {plan.Created}",
                        $"library albums: {plan.LibraryAlbumCount} / not diagnosed (no cover record): {plan.NotDiagnosedCount}",
                    };
                    foreach (var g in plan.Entries.GroupBy(e => (e.Kind ?? "album") + " " + e.Status).OrderBy(g => g.Key)) lines.Add($"{g.Key}: {g.Count()}");
                    lines.Add($"distance histogram (threshold {Imaging.SameThreshold}; bins of 8):");
                    foreach (var g in plan.Entries.Where(e => e.Distance is int).GroupBy(e => e.Distance!.Value / 8).OrderBy(g => g.Key))
                        lines.Add($"  {g.Key * 8,3}-{g.Key * 8 + 7,3}: {g.Count(),4} {new string('#', Math.Min(60, g.Count()))}");
                    int near = plan.Entries.Count(e => e.Distance is int d && d > 55 && d < 93);
                    lines.Add($"near the threshold (56-92, where the Mac measurement had no pairs): {near}");
                    var path = Path.Combine(work, "report.txt");
                    File.WriteAllLines(path, lines);
                    foreach (var l in lines) Say(l);
                    Say("saved: " + path);
                    break;
                }
                case "hash" when args.Length >= 2:
                {
                    // 画像ごとの dHash を「16進 タブ ファイル名」で出す（しきい値の測定用）
                    foreach (var f in args.Skip(1))
                    {
                        var h = Imaging.DHash(File.ReadAllBytes(f));
                        Say((h == null ? "?" : string.Concat(h.Select(w => w.ToString("x16")))) + "\t" + f);
                    }
                    break;
                }
                case "jpeg" when args.Length >= 3:
                {
                    var j = Imaging.ToJpeg(File.ReadAllBytes(args[1])); if (j == null) { Say("?"); return 1; }
                    File.WriteAllBytes(args[2], j);
                    break;
                }
                case "art" when args.Length >= 3:
                {
                    var a = EmbeddedArt.Extract(args[1]); if (a == null) { Say("none"); return 1; }
                    File.WriteAllBytes(args[2], a);
                    break;
                }
                case "version":
                    Say($"Cover Doctor {AppVersion}");
                    break;
                default:
                    Say($"Cover Doctor {AppVersion}");
#if TESTSWAP
                    Say("usage: coverdoctor diagnose [--reuse] | fix --yes | restore --yes | testswap [--count N] [--playlists M] --yes | testswap-undo --yes | report | version   (--work DIR)");
#else
                    Say("usage: coverdoctor diagnose [--reuse] | fix --yes | restore --yes | report | version   (--work DIR)");
#endif
                    code = 2; break;
            }
        }
        catch (EngineException e) { Say(e.Message); code = 1; }
        return code;
    }

    /// その場で呼ぶ IProgress（Progress<T> はスレッドプールに投げるため、出力の順番が乱れる）
    sealed class SyncProgress(Action<Progress> a) : IProgress<Progress> { public void Report(Progress p) => a(p); }
}
