using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CoverDoctor.Core;

// Cover Doctor エンジン（Mac 版 Engine.swift の Windows 版）— iPhone のミュージックの「別アルバムのジャケット」を、
// 曲ファイルの埋め込み画像で直す。コマンド（CoverDoctor.Cli）と画面（CoverDoctor.App）の両方から使う。

public sealed class PlanEntry
{
    public string Token { get; set; } = "";
    public string Album { get; set; } = "";
    public string RelativePath { get; set; } = "";
    /// same / wrong / noEmbeddedArt / custom（プレイリスト: 利用者が設定したかもしれないので触らない）
    public string Status { get; set; } = "";
    public int? Distance { get; set; }
    /// null または "album" / "playlist"
    public string? Kind { get; set; }
    [JsonIgnore] public bool IsPlaylist => Kind == "playlist";
}

public sealed class Plan
{
    public string Udid { get; set; } = "";
    public string IosVersion { get; set; } = "";
    public string Created { get; set; } = "";
    public List<PlanEntry> Entries { get; set; } = new();
    /// ライブラリにあるアルバムの総数（画像の記録の有無を問わない）
    public int? LibraryAlbumCount { get; set; }
    /// うち、画像の記録が無く、診断できなかった数
    public int? NotDiagnosedCount { get; set; }
}

public sealed class EngineException(string message) : Exception(message);

public sealed record Progress(string Message, int Done, int Total);

public sealed class FixResult
{
    public int Ok { get; set; }
    public List<string> Failed { get; } = new();
    /// 診断のあとに iPhone 側で画像が変わっていたため、書き込まなかったもの（もう一度診断が必要）
    public List<string> ChangedSinceDiagnosis { get; } = new();
}

public static class Engine
{
    public const string Originals = "/iTunes_Control/iTunes/Artwork/Originals/";
    const string DbRemote = "/iTunes_Control/iTunes/MediaLibrary.sqlitedb";

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// 作業フォルダ（Mac 版の ~/Library/Application Support/CoverRepair/&lt;UDID&gt; に対応）: %APPDATA%\CoverRepair\&lt;UDID&gt;
    public static string DefaultWorkDir(string udid)
    {
        var baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        // 取得できない環境（開発機で HOME が無いなど）でも、今いるフォルダに作らないようにする
        if (string.IsNullOrEmpty(baseDir)) baseDir = Path.Combine(Path.GetTempPath(), "CoverDoctor-AppData");
        return Path.Combine(baseDir, "CoverRepair", udid);
    }

    public static Plan? LoadPlan(string work)
    {
        try { return JsonSerializer.Deserialize<Plan>(File.ReadAllBytes(Path.Combine(work, "plan.json")), Json); }
        catch { return null; }
    }

    public static bool HasUndo(string work) => File.Exists(Path.Combine(work, "applied.json"));

    static byte[]? ReadOrNull(string path) { try { return File.Exists(path) ? File.ReadAllBytes(path) : null; } catch { return null; } }
    static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    /// 読み取りだけの診断。iPhone は変更しない。reuse が true なら、前回までに曲から取り出した画像を使い回す
    /// （途中で接続が切れても、もう一度実行すれば、取り出し済みのアルバムは iPhone から曲を取り出し直さない）。
    public static Plan Diagnose(string work, bool reuse, IProgress<Progress>? progress = null, CancellationToken ct = default)
    {
        var dev = Afc.ConnectedDevice()
            ?? throw new EngineException(L.T("No iPhone found. Connect it with a USB cable, unlock it, and choose “Trust This Computer”."));
        string originals = Path.Combine(work, "originals");   // 直す前の画像のバックアップ（最初の1回だけ保存。上書きしない）
        string current = Path.Combine(work, "current");       // 今の iPhone 上の画像（毎回取得）
        string fresh = Path.Combine(work, "fresh");           // 曲から取り出した正しい画像
        string tmp = Path.Combine(work, "tmp");
        foreach (var d in new[] { originals, current, fresh, tmp }) Directory.CreateDirectory(d);

        progress?.Report(new Progress(L.T("Reading the library information…"), 0, 0));
        string dbBase = Path.Combine(work, "MediaLibrary.sqlitedb");
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            TryDelete(dbBase + suffix);
            var r = Afc.Get(dev.Udid, DbRemote + suffix, dbBase + suffix);
            if (!r.Ok && suffix == "")
                throw new EngineException(L.T("Could not read the library information. Unlock your iPhone and try again. ({0})", r.Err.Trim()));
            if (!r.Ok) TryDelete(dbBase + suffix);
        }
        var albums = Library.LoadAlbums(dbBase);
        var entries = new List<PlanEntry>();
        for (int i = 0; i < albums.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = albums[i];
            progress?.Report(new Progress(a.Name, i, albums.Count));
            string cur = Path.Combine(current, a.Token + ".img");
            TryDelete(cur);
            Afc.Get(dev.Udid, Originals + a.RelativePath, cur);
            var curData = ReadOrNull(cur);
            string keep = Path.Combine(originals, a.Token + ".img");
            if (curData != null && !File.Exists(keep)) File.Copy(cur, keep);

            byte[]? art = reuse ? ReadOrNull(Path.Combine(fresh, a.Token + ".jpg")) : null;
            foreach (var t in a.Tracks.Where(t => t.Size > 200_000).Take(8))
            {
                if (art != null) break;
                ct.ThrowIfCancellationRequested();
                string local = Path.Combine(tmp, SafeFileName(t.Path));
                try
                {
                    if (!Afc.Get(dev.Udid, "/" + t.Path, local).Ok) continue;
                    var raw = EmbeddedArt.Extract(local);
                    var jpg = raw == null ? null : Imaging.ToJpeg(raw);
                    if (jpg != null) { art = jpg; break; }
                }
                finally { TryDelete(local); }
            }
            var entry = new PlanEntry { Token = a.Token, Album = a.Name, RelativePath = a.RelativePath, Status = "noEmbeddedArt" };
            if (art != null)
            {
                File.WriteAllBytes(Path.Combine(fresh, a.Token + ".jpg"), art);
                var h1 = curData == null ? null : Imaging.DHash(curData);
                var h2 = Imaging.DHash(art);
                if (h1 != null && h2 != null)
                {
                    int dist = Imaging.Hamming(h1, h2);
                    entry.Distance = dist;
                    entry.Status = dist <= Imaging.SameThreshold ? "same" : "wrong";
                }
                else entry.Status = "wrong";   // 現在の画像が壊れている・読めない
            }
            entries.Add(entry);
        }

        // --- プレイリストのジャケット ---
        var playlists = Library.LoadPlaylists(dbBase);
        if (playlists.Count > 0)
        {
            var coverByAlbum = new Dictionary<long, (byte[] Data, ulong[] Hash)>();
            foreach (var a in albums)
            {
                var d = ReadOrNull(Path.Combine(fresh, a.Token + ".jpg"));
                var h = d == null ? null : Imaging.DHash(d);
                if (d != null && h != null) coverByAlbum[a.AlbumPid] = (d, h);
            }
            var libraryHashes = coverByAlbum.Values.Select(v => v.Hash).ToList();
            for (int i = 0; i < playlists.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var p = playlists[i];
                progress?.Report(new Progress(p.Name, i, playlists.Count));
                string cur = Path.Combine(current, p.Token + ".img");
                TryDelete(cur);
                Afc.Get(dev.Udid, Originals + p.RelativePath, cur);
                var curData = ReadOrNull(cur);
                string keep = Path.Combine(originals, p.Token + ".img");
                if (curData != null && !File.Exists(keep)) File.Copy(cur, keep);
                var covers = p.AlbumPids.Where(coverByAlbum.ContainsKey).Select(x => coverByAlbum[x]).ToList();
                var expected = Imaging.PlaylistCover(covers.Select(c => c.Data).ToList());
                var eh = expected == null ? null : Imaging.DHash(expected);
                if (expected == null || eh == null) continue;   // 比べる材料が無い
                var entry = new PlanEntry { Token = p.Token, Album = p.Name, RelativePath = p.RelativePath, Status = "custom", Kind = "playlist" };
                var ch = curData == null ? null : Imaging.DHash(curData);
                if (ch != null)
                {
                    int theirs = covers.Count == 0 ? int.MaxValue : covers.Min(c => Imaging.Hamming(ch, c.Hash));
                    int dist = Math.Min(Imaging.Hamming(ch, eh), theirs);
                    entry.Distance = dist;
                    if (dist <= Imaging.SameThreshold) entry.Status = "same";
                    else if (libraryHashes.Any(h => Imaging.Hamming(ch, h) <= Imaging.SameThreshold)) entry.Status = "wrong";   // 収録外のアルバムのジャケット = 結びつきの誤り
                    else entry.Status = "custom";                                                                                 // どのアルバムとも違う = 利用者が設定した画像かもしれない
                }
                else entry.Status = "wrong";                                                                                      // 画像が壊れている・読めない
                if (entry.Status == "wrong") File.WriteAllBytes(Path.Combine(fresh, p.Token + ".jpg"), expected);
                entries.Add(entry);
            }
        }
        progress?.Report(new Progress(L.T("Done"), albums.Count, albums.Count));
        int libraryTotal = Library.AlbumCount(dbBase);
        var plan = new Plan
        {
            Udid = dev.Udid, IosVersion = dev.Version, Created = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            Entries = entries, LibraryAlbumCount = libraryTotal, NotDiagnosedCount = Math.Max(0, libraryTotal - albums.Count),
        };
        File.WriteAllBytes(Path.Combine(work, "plan.json"), JsonSerializer.SerializeToUtf8Bytes(plan, Json));
        return plan;
    }

    static string SafeFileName(string remotePath)
    {
        var name = remotePath.Split('/').Last();
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return string.IsNullOrEmpty(name) ? "track" : name;
    }

    /// iPhone 上の画像が、診断で見たものと同じか（診断のあとに同期などで変わっていないか）。
    static bool UnchangedSinceDiagnosis(string udid, string work, PlanEntry e)
    {
        string snap = Path.Combine(work, "current", e.Token + ".img");
        string now = Path.Combine(work, "tmp", "before.img");
        TryDelete(now);
        if (!Afc.Get(udid, Originals + e.RelativePath, now).Ok) return false;
        var a = ReadOrNull(snap); var b = ReadOrNull(now);
        if (a == null || b == null) return false;
        if (a.AsSpan().SequenceEqual(b)) return true;
        var ha = Imaging.DHash(a); var hb = Imaging.DHash(b);
        return ha != null && hb != null && Imaging.Hamming(ha, hb) <= Imaging.SameThreshold;
    }

    /// 診断結果の「直せる」画像を置き換える。置き換えた一覧は applied.json に残す。
    public static FixResult Fix(string work, Plan plan, IProgress<Progress>? progress = null, CancellationToken ct = default)
    {
        var dev = Afc.ConnectedDevice();
        if (dev == null || dev.Udid != plan.Udid) throw new EngineException(L.T("The iPhone you diagnosed is not connected."));
        var targets = plan.Entries.Where(e => e.Status == "wrong").ToList();
        var result = new FixResult();
        var applied = new List<PlanEntry>();
        Directory.CreateDirectory(Path.Combine(work, "tmp"));
        try
        {
            for (int i = 0; i < targets.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var e = targets[i];
                progress?.Report(new Progress(e.Album, i, targets.Count));
                string backup = Path.Combine(work, "originals", e.Token + ".img");
                string fresh = Path.Combine(work, "fresh", e.Token + ".jpg");
                if (!File.Exists(backup) || !File.Exists(fresh)) { result.Failed.Add(e.Album); continue; }
                if (!UnchangedSinceDiagnosis(dev.Udid, work, e)) { result.ChangedSinceDiagnosis.Add(e.Album); continue; }
                string remote = Originals + e.RelativePath;
                var r = Afc.Put(dev.Udid, fresh, remote);
                // 読み戻して見た目で確認（iOS がファイルを作り直すことがあるため、バイト一致ではなく見た目で判定）
                string check = Path.Combine(work, "tmp", "verify.img");
                TryDelete(check);
                Afc.Get(dev.Udid, remote, check);
                var d = ReadOrNull(check);
                var h1 = d == null ? null : Imaging.DHash(d);
                var h2 = Imaging.DHash(File.ReadAllBytes(fresh));
                if (r.Ok && h1 != null && h2 != null && Imaging.Hamming(h1, h2) <= Imaging.SameThreshold) { result.Ok++; applied.Add(e); }
                else result.Failed.Add(e.Album);
            }
        }
        finally
        {
            // 取り消し用に、置き換えた一覧を追記保存（途中で止まっても、それまでの分は残す）
            SaveApplied(work, applied);
        }
        progress?.Report(new Progress(L.T("Done"), targets.Count, targets.Count));
        return result;
    }

    static void SaveApplied(string work, List<PlanEntry> applied)
    {
        if (applied.Count == 0) return;
        string path = Path.Combine(work, "applied.json");
        List<PlanEntry> all;
        try { all = JsonSerializer.Deserialize<List<PlanEntry>>(File.ReadAllBytes(path), Json) ?? new(); } catch { all = new(); }
        all.RemoveAll(a => applied.Any(x => x.Token == a.Token));
        all.AddRange(applied);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(all, Json));
    }

    /// 置き換えた画像を、バックアップ（最初に見た画像）に戻す。
    public static int Restore(string work, IProgress<Progress>? progress = null)
    {
        var dev = Afc.ConnectedDevice() ?? throw new EngineException(L.T("No iPhone found."));
        string path = Path.Combine(work, "applied.json");
        List<PlanEntry>? applied;
        try { applied = JsonSerializer.Deserialize<List<PlanEntry>>(File.ReadAllBytes(path), Json); } catch { applied = null; }
        if (applied == null) throw new EngineException(L.T("There is nothing to undo."));
        int ok = 0;
        var remaining = new List<PlanEntry>();
        for (int i = 0; i < applied.Count; i++)
        {
            var e = applied[i];
            progress?.Report(new Progress(e.Album, i, applied.Count));
            string backup = Path.Combine(work, "originals", e.Token + ".img");
            if (!File.Exists(backup)) continue;
            if (Afc.Put(dev.Udid, backup, Originals + e.RelativePath).Ok) ok++;
            else remaining.Add(e);
        }
        // 戻せなかったものは、もう一度「元に戻す」ができるように履歴に残す
        if (remaining.Count == 0) TryDelete(path);
        else File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(remaining, Json));
        return ok;
    }
    // ---- 書き込みの試験（実機で「直す」を確かめるため。開発・確認用で、画面には出さない）----

    /// 診断で「正しい」だったアルバムを count 件選び、隣のアルバムの画像で上書きして、わざと「別のアルバムのジャケット」にする。
    /// 上書きする前の画像は、診断のときに originals/ に保存済みのものを使う（無いものは選ばない）。
    /// 本物の画像は testswap-originals/ に移し、originals/ には入れ替えた画像を置く。
    /// こうしないと、「元に戻す」が最初に見た正しい画像を書き戻すので、「元に戻す」の確認にならない。
    /// 選んだアルバムは testswap.json に残す。このあと「診断する」→「直す」で元の絵に戻るはず。戻らないときは TestSwapUndo で戻す。
    /// playlistCount を付けると、「正しい」プレイリストも同じ数だけ、そのプレイリストに入っていないアルバムの画像で上書きする（診断で「直せる」になるはず）。
    public static List<string> TestSwap(string work, int count, IProgress<Progress>? progress = null, int playlistCount = 0)
    {
        var plan = LoadPlan(work) ?? throw new EngineException("diagnose first");
        var dev = Afc.ConnectedDevice();
        if (dev == null || dev.Udid != plan.Udid) throw new EngineException(L.T("The iPhone you diagnosed is not connected."));
        var picks = plan.Entries
            .Where(e => e.Status == "same" && !e.IsPlaylist
                        && File.Exists(Path.Combine(work, "originals", e.Token + ".img"))
                        && File.Exists(Path.Combine(work, "fresh", e.Token + ".jpg")))
            .Take(count).ToList();
        if (count > 0 && picks.Count < 2) throw new EngineException("not enough correct albums to swap");
        // プレイリスト: 入っていないアルバムの画像を割り当てる
        var listPicks = new List<(PlanEntry Entry, string Image)>();
        if (playlistCount > 0)
        {
            string db = Path.Combine(work, "MediaLibrary.sqlitedb");
            var tokenByPid = Library.LoadAlbums(db).ToDictionary(a => a.AlbumPid, a => a.Token);
            var lists = Library.LoadPlaylists(db).ToDictionary(x => x.Token);
            var albumImages = plan.Entries
                .Where(e => e.Status == "same" && !e.IsPlaylist && File.Exists(Path.Combine(work, "fresh", e.Token + ".jpg")))
                .Select(e => e.Token).ToList();
            int next = 0;
            foreach (var e in plan.Entries.Where(e => e.IsPlaylist && e.Status == "same"
                                                     && File.Exists(Path.Combine(work, "originals", e.Token + ".img"))))
            {
                if (listPicks.Count >= playlistCount) break;
                if (!lists.TryGetValue(e.Token, out var pl)) continue;
                var inList = pl.AlbumPids.Where(tokenByPid.ContainsKey).Select(x => tokenByPid[x]).ToHashSet();
                for (; next < albumImages.Count; next++)
                    if (!inList.Contains(albumImages[next])) break;
                if (next >= albumImages.Count) break;
                listPicks.Add((e, Path.Combine(work, "fresh", albumImages[next] + ".jpg")));
                next++;
            }
        }
        var done = new List<PlanEntry>();
        string real = Path.Combine(work, "testswap-originals");
        Directory.CreateDirectory(real);
        try
        {
            for (int i = 0; i < picks.Count; i++)
            {
                var e = picks[i];
                var other = picks[(i + 1) % picks.Count];
                progress?.Report(new Progress(e.Album, i, picks.Count));
                if (!UnchangedSinceDiagnosis(dev.Udid, work, e)) continue;
                string keep = Path.Combine(work, "originals", e.Token + ".img");
                string realKeep = Path.Combine(real, e.Token + ".img");     // 入れ替える直前の iPhone の画像（取り消しで iPhone に戻す）
                string origKeep = Path.Combine(real, e.Token + ".orig");    // 元の originals/ の中身（取り消しで originals/ に戻す）
                if (!File.Exists(realKeep)) File.Copy(Path.Combine(work, "current", e.Token + ".img"), realKeep);
                if (!File.Exists(origKeep)) File.Copy(keep, origKeep);
                string swapped = Path.Combine(work, "fresh", other.Token + ".jpg");
                if (Afc.Put(dev.Udid, swapped, Originals + e.RelativePath).Ok) { File.Copy(swapped, keep, true); done.Add(e); }
            }
            foreach (var (e, swapped) in listPicks)
            {
                progress?.Report(new Progress(e.Album, done.Count, picks.Count + listPicks.Count));
                if (!UnchangedSinceDiagnosis(dev.Udid, work, e)) continue;
                string keep = Path.Combine(work, "originals", e.Token + ".img");
                string realKeep = Path.Combine(real, e.Token + ".img");     // 入れ替える直前の iPhone の画像（取り消しで iPhone に戻す）
                string origKeep = Path.Combine(real, e.Token + ".orig");    // 元の originals/ の中身（取り消しで originals/ に戻す）
                if (!File.Exists(realKeep)) File.Copy(Path.Combine(work, "current", e.Token + ".img"), realKeep);
                if (!File.Exists(origKeep)) File.Copy(keep, origKeep);
                if (Afc.Put(dev.Udid, swapped, Originals + e.RelativePath).Ok) { File.Copy(swapped, keep, true); done.Add(e); }
            }
        }
        finally
        {
            File.WriteAllBytes(Path.Combine(work, "testswap.json"), JsonSerializer.SerializeToUtf8Bytes(done, Json));
        }
        return done.Select(e => e.Album).ToList();
    }

    /// TestSwap で書き換えたものを、入れ替える直前の画像に戻す（originals/ も入れ替える前の中身に戻す）。
    public static int TestSwapUndo(string work)
    {
        var dev = Afc.ConnectedDevice() ?? throw new EngineException(L.T("No iPhone found."));
        string path = Path.Combine(work, "testswap.json");
        List<PlanEntry>? list;
        try { list = JsonSerializer.Deserialize<List<PlanEntry>>(File.ReadAllBytes(path), Json); } catch { list = null; }
        if (list == null) throw new EngineException(L.T("There is nothing to undo."));
        int ok = 0;
        foreach (var e in list)
        {
            string realKeep = Path.Combine(work, "testswap-originals", e.Token + ".img");
            string keep = Path.Combine(work, "originals", e.Token + ".img");
            string origKeep = Path.Combine(work, "testswap-originals", e.Token + ".orig");
            string src = File.Exists(realKeep) ? realKeep : keep;
            if (Afc.Put(dev.Udid, src, Originals + e.RelativePath).Ok)
            {
                ok++;
                if (File.Exists(origKeep)) { File.Copy(origKeep, keep, true); TryDelete(origKeep); }
                TryDelete(realKeep);
            }
        }
        if (ok == list.Count) TryDelete(path);
        return ok;
    }
}
