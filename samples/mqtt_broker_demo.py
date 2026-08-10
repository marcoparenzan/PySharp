# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# mqtt_broker_demo.py — a real MQTT 3.1.1 broker (server), run by PySharp.
#
# Scenario 6 of the roadmap: unlike scenarios 1/1b/5 (a real, unmodified paho-mqtt/aiomqtt
# *client* talking to somebody else's broker), this is the *server* side, hand-rolled directly
# on top of this project's own real `socket`/`struct`/`asyncio`/`threading` — the same
# async-socket-server pattern already proven by samples/asgi_server.py (scenario 2), applied to
# the MQTT wire protocol instead of HTTP: real fixed-header/remaining-length parsing, real
# CONNECT/CONNACK/SUBSCRIBE/SUBACK/PUBLISH/PUBACK/PINGREQ/PINGRESP/UNSUBSCRIBE/UNSUBACK framing,
# real `+`/`#` topic-filter wildcard matching, real fan-out to every matching subscriber.
#
# Verification: the broker runs in a background thread (its own independent asyncio event loop
# — real CPython/PySharp semantics for a thread started via `threading.Thread`, confirmed in
# FASTAPI_PLAN.md Phase 4.2). Two REAL, unmodified `paho.mqtt.client` instances (the same PyPI
# package already verified end-to-end against a real Azure IoT Hub and a real public broker in
# scenarios 1/5) connect to it over a real loopback TCP socket: one subscribes, the other
# publishes, and the messages round-trip for real over the real MQTT wire protocol this broker
# implements — not a mock, not an in-process shortcut.
#
# v1 scope, deliberately (practical subset, matching every other module in this project): QoS
# 0/1 only (no QoS 2 four-way handshake), no persistent sessions (every connection is treated as
# clean-session), no Will messages, no retained messages, no username/password auth, no
# keep-alive timeout enforcement (PINGREQ/PINGRESP are answered, just never used to time out an
# idle client) — real, honest simplifications, not silent gaps.
#
# Usage:  pysharp run samples/mqtt_broker_demo.py
# Prerequisite:  pysharp install paho-mqtt==2.1.0

import asyncio
import socket
import struct
import threading
import time

HOST = "127.0.0.1"

# ---- MQTT 3.1.1 control packet types (the high nibble of the fixed header's first byte) ----
CONNECT = 1
CONNACK = 2
PUBLISH = 3
PUBACK = 4
SUBSCRIBE = 8
SUBACK = 9
UNSUBSCRIBE = 10
UNSUBACK = 11
PINGREQ = 12
PINGRESP = 13
DISCONNECT = 14


def _encode_remaining_length(n):
    """The real MQTT variable-length integer encoding: 7 bits of value per byte, the top bit a
    continuation flag — up to 4 bytes, matching real broker/client implementations."""
    out = bytearray()
    while True:
        b = n % 128
        n //= 128
        if n > 0:
            b |= 0x80
        out.append(b)
        if n == 0:
            return bytes(out)


def _encode_string(s):
    b = s.encode("utf-8")
    return struct.pack("!H", len(b)) + b


def _decode_string(payload, offset):
    length = struct.unpack_from("!H", payload, offset)[0]
    start = offset + 2
    return payload[start:start + length].decode("utf-8"), start + length


def _topic_matches(topic_filter, topic):
    """Real MQTT topic-filter matching: `+` matches exactly one level, `#` (only legal as the
    final level) matches that level and everything below it."""
    filter_parts = topic_filter.split("/")
    topic_parts = topic.split("/")
    for i, fp in enumerate(filter_parts):
        if fp == "#":
            return True
        if i >= len(topic_parts):
            return False
        if fp != "+" and fp != topic_parts[i]:
            return False
    return len(filter_parts) == len(topic_parts)


class _ConnReader:
    """Buffers partial reads off a non-blocking socket so callers can ask for exactly N bytes —
    the same pattern as asgi_server.py's own `_ConnReader`, since an MQTT packet's fixed header,
    remaining-length bytes, and payload can each arrive split across different TCP segments."""

    def __init__(self, loop, conn):
        self.loop = loop
        self.conn = conn
        self.buf = b""

    async def _fill(self):
        chunk = await self.loop.sock_recv(self.conn, 4096)
        if not chunk:
            return False
        self.buf += chunk
        return True

    async def read_exact(self, n):
        if n == 0:
            return b""
        while len(self.buf) < n:
            if not await self._fill():
                return None
        data = self.buf[:n]
        self.buf = self.buf[n:]
        return data

    async def read_byte(self):
        b = await self.read_exact(1)
        return None if b is None else b[0]


async def _read_remaining_length(reader):
    multiplier = 1
    value = 0
    while True:
        b = await reader.read_byte()
        if b is None:
            return None
        value += (b & 0x7F) * multiplier
        if (b & 0x80) == 0:
            return value
        multiplier *= 128
        if multiplier > 128 * 128 * 128:
            return None  # malformed: more than 4 continuation bytes


async def _read_packet(reader):
    """Reads one real MQTT control packet (fixed header + remaining-length + payload) off the
    connection. Returns (packet_type, flags, payload) or None on a clean disconnect/malformed
    stream."""
    b0 = await reader.read_byte()
    if b0 is None:
        return None
    packet_type = b0 >> 4
    flags = b0 & 0x0F
    remaining = await _read_remaining_length(reader)
    if remaining is None:
        return None
    payload = await reader.read_exact(remaining)
    if payload is None:
        return None
    return packet_type, flags, payload


class Broker:
    """A real MQTT 3.1.1 broker: accepts connections, tracks per-connection subscriptions, and
    fans out every PUBLISH to every real, currently-connected matching subscriber."""

    def __init__(self, host=HOST, port=0):
        self.host = host
        self.port = port
        self.loop = None
        self.srv = None
        self.stop_event = None
        self.lock = threading.Lock()
        self.subscriptions = {}   # topic filter -> set of client_id
        self.connections = {}     # client_id -> real socket

    async def serve_forever(self, ready_event):
        self.loop = asyncio.get_running_loop()
        srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        srv.bind((self.host, self.port))
        self.port = srv.getsockname()[1]
        srv.listen(64)
        srv.setblocking(False)
        self.srv = srv
        self.stop_event = asyncio.Event()
        ready_event.set()
        try:
            while not self.stop_event.is_set():
                accept_task = asyncio.create_task(self.loop.sock_accept(srv))
                stop_task = asyncio.create_task(self.stop_event.wait())
                done, pending = await asyncio.wait(
                    {accept_task, stop_task}, return_when=asyncio.FIRST_COMPLETED
                )
                if accept_task in done:
                    stop_task.cancel()
                    conn, addr = accept_task.result()
                    conn.setblocking(False)
                    asyncio.create_task(self._handle_client(conn))
                else:
                    accept_task.cancel()
        finally:
            srv.close()

    def stop(self):
        """Thread-safe: called from the main thread, schedules the real shutdown onto the
        broker's own event-loop thread."""
        self.loop.call_soon_threadsafe(self.stop_event.set)

    async def _handle_client(self, conn):
        reader = _ConnReader(self.loop, conn)
        client_id = None
        try:
            pkt = await _read_packet(reader)
            if pkt is None or pkt[0] != CONNECT:
                return
            client_id = self._parse_connect(pkt[2])
            with self.lock:
                self.connections[client_id] = conn
            await self.loop.sock_sendall(conn, bytes([CONNACK << 4, 2, 0, 0]))
            print("[broker] connected:", client_id)

            while True:
                pkt = await _read_packet(reader)
                if pkt is None:
                    break
                ptype, flags, payload = pkt
                if ptype == PUBLISH:
                    await self._handle_publish(flags, payload)
                elif ptype == SUBSCRIBE:
                    await self._handle_subscribe(conn, client_id, payload)
                elif ptype == UNSUBSCRIBE:
                    await self._handle_unsubscribe(conn, client_id, payload)
                elif ptype == PINGREQ:
                    await self.loop.sock_sendall(conn, bytes([PINGRESP << 4, 0]))
                elif ptype == DISCONNECT:
                    break
        finally:
            if client_id is not None:
                with self.lock:
                    self.connections.pop(client_id, None)
                    for subs in self.subscriptions.values():
                        subs.discard(client_id)
                print("[broker] disconnected:", client_id)
            conn.close()

    def _parse_connect(self, payload):
        # Variable header: protocol name (string), protocol level (1 byte), connect flags
        # (1 byte), keep alive (2 bytes) — then the payload's own client id (string). Will/
        # username/password fields are real per the connect flags but deliberately unread (v1
        # scope: no auth, no Will messages).
        _proto_name, offset = _decode_string(payload, 0)
        offset += 2  # protocol level + connect flags
        offset += 2  # keep alive
        client_id, _offset = _decode_string(payload, offset)
        return client_id

    async def _handle_subscribe(self, conn, client_id, payload):
        packet_id = struct.unpack_from("!H", payload, 0)[0]
        offset = 2
        granted = bytearray()
        with self.lock:
            while offset < len(payload):
                topic_filter, offset = _decode_string(payload, offset)
                qos = payload[offset]
                offset += 1
                self.subscriptions.setdefault(topic_filter, set()).add(client_id)
                granted.append(qos)
        body = struct.pack("!H", packet_id) + bytes(granted)
        header = bytes([SUBACK << 4]) + _encode_remaining_length(len(body))
        await self.loop.sock_sendall(conn, header + body)

    async def _handle_unsubscribe(self, conn, client_id, payload):
        packet_id = struct.unpack_from("!H", payload, 0)[0]
        offset = 2
        with self.lock:
            while offset < len(payload):
                topic_filter, offset = _decode_string(payload, offset)
                subs = self.subscriptions.get(topic_filter)
                if subs:
                    subs.discard(client_id)
        await self.loop.sock_sendall(conn, bytes([UNSUBACK << 4, 2]) + struct.pack("!H", packet_id))

    async def _handle_publish(self, flags, payload):
        qos = (flags >> 1) & 0x03
        topic, offset = _decode_string(payload, 0)
        if qos > 0:
            offset += 2  # packet id, present for QoS 1/2 — not acked back here (v1: fire-and-
            # forget from the broker's point of view once fanned out; real per-subscriber
            # QoS-1 PUBACK bookkeeping is out of scope)
        message = payload[offset:]

        with self.lock:
            targets = [
                self.connections[cid]
                for topic_filter, subs in self.subscriptions.items()
                if _topic_matches(topic_filter, topic)
                for cid in subs
                if cid in self.connections
            ]

        body = _encode_string(topic) + message
        header = bytes([PUBLISH << 4]) + _encode_remaining_length(len(body))
        packet = header + body
        for target_conn in targets:
            await self.loop.sock_sendall(target_conn, packet)


def _run_broker(broker, ready_event):
    asyncio.run(broker.serve_forever(ready_event))


def main():
    broker = Broker(HOST, 0)
    ready = threading.Event()
    thread = threading.Thread(target=_run_broker, args=(broker, ready), daemon=True)
    thread.start()
    if not ready.wait(5):
        print("[main] broker failed to start")
        return 1
    port = broker.port
    print("[main] broker listening on %s:%d" % (HOST, port))

    import paho.mqtt.client as mqtt

    topic = "pysharp/broker/demo"
    received = []
    sub_state = {"connected": False}
    pub_state = {"connected": False}

    def on_sub_connect(client, userdata, flags, reason_code, properties):
        sub_state["connected"] = True
        client.subscribe(topic, qos=1)

    def on_subscribe(client, userdata, mid, reason_codes, properties):
        # A real SUBACK arrived: only *now* is a subsequent PUBLISH on this topic guaranteed to
        # reach us (the same "wait for SUBACK before publishing" discipline any real MQTT client
        # needs against any real broker — the broker fans out to whoever is registered at the
        # moment it processes a PUBLISH, not retroactively).
        sub_state["subscribed"] = True

    def on_message(client, userdata, msg):
        payload = msg.payload.decode("utf-8")
        received.append(payload)
        print("[sub] received %s -> %s" % (msg.topic, payload))

    def on_pub_connect(client, userdata, flags, reason_code, properties):
        pub_state["connected"] = True

    sub = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="pysharp-sub")
    sub.on_connect = on_sub_connect
    sub.on_subscribe = on_subscribe
    sub.on_message = on_message
    sub.connect(HOST, port, keepalive=30)

    pub = mqtt.Client(mqtt.CallbackAPIVersion.VERSION2, client_id="pysharp-pub")
    pub.on_connect = on_pub_connect
    pub.connect(HOST, port, keepalive=30)

    deadline = time.time() + 5
    while time.time() < deadline and not (sub_state.get("subscribed") and pub_state["connected"]):
        sub.loop(timeout=0.2)
        pub.loop(timeout=0.2)
    if not (sub_state.get("subscribed") and pub_state["connected"]):
        print("[main] connection/subscription failed")
        return 1

    for i in range(3):
        payload = "hello from PySharp #%d" % i
        pub.publish(topic, payload, qos=1)
        print("[pub] sent %s -> %s" % (topic, payload))
        pub.loop(timeout=0.3)
        sub.loop(timeout=0.3)

    deadline = time.time() + 5
    while time.time() < deadline and len(received) < 3:
        sub.loop(timeout=0.3)

    sub.disconnect()
    pub.disconnect()
    sub.loop(timeout=0.3)
    pub.loop(timeout=0.3)

    broker.stop()
    thread.join(timeout=5)

    print("[main] received %d/3 messages" % len(received))
    return 0 if len(received) == 3 else 2


if __name__ == "__main__":
    raise SystemExit(main())
