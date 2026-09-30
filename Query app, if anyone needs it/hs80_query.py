#!/usr/bin/env python3
"""
Corsair HS80 MAX – battery / status reader
"""
import sys
import time

try:
    import hid
except ImportError:
    print("pip install hidapi")
    sys.exit(1)

VID, PID = 0x1B1C, 0x0A97
USAGE_PAGE, USAGE = 0xFF42, 0x0001

def find():
    for d in hid.enumerate(VID, PID):
        if d.get("usage_page") == USAGE_PAGE and d.get("usage") == USAGE:
            return d
    return None

def pkt(report_id, *data):
    buf = bytearray(65)
    buf[0] = report_id
    for i, b in enumerate(data):
        buf[1 + i] = b
    return buf

def flush(dev):
    try:
        while dev.read(64, timeout_ms=10):
            pass
    except Exception:
        pass

def transact(dev, *payload, timeout=400):
    flush(dev)
    dev.write(pkt(0x02, *payload))
    time.sleep(0.08)
    return dev.read(64, timeout_ms=timeout)

def main():
    info = find()
    if not info:
        print("HS80 MAX control interface not found.")
        sys.exit(1)

    print(f"Device : {info.get('product_string')}")
    print(f"Path   : {info['path']}")

    try:
        dev = hid.device()
        dev.open_path(info["path"])
        dev.set_nonblocking(False)
    except Exception as e:
        print("Open failed:", e)
        sys.exit(1)

    try:
        # Wake / identity
        r = transact(dev, 0x08, 0x02, 0x12)          # receiver heartbeat
        if r and len(r) >= 6:
            pid = r[4] | (r[5] << 8)
            print(f"Dongle PID : 0x{pid:04X}")

        r = transact(dev, 0x09, 0x02, 0x12)          # headset heartbeat
        if r and len(r) >= 6:
            pid = r[4] | (r[5] << 8)
            print(f"Headset PID: 0x{pid:04X}  ← connected")
        else:
            print("Headset did not answer heartbeat (is it powered on / in RF mode?)")
            return

        # Battery (property 0x0F) – value is 0–1000 → percent = value / 10
        r = transact(dev, 0x09, 0x02, 0x0F)
        if r and len(r) >= 6:
            raw = r[4] | (r[5] << 8)
            if 1 <= raw <= 1000:
                print(f"Battery   : {raw / 10:.0f} %")
            else:
                print(f"Battery raw value out of range: {raw}")
                print("RAW:", " ".join(f"{b:02X}" for b in r[:12]))
        else:
            print("No battery reply")
    finally:
        dev.close()

if __name__ == "__main__":
    main()