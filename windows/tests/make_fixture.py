#!/usr/bin/env python3
"""偽の iPhone（AFC ルート）を作る。実機の iTunes_Control と同じ構造・列名の最小版。
使い方: python3 make_fixture.py <出力フォルダ>   → <出力>/iphone に偽iPhone、<出力>/expected.json に期待値を書く
必要なもの: ffmpeg（曲ファイルと画像の生成にだけ使う）"""
import os, sys, json, random, sqlite3, subprocess, hashlib, shutil

out = os.path.abspath(sys.argv[1])
root = os.path.join(out, "iphone")
shutil.rmtree(out, ignore_errors=True)
itunes = os.path.join(root, "iTunes_Control", "iTunes")
orig_dir = os.path.join(itunes, "Artwork", "Originals")
music = os.path.join(root, "iTunes_Control", "Music", "F00")
img_dir = os.path.join(out, "images")
for d in (orig_dir, music, img_dir): os.makedirs(d, exist_ok=True)

def ff(*args):
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", *args], check=True)

def make_cover(name, seed):
    """乱数ブロック模様の画像（別々のseedなら見た目が確実に違う）"""
    rnd = random.Random(seed)
    ppm = os.path.join(img_dir, name + ".ppm")
    n, cell = 8, 50
    blocks = [[tuple(rnd.randrange(256) for _ in range(3)) for _ in range(n)] for _ in range(n)]
    with open(ppm, "wb") as f:
        f.write(f"P6 {n*cell} {n*cell} 255\n".encode())
        for y in range(n * cell):
            row = b"".join(bytes(blocks[y // cell][x // cell]) * 1 for x in range(n * cell) for _ in [0])
            f.write(row)
    jpg = os.path.join(img_dir, name + ".jpg")
    ff("-i", ppm, "-q:v", "2", jpg)
    return jpg

def make_track(path, cover, title, album, ext):
    if ext == "mp3":
        args = ["-f", "lavfi", "-i", "sine=frequency=440:duration=2"]
        if cover: args += ["-i", cover, "-map", "0:a", "-map", "1:v", "-c:v", "copy", "-disposition:v", "attached_pic"]
        args += ["-c:a", "libmp3lame", "-b:a", "96k", "-id3v2_version", "3"]
    else:
        args = ["-f", "lavfi", "-i", "sine=frequency=660:duration=2"]
        if cover: args += ["-i", cover, "-map", "0:a", "-map", "1:v", "-c:v", "mjpeg", "-disposition:v", "attached_pic"]
        args += ["-c:a", "aac", "-b:a", "96k", "-f", "ipod"]
    args += ["-metadata", f"title={title}", "-metadata", f"album={album}", path]
    ff(*args)

db_path = os.path.join(itunes, "MediaLibrary.sqlitedb")
db = sqlite3.connect(db_path)
db.executescript("""
create table album (album_pid integer primary key, album text);
create table artwork_token (entity_pid integer, entity_type integer, artwork_source_type integer, artwork_token text, artwork_variant_type integer default 0);
create table artwork (artwork_token text, artwork_source_type integer, artwork_variant_type integer default 0, relative_path text);
create table item (item_pid integer primary key, album_pid integer, in_my_library integer, base_location_id integer);
create table item_extra (item_pid integer primary key, location text, file_size integer);
create table base_location (base_location_id integer primary key, path text);
create table container (container_pid integer primary key, name text);
create table container_item (container_item_pid integer primary key, container_pid integer, item_pid integer, position integer);
insert into base_location values (3850, 'iTunes_Control/Music/F00');
""")

covers = {k: make_cover(k, i + 1) for i, k in enumerate("ABCDEFGH")}
counter = {"token": 4600, "item": 6000000, "file": 0}
expected = {}
initial = {}          # トークン -> 初期の Originals ファイルのsha256（元に戻すテスト用）
items_by_album = {}   # album_pid -> [item_pid]

def put_original(token, src_bytes):
    h = hashlib.sha1(f"orig-{token}".encode()).hexdigest()
    rel = f"{h[:2]}/{h[2:]}"
    p = os.path.join(orig_dir, rel); os.makedirs(os.path.dirname(p), exist_ok=True)
    open(p, "wb").write(src_bytes)
    return rel

def add_album(pid, name, tracks, original_bytes, expect, in_lib=1):
    """tracks: [(ext, cover_key or None)]。最初に作った曲のうち、画像付きの曲があればそれを『アルバム画像の出どころ』にする"""
    counter["token"] += 1
    token = str(counter["token"])
    db.execute("insert into album values (?,?)", (pid, name))
    src_item = None
    for ext, cov in tracks:
        counter["item"] += 1; counter["file"] += 1
        item = counter["item"]
        fname = f"T{counter['file']:04d}.{ext}"
        make_track(os.path.join(music, fname), covers[cov] if cov else None, f"{name} track", name, ext)
        items_by_album.setdefault(pid, []).append(item)
        db.execute("insert into item values (?,?,?,3850)", (item, pid, in_lib))
        db.execute("insert into item_extra values (?,?,?)", (item, fname, 400000 + counter["file"]))   # file_size は「200KB超」の判定用
        if cov and src_item is None: src_item = item
    rel = put_original(token, original_bytes)
    db.execute("insert into artwork_token values (?,4,300,?,0)", (pid, token))
    db.execute("insert into artwork values (?,300,0,?)", (token, rel))
    if src_item: db.execute("insert into artwork_token values (?,0,300,?,0)", (src_item, token))
    expected[token] = {"album": name, "status": expect}
    initial[token] = hashlib.sha256(original_bytes).hexdigest()

rd = lambda k: open(covers[k], "rb").read()

# 1. すでに正しい（同じ画像）
add_album(1, "Correct", [("mp3", "A"), ("mp3", "A")], rd("A"), "same")
# 2. 別アルバムの画像が付いている（誤り）
add_album(2, "SwappedWithA", [("mp3", "B"), ("mp3", "B")], rd("A"), "wrong")
add_album(3, "SwappedWithB", [("m4a", "C")], rd("B"), "wrong")
# 3. 画像が埋め込まれた曲が、アルバム内に1曲だけ（実機の Winger IV と同じ形）
add_album(4, "OneTrackHasArt", [("mp3", None), ("mp3", None), ("mp3", None), ("mp3", None), ("mp3", None), ("mp3", "D")], rd("A"), "wrong")
# 4. 曲に画像が1枚も無い
add_album(5, "NoArtAnywhere", [("mp3", None), ("mp3", None)], rd("E"), "noEmbeddedArt")
# 5. 同じ絵柄で、解像度・圧縮が違う（誤判定しないこと）
subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", covers["F"], "-vf", "scale=150:150", "-q:v", "9", os.path.join(img_dir, "F_small.jpg")], check=True)
add_album(6, "SameButSmaller", [("mp3", "F")], open(os.path.join(img_dir, "F_small.jpg"), "rb").read(), "same")
# 6. 現在の画像ファイルが壊れている
add_album(7, "CorruptOriginal", [("mp3", "G")], bytes(random.Random(9).randrange(256) for _ in range(3000)), "wrong")
# 7. ライブラリに入っていない曲だけのアルバム（対象外）
db.execute("insert into album values (8,'NotInLibrary')")
for _ in range(2):
    counter["item"] += 1
    db.execute("insert into item values (?,8,0,3850)", (counter["item"],))
    db.execute("insert into item_extra values (?,?,?)", (counter["item"], "none.mp3", 400000))
# 7b. プレイリスト（ジャケットは「収録アルバムのコラージュ（4種類以上）または先頭1枚」が正しい）
def add_playlist(cid, name, album_pids, original_bytes, expect, with_art=True):
    db.execute("insert into container values (?,?)", (cid, name))
    pos = 0
    for ap in album_pids:
        for it in items_by_album[ap]:
            pos += 1; db.execute("insert into container_item (container_pid, item_pid, position) values (?,?,?)", (cid, it, pos))
    if not with_art: return
    counter["token"] += 1
    token = str(counter["token"])
    rel = put_original(token, original_bytes)
    db.execute("insert into artwork_token values (?,1,300,?,0)", (cid, token))
    db.execute("insert into artwork values (?,300,0,?)", (token, rel))
    expected[token] = {"album": "PL:" + name, "status": expect, "kind": "playlist"}
    initial[token] = hashlib.sha256(original_bytes).hexdigest()

covers["X"] = make_cover("X", 77)     # ライブラリのどのアルバムにも無い画像（利用者が設定したカバー）
# 4種類以上の収録なのに、収録外アルバム（G）のジャケットが付いている → 誤り（コラージュに直る）
add_playlist(1, "PlaylistWrongCollage", [1, 2, 3, 4, 6], rd("G"), "wrong")
# 1アルバムだけの収録に、別のアルバム（A）のジャケット → 誤り（先頭アルバム＝B の1枚に直る）
add_playlist(2, "PlaylistWrongSingle", [2], rd("A"), "wrong")
# 収録アルバム（A）のジャケットが付いている → 正しい
add_playlist(3, "PlaylistOk", [1, 2], rd("A"), "same")
# どのアルバムとも違う画像 → 利用者が設定したカバーかもしれないので触らない
add_playlist(4, "PlaylistCustom", [1], rd("X"), "custom")
# ジャケット設定なし → 対象外
add_playlist(5, "PlaylistNoArt", [1, 2], b"", "none", with_art=False)

# 7c. 画像の記録がまったく無いアルバム（診断の対象外。ライブラリの総数には数える）
db.execute("insert into album values (9,'NoRecordAlbum')")
counter["item"] += 1; counter["file"] += 1
fname = f"T{counter['file']:04d}.mp3"
make_track(os.path.join(music, fname), None, "NoRecord track", "NoRecordAlbum", "mp3")
db.execute("insert into item values (?,9,1,3850)", (counter["item"],))
db.execute("insert into item_extra values (?,?,?)", (counter["item"], fname, 400000))
not_diagnosed = 1

# 8. 規模テスト用の追加アルバム（第2引数で枚数。3種類を順番に: 正しい / 誤り / 曲に画像なし）
extra = int(sys.argv[2]) if len(sys.argv) > 2 else 0
if extra:
    keys = [f"S{i}" for i in range(40)]
    for i, k in enumerate(keys): covers[k] = make_cover(k, 100 + i)
    for i in range(extra):
        k = keys[i % 40]; other = keys[(i + 7) % 40]
        kind = i % 3
        if kind == 0: add_album(100 + i, f"Bulk{i}-correct", [("mp3", k), ("mp3", k)], rd(k), "same")
        elif kind == 1: add_album(100 + i, f"Bulk{i}-wrong", [("mp3", k)], rd(other), "wrong")
        else: add_album(100 + i, f"Bulk{i}-noart", [("mp3", None)], rd(other), "noEmbeddedArt")
db.commit(); db.close()

json.dump({"expected": expected, "initial": initial, "not_diagnosed": not_diagnosed, "library_albums": len([k for k in expected if not expected[k].get("kind")]) + not_diagnosed}, open(os.path.join(out, "expected.json"), "w"), indent=2, ensure_ascii=False)
print(f"偽のiPhoneを作成: {root}（アルバム {len(expected)}）")
