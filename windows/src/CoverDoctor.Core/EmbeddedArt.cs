using System.Text;

namespace CoverDoctor.Core;

/// 曲ファイルの埋め込みジャケットを取り出す（MP3 の ID3v2 APIC/PIC、M4A/MP4 の covr）。
/// Mac 版は AVFoundation に任せているが、Windows には同じものが無いので、形式を直接読む。
public static class EmbeddedArt
{
    public static byte[]? Extract(string path)
    {
        try
        {
            using var f = File.OpenRead(path);
            var head = new byte[12];
            if (f.Read(head, 0, 12) < 12) return null;
            f.Position = 0;
            if (head[0] == 'I' && head[1] == 'D' && head[2] == '3') return FromId3(f);
            if (Encoding.ASCII.GetString(head, 4, 4) is "ftyp" or "moov" or "free" or "wide" or "mdat" or "skip") return FromMp4(f);
            return null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    // ---- ID3v2 ----

    static int SyncSafe(byte[] b, int o) => (b[o] & 0x7f) << 21 | (b[o + 1] & 0x7f) << 14 | (b[o + 2] & 0x7f) << 7 | (b[o + 3] & 0x7f);
    static int BigEndian(byte[] b, int o, int n) { int v = 0; for (int i = 0; i < n; i++) v = v << 8 | b[o + i]; return v; }

    static byte[] RemoveUnsync(byte[] data, int start, int length)
    {
        var o = new List<byte>(length);
        for (int i = start; i < start + length; i++)
        {
            o.Add(data[i]);
            if (data[i] == 0xFF && i + 1 < start + length && data[i + 1] == 0x00) i++;
        }
        return o.ToArray();
    }

    static byte[]? FromId3(Stream f)
    {
        var hdr = new byte[10];
        if (f.Read(hdr, 0, 10) < 10) return null;
        int ver = hdr[3], flags = hdr[5], size = SyncSafe(hdr, 6);
        if (ver < 2 || ver > 4 || size <= 0) return null;
        var tag = new byte[size];
        if (f.Read(tag, 0, size) < size) return null;
        if ((flags & 0x80) != 0 && ver < 4) tag = RemoveUnsync(tag, 0, tag.Length);   // v2.4 はフレームごと
        int pos = 0;
        if ((flags & 0x40) != 0 && ver >= 3)   // 拡張ヘッダー
        {
            int ext = ver == 4 ? SyncSafe(tag, 0) : BigEndian(tag, 0, 4) + 4;
            pos = ext;
        }
        byte[]? first = null;
        int idLen = ver == 2 ? 3 : 4, hLen = ver == 2 ? 6 : 10;
        while (pos + hLen <= tag.Length)
        {
            string id = Encoding.ASCII.GetString(tag, pos, idLen);
            if (id[0] == '\0') break;
            int fsize = ver == 2 ? BigEndian(tag, pos + 3, 3) : ver == 4 ? SyncSafe(tag, pos + 4) : BigEndian(tag, pos + 4, 4);
            int fflags = ver == 2 ? 0 : BigEndian(tag, pos + 8, 2);
            int body = pos + hLen;
            if (fsize <= 0 || body + fsize > tag.Length) break;
            if (id is "APIC" or "PIC")
            {
                byte[] frame = tag.AsSpan(body, fsize).ToArray();
                if (ver == 4)
                {
                    int skip = (fflags & 0x0001) != 0 ? 4 : 0;   // データ長の指示子
                    if ((fflags & 0x0002) != 0 || (flags & 0x80) != 0) frame = RemoveUnsync(frame, skip, frame.Length - skip);
                    else frame = frame[skip..];
                    if ((fflags & 0x000C) != 0) { pos = body + fsize; continue; }   // 圧縮・暗号化は扱わない
                }
                var (type, img) = ParsePicture(frame, id == "PIC");
                if (img != null && img.Length > 0)
                {
                    if (type == 3) return img;          // 表紙（front cover）を優先
                    first ??= img;
                }
            }
            pos = body + fsize;
        }
        return first;
    }

    static (int type, byte[]? data) ParsePicture(byte[] fr, bool v22)
    {
        if (fr.Length < 4) return (0, null);
        int enc = fr[0], p = 1;
        if (v22) p += 3;                                  // 画像形式 "JPG" など
        else { while (p < fr.Length && fr[p] != 0) p++; p++; }   // MIME（Latin-1, 終端 0）
        if (p >= fr.Length) return (0, null);
        int type = fr[p++];
        // 説明文（文字コードに応じた終端）
        if (enc == 1 || enc == 2)
        {
            while (p + 1 < fr.Length && !(fr[p] == 0 && fr[p + 1] == 0)) p += 2;
            p += 2;
        }
        else { while (p < fr.Length && fr[p] != 0) p++; p++; }
        if (p >= fr.Length) return (type, null);
        return (type, fr[p..]);
    }

    // ---- MP4 (moov/udta/meta/ilst/covr/data) ----

    static byte[]? FromMp4(Stream f)
    {
        long end = f.Length;
        return FindCovr(f, 0, end, new[] { "moov", "udta", "meta", "ilst", "covr" }, 0);
    }

    static byte[]? FindCovr(Stream f, long start, long end, string[] path, int depth)
    {
        long pos = start;
        var h = new byte[16];
        while (pos + 8 <= end)
        {
            f.Position = pos;
            if (f.Read(h, 0, 8) < 8) return null;
            long size = (uint)BigEndian(h, 0, 4);
            string type = Encoding.ASCII.GetString(h, 4, 4);
            int hdr = 8;
            if (size == 1) { if (f.Read(h, 8, 8) < 8) return null; size = (long)ReadU64(h, 8); hdr = 16; }
            else if (size == 0) size = end - pos;
            if (size < hdr || pos + size > end) return null;
            if (type == path[depth])
            {
                long bodyStart = pos + hdr;
                if (type == "meta") bodyStart += 4;          // meta は version/flags の4バイトを持つ
                if (depth == path.Length - 1) return FirstData(f, bodyStart, pos + size);
                var r = FindCovr(f, bodyStart, pos + size, path, depth + 1);
                if (r != null) return r;
            }
            pos += size;
        }
        return null;
    }

    static ulong ReadU64(byte[] b, int o) { ulong v = 0; for (int i = 0; i < 8; i++) v = v << 8 | b[o + i]; return v; }

    static byte[]? FirstData(Stream f, long start, long end)
    {
        long pos = start;
        var h = new byte[16];
        while (pos + 16 <= end)
        {
            f.Position = pos;
            if (f.Read(h, 0, 16) < 16) return null;
            long size = (uint)BigEndian(h, 0, 4);
            if (size < 16 || pos + size > end) return null;
            if (Encoding.ASCII.GetString(h, 4, 4) == "data")
            {
                var d = new byte[size - 16];      // 8 (ヘッダー) + 4 (型) + 4 (ロケール)
                if (f.Read(d, 0, d.Length) < d.Length) return null;
                if (d.Length > 0) return d;
            }
            pos += size;
        }
        return null;
    }
}
