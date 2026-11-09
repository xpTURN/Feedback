// windows.swift — the windows of one process: "<number> <x> <y> <width> <height> <layer> <on screen> <name>" a line, in points
// (CGWindowListCopyWindowInfo). tools/doc-images.sh builds it with xcrun swiftc
import CoreGraphics
import Foundation

guard CommandLine.arguments.count == 2, let pid = Int32(CommandLine.arguments[1]) else {
    FileHandle.standardError.write("usage: windows <pid>\n".data(using: .utf8)!)
    exit(2)
}
let list = CGWindowListCopyWindowInfo([.optionAll], kCGNullWindowID) as? [[String: Any]] ?? []
for w in list {
    guard let p = w[kCGWindowOwnerPID as String] as? Int32, p == pid,
          let id = w[kCGWindowNumber as String] as? Int,
          let b = w[kCGWindowBounds as String] as? [String: Any] else { continue }
    let layer = w[kCGWindowLayer as String] as? Int ?? 0
    let onscreen = w[kCGWindowIsOnscreen as String] as? Bool ?? false
    let name = w[kCGWindowName as String] as? String ?? "-"
    print("\(id) \(b["X"] ?? 0) \(b["Y"] ?? 0) \(b["Width"] ?? 0) \(b["Height"] ?? 0) \(layer) \(onscreen) \(name)")
}
