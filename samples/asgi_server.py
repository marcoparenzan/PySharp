# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# asgi_server.py — a minimal, real ASGI/3 HTTP + WebSocket server, run by PySharp.
#
# Scenario 2 of the roadmap (phase 3.2, WebSocket support added in phase 4.3, graceful shutdown
# in phase 4.4): unlike async_api.py (a hand-rolled router that builds its own response objects),
# this bridges raw HTTP/1.1 (and, for a WebSocket Upgrade request, a real RFC 6455 connection) to
# the real ASGI protocol (scope/receive/send) — the same interface a real ASGI application
# (starlette, FastAPI, ...) expects from its server. `serve(app, host, port)` is reusable: pass it
# any real ASGI callable. The demo app below is hand-written (no external dependency) so this
# sample runs standalone, but any real ASGI app works identically — see FASTAPI_PLAN.md
# Phase 3.2/4.3/4.4.
#
# What it exercises in the interpreter (built up across scenarios 1b/2a/2b/2/3):
#   - the .NET-backed event loop's asynchronous socket I/O
#     (loop.sock_accept / loop.sock_recv / loop.sock_sendall)
#   - the full real ASGI 3.0 http AND websocket scope/receive/send contracts
#   - a real RFC 6455 WebSocket handshake (SHA1 + base64 on Sec-WebSocket-Key) and real frame
#     framing/masking (hashlib, base64, struct)
#   - a real `signal.signal()` (SIGINT/SIGTERM) for graceful shutdown: stop accepting new
#     connections, drain in-flight ones (up to 10s), then exit — instead of dying mid-request
#
# v1 scope, deliberately: request bodies are read fully before the app is invoked (no
# streaming request bodies), and every HTTP connection closes after one response (no HTTP/1.1
# keep-alive/pipelining) — real, honest simplifications, not silent gaps. WebSocket fragmented
# messages ARE reassembled (a real client, especially a browser, fragments any sufficiently
# large message), but an unmasked client frame is still accepted rather than rejected (RFC 6455
# requires the server to close the connection on one — a real, deliberately-skipped
# protocol-strictness simplification, not a functional gap for any real client, which always
# masks).
#
# Usage:  pysharp run samples/asgi_server.py
#   curl http://127.0.0.1:8000/
#   curl http://127.0.0.1:8000/items/42
#   curl -X POST http://127.0.0.1:8000/echo -d "hello"
#   curl http://127.0.0.1:8000/nope            # 404 from the demo app
#   (WebSocket: connect to ws://127.0.0.1:8000/ws and send text — it echoes back "echo: ...")

import asyncio
import base64
import hashlib
import signal
import socket
import struct

HOST = "127.0.0.1"
PORT = 8000

_WS_GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

_STATUS_TEXT = {
    200: "OK", 201: "Created", 204: "No Content",
    301: "Moved Permanently", 302: "Found", 304: "Not Modified",
    307: "Temporary Redirect", 308: "Permanent Redirect",
    400: "Bad Request", 401: "Unauthorized", 403: "Forbidden", 404: "Not Found",
    405: "Method Not Allowed", 422: "Unprocessable Entity",
    500: "Internal Server Error", 502: "Bad Gateway", 503: "Service Unavailable",
}


class _ConnReader:
    """Buffers partial reads off a non-blocking socket so callers can ask for exactly N bytes
    (or up to a delimiter) without losing any bytes read past what they needed — the initial
    HTTP request line/headers may arrive in the same TCP segment as the first WebSocket frame
    that follows them, and this is what lets the WebSocket path pick up exactly where the HTTP
    parse left off instead of dropping bytes."""

    def __init__(self, loop, conn):
        self.loop = loop
        self.conn = conn
        self.buf = b""

    async def _fill(self):
        chunk = await self.loop.sock_recv(self.conn, 4096)
        if not chunk:
            return False
        self.buf = self.buf + chunk
        return True

    async def read_until(self, delim):
        while delim not in self.buf:
            if not await self._fill():
                return None
        head, _, rest = self.buf.partition(delim)
        self.buf = rest
        return head

    async def read_exact(self, n):
        if n == 0:
            return b""
        while len(self.buf) < n:
            if not await self._fill():
                return None
        data = self.buf[:n]
        self.buf = self.buf[n:]
        return data


async def _recv_request(reader):
    head = await reader.read_until(b"\r\n\r\n")
    if head is None:
        return None
    lines = head.split(b"\r\n")
    method, target, http_version = lines[0].decode("latin-1").split(" ")
    headers = []
    content_length = 0
    for line in lines[1:]:
        if b":" not in line:
            continue
        k, _, v = line.partition(b":")
        k = k.strip().lower()
        v = v.strip()
        headers.append((k, v))
        if k == b"content-length":
            content_length = int(v)
    body = await reader.read_exact(content_length) if content_length else b""
    path, _, query = target.partition("?")
    return {
        "method": method,
        "path": path,
        "query": query,
        "headers": headers,
        "body": body,
    }


def _header(headers, name):
    key = name.encode("latin-1")
    for k, v in headers:
        if k == key:
            return v
    return None


def _is_websocket_upgrade(headers):
    upgrade = _header(headers, "upgrade")
    connection = _header(headers, "connection")
    return (
        upgrade is not None and upgrade.lower() == b"websocket"
        and connection is not None and b"upgrade" in connection.lower()
    )


def _ws_accept_key(key):
    """The real RFC 6455 handshake computation: base64(SHA1(client_key + the spec's own fixed
    GUID)) — proves the server actually understood the WebSocket handshake, not just any HTTP
    Upgrade request."""
    digest = hashlib.sha1((key + _WS_GUID).encode("latin-1")).digest()
    return base64.b64encode(digest).decode("latin-1")


async def _recv_ws_frame(reader):
    """Reads one real RFC 6455 frame off a WebSocket connection: 2-byte header, the (possibly
    extended) payload length, a 4-byte mask key if MASK is set, and the (unmasked-on-read)
    payload. Returns (fin, opcode, payload), or None if the connection closed mid-frame. FIN is
    real: a large message a real client fragments across several frames (opcode 0x0 continuation
    frames, FIN=0 on every one but the last) needs it to know when the message is actually
    complete — see `_handle_websocket`'s own reassembly loop."""
    head = await reader.read_exact(2)
    if head is None:
        return None
    b0, b1 = head[0], head[1]
    fin = (b0 & 0x80) != 0
    opcode = b0 & 0x0F
    masked = (b1 & 0x80) != 0
    length = b1 & 0x7F
    if length == 126:
        ext = await reader.read_exact(2)
        if ext is None:
            return None
        length = struct.unpack("!H", ext)[0]
    elif length == 127:
        ext = await reader.read_exact(8)
        if ext is None:
            return None
        length = struct.unpack("!Q", ext)[0]
    mask_key = b""
    if masked:
        mask_key = await reader.read_exact(4)
        if mask_key is None:
            return None
    payload = await reader.read_exact(length)
    if payload is None:
        return None
    if masked:
        payload = bytes(payload[i] ^ mask_key[i % 4] for i in range(len(payload)))
    return fin, opcode, payload


def _build_ws_frame(opcode, payload):
    """Builds one real RFC 6455 frame, server-to-client — never masked, per spec (only
    client-to-server frames are)."""
    b0 = 0x80 | (opcode & 0x0F)  # FIN=1, no fragmentation on the way out either
    length = len(payload)
    if length < 126:
        header = bytes([b0, length])
    elif length < 65536:
        header = bytes([b0, 126]) + struct.pack("!H", length)
    else:
        header = bytes([b0, 127]) + struct.pack("!Q", length)
    return header + payload


def _finish_message(opcode, payload):
    if opcode == 0x1:
        return {"type": "websocket.receive", "text": payload.decode("utf-8", errors="replace")}
    return {"type": "websocket.receive", "bytes": payload}


async def _handle_websocket(loop, conn, app, scope, reader):
    """Drives `app` with the real ASGI websocket scope/receive/send protocol: receive() first
    yields `websocket.connect`, then a real (fragment-reassembled) message per subsequent call
    (auto-answering pings, and echoing a real close frame back once the client sends one — the
    real RFC 6455 closing handshake); send() completes the real HTTP 101 handshake on
    `websocket.accept` and writes real frames for `websocket.send`/`websocket.close`."""
    accepted = False
    closed = False

    async def receive():
        nonlocal closed
        if not accepted:
            return {"type": "websocket.connect"}
        if closed:
            # The app called receive() again after already seeing a disconnect — real ASGI apps
            # don't do this, but stay well-defined rather than trying to read a closed socket.
            return {"type": "websocket.disconnect", "code": 1006}
        fragment_opcode = None
        fragment_payload = b""
        while True:
            frame = await _recv_ws_frame(reader)
            if frame is None:
                closed = True
                return {"type": "websocket.disconnect", "code": 1006}
            fin, opcode, payload = frame
            if opcode == 0x8:  # close: echo one back — the real RFC 6455 closing handshake —
                # before telling the app the connection is gone.
                code = struct.unpack("!H", payload[:2])[0] if len(payload) >= 2 else 1000
                if not closed:
                    reply = payload[:2] if len(payload) >= 2 else struct.pack("!H", 1000)
                    await loop.sock_sendall(conn, _build_ws_frame(0x8, reply))
                closed = True
                return {"type": "websocket.disconnect", "code": code}
            if opcode == 0x9:  # ping: answer with a real pong, keep waiting for a real message
                await loop.sock_sendall(conn, _build_ws_frame(0xA, payload))
                continue
            if opcode == 0xA:  # pong: nothing to do, keep waiting
                continue
            if opcode == 0x0:  # continuation of an already-started fragmented message
                if fragment_opcode is None:
                    closed = True  # continuation with nothing to continue: a real protocol error
                    return {"type": "websocket.disconnect", "code": 1002}
                fragment_payload += payload
                if fin:
                    result = _finish_message(fragment_opcode, fragment_payload)
                    return result
                continue
            if opcode in (0x1, 0x2):
                if not fin:  # first frame of a new fragmented message: start accumulating
                    fragment_opcode = opcode
                    fragment_payload = payload
                    continue
                return _finish_message(opcode, payload)
            # another unsupported opcode
            closed = True
            return {"type": "websocket.disconnect", "code": 1003}

    async def send(message):
        nonlocal accepted, closed
        mtype = message["type"]
        if mtype == "websocket.accept":
            key = _header(scope["headers"], "sec-websocket-key")
            resp = (
                "HTTP/1.1 101 Switching Protocols\r\n"
                "Upgrade: websocket\r\n"
                "Connection: Upgrade\r\n"
                "Sec-WebSocket-Accept: " + _ws_accept_key(key.decode("latin-1")) + "\r\n"
                "\r\n"
            ).encode("latin-1")
            await loop.sock_sendall(conn, resp)
            accepted = True
        elif mtype == "websocket.send" and not closed:
            if message.get("text") is not None:
                await loop.sock_sendall(conn, _build_ws_frame(0x1, message["text"].encode("utf-8")))
            elif message.get("bytes") is not None:
                await loop.sock_sendall(conn, _build_ws_frame(0x2, message["bytes"]))
        elif mtype == "websocket.close" and not closed:
            code = message.get("code") or 1000
            await loop.sock_sendall(conn, _build_ws_frame(0x8, struct.pack("!H", code)))
            closed = True

    await app(scope, receive, send)


async def _handle(loop, conn, addr, app):
    try:
        reader = _ConnReader(loop, conn)
        req = await _recv_request(reader)
        if req is None:
            return

        if _is_websocket_upgrade(req["headers"]):
            if _header(req["headers"], "sec-websocket-key") is None:
                # A malformed handshake (real Upgrade headers but no real key to answer) — a
                # real 400 response instead of crashing trying to compute an accept for None.
                body = b"Bad Request: missing Sec-WebSocket-Key"
                head = (
                    "HTTP/1.1 400 Bad Request\r\n"
                    "Content-Type: text/plain; charset=utf-8\r\n"
                    "Content-Length: " + str(len(body)) + "\r\n"
                    "Connection: close\r\n\r\n"
                ).encode("latin-1")
                await loop.sock_sendall(conn, head + body)
                return
            scope = {
                "type": "websocket",
                "asgi": {"version": "3.0", "spec_version": "2.3"},
                "http_version": "1.1",
                "scheme": "ws",
                "path": req["path"],
                "raw_path": req["path"].encode("utf-8"),
                "query_string": req["query"].encode("utf-8"),
                "root_path": "",
                "headers": req["headers"],
                "subprotocols": [],
                "server": (HOST, PORT),
                "client": (addr[0], addr[1]),
                "state": {},
            }
            await _handle_websocket(loop, conn, app, scope, reader)
            return

        scope = {
            "type": "http",
            "asgi": {"version": "3.0", "spec_version": "2.3"},
            "http_version": "1.1",
            "method": req["method"],
            "scheme": "http",
            "path": req["path"],
            "raw_path": req["path"].encode("utf-8"),
            "query_string": req["query"].encode("utf-8"),
            "root_path": "",
            "headers": req["headers"],
            "server": (HOST, PORT),
            "client": (addr[0], addr[1]),
            "state": {},
        }

        body = req["body"]
        received = False

        async def receive():
            nonlocal received
            if not received:
                received = True
                return {"type": "http.request", "body": body, "more_body": False}
            return {"type": "http.request", "body": b"", "more_body": False}

        async def send(message):
            if message["type"] == "http.response.start":
                status = message["status"]
                resp_headers = message.get("headers", [])
                status_line = "HTTP/1.1 " + str(status) + " " + _STATUS_TEXT.get(status, "OK") + "\r\n"
                header_lines = "".join(
                    h[0].decode("latin-1") + ": " + h[1].decode("latin-1") + "\r\n"
                    for h in resp_headers
                )
                head = (status_line + header_lines + "Connection: close\r\n\r\n").encode("latin-1")
                await loop.sock_sendall(conn, head)
            elif message["type"] == "http.response.body":
                chunk = message.get("body", b"")
                if chunk:
                    await loop.sock_sendall(conn, chunk)

        await app(scope, receive, send)
    finally:
        conn.close()


async def _serve_until_stopped(app, srv, loop, stop_event):
    """The real accept-until-stopped-then-drain shutdown sequence, factored out of `serve()` so
    it can be exercised directly with a caller-controlled `stop_event` — no real OS signal needed
    to test the actual graceful-shutdown *logic* (draining in-flight connections before the
    server actually stops); real signal delivery itself (`serve()`'s own SIGINT/SIGTERM handlers)
    is verified separately, live, in a real interactive terminal — see FASTAPI_PLAN.md Phase 4.4.
    Races each accept against `stop_event`, so a signal received while idle between connections
    stops the server immediately rather than only being noticed after the next connection."""
    connections = set()
    try:
        while not stop_event.is_set():
            accept_task = asyncio.create_task(loop.sock_accept(srv))
            stop_task = asyncio.create_task(stop_event.wait())
            done, pending = await asyncio.wait(
                {accept_task, stop_task}, return_when=asyncio.FIRST_COMPLETED
            )
            if accept_task in done:
                stop_task.cancel()
                conn, addr = accept_task.result()
                conn.setblocking(False)
                task = asyncio.create_task(_handle(loop, conn, addr, app))
                connections.add(task)
                task.add_done_callback(connections.discard)
            else:
                accept_task.cancel()
    finally:
        srv.close()
        if connections:
            print("shutting down: waiting for " + str(len(connections)) + " active connection(s)...")
            await asyncio.wait(connections, timeout=10)
        print("shutdown complete")


async def serve(app, host=HOST, port=PORT):
    """Real, minimal ASGI/3 HTTP + WebSocket server: accepts connections and drives `app` (any
    real ASGI callable) with a genuine scope/receive/send triple built from the raw HTTP/1.1
    bytes, or (for a real WebSocket Upgrade request) a genuine RFC 6455 handshake + framing.

    Real graceful shutdown on SIGINT/SIGTERM: stops accepting new connections and waits (up to 10s)
    for in-flight ones to finish before actually closing, instead of dying mid-request. Uses this
    project's own real `signal.signal()` (FASTAPI_PLAN.md Phase 4.4) — verified by hand, live, in a
    real interactive terminal (`Ctrl+C` correctly ran the handler and exited cleanly, no traceback)."""
    loop = asyncio.get_running_loop()
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((host, port))
    srv.listen(64)
    srv.setblocking(False)

    stop_event = asyncio.Event()

    def _request_stop(signum, frame):
        stop_event.set()

    previous = {
        signal.SIGINT: signal.signal(signal.SIGINT, _request_stop),
        signal.SIGTERM: signal.signal(signal.SIGTERM, _request_stop),
    }
    try:
        print("listening on http://" + host + ":" + str(port))
        await _serve_until_stopped(app, srv, loop, stop_event)
    finally:
        for sig, handler in previous.items():
            signal.signal(sig, handler)


# ------------------------------------------------------------- demo ASGI app (no dependency)
async def demo_app(scope, receive, send):
    if scope["type"] == "websocket":
        if scope["path"] != "/ws":
            await send({"type": "websocket.close", "code": 1008})
            return
        await send({"type": "websocket.accept"})
        while True:
            event = await receive()
            if event["type"] == "websocket.disconnect":
                return
            if event.get("text") is not None:
                await send({"type": "websocket.send", "text": "echo: " + event["text"]})
            elif event.get("bytes") is not None:
                await send({"type": "websocket.send", "bytes": event["bytes"]})
        return

    if scope["type"] != "http":
        return
    path = scope["path"]
    method = scope["method"]

    async def respond(status, body_text, content_type="text/plain; charset=utf-8"):
        body = body_text.encode("utf-8")
        await send({
            "type": "http.response.start",
            "status": status,
            "headers": [
                (b"content-type", content_type.encode("utf-8")),
                (b"content-length", str(len(body)).encode("utf-8")),
            ],
        })
        await send({"type": "http.response.body", "body": body})

    if path == "/" and method == "GET":
        await respond(200, "hello from a real ASGI app served by PySharp")
    elif path.startswith("/items/") and method == "GET":
        item_id = path[len("/items/"):]
        await respond(200, "item_id=" + item_id)
    elif path == "/echo" and method == "POST":
        body = await receive()
        await respond(200, "echo: " + body["body"].decode("utf-8", errors="replace"))
    else:
        await respond(404, "not found: " + path)


if __name__ == "__main__":
    asyncio.run(serve(demo_app))
