import Foundation
import AVFoundation
import ImageIO
import CoreGraphics
import UniformTypeIdentifiers
import SQLite3

// CoverRepair エンジン — iPhone のミュージックの「別アルバムのジャケット」を、曲ファイルの埋め込み画像で直す。
// コマンド(CLI.swift)と画面(App.swift)の両方から使う。

// MARK: - 外部ツール(afcfs)

let exeDir = URL(fileURLWithPath: CommandLine.arguments[0]).resolvingSymlinksInPath().deletingLastPathComponent()

let afcfsPath = exeDir.appendingPathComponent("afcfs").path

@discardableResult
func afcfs(_ args: [String]) -> (ok: Bool, out: String, err: String) {
    let p = Process()
    p.executableURL = URL(fileURLWithPath: afcfsPath)
    p.arguments = args
    let o = Pipe(), e = Pipe()
    p.standardOutput = o
    p.standardError = e
    do { try p.run() } catch { return (false, "", String(localized: "Could not start the helper tool: \(error.localizedDescription)")) }
    let od = o.fileHandleForReading.readDataToEndOfFile()
    let ed = e.fileHandleForReading.readDataToEndOfFile()
    p.waitUntilExit()
    return (p.terminationStatus == 0, String(decoding: od, as: UTF8.self), String(decoding: ed, as: UTF8.self))
}

// MARK: - 画像処理

func jpegData(from data: Data, maxSide: Int = 1200) -> Data? {
    guard let src = CGImageSourceCreateWithData(data as CFData, nil) else { return nil }
    let opts: [CFString: Any] = [
        kCGImageSourceCreateThumbnailFromImageAlways: true,
        kCGImageSourceCreateThumbnailWithTransform: true,
        kCGImageSourceThumbnailMaxPixelSize: maxSide,
    ]
    guard let img = CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary) else { return nil }
    let out = NSMutableData()
    guard let dest = CGImageDestinationCreateWithData(out, UTType.jpeg.identifier as CFString, 1, nil) else { return nil }
    CGImageDestinationAddImage(dest, img, [kCGImageDestinationLossyCompressionQuality: 0.9] as CFDictionary)
    return CGImageDestinationFinalize(dest) ? out as Data : nil
}

/// 17x16 のグレースケールにして、隣り合う画素の明暗を256ビットにする(dHash)。
/// 実データ(167組の同一画像と164組の別画像)では、同じ絵柄の距離は最大55（トリミング違い含む）、別の絵柄は最小93（Swift実測）。しきい値はその中間。
func dHash(of data: Data) -> [UInt64]? {
    guard let src = CGImageSourceCreateWithData(data as CFData, nil),
          let img = CGImageSourceCreateImageAtIndex(src, 0, nil) else { return nil }
    let w = 17, h = 16
    var px = [UInt8](repeating: 0, count: w * h)
    guard let ctx = CGContext(data: &px, width: w, height: h, bitsPerComponent: 8, bytesPerRow: w,
                              space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue) else { return nil }
    ctx.interpolationQuality = .medium
    ctx.draw(img, in: CGRect(x: 0, y: 0, width: w, height: h))
    var words = [UInt64](repeating: 0, count: 4)
    var n = 0
    for y in 0..<h { for x in 0..<(w - 1) {
        if px[y * w + x] > px[y * w + x + 1] { words[n / 64] |= (1 << UInt64(n % 64)) }
        n += 1
    } }
    return words
}

func hamming(_ a: [UInt64], _ b: [UInt64]) -> Int { zip(a, b).reduce(0) { $0 + ($1.0 ^ $1.1).nonzeroBitCount } }
let sameThreshold = 70

/// 曲ファイルの埋め込みジャケットを取り出す(MP3/M4A)。
func embeddedArtwork(at url: URL) async -> Data? {
    let asset = AVURLAsset(url: url)
    guard let items = try? await asset.load(.commonMetadata) else { return nil }
    for item in AVMetadataItem.metadataItems(from: items, filteredByIdentifier: .commonIdentifierArtwork) {
        if let d = try? await item.load(.dataValue), !d.isEmpty { return d }
    }
    return nil
}

// MARK: - SQLite(読み取り専用)

struct AlbumRow {
    let albumPID: Int64
    let name: String
    let token: String
    let relativePath: String      // Originals 配下
    let tracks: [(path: String, size: Int64)]   // 小さい順
}

func loadAlbums(dbPath: String) -> [AlbumRow] {
    var db: OpaquePointer?
    guard sqlite3_open_v2(dbPath, &db, SQLITE_OPEN_READONLY, nil) == SQLITE_OK else { return [] }
    defer { sqlite3_close(db) }
    let sql = """
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
    """
    var stmt: OpaquePointer?
    guard sqlite3_prepare_v2(db, sql, -1, &stmt, nil) == SQLITE_OK else { return [] }
    defer { sqlite3_finalize(stmt) }
    var order: [Int64] = []
    var meta: [Int64: (String, String, String)] = [:]
    var tracks: [Int64: [(String, Int64)]] = [:]
    while sqlite3_step(stmt) == SQLITE_ROW {
        let pid = sqlite3_column_int64(stmt, 0)
        func s(_ i: Int32) -> String { sqlite3_column_text(stmt, i).map { String(cString: $0) } ?? "" }
        if meta[pid] == nil { order.append(pid); meta[pid] = (s(1), s(2), s(3)) }
        tracks[pid, default: []].append((s(4), sqlite3_column_int64(stmt, 5)))
    }
    return order.map { pid in
        let m = meta[pid]!
        return AlbumRow(albumPID: pid, name: m.0, token: m.1, relativePath: m.2,
                        tracks: (tracks[pid] ?? []).map { (path: $0.0, size: $0.1) })
    }
}

struct PlaylistRow {
    let pid: Int64
    let name: String
    let token: String
    let relativePath: String
    let albumPIDs: [Int64]      // 収録順・重複なし
}

/// 画像（ジャケット）が設定されているプレイリストと、その収録アルバム（収録順・重複なし）を読む。
func loadPlaylists(dbPath: String) -> [PlaylistRow] {
    var db: OpaquePointer?
    guard sqlite3_open_v2(dbPath, &db, SQLITE_OPEN_READONLY, nil) == SQLITE_OK else { return [] }
    defer { sqlite3_close(db) }
    func text(_ st: OpaquePointer?, _ i: Int32) -> String { sqlite3_column_text(st, i).map { String(cString: $0) } ?? "" }
    var stmt: OpaquePointer?
    let q1 = """
    select c.container_pid, c.name, t.artwork_token, r.relative_path
    from container c
    join artwork_token t on t.entity_pid = c.container_pid and t.entity_type = 1 and t.artwork_source_type = 300
    join artwork r on r.artwork_token = t.artwork_token and r.artwork_source_type = t.artwork_source_type
                  and r.artwork_variant_type = t.artwork_variant_type
    order by c.container_pid
    """
    guard sqlite3_prepare_v2(db, q1, -1, &stmt, nil) == SQLITE_OK else { return [] }
    var heads: [(Int64, String, String, String)] = []
    while sqlite3_step(stmt) == SQLITE_ROW { heads.append((sqlite3_column_int64(stmt, 0), text(stmt, 1), text(stmt, 2), text(stmt, 3))) }
    sqlite3_finalize(stmt)
    var result: [PlaylistRow] = []
    for h in heads {
        var st: OpaquePointer?
        let q2 = """
        select i.album_pid from container_item ci
        join item i on i.item_pid = ci.item_pid and i.in_my_library = 1
        where ci.container_pid = ? order by ci.position
        """
        guard sqlite3_prepare_v2(db, q2, -1, &st, nil) == SQLITE_OK else { continue }
        sqlite3_bind_int64(st, 1, h.0)
        var albums: [Int64] = []
        while sqlite3_step(st) == SQLITE_ROW { let a = sqlite3_column_int64(st, 0); if !albums.contains(a) { albums.append(a) } }
        sqlite3_finalize(st)
        result.append(PlaylistRow(pid: h.0, name: h.1, token: h.2, relativePath: h.3, albumPIDs: albums))
    }
    return result
}

/// 4種類以上のアルバムがあれば、先頭4枚を2×2に並べたコラージュ。それ未満なら、先頭のジャケット1枚。
func playlistCoverImage(from covers: [Data]) -> Data? {
    guard let first = covers.first else { return nil }
    guard covers.count >= 4 else { return first }
    let size = 600, cell = 300
    guard let ctx = CGContext(data: nil, width: size, height: size, bitsPerComponent: 8, bytesPerRow: 0,
                              space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.noneSkipLast.rawValue) else { return nil }
    ctx.setFillColor(CGColor(gray: 1, alpha: 1)); ctx.fill(CGRect(x: 0, y: 0, width: size, height: size))
    let origins = [(0, cell), (cell, cell), (0, 0), (cell, 0)]      // 左上, 右上, 左下, 右下
    for (i, data) in covers.prefix(4).enumerated() {
        guard let src = CGImageSourceCreateWithData(data as CFData, nil), let img = CGImageSourceCreateImageAtIndex(src, 0, nil) else { return nil }
        let rect = CGRect(x: origins[i].0, y: origins[i].1, width: cell, height: cell)
        ctx.saveGState(); ctx.clip(to: rect)
        let w = CGFloat(img.width), h = CGFloat(img.height), k = max(CGFloat(cell) / w, CGFloat(cell) / h)      // 縦横比を保って、枠いっぱいに
        ctx.draw(img, in: CGRect(x: rect.midX - w * k / 2, y: rect.midY - h * k / 2, width: w * k, height: h * k))
        ctx.restoreGState()
    }
    guard let out = ctx.makeImage() else { return nil }
    let buf = NSMutableData()
    guard let dest = CGImageDestinationCreateWithData(buf, UTType.jpeg.identifier as CFString, 1, nil) else { return nil }
    CGImageDestinationAddImage(dest, out, [kCGImageDestinationLossyCompressionQuality: 0.9] as CFDictionary)
    return CGImageDestinationFinalize(dest) ? buf as Data : nil
}

/// ライブラリにある（曲が入っている）アルバムの総数。
func libraryAlbumCount(dbPath: String) -> Int {
    var db: OpaquePointer?
    guard sqlite3_open_v2(dbPath, &db, SQLITE_OPEN_READONLY, nil) == SQLITE_OK else { return 0 }
    defer { sqlite3_close(db) }
    var st: OpaquePointer?
    guard sqlite3_prepare_v2(db, "select count(distinct album_pid) from item where in_my_library = 1", -1, &st, nil) == SQLITE_OK else { return 0 }
    defer { sqlite3_finalize(st) }
    return sqlite3_step(st) == SQLITE_ROW ? Int(sqlite3_column_int64(st, 0)) : 0
}

// MARK: - 計画ファイル

struct PlanEntry: Codable, Identifiable {
    var id: String { token }
    var token: String
    var album: String
    var relativePath: String
    var status: String          // same / wrong / noEmbeddedArt / custom（プレイリスト: 利用者が設定したかもしれないので触らない）
    var distance: Int?
    var kind: String? = nil     // nil または "album" / "playlist"
    var isPlaylist: Bool { kind == "playlist" }
}

struct Plan: Codable {
    var udid: String
    var iosVersion: String
    var created: String
    var entries: [PlanEntry]
    var libraryAlbumCount: Int? = nil       // ライブラリにあるアルバムの総数（画像の記録の有無を問わない）
    var notDiagnosedCount: Int? = nil       // うち、画像の記録が無く、診断できなかった数
}

// MARK: - エンジン

struct EngineError: Error { let message: String }

struct DeviceInfo: Equatable { let udid: String; let version: String; let name: String }

struct Progress { let message: String; let done: Int; let total: Int }
typealias ProgressHandler = @Sendable (Progress) -> Void

func connectedDevice() -> DeviceInfo? {
    let r = afcfs(["devices"])
    guard let line = r.out.split(separator: "\n").first else { return nil }
    let f = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
    guard f.count >= 4 else { return nil }
    return DeviceInfo(udid: f[1], version: f[2], name: f[3])
}

func defaultWorkDir(for udid: String) -> URL {
    FileManager.default.homeDirectoryForCurrentUser
        .appendingPathComponent("Library/Application Support/CoverRepair/\(udid)")
}

func loadPlan(work: URL) -> Plan? {
    guard let data = try? Data(contentsOf: work.appendingPathComponent("plan.json")) else { return nil }
    return try? JSONDecoder().decode(Plan.self, from: data)
}

/// 読み取りだけの診断。iPhone は変更しない。
func engineDiagnose(work: URL, reuse: Bool, progress: ProgressHandler) async throws -> Plan {
    guard let dev = connectedDevice() else {
        throw EngineError(message: String(localized: "No iPhone found. Connect it with a USB cable, unlock it, and choose “Trust This Computer”."))
    }
    let fm = FileManager.default
    let originals = work.appendingPathComponent("originals")   // 直す前の画像のバックアップ（最初の1回だけ保存。上書きしない）
    let current = work.appendingPathComponent("current")       // 今の iPhone 上の画像（毎回取得）
    let fresh = work.appendingPathComponent("fresh")           // 曲から取り出した正しい画像
    let tmp = work.appendingPathComponent("tmp")
    for d in [originals, current, fresh, tmp] { try? fm.createDirectory(at: d, withIntermediateDirectories: true) }

    progress(Progress(message: String(localized: "Reading the library information…"), done: 0, total: 0))
    let dbBase = work.appendingPathComponent("MediaLibrary.sqlitedb").path
    for suffix in ["", "-wal", "-shm"] {
        try? fm.removeItem(atPath: dbBase + suffix)
        let r = afcfs(["get", dev.udid, "/iTunes_Control/iTunes/MediaLibrary.sqlitedb" + suffix, dbBase + suffix])
        if !r.ok && suffix.isEmpty { throw EngineError(message: String(localized: "Could not read the library information. Unlock your iPhone and try again. (\(r.err.trimmingCharacters(in: .whitespacesAndNewlines)))")) }
    }
    let albums = loadAlbums(dbPath: dbBase)
    var entries: [PlanEntry] = []
    for (i, a) in albums.enumerated() {
        try Task.checkCancellation()
        progress(Progress(message: a.name, done: i, total: albums.count))
        let cur = current.appendingPathComponent("\(a.token).img")
        try? fm.removeItem(at: cur)
        _ = afcfs(["get", dev.udid, "/iTunes_Control/iTunes/Artwork/Originals/" + a.relativePath, cur.path])
        let curData = try? Data(contentsOf: cur)
        let keep = originals.appendingPathComponent("\(a.token).img")
        if curData != nil, !fm.fileExists(atPath: keep.path) { try? fm.copyItem(at: cur, to: keep) }

        var art: Data?
        if reuse, let cached = try? Data(contentsOf: fresh.appendingPathComponent("\(a.token).jpg")) { art = cached }
        for t in a.tracks.filter({ $0.size > 200_000 }).prefix(8) {
            if art != nil { break }
            let local = tmp.appendingPathComponent(URL(fileURLWithPath: t.path).lastPathComponent)
            let r = afcfs(["get", dev.udid, "/" + t.path, local.path])
            defer { try? fm.removeItem(at: local) }
            guard r.ok else { continue }
            if let raw = await embeddedArtwork(at: local), let jpg = jpegData(from: raw) { art = jpg; break }
        }
        var entry = PlanEntry(token: a.token, album: a.name, relativePath: a.relativePath, status: "noEmbeddedArt", distance: nil)
        if let art = art {
            try? art.write(to: fresh.appendingPathComponent("\(a.token).jpg"))
            if let cd = curData, let h1 = dHash(of: cd), let h2 = dHash(of: art) {
                let d = hamming(h1, h2)
                entry.distance = d
                entry.status = d <= sameThreshold ? "same" : "wrong"
            } else {
                entry.status = "wrong"   // 現在の画像が壊れている・読めない
            }
        }
        entries.append(entry)
    }
    // --- プレイリストのジャケット ---
    let playlists = loadPlaylists(dbPath: dbBase)
    if !playlists.isEmpty {
        var coverByAlbum: [Int64: (data: Data, hash: [UInt64])] = [:]
        for a in albums {
            if let d = try? Data(contentsOf: fresh.appendingPathComponent("\(a.token).jpg")), let h = dHash(of: d) { coverByAlbum[a.albumPID] = (d, h) }
        }
        let libraryHashes = coverByAlbum.values.map { $0.hash }
        for (i, p) in playlists.enumerated() {
            try Task.checkCancellation()
            progress(Progress(message: p.name, done: i, total: playlists.count))
            let cur = current.appendingPathComponent("\(p.token).img")
            try? fm.removeItem(at: cur)
            _ = afcfs(["get", dev.udid, "/iTunes_Control/iTunes/Artwork/Originals/" + p.relativePath, cur.path])
            let curData = try? Data(contentsOf: cur)
            let keep = originals.appendingPathComponent("\(p.token).img")
            if curData != nil, !fm.fileExists(atPath: keep.path) { try? fm.copyItem(at: cur, to: keep) }
            let covers = p.albumPIDs.compactMap { coverByAlbum[$0] }
            guard let expected = playlistCoverImage(from: covers.map { $0.data }), let eh = dHash(of: expected) else { continue }   // 比べる材料が無い
            var entry = PlanEntry(token: p.token, album: p.name, relativePath: p.relativePath, status: "custom", distance: nil, kind: "playlist")
            if let cd = curData, let ch = dHash(of: cd) {
                let theirs = covers.map { hamming(ch, $0.hash) }.min() ?? Int.max
                let d = min(hamming(ch, eh), theirs)
                entry.distance = d
                if d <= sameThreshold { entry.status = "same" }
                else if libraryHashes.contains(where: { hamming(ch, $0) <= sameThreshold }) { entry.status = "wrong" }     // 収録外のアルバムのジャケット = 結びつきの誤り
                else { entry.status = "custom" }                                                                              // どのアルバムとも違う = 利用者が設定した画像かもしれない
            } else { entry.status = "wrong" }                                                                                  // 画像が壊れている・読めない
            if entry.status == "wrong" { try? expected.write(to: fresh.appendingPathComponent("\(p.token).jpg")) }
            entries.append(entry)
        }
    }
    progress(Progress(message: String(localized: "Done"), done: albums.count, total: albums.count))
    let libraryTotal = libraryAlbumCount(dbPath: dbBase)
    let plan = Plan(udid: dev.udid, iosVersion: dev.version, created: ISO8601DateFormatter().string(from: Date()), entries: entries,
                    libraryAlbumCount: libraryTotal, notDiagnosedCount: max(0, libraryTotal - albums.count))
    let enc = JSONEncoder(); enc.outputFormatting = [.prettyPrinted, .sortedKeys]
    try? enc.encode(plan).write(to: work.appendingPathComponent("plan.json"))
    return plan
}

struct FixResult { var ok = 0; var failed: [String] = [] }

/// 診断結果の「直せる」アルバムの画像を置き換える。置き換えた一覧は applied.json に残す。
func engineFix(work: URL, plan: Plan, progress: ProgressHandler) async throws -> FixResult {
    guard let dev = connectedDevice(), dev.udid == plan.udid else {
        throw EngineError(message: String(localized: "The iPhone you diagnosed is not connected."))
    }
    let targets = plan.entries.filter { $0.status == "wrong" }
    var result = FixResult()
    var applied: [PlanEntry] = []
    for (i, e) in targets.enumerated() {
        try Task.checkCancellation()
        progress(Progress(message: e.album, done: i, total: targets.count))
        let backup = work.appendingPathComponent("originals/\(e.token).img")
        let fresh = work.appendingPathComponent("fresh/\(e.token).jpg")
        guard FileManager.default.fileExists(atPath: backup.path), FileManager.default.fileExists(atPath: fresh.path) else {
            result.failed.append(e.album); continue
        }
        let remote = "/iTunes_Control/iTunes/Artwork/Originals/" + e.relativePath
        let r = afcfs(["put", dev.udid, fresh.path, remote])
        // 読み戻して見た目で確認（iOS がファイルを作り直すことがあるため、バイト一致ではなく見た目で判定）
        let check = work.appendingPathComponent("tmp/verify.img")
        _ = afcfs(["get", dev.udid, remote, check.path])
        if r.ok, let d = try? Data(contentsOf: check), let h1 = dHash(of: d),
           let f = try? Data(contentsOf: fresh), let h2 = dHash(of: f), hamming(h1, h2) <= sameThreshold {
            result.ok += 1; applied.append(e)
        } else { result.failed.append(e.album) }
    }
    progress(Progress(message: String(localized: "Done"), done: targets.count, total: targets.count))
    // 取り消し用に、置き換えた一覧を追記保存
    var all = (try? JSONDecoder().decode([PlanEntry].self, from: Data(contentsOf: work.appendingPathComponent("applied.json")))) ?? []
    all.removeAll { a in applied.contains { $0.token == a.token } }
    all.append(contentsOf: applied)
    try? JSONEncoder().encode(all).write(to: work.appendingPathComponent("applied.json"))
    return result
}

/// 置き換えたアルバムを、バックアップ（最初に見た画像）に戻す。
func engineRestore(work: URL, progress: ProgressHandler) async throws -> Int {
    guard let dev = connectedDevice() else { throw EngineError(message: String(localized: "No iPhone found.")) }
    guard let data = try? Data(contentsOf: work.appendingPathComponent("applied.json")),
          let applied = try? JSONDecoder().decode([PlanEntry].self, from: data) else {
        throw EngineError(message: String(localized: "There is nothing to undo."))
    }
    var ok = 0
    for (i, e) in applied.enumerated() {
        progress(Progress(message: e.album, done: i, total: applied.count))
        let backup = work.appendingPathComponent("originals/\(e.token).img")
        guard FileManager.default.fileExists(atPath: backup.path) else { continue }
        if afcfs(["put", dev.udid, backup.path, "/iTunes_Control/iTunes/Artwork/Originals/" + e.relativePath]).ok { ok += 1 }
    }
    try? FileManager.default.removeItem(at: work.appendingPathComponent("applied.json"))
    return ok
}
