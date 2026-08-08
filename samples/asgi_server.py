# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# asgi_server.py — a minimal, real ASGI/3 HTTP server, run by PySharp.
#
# Scenario 2 of the roadmap (phase 3.2): unlike async_api.py (a hand-rolled router that
# builds its own response objects), this bridges raw HTTP/1.1 to the real ASGI protocol
# (scope/receive/send) — the same interface a real ASGI application (starlette, FastAPI, ...)
# expects from its server. `serve(app, host, port)` is reusable: pass it any real ASGI
# callable. The demo app below is hand-written (no external dependency) so this sample runs
# standalone, but any real ASGI app works identically — see FASTAPI_PLAN.md Phase 3.2.
#
# What it exercises in the interpreter (built up across scenarios 1b/2a/2b/2/3):
#   - the .NET-backed event loop's asynchronous socket I/O
#     (loop.sock_accept / loop.sock_recv / loop.sock_sendall)
#   - the full real ASGI 3.0 http scope/receive/send contract
#
# v1 scope, deliberately: request bodies are read fully before the app is invoked (no
# streaming request bodies), and every connection closes after one response (no HTTP/1.1
# keep-alive/pipelining) — real, honest simplifications, not silent gaps. No WebSocket
# support (needs the HTTP/1.1 Upgrade handshake, out of scope for "minimal").
#
# Usage:  pysharp run samples/asgi_server.py
#   curl http://127.0.0.1:8000/
#   curl http://127.0.0.1:8000/items/42
#   curl -X POST http://127.0.0.1:8000/echo -d "hello"
#   curl http://127.0.0.1:8000/nope            # 404 from the demo app

import asyncio
import socket

HOST = "127.0.0.1"
PORT = 8000

_STATUS_TEXT = {
    200: "OK", 201: "Created", 204: "No Content",
    301: "Moved Permanently", 302: "Found", 304: "Not Modified",
    307: "Temporary Redirect", 308: "Permanent Redirect",
    400: "Bad Request", 401: "Unauthorized", 403: "Forbidden", 404: "Not Found",
    405: "Method Not Allowed", 422: "Unprocessable Entity",
    500: "Internal Server Error", 502: "Bad Gateway", 503: "Service Unavailable",
}


async def _recv_request(loop, conn):
    buf = b""
    while b"\r\n\r\n" not in buf:
        chunk = await loop.sock_recv(conn, 4096)
        if not chunk:
            return None
        buf = buf + chunk
    head, _, rest = buf.partition(b"\r\n\r\n")
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
    body = rest
    while len(body) < content_length:
        chunk = await loop.sock_recv(conn, 4096)
        if not chunk:
            break
        body = body + chunk
    path, _, query = target.partition("?")
    return {
        "method": method,
        "path": path,
        "query": query,
        "headers": headers,
        "body": body,
    }


async def _handle(loop, conn, addr, app):
    try:
        req = await _recv_request(loop, conn)
        if req is None:
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


async def serve(app, host=HOST, port=PORT):
    """Real, minimal ASGI/3 HTTP server: accepts connections and drives `app` (any real ASGI
    callable) with a genuine scope/receive/send triple built from the raw HTTP/1.1 bytes."""
    loop = asyncio.get_running_loop()
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    srv.bind((host, port))
    srv.listen(64)
    srv.setblocking(False)
    print("listening on http://" + host + ":" + str(port))
    while True:
        conn, addr = await loop.sock_accept(srv)
        conn.setblocking(False)
        asyncio.create_task(_handle(loop, conn, addr, app))


# ------------------------------------------------------------- demo ASGI app (no dependency)
async def demo_app(scope, receive, send):
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
