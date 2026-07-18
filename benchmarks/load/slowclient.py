"""Slow-reader load: N connections, each GET a 1 MB response and drain it at ~64 KB/s.

This is the ERP-over-WAN shape: the upstream hands the gateway the whole response instantly,
the client takes ~16 s to sip it down. What the gateway does with the 1 MB in the meantime is
the entire question — hold it (RAM), spill it (disk), or backpressure the upstream (stream).
"""
import socket
import sys
import threading
import time

HOST, PORT, PATH, N, RATE = sys.argv[1], int(sys.argv[2]), sys.argv[3], int(sys.argv[4]), 65536
CHUNK, INTERVAL = 8192, 8192 / RATE

done = errors = 0
lock = threading.Lock()


def one():
    global done, errors
    try:
        s = socket.create_connection((HOST, PORT), timeout=30)
        s.sendall(f"GET {PATH} HTTP/1.1\r\nHost: bench\r\nConnection: close\r\n\r\n".encode())
        s.settimeout(30)
        while True:
            b = s.recv(CHUNK)
            if not b:
                break
            time.sleep(INTERVAL)  # the slow WAN
        s.close()
        with lock:
            done += 1
    except Exception:
        with lock:
            errors += 1


threads = [threading.Thread(target=one, daemon=True) for _ in range(N)]
start = time.time()
for t in threads:
    t.start()
    time.sleep(0.01)  # ramp over ~2 s, not a thundering herd
for t in threads:
    t.join(timeout=60)
print(f"done={done} errors={errors} elapsed={time.time()-start:.0f}s")
