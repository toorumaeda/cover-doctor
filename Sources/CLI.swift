import Foundation

// coverrepair diagnose [--reuse] [--work DIR]   読み取りだけ。結果は plan.json に保存
// coverrepair fix      [--work DIR] --yes       画像を置き換える
// coverrepair restore  [--work DIR] --yes       置き換えを取り消す
// coverrepair compare A B                       2つの画像の見た目の距離（開発用）

func argValue(_ name: String) -> String? {
    guard let i = CommandLine.arguments.firstIndex(of: name), i + 1 < CommandLine.arguments.count else { return nil }
    return CommandLine.arguments[i + 1]
}
func say(_ s: String) { FileHandle.standardOutput.write((s + "\n").data(using: .utf8)!) }

func cliWork() -> URL? {
    if let w = argValue("--work") { return URL(fileURLWithPath: w) }
    guard let dev = connectedDevice() else { say("iPhone が見つかりません。"); return nil }
    return defaultWorkDir(for: dev.udid)
}

@main
struct CoverRepairCLI {
    static func main() async {
        let cmd = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : ""
        var code: Int32 = 0
        let step: ProgressHandler = { p in if p.total > 0, (p.done + 1) % 20 == 0 || p.done == p.total { say("  \(p.done)/\(p.total)") } }
        do {
            switch cmd {
            case "diagnose":
                guard let work = cliWork() else { exit(1) }
                let plan = try await engineDiagnose(work: work, reuse: CommandLine.arguments.contains("--reuse"), progress: step)
                let wrong = plan.entries.filter { $0.status == "wrong" }
                if let total = plan.libraryAlbumCount, let skipped = plan.notDiagnosedCount {
                    say("ライブラリのアルバム \(total) 件 / 診断した \(total - skipped) 件 / 画像の記録なし \(skipped) 件（診断・修復できません）")
                }
                say("結果: 正しい \(plan.entries.filter { $0.status == "same" }.count) / 直せる \(wrong.count) / 画像が曲に無い \(plan.entries.filter { $0.status == "noEmbeddedArt" }.count)")
                for e in wrong.prefix(15) { say("  直す対象: \(e.isPlaylist ? "[プレイリスト] " : "")\(e.album)") }
                let custom = plan.entries.filter { $0.status == "custom" }
                if !custom.isEmpty { say("  触らない（自分で設定したカバーかもしれないプレイリスト）: \(custom.map { $0.album }.joined(separator: ", "))") }
                if wrong.count > 15 { say("  …ほか \(wrong.count - 15) 件") }
            case "fix":
                guard CommandLine.arguments.contains("--yes") else { say("--yes を付けてください。"); exit(2) }
                guard let work = cliWork(), let plan = loadPlan(work: work) else { say("先に diagnose を実行してください。"); exit(1) }
                let r = try await engineFix(work: work, plan: plan, progress: step)
                say("完了: 成功 \(r.ok) / 失敗 \(r.failed.count)。ミュージックを完全に終了して開き直してください。")
                if !r.failed.isEmpty { code = 1 }
            case "restore":
                guard CommandLine.arguments.contains("--yes") else { say("--yes を付けてください。"); exit(2) }
                guard let work = cliWork() else { exit(1) }
                say("元に戻しました: \(try await engineRestore(work: work, progress: step)) 件")
            case "compare":
                let a = CommandLine.arguments.dropFirst(2).map { URL(fileURLWithPath: $0) }
                if a.count == 2, let d1 = try? Data(contentsOf: a[0]), let d2 = try? Data(contentsOf: a[1]),
                   let h1 = dHash(of: d1), let h2 = dHash(of: d2) { say("\(hamming(h1, h2))") } else { say("?"); code = 1 }
            default:
                say("使い方: coverrepair diagnose [--reuse] | fix --yes | restore --yes   （--work DIR で作業フォルダを指定）")
                code = 2
            }
        } catch let e as EngineError { say(e.message); code = 1 }
        catch { say("\(error)"); code = 1 }
        exit(code)
    }
}
