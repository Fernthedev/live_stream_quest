#!/usr/bin/env python3
import socket
import struct
import time

HOST = "0.0.0.0"
PORT = 9542

def send_packet(conn, payload: bytes):
    length = len(payload)
    header = struct.pack("!I", length)  # ! = network byte order (big endian)
    print(f"Sending packet: length={length} (Hex header: {header.hex()})")
    conn.sendall(header + payload)

server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
# Allow quick socket reuse upon restarting script
server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
server.bind((HOST, PORT))
server.listen()

print(f"Listening on {HOST}:{PORT}")

conn, addr = server.accept()
print(f"Client connected: {addr}")

# 2. CRITICAL TEST CASE: Exact 69-byte payload length
# This reproduces the exact packet size from your C++ logs.
# If your C# client is fixed, it will log "Incoming packet payload size: 69 bytes".
# If it is broken, it will log "Len network order 1157627904".
critical_payload = b"A" * 69  
send_packet(conn, critical_payload)
time.sleep(1)

# 3. Test trailing data parsing after the critical case
send_packet(conn, b"stream synchronization check")
time.sleep(1)

send_packet(conn, b"bye")

conn.close()
server.close()
print("Test completed successfully.")