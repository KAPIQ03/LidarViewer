#!/usr/bin/env python3
"""Nadajnik pakietow Velodyne VLP-16 do testow trybu "Na zywo" w LidarViewer.

Tryby:
  1) scena syntetyczna (domyslnie): pokoj 20 x 14 m, czujnik w (0, 0, 1,8 m) nad podloga, po okregu
     o promieniu 4 m krazy "czlowiek" (walec). 600 obr./min = 10 klatek/s, ok. 754 pakiety/s.
  2) odtwarzanie nagrania:  --pcap plik.pcap  (pcap lub pcapng, pakiety UDP 1206 B, z oryginalnym tempem).

W aplikacji: panel boczny -> "Zrodlo na zywo" -> protokol "Velodyne (kalibracja)", czujnik VLP-16,
port 2368 -> Start. Tylko biblioteka standardowa Pythona 3.

Przyklady:
  python3 tools/velodyne_sender.py
  python3 tools/velodyne_sender.py --port 2370 --rpm 300 --seconds 30
  python3 tools/velodyne_sender.py --pcap ~/nagranie.pcap --loop
  python3 tools/velodyne_sender.py --loss 0.05        # gubi 5% pakietow
"""
import argparse
import math
import random
import socket
import struct
import sys
import time

BLOCKS = 12
FIRING_US = 55.296
VLP16_ANGLES = [-15, 1, -13, 3, -11, 5, -9, 7, -7, 9, -5, 11, -3, 13, -1, 15]
SENSOR_HEIGHT = 1.8
ROOM_HALF_X, ROOM_HALF_Y = 10.0, 7.0
CYL_RADIUS, ORBIT_RADIUS, ORBIT_PERIOD_S = 0.35, 4.0, 6.0


def room_distance(az):
    """Odleglosc pozioma od czujnika do sciany pokoju w kierunku az (rad, od +Y zgodnie z zegarem)."""
    dx, dy = math.sin(az), math.cos(az)
    candidates = []
    if dx > 1e-9: candidates.append(ROOM_HALF_X / dx)
    if dx < -1e-9: candidates.append(-ROOM_HALF_X / dx)
    if dy > 1e-9: candidates.append(ROOM_HALF_Y / dy)
    if dy < -1e-9: candidates.append(-ROOM_HALF_Y / dy)
    return min(candidates)


def static_scan(az_deg, laser):
    """(odleglosc 3D [m], odbicie) dla sceny bez poruszajacego sie obiektu."""
    az = math.radians(az_deg)
    el = math.radians(VLP16_ANGLES[laser])
    wall = room_distance(az)
    if el < 0:
        ground = SENSOR_HEIGHT / -math.tan(el)
        horizontal = min(ground, wall)
    else:
        horizontal = wall
    z = horizontal * math.tan(el)
    if z > 2.5:  # sufit poza zasiegiem
        return None
    return horizontal / math.cos(el), 60 + (laser * 9) % 100




def make_packets(rpm, seconds, loss, rng):
    """Generator (pakiet bajty, czas wyslania wzgledem startu [s])."""
    step_deg = rpm * 360.0 / 60.0 * (2 * FIRING_US * 1e-6)  # przyrost azymutu na blok
    cache = {}
    packet_period = BLOCKS * 2 * FIRING_US * 1e-6
    t = 0.0
    az = 0.0
    sent_us = 0
    while seconds is None or t < seconds:
        cyl_angle = 2 * math.pi * (t / ORBIT_PERIOD_S)
        cx, cy = ORBIT_RADIUS * math.cos(cyl_angle), ORBIT_RADIUS * math.sin(cyl_angle)
        cyl_az = math.degrees(math.atan2(cx, cy)) % 360.0
        packet = bytearray()
        for _ in range(BLOCKS):
            block_az = az % 360.0
            packet += struct.pack("<HH", 0xEEFF, int(round(block_az * 100)) % 36000)
            for firing in range(2):
                f_az = (block_az + step_deg / 2 * firing) % 360.0
                key = int(round(f_az * 20))  # krok 0,05 stopnia - wystarczy dla stalej sceny
                near = abs((f_az - cyl_az + 180) % 360 - 180) < 8
                for laser in range(16):
                    if (key, laser) not in cache:
                        cache[(key, laser)] = static_scan(key / 20.0, laser)
                    hit = cache[(key, laser)]
                    if near:
                        hit = cylinder_hit(f_az, laser, cx, cy, hit)
                    if hit is None:
                        packet += b"\x00\x00\x00"
                    else:
                        packet += struct.pack("<HB", min(int(hit[0] / 0.002), 65535), hit[1])
            az += step_deg
        packet += struct.pack("<IBB", int(sent_us) & 0xFFFFFFFF, 0x37, 0x22)
        assert len(packet) == 1206
        if loss <= 0 or rng.random() >= loss:
            yield bytes(packet), t
        t += packet_period
        sent_us += packet_period * 1e6


def cylinder_hit(az_deg, laser, cx, cy, current):
    az = math.radians(az_deg)
    el = math.radians(VLP16_ANGLES[laser])
    dx, dy = math.sin(az), math.cos(az)
    proj = dx * cx + dy * cy
    perp = abs(dx * cy - dy * cx)
    if proj <= 0 or perp >= CYL_RADIUS:
        return current
    horizontal = proj - math.sqrt(CYL_RADIUS ** 2 - perp ** 2)
    z = horizontal * math.tan(el)
    if not (-SENSOR_HEIGHT <= z <= 0.0):
        return current
    dist = horizontal / math.cos(el)
    if current is None or dist < current[0]:
        return dist, 220
    return current


# ---------- odtwarzanie pcap / pcapng ----------

def udp_payload(data, linktype):
    """Ladunek UDP z ramki albo None."""
    if linktype == 1:      # Ethernet
        off = 14
        if len(data) >= 14 and data[12:14] == b"\x81\x00":
            off = 18
    elif linktype == 113:  # Linux cooked
        off = 16
    elif linktype == 101:  # raw IP
        off = 0
    elif linktype == 0:    # BSD loopback
        off = 4
    else:
        return None
    if len(data) < off + 20 or data[off] >> 4 != 4 or data[off + 9] != 17:
        return None
    ihl = (data[off] & 0x0F) * 4
    start = off + ihl + 8
    return data[start:] if len(data) > start else None


def read_pcap(path):
    """Generator (czas [s], ladunek UDP)."""
    with open(path, "rb") as f:
        raw = f.read()
    magic = raw[:4]
    if magic in (b"\xd4\xc3\xb2\xa1", b"\xa1\xb2\xc3\xd4", b"\x4d\x3c\xb2\xa1", b"\xa1\xb2\x3c\x4d"):
        endian = "<" if magic in (b"\xd4\xc3\xb2\xa1", b"\x4d\x3c\xb2\xa1") else ">"
        nano = magic in (b"\x4d\x3c\xb2\xa1", b"\xa1\xb2\x3c\x4d")
        linktype = struct.unpack(endian + "I", raw[20:24])[0]
        pos = 24
        while pos + 16 <= len(raw):
            sec, frac, incl, _ = struct.unpack(endian + "IIII", raw[pos:pos + 16])
            data = raw[pos + 16:pos + 16 + incl]
            pos += 16 + incl
            payload = udp_payload(data, linktype)
            if payload:
                yield sec + frac / (1e9 if nano else 1e6), payload
    elif magic == b"\x0a\x0d\x0d\x0a":
        pos, linktypes = 0, []
        while pos + 12 <= len(raw):
            btype, blen = struct.unpack("<II", raw[pos:pos + 8])
            body = raw[pos + 8:pos + blen - 4]
            if btype == 1:
                linktypes.append(struct.unpack("<H", body[:2])[0])
            elif btype == 6:
                iface, hi, lo, cap, _ = struct.unpack("<IIIII", body[:20])
                ts = ((hi << 32) | lo) / 1e6  # domyslna rozdzielczosc 1 us
                payload = udp_payload(body[20:20 + cap], linktypes[iface] if iface < len(linktypes) else 1)
                if payload:
                    yield ts, payload
            pos += blen
    else:
        sys.exit("To nie jest plik pcap ani pcapng.")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", default="127.0.0.1", help="adres odbiorcy (domyslnie 127.0.0.1)")
    ap.add_argument("--port", type=int, default=2368, help="port UDP (domyslnie 2368)")
    ap.add_argument("--rpm", type=float, default=600, help="obroty na minute: 300 / 600 / 1200 (domyslnie 600)")
    ap.add_argument("--seconds", type=float, default=None, help="czas nadawania; domyslnie bez konca (Ctrl+C)")
    ap.add_argument("--pcap", help="odtworz nagranie zamiast sceny syntetycznej")
    ap.add_argument("--loop", action="store_true", help="z --pcap: powtarzaj w kolko")
    ap.add_argument("--loss", type=float, default=0.0, help="odsetek gubionych pakietow 0..1 (test odrzuconych)")
    ap.add_argument("--junk", type=float, default=0.0, help="odsetek dodatkowych smieciowych datagramow 0..1")
    args = ap.parse_args()

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.host, args.port)
    rng = random.Random(1)
    sent = 0
    start = time.perf_counter()
    last_report = start
    print(f"Nadaje na {args.host}:{args.port}  (Ctrl+C konczy)")

    def source():
        if args.pcap:
            while True:
                first = None
                for ts, payload in read_pcap(args.pcap):
                    if first is None:
                        first = ts
                    yield payload, ts - first
                if first is None:
                    sys.exit("W nagraniu nie ma pakietow UDP.")
                if not args.loop:
                    return
        else:
            yield from make_packets(args.rpm, args.seconds, args.loss, rng)

    try:
        base = 0.0
        prev_t = 0.0
        for payload, t in source():
            if t < prev_t:   # poczatek kolejnej petli nagrania
                base += prev_t
            prev_t = t
            due = start + base + t
            delay = due - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
            sock.sendto(payload, target)
            sent += 1
            if args.junk > 0 and rng.random() < args.junk:
                sock.sendto(b"to nie jest pakiet lidaru", target)
            now = time.perf_counter()
            if now - last_report >= 2:
                print(f"  wyslano {sent} pakietow ({sent / (now - start):.0f}/s)")
                last_report = now
    except KeyboardInterrupt:
        pass
    print(f"Koniec: wyslano {sent} pakietow.")


if __name__ == "__main__":
    main()
