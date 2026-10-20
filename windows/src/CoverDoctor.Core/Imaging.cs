using SkiaSharp;

namespace CoverDoctor.Core;

// 画像処理（Mac 版 Engine.swift の jpegData / dHash / playlistCoverImage に対応）。
// 画像の読み書きは SkiaSharp。dHash の縮小は、OS の画像処理に左右されないように自前の面積平均で行う
// （Windows でも、開発機の Linux でも、同じ入力から同じハッシュになる）。
public static class Imaging
{
    /// 同じ絵柄とみなす距離の上限。Mac 版と同じ値。Windows 版での実測は docs/THRESHOLD.md。
    public const int SameThreshold = 70;

    static SKBitmap? Decode(byte[] data, bool applyOrientation)
    {
        using var codec = SKCodec.Create(new MemoryStream(data));
        if (codec == null) return null;
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        var bmp = new SKBitmap(info);
        var r = codec.GetPixels(info, bmp.GetPixels());
        if (r != SKCodecResult.Success && r != SKCodecResult.IncompleteInput) { bmp.Dispose(); return null; }
        if (!applyOrientation || codec.EncodedOrigin == SKEncodedOrigin.TopLeft) return bmp;
        var rotated = Orient(bmp, codec.EncodedOrigin);
        bmp.Dispose();
        return rotated;
    }

    /// EXIF の向きを画素に反映する（ジャケットでは稀だが、Mac 版の縮小処理は向きを反映するため）。
    static SKBitmap Orient(SKBitmap src, SKEncodedOrigin origin)
    {
        int W = src.Width, H = src.Height;
        bool swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        int w = swap ? H : W, h = swap ? W : H;
        var dst = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        var s = src.GetPixelSpan();
        var d = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                (int sx, int sy) = origin switch
                {
                    SKEncodedOrigin.TopRight => (W - 1 - x, y),
                    SKEncodedOrigin.BottomRight => (W - 1 - x, H - 1 - y),
                    SKEncodedOrigin.BottomLeft => (x, H - 1 - y),
                    SKEncodedOrigin.LeftTop => (y, x),
                    SKEncodedOrigin.RightTop => (y, H - 1 - x),
                    SKEncodedOrigin.RightBottom => (W - 1 - y, H - 1 - x),
                    SKEncodedOrigin.LeftBottom => (W - 1 - y, x),
                    _ => (x, y),
                };
                s.Slice((sy * W + sx) * 4, 4).CopyTo(d.AsSpan((y * w + x) * 4, 4));
            }
        System.Runtime.InteropServices.Marshal.Copy(d, 0, dst.GetPixels(), d.Length);
        return dst;
    }

    static byte[]? EncodeJpeg(SKBitmap bmp, int quality)
    {
        // JPEG は透明を持たないので、白の上に描いてから書き出す
        using var opaque = new SKBitmap(new SKImageInfo(bmp.Width, bmp.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using (var c = new SKCanvas(opaque)) { c.Clear(SKColors.White); using var img = SKImage.FromBitmap(bmp); c.DrawImage(img, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), null); }
        using var data = opaque.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data?.ToArray();
    }

    /// 長辺を最大 maxSide ピクセルに収めて（拡大はしない）、品質 0.9 の JPEG にする。
    public static byte[]? ToJpeg(byte[] data, int maxSide = 1200)
    {
        using var bmp = Decode(data, applyOrientation: true);
        if (bmp == null || bmp.Width == 0 || bmp.Height == 0) return null;
        double k = Math.Min(1.0, (double)maxSide / Math.Max(bmp.Width, bmp.Height));
        if (k >= 1.0) return EncodeJpeg(bmp, 90);
        int w = Math.Max(1, (int)Math.Round(bmp.Width * k)), h = Math.Max(1, (int)Math.Round(bmp.Height * k));
        using var small = bmp.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul), new SKSamplingOptions(SKCubicResampler.Mitchell));
        return small == null ? null : EncodeJpeg(small, 90);
    }

    /// 17×16 のグレースケールに縮め、隣り合う画素の明暗を 256 ビットにする（dHash）。
    public static ulong[]? DHash(byte[] data)
    {
        using var bmp = Decode(data, applyOrientation: false);
        if (bmp == null || bmp.Width == 0 || bmp.Height == 0) return null;
        var px = AreaAverageGray(bmp, 17, 16);
        var words = new ulong[4];
        int n = 0;
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                if (px[y * 17 + x] > px[y * 17 + x + 1]) words[n / 64] |= 1UL << (n % 64);
                n++;
            }
        return words;
    }

    /// 面積平均で縮小したグレースケール（0〜255）。透明部分は白の上に置いたものとして扱う。
    static byte[] AreaAverageGray(SKBitmap bmp, int w, int h)
    {
        int sw = bmp.Width, sh = bmp.Height;
        var gray = new double[sw * sh];
        var span = bmp.GetPixelSpan();
        for (int i = 0; i < sw * sh; i++)
        {
            double r = span[i * 4], g = span[i * 4 + 1], b = span[i * 4 + 2], a = span[i * 4 + 3];
            // プリマルチプライド → 白背景に合成
            r += 255 - a; g += 255 - a; b += 255 - a;
            gray[i] = 0.299 * r + 0.587 * g + 0.114 * b;
        }
        var outPx = new byte[w * h];
        double fx = (double)sw / w, fy = (double)sh / h;
        for (int oy = 0; oy < h; oy++)
        {
            double y0 = oy * fy, y1 = (oy + 1) * fy;
            for (int ox = 0; ox < w; ox++)
            {
                double x0 = ox * fx, x1 = (ox + 1) * fx, sum = 0, area = 0;
                for (int sy = (int)y0; sy < Math.Min(sh, (int)Math.Ceiling(y1)); sy++)
                {
                    double wy = Math.Min(y1, sy + 1) - Math.Max(y0, sy);
                    if (wy <= 0) continue;
                    for (int sx = (int)x0; sx < Math.Min(sw, (int)Math.Ceiling(x1)); sx++)
                    {
                        double wx = Math.Min(x1, sx + 1) - Math.Max(x0, sx);
                        if (wx <= 0) continue;
                        sum += gray[sy * sw + sx] * wx * wy; area += wx * wy;
                    }
                }
                outPx[oy * w + ox] = (byte)Math.Clamp((int)Math.Round(area > 0 ? sum / area : 255), 0, 255);
            }
        }
        return outPx;
    }

    public static int Hamming(ulong[] a, ulong[] b)
    {
        int d = 0;
        for (int i = 0; i < a.Length; i++) d += System.Numerics.BitOperations.PopCount(a[i] ^ b[i]);
        return d;
    }

    /// 4種類以上のアルバムがあれば、先頭4枚を 2×2 に並べたコラージュ（600×600）。それ未満なら、先頭のジャケット1枚。
    public static byte[]? PlaylistCover(IReadOnlyList<byte[]> covers)
    {
        if (covers.Count == 0) return null;
        if (covers.Count < 4) return covers[0];
        const int size = 600, cell = 300;
        using var canvasBmp = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Opaque));
        using var c = new SKCanvas(canvasBmp);
        c.Clear(SKColors.White);
        var origins = new[] { (0, 0), (cell, 0), (0, cell), (cell, cell) };   // 左上, 右上, 左下, 右下
        using var paint = new SKPaint();
        for (int i = 0; i < 4; i++)
        {
            using var img = Decode(covers[i], applyOrientation: false);
            if (img == null) return null;
            var rect = SKRect.Create(origins[i].Item1, origins[i].Item2, cell, cell);
            c.Save(); c.ClipRect(rect);
            float k = Math.Max((float)cell / img.Width, (float)cell / img.Height);   // 縦横比を保って、枠いっぱいに
            float w = img.Width * k, h = img.Height * k;
            using var image = SKImage.FromBitmap(img);
            c.DrawImage(image, SKRect.Create(rect.MidX - w / 2, rect.MidY - h / 2, w, h), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
            c.Restore();
        }
        return EncodeJpeg(canvasBmp, 90);
    }
}
