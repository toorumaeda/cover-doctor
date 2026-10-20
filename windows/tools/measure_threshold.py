#!/usr/bin/env python3
"""しきい値（同じ絵柄とみなす dHash の距離 70）が、Windows 版の画像処理でも妥当かを測る。
Mac 版は実機の 167 組（同じ絵柄）と 164 組（別の絵柄）で、同じ絵柄は最大55、別の絵柄は最小93 だった。
ここでは、ジャケットに似せた合成画像を作り、「同じ絵柄の変形（縮小・再圧縮・PNG化・端の切り落とし・曲からの取り出しと同じJPEG化）」と
「別の絵柄（似たデザインどうしを含む）」の距離を、coverdoctor hash（アプリと同じコード）で測る。
実機の画像での測定は tools/measure_plan.py（診断結果の分布）で行う。
使い方: python3 tools/measure_threshold.py <coverdoctor のパス> [作業フォルダ]   必要: Pillow"""
import os, sys, random, subprocess, itertools, json
from PIL import Image, ImageDraw, ImageFilter, ImageFont

BIN = sys.argv[1]
W = sys.argv[2] if len(sys.argv) > 2 else "/tmp/cd-threshold"
os.makedirs(W, exist_ok=True)
rnd = random.Random(2026)

def base_image(i):
    """ジャケット風の画像。種類を変えて、似たデザインの組（同じテンプレート・別の色や文字）も作る"""
    kind = i % 6
    s = 1000
    im = Image.new("RGB", (s, s))
    d = ImageDraw.Draw(im)
    r = random.Random(i * 7919)
    col = lambda: tuple(r.randrange(256) for _ in range(3))
    if kind == 0:   # 写真風（ぼかした乱数）
        small = Image.new("RGB", (12, 12)); small.putdata([col() for _ in range(144)])
        im = small.resize((s, s), Image.BICUBIC).filter(ImageFilter.GaussianBlur(30))
        d = ImageDraw.Draw(im)
    elif kind == 1:  # 単色背景に大きな図形
        d.rectangle([0, 0, s, s], fill=col())
        for _ in range(3): x, y = r.randrange(s), r.randrange(s); rr = r.randrange(100, 400); d.ellipse([x - rr, y - rr, x + rr, y + rr], fill=col())
    elif kind == 2:  # 縦のグラデーション
        a, b = col(), col()
        for y in range(s): t = y / s; d.line([(0, y), (s, y)], fill=tuple(int(a[k] * (1 - t) + b[k] * t) for k in range(3)))
    elif kind == 3:  # ブロック模様（テストの fixture と同じ系統）
        n = r.choice([4, 6, 8]); c = s // n
        for y in range(n):
            for x in range(n): d.rectangle([x * c, y * c, x * c + c, y * c + c], fill=col())
    elif kind == 4:  # ほぼ同じテンプレート（黒地に白い帯と文字）。別の絵柄どうしでも似る「難しい組」
        d.rectangle([0, 0, s, s], fill=(10, 10, 10)); y = 300 + (i // 6 % 5) * 80
        d.rectangle([0, y, s, y + 120], fill=(240, 240, 240))
    else:            # 斜めの縞
        a, b = col(), col(); w = r.randrange(40, 160)
        for k in range(-s, 2 * s, w * 2): d.polygon([(k, 0), (k + w, 0), (k + w - s, s), (k - s, s)], fill=a)
        d.rectangle([0, 0, s, 0], fill=b)
    # 文字（タイトル風）
    try: font = ImageFont.truetype("DejaVuSans-Bold.ttf", r.randrange(60, 140))
    except OSError: font = ImageFont.load_default()
    d.text((r.randrange(40, 300), r.randrange(40, 800)), "".join(r.choice("ABCDEFGHJKLMNPRSTUVWXYZ") for _ in range(r.randrange(3, 9))), fill=col(), font=font)
    return im

def variants(im, stem):
    out = []
    def save(img, name, **kw):
        p = os.path.join(W, f"{stem}_{name}"); img.save(p, **kw); out.append(p)
    for side in (1200, 600, 300, 150):
        save(im.resize((side, side), Image.LANCZOS), f"s{side}.jpg", quality=88)
    for q in (25, 50, 95): save(im, f"q{q}.jpg", quality=q)
    save(im, "png.png")
    for pct in (2, 4):
        c = int(im.width * pct / 100); save(im.crop((c, c, im.width - c, im.height - c)).resize((800, 800)), f"crop{pct}.jpg", quality=90)
    # 曲から取り出したときと同じ処理（長辺1200・品質0.9）を、アプリのコードで通す
    src = os.path.join(W, f"{stem}_src.jpg"); im.save(src, quality=92)
    j = os.path.join(W, f"{stem}_app.jpg"); subprocess.run([BIN, "jpeg", src, j], check=True); out.append(j)
    return src, out

N = int(os.environ.get("N", "60"))
bases, groups = [], []
for i in range(N):
    im = base_image(i)
    src, vs = variants(im, f"b{i:03d}")
    bases.append(src); groups.append(vs)

files = bases + [v for g in groups for v in g]
res = subprocess.run([BIN, "hash", *files], check=True, capture_output=True, text=True).stdout.strip().splitlines()
H = {}
for line in res:
    h, f = line.split("\t", 1)
    H[f] = int(h, 16) if h != "?" else None
dist = lambda a, b: bin(H[a] ^ H[b]).count("1")
same = [dist(b, v) for b, g in zip(bases, groups) for v in g]
diff = [dist(a, b) for a, b in itertools.combinations(bases, 2)]
hard = [dist(bases[i], bases[j]) for i, j in itertools.combinations(range(N), 2) if i % 6 == 4 and j % 6 == 4]
worst_same = sorted(((dist(b, v), os.path.basename(v)) for b, g in zip(bases, groups) for v in g), reverse=True)[:5]
summary = {
    "same_pairs": len(same), "same_max": max(same), "same_p99": sorted(same)[int(len(same) * .99) - 1],
    "diff_pairs": len(diff), "diff_min": min(diff), "diff_p01": sorted(diff)[int(len(diff) * .01)],
    "hard_diff_pairs": len(hard), "hard_diff_min": min(hard) if hard else None,
    "threshold": 70, "same_over_threshold": sum(d > 70 for d in same), "diff_within_threshold": sum(d <= 70 for d in diff),
    "worst_same": worst_same,
}
print(json.dumps(summary, indent=2, ensure_ascii=False))
