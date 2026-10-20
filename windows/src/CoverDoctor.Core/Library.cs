using Microsoft.Data.Sqlite;

namespace CoverDoctor.Core;

public sealed record Track(string Path, long Size);
public sealed record AlbumRow(long AlbumPid, string Name, string Token, string RelativePath, IReadOnlyList<Track> Tracks);
public sealed record PlaylistRow(long Pid, string Name, string Token, string RelativePath, IReadOnlyList<long> AlbumPids);

/// iPhone から取り出したライブラリ DB（MediaLibrary.sqlitedb）を、読み取り専用で読む。SQL は Mac 版と同じ。
public static class Library
{
    static SqliteConnection Open(string dbPath)
    {
        var cs = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
        var c = new SqliteConnection(cs);
        c.Open();
        return c;
    }

    static string Text(SqliteDataReader r, int i) => r.IsDBNull(i) ? "" : Convert.ToString(r.GetValue(i)) ?? "";

    public static List<AlbumRow> LoadAlbums(string dbPath)
    {
        const string sql = """
        select a.album_pid, a.album, t.artwork_token, r.relative_path,
               b.path || '/' || e.location, e.file_size
        from album a
        join artwork_token t on t.entity_pid = a.album_pid and t.entity_type = 4 and t.artwork_source_type = 300
        join artwork r on r.artwork_token = t.artwork_token and r.artwork_source_type = t.artwork_source_type
                      and r.artwork_variant_type = t.artwork_variant_type
        join item i on i.album_pid = a.album_pid and i.in_my_library = 1
        join item_extra e on e.item_pid = i.item_pid and e.location <> ''
        join base_location b on b.base_location_id = i.base_location_id
        left join artwork_token it on it.entity_pid = i.item_pid and it.entity_type = 0
                                  and it.artwork_source_type = 300 and it.artwork_token = t.artwork_token
        order by a.album_pid, (it.artwork_token is null), e.file_size
        """;
        var order = new List<long>();
        var meta = new Dictionary<long, (string, string, string)>();
        var tracks = new Dictionary<long, List<Track>>();
        try
        {
            using var c = Open(dbPath);
            using var cmd = c.CreateCommand();
            cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long pid = r.GetInt64(0);
                if (!meta.ContainsKey(pid)) { order.Add(pid); meta[pid] = (Text(r, 1), Text(r, 2), Text(r, 3)); tracks[pid] = new(); }
                tracks[pid].Add(new Track(Text(r, 4), r.IsDBNull(5) ? 0 : r.GetInt64(5)));
            }
        }
        catch (SqliteException) { return new(); }
        return order.Select(p => new AlbumRow(p, meta[p].Item1, meta[p].Item2, meta[p].Item3, tracks[p])).ToList();
    }

    /// 画像（ジャケット）が設定されているプレイリストと、その収録アルバム（収録順・重複なし）を読む。
    public static List<PlaylistRow> LoadPlaylists(string dbPath)
    {
        var result = new List<PlaylistRow>();
        try
        {
            using var c = Open(dbPath);
            var heads = new List<(long, string, string, string)>();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = """
                select c.container_pid, c.name, t.artwork_token, r.relative_path
                from container c
                join artwork_token t on t.entity_pid = c.container_pid and t.entity_type = 1 and t.artwork_source_type = 300
                join artwork r on r.artwork_token = t.artwork_token and r.artwork_source_type = t.artwork_source_type
                              and r.artwork_variant_type = t.artwork_variant_type
                order by c.container_pid
                """;
                using var r = cmd.ExecuteReader();
                while (r.Read()) heads.Add((r.GetInt64(0), Text(r, 1), Text(r, 2), Text(r, 3)));
            }
            foreach (var h in heads)
            {
                using var cmd = c.CreateCommand();
                cmd.CommandText = """
                select i.album_pid from container_item ci
                join item i on i.item_pid = ci.item_pid and i.in_my_library = 1
                where ci.container_pid = $pid order by ci.position
                """;
                cmd.Parameters.AddWithValue("$pid", h.Item1);
                var albums = new List<long>();
                using var r = cmd.ExecuteReader();
                while (r.Read()) { long a = r.GetInt64(0); if (!albums.Contains(a)) albums.Add(a); }
                result.Add(new PlaylistRow(h.Item1, h.Item2, h.Item3, h.Item4, albums));
            }
        }
        catch (SqliteException) { }
        return result;
    }

    /// ライブラリにある（曲が入っている）アルバムの総数。
    public static int AlbumCount(string dbPath)
    {
        try
        {
            using var c = Open(dbPath);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "select count(distinct album_pid) from item where in_my_library = 1";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch (SqliteException) { return 0; }
    }
}
