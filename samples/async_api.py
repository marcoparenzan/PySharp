# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# async_api.py — asynchronous "FastAPI-shaped" HTTP API, run by PySharp.
#
# Scenario 2 of the roadmap (phase 2a/2b): the same signature-driven mechanism as
# http_api.py, but now fully ASYNCHRONOUS on a real asyncio event loop. Each connection
# is handled by its own Task, so slow handlers (that `await`) do NOT block the others —
# genuine cooperative concurrency, single-threaded, exactly like CPython's asyncio.
#
# What it exercises in the interpreter (all added for this scenario):
#   - async def / await / async handlers
#   - asyncio.run, asyncio.create_task, asyncio.sleep, asyncio.gather
#   - the .NET-backed event loop with asynchronous socket I/O
#     (loop.sock_accept / loop.sock_recv / loop.sock_sendall)
#   - parameter validation + injection driven by the handler's type hints
#     (handler.__annotations__ + handler.__code__.co_varnames)
#
# Usage:  pysharp run samples/async_api.py
#   curl http://127.0.0.1:8080/
#   curl http://127.0.0.1:8080/items/42
#   curl "http://127.0.0.1:8080/slow?ms=500"      # sleeps 500ms without blocking others
#   curl -X POST http://127.0.0.1:8080/echo -d "{\"hello\": \"world\"}"

import asyncio
import json
import socket

HOST = "127.0.0.1"
PORT = 8080


class HTTPError(Exception):
    def __init__(self, status, detail):
        self.status = status
        self.detail = detail


# ------------------------------------------------------------------ routing
_routes = []  # (method, segments, handler)


def _segments(path):
    return [s for s in path.split("/") if s != ""]


def route(method, path):
    segs = _segments(path)

    def deco(fn):
        _routes.append((method, segs, fn))
        return fn
    return deco


def get(path):
    return route("GET", path)


def post(path):
    return route("POST", path)


def _match(method, path):
    req = _segments(path)
    for m, segs, fn in _routes:
        if m != method or len(segs) != len(req):
            continue
        params = {}
        ok = True
        i = 0
        while i < len(segs):
            s = segs[i]
            if len(s) >= 2 and s[0] == "{" and s[-1] == "}":
                params[s[1:-1]] = req[i]
            elif s != req[i]:
                ok = False
                break
            i += 1
        if ok:
            return fn, params
    return None, None


# --------------------------------------------------------------- validation
def _coerce(name, raw, typ):
    if typ is bool:
        s = str(raw).lower()
        if s in ("1", "true", "yes", "on"):
            return True
        if s in ("0", "false", "no", "off", ""):
            return False
        raise HTTPError(422, "parameter '" + name + "': expected bool, got " + repr(raw))
    try:
        return typ(raw)
    except Exception:
        raise HTTPError(422, "parameter '" + name + "': expected " + typ.__name__ + ", got " + repr(raw))


def _build_kwargs(fn, path_params, query, body):
    # full handler signature = names (from __code__.co_varnames) + types (from __annotations__)
    anns = fn.__annotations__
    code = fn.__code__
    names = list(code.co_varnames)[:code.co_argcount]
    defaults = fn.__defaults__ or ()
    default_map = {}
    first = code.co_argcount - len(defaults)
    i = 0
    while i < len(defaults):
        default_map[names[first + i]] = defaults[i]
        i += 1

    kwargs = {}
    for name in names:
        typ = anns.get(name, str)              # unannotated -> str (like FastAPI)
        if name in path_params:
            kwargs[name] = _coerce(name, path_params[name], typ)
        elif typ is dict:                      # JSON body parameter
            kwargs[name] = body if body is not None else {}
        elif name in query:
            kwargs[name] = _coerce(name, query[name], typ)
        elif name in default_map:
            kwargs[name] = default_map[name]
        else:
            raise HTTPError(422, "missing parameter: '" + name + "'")
    return kwargs


# ------------------------------------------------------------------ handlers
@get("/")
async def index():
    return {"message": "PySharp async HTTP API", "engine": "PySharp", "async": True}


@get("/items/{item_id}")
async def get_item(item_id: int):
    return {"item_id": item_id, "next": item_id + 1}


@get("/slow")
async def slow(ms: int = 200):
    # await an asynchronous sleep: the loop keeps serving other requests meanwhile
    await asyncio.sleep(ms / 1000.0)
    return {"slept_ms": ms}


@get("/gather")
async def gather_demo():
    async def piece(n):
        await asyncio.sleep(0.01 * n)
        return n * n
    squares = await asyncio.gather(piece(1), piece(2), piece(3))
    return {"squares": squares}


@post("/echo")
async def echo(payload: dict):
    return {"echoed": payload, "fields": list(payload)}


# ------------------------------------------------------------------- server
_STATUS = {200: "OK", 404: "Not Found", 422: "Unprocessable Entity"}


async def _recv_request(loop, conn):
    buf = b""
    while b"\r\n\r\n" not in buf:
        chunk = await loop.sock_recv(conn, 4096)
        if not chunk:
            break
        buf = buf + chunk
    text = buf.decode("utf-8")
    idx = text.find("\r\n\r\n")
    if idx < 0:
        return None, None, None, None
    head = text[:idx]
    body_text = text[idx + 4:]
    lines = head.split("\r\n")
    request_line = lines[0].split(" ")
    method, target = request_line[0], request_line[1]
    length = 0
    for line in lines[1:]:
        if ":" in line:
            k, v = line.split(":", 1)
            if k.lower().strip() == "content-length":
                length = int(v.strip())
    while len(body_text.encode("utf-8")) < length:
        chunk = await loop.sock_recv(conn, 4096)
        if not chunk:
            break
        body_text = body_text + chunk.decode("utf-8")
    path = target
    query = {}
    if "?" in target:
        path, qs = target.split("?", 1)
        for pair in qs.split("&"):
            if "=" in pair:
                k, v = pair.split("=", 1)
                query[k] = v
    return method, path, query, body_text


async def _respond(loop, conn, status, obj):
    data = json.dumps(obj).encode("utf-8")
    text = _STATUS.get(status, "OK")
    head = "HTTP/1.1 " + str(status) + " " + text + "\r\n"
    head += "Content-Type: application/json\r\n"
    head += "Content-Length: " + str(len(data)) + "\r\n"
    head += "Connection: close\r\n\r\n"
    await loop.sock_sendall(conn, head.encode("utf-8") + data)


async def _handle(loop, conn):
    try:
        method, path, query, body_text = await _recv_request(loop, conn)
        if method is None:
            await _respond(loop, conn, 422, {"detail": "invalid request"})
            return
        fn, path_params = _match(method, path)
        if fn is None:
            await _respond(loop, conn, 404, {"detail": "not found", "path": path})
            return
        body = None
        if body_text is not None and body_text.strip() != "":
            try:
                body = json.loads(body_text)
            except Exception:
                await _respond(loop, conn, 422, {"detail": "invalid JSON body"})
                return
        try:
            kwargs = _build_kwargs(fn, path_params, query, body)
            result = await fn(**kwargs)          # <-- await the async handler
        except HTTPError as e:
            await _respond(loop, conn, e.status, {"detail": e.detail})
            print(method, path, "->", e.status)
            return
        await _respond(loop, conn, 200, result)
        print(method, path, "-> 200")
    finally:
        conn.close()


async def main():
    loop = asyncio.get_running_loop()
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.bind((HOST, PORT))
    srv.listen(64)
    srv.setblocking(False)
    print("listening on http://" + HOST + ":" + str(PORT))
    while True:
        conn, addr = await loop.sock_accept(srv)
        conn.setblocking(False)
        # each connection is its own Task: concurrent, non-blocking
        asyncio.create_task(_handle(loop, conn))


asyncio.run(main())
