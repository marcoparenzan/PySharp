# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# amqp_broker_demo.py — a real AMQP 0-9-1 broker (server), run by PySharp.
#
# Scenario 7 of the roadmap: no real, publicly reachable AMQP test broker exists the way
# test.mosquitto.org does for MQTT (scenario 5), and no Docker/local RabbitMQ instance is
# available in this environment — so, following the exact same strategy that worked for
# scenario 6 (MQTT broker), the *server* side is hand-rolled directly on this project's own
# `socket`/`asyncio`/`struct`/`threading`, and a REAL, unmodified `pika` client (pure-Python
# AMQP 0-9-1, downloaded from PyPI — not a mock) drives it over a real loopback TCP socket.
#
# What's real: the actual AMQP 0-9-1 wire protocol — the 8-byte "AMQP\x00\x00\x09\x01" protocol
# header, real frame framing (type/channel/size/payload/0xCE frame-end), the real
# Connection.Start/Start-Ok/Tune/Tune-Ok/Open/Open-Ok negotiation, real Channel.Open/Open-Ok,
# real Queue.Declare/Declare-Ok, real Basic.Consume/Consume-Ok and Basic.Cancel/Cancel-Ok, real
# Basic.Publish followed by a real content-header frame (class/weight/body-size/property-flags)
# and one or more real content-body frames, real Basic.Deliver fanning a published message out to
# a registered consumer, real Basic.Ack, and a real Channel.Close/Connection.Close shutdown
# handshake (the real `BlockingConnection.close()` sequence: Basic.Cancel for every active
# consumer, then Channel.Close, then Connection.Close — all three genuinely round-trip here).
#
# v1 scope, deliberately (practical subset, matching every other module in this project):
# no real SASL auth (any PLAIN credentials are accepted, matching the project's consistent
# "auth complexity is out of scope unless it IS the scenario" stance), the default exchange
# only (routing-key == queue name, direct-to-queue — no exchange types/bindings), no
# server-generated queue names, at most one consumer per queue, `multiple=True` on Basic.Ack
# not specially handled (acks are accepted and otherwise no-ops — nothing here relies on
# redelivery/requeue semantics), heartbeats negotiated to 0 (disabled) since this is a
# short-lived demo, and — the one that most bounds the content-header parser — property-flags
# are assumed to be 0 (no BasicProperties fields set), true for every `basic_publish` call this
# demo makes (and for pika's own default `properties=None`), with a clear, deliberate exception
# raised if a real publisher ever sends a nonzero property-flags this broker doesn't parse.
#
# Usage:  pysharp run samples/amqp_broker_demo.py
# Prerequisite:  pysharp install pika

import asyncio
import socket
import struct
import threading
import time
import uuid

HOST = "127.0.0.1"

FRAME_METHOD = 1
FRAME_HEADER = 2
FRAME_BODY = 3
FRAME_HEARTBEAT = 8
FRAME_END = 0xCE

CLASS_CONNECTION = 10
CLASS_CHANNEL = 20
CLASS_QUEUE = 50
CLASS_BASIC = 60

CONN_START, CONN_START_OK = 10, 11
CONN_TUNE, CONN_TUNE_OK = 30, 31
CONN_OPEN, CONN_OPEN_OK = 40, 41
CONN_CLOSE, CONN_CLOSE_OK = 50, 51

CHAN_OPEN, CHAN_OPEN_OK = 10, 11
CHAN_CLOSE, CHAN_CLOSE_OK = 40, 41

QUEUE_DECLARE, QUEUE_DECLARE_OK = 10, 11

BASIC_CONSUME, BASIC_CONSUME_OK = 20, 21
BASIC_CANCEL, BASIC_CANCEL_OK = 30, 31
BASIC_PUBLISH = 40
BASIC_DELIVER = 60
BASIC_ACK = 80


# ---------------------------------------------------------------- wire-format helpers

def _short_str(s):
    b = s.encode("utf-8") if isinstance(s, str) else s
    return bytes([len(b)]) + b


def _long_str(b):
    b = b.encode("utf-8") if isinstance(b, str) else b
    return struct.pack("!I", len(b)) + b


def _decode_short_str(buf, offset):
    length = buf[offset]
    start = offset + 1
    return buf[start:start + length].decode("utf-8"), start + length


def _empty_table():
    return struct.pack("!I", 0)


def _skip_table(buf, offset):
    """A field table is a 4-byte length prefix followed by exactly that many bytes — since this
    broker never needs the *contents* of a client-sent table (client-properties, Queue.Declare/
    Basic.Consume arguments), it can always be skipped wholesale from its own declared length,
    with no need to actually decode each entry's AMQP type tag."""
    (length,) = struct.unpack_from("!I", buf, offset)
    return offset + 4 + length


def _frame(frame_type, channel, payload):
    return struct.pack("!BHI", frame_type, channel, len(payload)) + payload + bytes([FRAME_END])


def _method_frame(channel, class_id, method_id, args):
    return _frame(FRAME_METHOD, channel, struct.pack("!HH", class_id, method_id) + args)


class _ConnReader:
    """Buffers partial reads off a non-blocking socket so callers can ask for exactly N bytes —
    the same pattern as asgi_server.py/mqtt_broker_demo.py's own `_ConnReader`."""

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


async def _read_frame(reader):
    """Reads one real AMQP 0-9-1 frame: 1-byte type, 2-byte channel, 4-byte payload size, the
    payload itself, and the mandatory 0xCE frame-end octet. Returns (type, channel, payload) or
    None on a clean disconnect/malformed stream."""
    head = await reader.read_exact(7)
    if head is None:
        return None
    frame_type, channel, size = struct.unpack("!BHI", head)
    payload = await reader.read_exact(size)
    if payload is None:
        return None
    end = await reader.read_exact(1)
    if end is None or end[0] != FRAME_END:
        return None
    return frame_type, channel, payload


# ---------------------------------------------------------------- the broker

class Broker:
    def __init__(self, host=HOST, port=0):
        self.host = host
        self.port = port
        self.loop = None
        self.srv = None
        self.stop_event = None
        self.lock = threading.Lock()
        self.queues = {}      # queue name -> list of pending (not yet consumed) message bodies
        self.consumers = {}   # queue name -> (conn, channel, consumer_tag)

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
                    asyncio.create_task(self._handle_connection(conn))
                else:
                    accept_task.cancel()
        finally:
            srv.close()

    def stop(self):
        self.loop.call_soon_threadsafe(self.stop_event.set)

    async def _handle_connection(self, conn):
        reader = _ConnReader(self.loop, conn)
        try:
            if not await self._negotiate(conn, reader):
                return
            pending_publish = {}  # channel -> {"queue": str, "body_size": int, "body": bytes}
            while True:
                frame = await _read_frame(reader)
                if frame is None:
                    break
                frame_type, channel, payload = frame
                if frame_type == FRAME_METHOD:
                    if not await self._handle_method(conn, channel, payload, pending_publish):
                        break
                elif frame_type == FRAME_HEADER:
                    self._handle_header(channel, payload, pending_publish)
                elif frame_type == FRAME_BODY:
                    await self._handle_body(channel, payload, pending_publish)
                # FRAME_HEARTBEAT: nothing to do — heartbeats were negotiated to 0 (disabled),
                # but tolerate one anyway rather than treating it as a protocol error.
        finally:
            with self.lock:
                for queue_name, (c, _ch, _tag) in list(self.consumers.items()):
                    if c is conn:
                        del self.consumers[queue_name]
            conn.close()

    async def _negotiate(self, conn, reader):
        """The real Connection.Start/Start-Ok/Tune/Tune-Ok/Open/Open-Ok handshake. Only the
        8-byte protocol header and the method class/method ids are ever actually inspected —
        every method's *argument* bytes are real (and correctly framed/sized by the caller), but
        this broker doesn't need to read them: it has nothing to validate (no real auth, a
        single fixed virtual host)."""
        header = await reader.read_exact(8)
        if header is None or header[:4] != b"AMQP":
            return False

        server_props = _empty_table()
        mechanisms = _long_str(b"PLAIN")
        locales = _long_str(b"en_US")
        start_args = struct.pack("!BB", 0, 9) + server_props + mechanisms + locales
        await self.loop.sock_sendall(conn, _method_frame(0, CLASS_CONNECTION, CONN_START, start_args))
        if await _read_frame(reader) is None:  # Connection.Start-Ok
            return False

        tune_args = struct.pack("!HIH", 0, 131072, 0)  # channel-max, frame-max, heartbeat=0
        await self.loop.sock_sendall(conn, _method_frame(0, CLASS_CONNECTION, CONN_TUNE, tune_args))
        if await _read_frame(reader) is None:  # Connection.Tune-Ok
            return False

        if await _read_frame(reader) is None:  # Connection.Open
            return False
        await self.loop.sock_sendall(conn, _method_frame(0, CLASS_CONNECTION, CONN_OPEN_OK, _short_str("")))
        return True

    async def _handle_method(self, conn, channel, payload, pending_publish):
        """Returns False when the connection should close (a real Connection.Close)."""
        class_id, method_id = struct.unpack_from("!HH", payload, 0)
        args = payload[4:]

        if (class_id, method_id) == (CLASS_CHANNEL, CHAN_OPEN):
            await self.loop.sock_sendall(conn, _method_frame(channel, CLASS_CHANNEL, CHAN_OPEN_OK, _long_str(b"")))
        elif (class_id, method_id) == (CLASS_QUEUE, QUEUE_DECLARE):
            await self._handle_queue_declare(conn, channel, args)
        elif (class_id, method_id) == (CLASS_BASIC, BASIC_CONSUME):
            await self._handle_basic_consume(conn, channel, args)
        elif (class_id, method_id) == (CLASS_BASIC, BASIC_CANCEL):
            await self._handle_basic_cancel(conn, channel, args)
        elif (class_id, method_id) == (CLASS_BASIC, BASIC_PUBLISH):
            offset = 2  # reserved-1 (short)
            exchange, offset = _decode_short_str(args, offset)
            routing_key, offset = _decode_short_str(args, offset)
            pending_publish[channel] = {"queue": routing_key, "body_size": None, "body": b""}
        elif (class_id, method_id) == (CLASS_BASIC, BASIC_ACK):
            pass  # v1 scope: acks accepted, no redelivery/requeue bookkeeping
        elif (class_id, method_id) == (CLASS_CHANNEL, CHAN_CLOSE):
            await self.loop.sock_sendall(conn, _method_frame(channel, CLASS_CHANNEL, CHAN_CLOSE_OK, b""))
        elif (class_id, method_id) == (CLASS_CONNECTION, CONN_CLOSE):
            await self.loop.sock_sendall(conn, _method_frame(0, CLASS_CONNECTION, CONN_CLOSE_OK, b""))
            return False
        return True

    async def _handle_queue_declare(self, conn, channel, args):
        offset = 2  # reserved-1 (short)
        queue_name, offset = _decode_short_str(args, offset)
        offset += 1  # bits: passive/durable/exclusive/auto-delete/no-wait
        with self.lock:
            self.queues.setdefault(queue_name, [])
        body = _short_str(queue_name) + struct.pack("!II", 0, 0)  # message-count, consumer-count
        await self.loop.sock_sendall(conn, _method_frame(channel, CLASS_QUEUE, QUEUE_DECLARE_OK, body))

    async def _handle_basic_consume(self, conn, channel, args):
        offset = 2  # reserved-1 (short)
        queue_name, offset = _decode_short_str(args, offset)
        consumer_tag, offset = _decode_short_str(args, offset)
        if not consumer_tag:
            consumer_tag = "pysharp-ctag-" + uuid.uuid4().hex[:8]

        backlog = []
        with self.lock:
            self.consumers[queue_name] = (conn, channel, consumer_tag)
            backlog = self.queues.get(queue_name, [])
            self.queues[queue_name] = []

        await self.loop.sock_sendall(
            conn, _method_frame(channel, CLASS_BASIC, BASIC_CONSUME_OK, _short_str(consumer_tag))
        )
        for body in backlog:
            await self._deliver(conn, channel, consumer_tag, queue_name, body)

    async def _handle_basic_cancel(self, conn, channel, args):
        consumer_tag, _offset = _decode_short_str(args, 0)
        with self.lock:
            for queue_name, (c, ch, tag) in list(self.consumers.items()):
                if tag == consumer_tag:
                    del self.consumers[queue_name]
        await self.loop.sock_sendall(
            conn, _method_frame(channel, CLASS_BASIC, BASIC_CANCEL_OK, _short_str(consumer_tag))
        )

    def _handle_header(self, channel, payload, pending_publish):
        # class-id(2) + weight(2) + body-size(8) + property-flags(2) [+ property list...]
        body_size = struct.unpack_from("!Q", payload, 4)[0]
        property_flags = struct.unpack_from("!H", payload, 12)[0]
        if property_flags != 0:
            raise NotImplementedError(
                "amqp_broker_demo: a real BasicProperties field was set (property-flags=0x%04x) "
                "— this v1 broker only handles the no-properties case (see the module docstring)"
                % property_flags
            )
        pub = pending_publish.get(channel)
        if pub is not None:
            pub["body_size"] = body_size
            if body_size == 0:
                pub["body"] = b""

    async def _handle_body(self, channel, payload, pending_publish):
        pub = pending_publish.get(channel)
        if pub is None:
            return
        pub["body"] += payload
        if pub["body_size"] is not None and len(pub["body"]) >= pub["body_size"]:
            queue_name = pub["queue"]
            body = pub["body"]
            del pending_publish[channel]
            with self.lock:
                consumer = self.consumers.get(queue_name)
            if consumer is not None:
                c_conn, c_channel, c_tag = consumer
                await self._deliver(c_conn, c_channel, c_tag, queue_name, body)
            else:
                with self.lock:
                    self.queues.setdefault(queue_name, []).append(body)

    async def _deliver(self, conn, channel, consumer_tag, queue_name, body):
        deliver_args = (
            _short_str(consumer_tag)
            + struct.pack("!Q", 1)   # delivery-tag
            + bytes([0])             # redelivered = False
            + _short_str("")         # exchange (default exchange)
            + _short_str(queue_name)  # routing-key
        )
        await self.loop.sock_sendall(conn, _method_frame(channel, CLASS_BASIC, BASIC_DELIVER, deliver_args))
        header_payload = struct.pack("!HHQH", CLASS_BASIC, 0, len(body), 0)
        await self.loop.sock_sendall(conn, _frame(FRAME_HEADER, channel, header_payload))
        if body:
            await self.loop.sock_sendall(conn, _frame(FRAME_BODY, channel, body))


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

    import pika

    queue_name = "pysharp.amqp.demo"
    received = []

    params = pika.ConnectionParameters(host=HOST, port=port)

    sub_conn = pika.BlockingConnection(params)
    sub_channel = sub_conn.channel()
    sub_channel.queue_declare(queue=queue_name)

    def on_message(ch, method, properties, body):
        text = body.decode("utf-8")
        received.append(text)
        print("[sub] received -> %s" % text)

    sub_channel.basic_consume(queue=queue_name, on_message_callback=on_message, auto_ack=True)

    pub_conn = pika.BlockingConnection(params)
    pub_channel = pub_conn.channel()
    pub_channel.queue_declare(queue=queue_name)

    for i in range(3):
        text = "hello from PySharp #%d" % i
        pub_channel.basic_publish(exchange="", routing_key=queue_name, body=text)
        print("[pub] sent -> %s" % text)

    deadline = time.time() + 5
    while time.time() < deadline and len(received) < 3:
        sub_conn.process_data_events(time_limit=0.3)

    sub_conn.close()
    pub_conn.close()

    broker.stop()
    thread.join(timeout=5)

    print("[main] received %d/3 messages" % len(received))
    return 0 if len(received) == 3 else 2


if __name__ == "__main__":
    raise SystemExit(main())
