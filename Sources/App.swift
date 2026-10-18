import SwiftUI
import AppKit

// MARK: - 画面の状態

@MainActor
final class Model: ObservableObject {
    enum Phase: Equatable {
        case idle
        case diagnosing
        case diagnosed
        case fixing
        case fixed(ok: Int, failed: Int)
        case restored(Int)
    }

    @Published var device: DeviceInfo?
    @Published var phase: Phase = .idle
    @Published var progress = Progress(message: "", done: 0, total: 0)
    @Published var plan: Plan?
    @Published var errorMessage: String?
    @Published var hasUndo = false
    private var task: Task<Void, Never>?
    private var poller: Task<Void, Never>?

    var work: URL? { device.map { defaultWorkDir(for: $0.udid) } }
    var busy: Bool { phase == .diagnosing || phase == .fixing }
    var isFixedPhase: Bool { if case .fixed = phase { return true }; return false }

    func startPolling() {
        guard poller == nil else { return }
        poller = Task { [weak self] in
            while !Task.isCancelled {
                let d = await Task.detached { connectedDevice() }.value
                guard let self else { return }
                if d != self.device {
                    self.device = d
                    if d == nil, !self.busy { self.phase = .idle; self.plan = nil }
                    if let d { self.hasUndo = FileManager.default.fileExists(atPath: defaultWorkDir(for: d.udid).appendingPathComponent("applied.json").path) }
                }
                try? await Task.sleep(nanoseconds: 2_000_000_000)
            }
        }
    }

    private func handler() -> ProgressHandler {
        { [weak self] p in Task { @MainActor in self?.progress = p } }
    }

    func diagnose(reuse: Bool = false) {
        guard let work = work, !busy else { return }
        errorMessage = nil; plan = nil; phase = .diagnosing
        progress = Progress(message: String(localized: "Preparing…"), done: 0, total: 0)
        let h = handler()
        task = Task {
            do {
                let p = try await Task.detached { try await engineDiagnose(work: work, reuse: reuse, progress: h) }.value
                self.plan = p; self.phase = .diagnosed
            } catch is CancellationError {
                self.phase = .idle
            } catch let e as EngineError {
                self.errorMessage = e.message; self.phase = .idle
            } catch { self.errorMessage = "\(error)"; self.phase = .idle }
        }
    }

    func fix() {
        guard let work = work, let plan = plan, !busy else { return }
        errorMessage = nil; phase = .fixing
        progress = Progress(message: String(localized: "Preparing…"), done: 0, total: 0)
        let h = handler()
        task = Task {
            do {
                let r = try await Task.detached { try await engineFix(work: work, plan: plan, progress: h) }.value
                self.phase = .fixed(ok: r.ok, failed: r.failed.count)
                self.hasUndo = r.ok > 0 || self.hasUndo
            } catch let e as EngineError {
                self.errorMessage = e.message; self.phase = .diagnosed
            } catch is CancellationError {
                self.phase = .diagnosed
            } catch { self.errorMessage = "\(error)"; self.phase = .diagnosed }
        }
    }

    func restore() {
        guard let work = work, !busy else { return }
        errorMessage = nil; phase = .fixing
        progress = Progress(message: String(localized: "Restoring…"), done: 0, total: 0)
        let h = handler()
        task = Task {
            do {
                let n = try await Task.detached { try await engineRestore(work: work, progress: h) }.value
                self.phase = .restored(n); self.hasUndo = false; self.plan = nil
            } catch let e as EngineError {
                self.errorMessage = e.message; self.phase = .idle
            } catch { self.errorMessage = "\(error)"; self.phase = .idle }
        }
    }

    func cancel() { task?.cancel() }

    // MARK: 自己テスト（開発用。環境変数 COVERDOCTOR_SELFTEST があるときだけ、起動時に実行する）
    private func waitUntil(_ timeout: Double, _ cond: @escaping @MainActor () -> Bool) async -> Bool {
        let start = Date()
        while Date().timeIntervalSince(start) < timeout {
            if cond() { return true }
            try? await Task.sleep(nanoseconds: 100_000_000)
        }
        return false
    }

    /// 画面のボタンと同じ経路（diagnose / fix / restore）を順に呼び、結果を文字列で返す。失敗があれば先頭が "FAIL"。
    func selfTest() async -> String {
        var log: [String] = []
        func fail(_ m: String) -> String { "FAIL: " + m + "\n" + log.joined(separator: "\n") }
        guard await waitUntil(30, { self.device != nil }) else { return fail("device not found") }
        log.append("device: \(device!.name)")
        diagnose()
        guard await waitUntil(300, { self.phase == .diagnosed || self.errorMessage != nil }), phase == .diagnosed, let p1 = plan else {
            return fail("diagnose failed: \(errorMessage ?? "timeout")")
        }
        let wrong = p1.entries.filter { $0.status == "wrong" }.count
        log.append("diagnosed: total=\(p1.entries.count) wrong=\(wrong)")
        guard wrong > 0 else { return fail("expected fixable albums, got 0") }
        fix()
        guard await waitUntil(300, { if case .fixed = self.phase { return true }; return self.errorMessage != nil }),
              case .fixed(let ok, let failed) = phase else { return fail("fix failed: \(errorMessage ?? "timeout")") }
        log.append("fixed: ok=\(ok) failed=\(failed)")
        guard ok == wrong, failed == 0 else { return fail("fix result mismatch") }
        guard hasUndo else { return fail("undo history missing") }
        diagnose(reuse: true)
        guard await waitUntil(300, { self.phase == .diagnosed }), let p2 = plan else { return fail("re-diagnose failed") }
        let left = p2.entries.filter { $0.status == "wrong" }.count
        log.append("re-diagnosed: wrong=\(left)")
        guard left == 0 else { return fail("still fixable after fix: \(left)") }
        restore()
        guard await waitUntil(300, { if case .restored = self.phase { return true }; return self.errorMessage != nil }),
              case .restored(let n) = phase else { return fail("restore failed: \(errorMessage ?? "timeout")") }
        log.append("restored: \(n)")
        guard n == wrong, !hasUndo else { return fail("restore result mismatch") }
        return "PASS\n" + log.joined(separator: "\n")
    }
}

// MARK: - 画面

struct ContentView: View {
    @StateObject private var model = Model()
    @AppStorage("acceptedNotice") private var accepted = false
    @State private var confirmFix = false
    @State private var confirmRestore = false
    private var fixCount: Int { model.plan?.entries.filter { $0.status == "wrong" }.count ?? 0 }

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            header
            deviceRow
            Divider()
            content
            Spacer(minLength: 0)
            footer
        }
        .padding(20)
        .frame(minWidth: 620, idealWidth: 760, maxWidth: .infinity, minHeight: 660, idealHeight: 780, maxHeight: .infinity, alignment: .topLeading)
        .onAppear {
            model.startPolling()
            if let out = ProcessInfo.processInfo.environment["COVERDOCTOR_SELFTEST_OUT"] {
                Task { @MainActor in
                    let result = await model.selfTest()
                    try? result.write(toFile: out, atomically: true, encoding: .utf8)
                    exit(result.hasPrefix("PASS") ? 0 : 1)
                }
            }
        }
        .sheet(isPresented: Binding(get: { !accepted }, set: { _ in })) { NoticeView { accepted = true } }
        .alert("Fix the covers?", isPresented: $confirmFix) {
            Button("Fix", role: nil) { model.fix() }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This replaces \(fixCount) cover images (albums and playlists) on your iPhone. The original images are saved on this Mac, so you can undo this later.")
        }
        .alert("Undo the changes?", isPresented: $confirmRestore) {
            Button("Undo") { model.restore() }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This puts the covers replaced by this app back to how they were before.")
        }
    }

    private var header: some View {
        VStack(alignment: .leading, spacing: 4) {
            Text("Cover Doctor").font(.largeTitle.bold())
            Text("Fixes the problem where the Music app on your iPhone shows another album's cover.")
                .foregroundStyle(.secondary)
        }
    }

    private var deviceRow: some View {
        HStack(spacing: 8) {
            Circle().fill(model.device == nil ? Color.gray : Color.green).frame(width: 10, height: 10)
            if let d = model.device {
                Text("Connected to \(d.name) (iOS \(d.version))")
            } else {
                Text("Connect your iPhone with a USB cable and unlock it").foregroundStyle(.secondary)
            }
        }
    }

    @ViewBuilder private var content: some View {
        if let msg = model.errorMessage {
            Label(msg, systemImage: "exclamationmark.triangle.fill").foregroundStyle(.orange).textSelection(.enabled)
        }
        switch model.phase {
        case .idle:
            VStack(alignment: .leading, spacing: 12) {
                Text("Start with a diagnosis. It only reads data and does not change your iPhone.").foregroundStyle(.secondary)
                Button { model.diagnose() } label: { Label("Diagnose", systemImage: "stethoscope").frame(minWidth: 140) }
                    .buttonStyle(.borderedProminent).controlSize(.large).disabled(model.device == nil)
            }
        case .diagnosing, .fixing:
            VStack(alignment: .leading, spacing: 10) {
                Text(model.phase == .diagnosing ? "Diagnosing…" : "Working…").font(.headline)
                if model.progress.total > 0 {
                    ProgressView(value: Double(model.progress.done), total: Double(model.progress.total))
                    Text("\(model.progress.done) / \(model.progress.total)  \(model.progress.message)").font(.caption).foregroundStyle(.secondary).lineLimit(1)
                } else {
                    ProgressView(); Text(model.progress.message).font(.caption).foregroundStyle(.secondary)
                }
                Button("Cancel") { model.cancel() }
                Text("The first run extracts artwork from your songs and can take a few minutes. Keep your iPhone connected.").font(.caption).foregroundStyle(.secondary)
            }
        case .diagnosed:
            if let plan = model.plan { ResultView(plan: plan, work: model.work!, onFix: { confirmFix = true }, onRescan: { model.diagnose(reuse: true) }) }
        case .fixed(let ok, let failed):
            VStack(alignment: .leading, spacing: 12) {
                Label("Fixed \(ok) covers", systemImage: "checkmark.circle.fill")
                    .font(.title3).foregroundStyle(.green)
                if failed > 0 { Text("Could not fix: \(failed)").foregroundStyle(.orange) }
                Text("Fully quit the Music app (swipe it up in the app switcher) and open it again.")
                HStack {
                    Button("Diagnose Again") { model.diagnose(reuse: true) }
                    Button("Undo…") { confirmRestore = true }
                }
            }
        case .restored(let n):
            VStack(alignment: .leading, spacing: 12) {
                Label("Restored \(n) covers", systemImage: "arrow.uturn.backward.circle.fill").font(.title3)
                Button("Diagnose") { model.diagnose() }.disabled(model.device == nil)
            }
        }
    }

    private var footer: some View {
        HStack {
            if model.hasUndo, !model.busy, !model.isFixedPhase {
                Button("Undo what this app fixed…") { confirmRestore = true }.buttonStyle(.link)
            }
            Spacer()
            Text("Unofficial · use at your own risk").font(.caption).foregroundStyle(.secondary)
        }
    }
}

struct ResultView: View {
    let plan: Plan
    let work: URL
    let onFix: () -> Void
    let onRescan: () -> Void

    var body: some View {
        let wrong = plan.entries.filter { $0.status == "wrong" }
        let same = plan.entries.filter { $0.status == "same" }.count
        let none = plan.entries.filter { $0.status == "noEmbeddedArt" }.count
        let custom = plan.entries.filter { $0.status == "custom" }.count
        VStack(alignment: .leading, spacing: 12) {
            HStack(spacing: 20) {
                counter("Correct", same, .green)
                counter("Fixable", wrong.count, wrong.isEmpty ? .secondary : .orange)
                counter("Not fixable", none, .secondary)
            }
            if let total = plan.libraryAlbumCount, let skipped = plan.notDiagnosedCount, skipped > 0 {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Albums in the library: \(total) · checked: \(total - skipped) · no cover record: \(skipped)")
                    Text("Albums with no cover record on the iPhone cannot be checked or fixed by this app.").foregroundStyle(.secondary)
                }.font(.caption)
            }
            if none > 0 {
                Text("“Not fixable” albums have no cover image inside their song files.")
                    .font(.caption).foregroundStyle(.secondary)
            }
            if custom > 0 {
                Text("Playlist covers that may be custom (left alone): \(custom)")
                    .font(.caption).foregroundStyle(.secondary)
            }
            if wrong.isEmpty {
                Label("Nothing to fix", systemImage: "checkmark.circle.fill").foregroundStyle(.green)
                Button("Diagnose Again", action: onRescan)
            } else {
                HStack {
                    Button(action: onFix) { Label("Fix \(wrong.count) covers", systemImage: "wand.and.stars").frame(minWidth: 140) }
                        .buttonStyle(.borderedProminent).controlSize(.large)
                    Button("Diagnose Again", action: onRescan)
                }
                Text("Left: the cover on your iPhone now. Right: the image from the song file (how it will look after the fix).").font(.caption).foregroundStyle(.secondary)
                List(wrong) { e in
                    HStack(spacing: 10) {
                        Thumb(url: work.appendingPathComponent("current/\(e.token).img"))
                        Image(systemName: "arrow.right").foregroundStyle(.secondary)
                        Thumb(url: work.appendingPathComponent("fresh/\(e.token).jpg"))
                        VStack(alignment: .leading, spacing: 2) {
                            Text(e.album).lineLimit(2)
                            if e.isPlaylist { Text("Playlist").font(.caption2).foregroundStyle(.secondary) }
                        }
                    }
                }
                .frame(minHeight: 200, maxHeight: .infinity)
            }
        }
    }

    private func counter(_ title: LocalizedStringKey, _ n: Int, _ color: Color) -> some View {
        VStack(alignment: .leading) {
            Text("\(n)").font(.system(size: 34, weight: .bold)).foregroundStyle(color)
            Text(title).font(.caption).foregroundStyle(.secondary)
        }
    }
}

struct Thumb: View {
    let url: URL
    var body: some View {
        if let img = NSImage(contentsOf: url) {
            Image(nsImage: img).resizable().scaledToFill().frame(width: 52, height: 52).clipped().cornerRadius(4)
        } else {
            RoundedRectangle(cornerRadius: 4).fill(.quaternary).frame(width: 52, height: 52)
        }
    }
}

struct NoticeView: View {
    let accept: () -> Void
    var body: some View {
        VStack(alignment: .leading, spacing: 14) {
            Text("Before you start").font(.title2.bold())
            VStack(alignment: .leading, spacing: 8) {
                Text("This app is not an official Apple method. It replaces the cover image files that the Music app on your iPhone keeps internally with the correct images found in your song files, which fixes what is displayed.")
                Text("• It does not change the Music database itself, your song files, or the Music library on your Mac.")
                Text("• The images it replaces are saved on this Mac, so you can undo the change later.")
                Text("• It may stop working depending on the iOS version.")
                Text("• Syncing again with Finder or similar tools can bring the wrong covers back. If that happens, diagnose and fix again.")
                Text("• Use it at your own risk.")
            }.fixedSize(horizontal: false, vertical: true)
            HStack { Spacer(); Button("I Understand", action: accept).buttonStyle(.borderedProminent).keyboardShortcut(.defaultAction) }
        }
        .padding(24).frame(width: 480)
    }
}

@main
struct CoverRepairApp: App {
    var body: some Scene {
        WindowGroup("Cover Doctor") { ContentView() }
            .windowResizability(.contentMinSize)
    }
}
