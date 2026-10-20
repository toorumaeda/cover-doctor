#!/usr/bin/env python3
"""開発用: Apple Mobile Device Support の「入口」（usbmuxd、TCP 127.0.0.1:27015）のまね。
Windows 用の afcfs.exe が、Windows と同じ経路（TCP 27015）で接続し、端末一覧を受け取れることを確かめるためだけに使う。
iPhone 本体（lockdown）のまねはしないので、接続（Connect）は常に断る。実機の代わりにはならない。
使い方: python3 fake_usbmuxd.py [ポート(既定 27015)]   — 受け取った要求は標準エラーに出す"""
import plistlib, socket, struct, sys, threading

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 27015
UDID = "00008150000FAKEMUXD00000"

def send(conn, tag, obj):
    body = plistlib.dumps(obj)
    conn.sendall(struct.pack("<IIII", 16 + len(body), 1, 8, tag) + body)

def handle(conn):
    with conn:
        while True:
            hdr = conn.recv(16, socket.MSG_WAITALL)
            if len(hdr) < 16: return
            length, version, message, tag = struct.unpack("<IIII", hdr)
            body = conn.recv(length - 16, socket.MSG_WAITALL) if length > 16 else b""
            req = plistlib.loads(body) if body else {}
            mt = req.get("MessageType")
            print(f"request: {mt}", file=sys.stderr, flush=True)
            if mt == "ListDevices":
                send(conn, tag, {"DeviceList": [{"DeviceID": 1, "MessageType": "Attached", "Properties": {
                    "ConnectionType": "USB", "DeviceID": 1, "LocationID": 0, "ProductID": 4776, "SerialNumber": UDID}}]})
            elif mt == "ReadBUID":
                send(conn, tag, {"BUID": "00000000-0000-0000-0000-000000000000"})
            else:   # Connect / ReadPairRecord など: 断る（3 = 接続拒否、2 = 該当なし）
                send(conn, tag, {"MessageType": "Result", "Number": 3 if mt == "Connect" else 2})

s = socket.socket(); s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
s.bind(("127.0.0.1", PORT)); s.listen(8)
print(f"listening on 127.0.0.1:{PORT}", file=sys.stderr, flush=True)
while True:
    c, _ = s.accept()
    threading.Thread(target=handle, args=(c,), daemon=True).start()
