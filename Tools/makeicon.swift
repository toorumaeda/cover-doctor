import AppKit
// usage: makeicon design.svg out.png size [nologo]
// design.svg(1024) → 824pxに縮小 → ロゴ文字を重ねる → 1024の余白付きキャンバスに影つきで配置
let a = CommandLine.arguments
guard a.count >= 4, let art = NSImage(contentsOf: URL(fileURLWithPath: a[1])) else { print("load failed"); exit(1) }
let out = Int(a[3])!, withLogo = !(a.count > 4 && a[4] == "nologo")
func bitmap(_ px: Int) -> NSBitmapImageRep { NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: px, pixelsHigh: px, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)! }
let S = 1024.0
let rep = bitmap(1024)
NSGraphicsContext.saveGraphicsState()
let ctx = NSGraphicsContext(bitmapImageRep: rep)!
NSGraphicsContext.current = ctx
let cg = ctx.cgContext
// 影
cg.saveGState()
cg.setShadow(offset: CGSize(width: 0, height: -14), blur: 26, color: NSColor.black.withAlphaComponent(0.35).cgColor)
let rect = NSRect(x: 100, y: 100, width: 824, height: 824)
NSColor(calibratedRed: 0.04, green: 0.18, blue: 0.66, alpha: 1).setFill()
NSBezierPath(roundedRect: rect, xRadius: 184, yRadius: 184).fill()
cg.restoreGState()
// 絵本体
NSBezierPath(roundedRect: rect, xRadius: 184, yRadius: 184).addClip()
art.draw(in: rect)
if withLogo {
    // ロゴ（デザインした文字）。座標は design.svg(1024) 基準で、824px に縮小して配置
    let k = 824.0 / 1024.0
    let size = 150.0 * k
    func font(_ w: NSFont.Weight) -> NSFont {
        let f = NSFont.systemFont(ofSize: size, weight: w)
        if let d = f.fontDescriptor.withDesign(.rounded) { return NSFont(descriptor: d, size: size) ?? f }
        return f
    }
    let f = font(.heavy)
    let shadow = NSShadow(); shadow.shadowColor = NSColor(calibratedRed: 0, green: 0.1, blue: 0.5, alpha: 0.55); shadow.shadowOffset = NSSize(width: 0, height: -5); shadow.shadowBlurRadius = 8
    let t = NSMutableAttributedString(string: "Cover", attributes: [.font: f, .foregroundColor: NSColor.white, .kern: -2.0, .shadow: shadow])
    t.append(NSAttributedString(string: "Doctor", attributes: [.font: f, .foregroundColor: NSColor(calibratedRed: 0.62, green: 0.86, blue: 1.0, alpha: 1), .kern: -2.0, .shadow: shadow]))
    let w = t.size().width, h = t.size().height
    let x = rect.minX + (rect.width - w) / 2
    let y = rect.minY + (1024 - 950) * k   // ベースライン付近
    t.draw(at: NSPoint(x: x, y: y - h * 0.18))
}
NSGraphicsContext.restoreGraphicsState()
// 出力サイズへ縮小
let final = bitmap(out)
NSGraphicsContext.saveGraphicsState()
NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: final)
NSGraphicsContext.current?.imageInterpolation = .high
let tmp = NSImage(size: NSSize(width: 1024, height: 1024)); tmp.addRepresentation(rep)
tmp.draw(in: NSRect(x: 0, y: 0, width: out, height: out))
NSGraphicsContext.restoreGraphicsState()
try! final.representation(using: .png, properties: [:])!.write(to: URL(fileURLWithPath: a[2]))
