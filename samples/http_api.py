# http_api.py — "FastAPI-shaped" synchronous HTTP mini-framework, run by PySharp.
#
# Hardening round of phase 2.0 (see ROADMAP.md, scenario 2). Adds, while staying
# synchronous and pure Python over the `socket` and `json` modules:
#   - routing with PATH PARAMETERS:  /items/{item_id}
#   - GET/POST methods, query string and JSON body
#   - parameter VALIDATION + COERCION driven by the handler's type hints,
#     read at runtime from handler.__annotations__ (422 response if invalid)
#   - parameter INJECTION by name into the handler arguments (FastAPI-style),
#     including UNANNOTATED parameters (treated as 'str', as FastAPI does)
#
# This is exactly FastAPI's internal mechanism (signature + type hints -> validation
# -> injection), here without async and without pydantic. The handler signature is read
# at runtime by combining __code__.co_varnames (names) and __annotations__ (types) — two
# introspection features added to the interpreter for this very scenario.
#
# Usage:  pysharp run samples/http_api.py
#   curl http://127.0.0.1:8080/
#   curl http://127.0.0.1:8080/items/42
#   curl "http://127.0.0.1:8080/search?q=pump&limit=5&verbose=true"
#   curl -X POST http://127.0.0.1:8080/items -d "{\"name\": \"pump\", \"qty\": 3}"
#   curl http://127.0.0.1:8080/items/abc        # -> 422 (not an int)

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
    # full handler signature = NAMES (from __code__.co_varnames) + TYPES (from __annotations__).
    # The names include unannotated parameters too, which FastAPI treats as 'str'.
    anns = fn.__annotations__
    code = fn.__code__
    argcount = code.co_argcount
    names = list(code.co_varnames)[:argcount]   # positional-or-keyword parameters

    # positional defaults map onto the tail of the parameters
    defaults = fn.__defaults__ or ()
    default_map = {}
    first = argcount - len(defaults)
    i = 0
    while i < len(defaults):
        default_map[names[first + i]] = defaults[i]
        i += 1

    kwargs = {}
    for name in names:
        typ = anns.get(name, str)             # unannotated -> str (like FastAPI)
        if name in path_params:
            kwargs[name] = _coerce(name, path_params[name], typ)
        elif typ is dict:                     # "body" parameter (JSON)
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
def index():
    return {"message": "PySharp HTTP API", "engine": "PySharp"}


@get("/items/{item_id}")
def get_item(item_id: int):
    # item_id arrives coerced to int: prove it by doing arithmetic
    return {"item_id": item_id, "next": item_id + 1}


@get("/search")
def search(q: str, limit: int = 10, verbose: bool = False):
    return {"q": q, "limit": limit, "verbose": verbose}


@get("/greet/{name}")
def greet(name, excited: bool = False):
    # 'name' is NOT annotated: it is injected anyway (defaults to str, like FastAPI)
    text = "Hello " + name
    if excited:
        text = text + "!"
    return {"greeting": text}


@post("/items")
def create_item(payload: dict):
    return {"created": payload, "fields": list(payload)}


# ------------------------------------------------------------------- server
_STATUS = {200: "OK", 404: "Not Found", 422: "Unprocessable Entity"}


def _recv_request(conn):
    buf = b""
    while b"\r\n\r\n" not in buf:
        chunk = conn.recv(4096)
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
    # complete the body up to Content-Length
    length = 0
    for line in lines[1:]:
        if ":" in line:
            k, v = line.split(":", 1)
            if k.lower().strip() == "content-length":
                length = int(v.strip())
    while len(body_text.encode("utf-8")) < length:
        chunk = conn.recv(4096)
        if not chunk:
            break
        body_text = body_text + chunk.decode("utf-8")
    # split path and query string
    path = target
    query = {}
    if "?" in target:
        path, qs = target.split("?", 1)
        for pair in qs.split("&"):
            if "=" in pair:
                k, v = pair.split("=", 1)
                query[k] = v
    return method, path, query, body_text


def _respond(conn, status, obj):
    data = json.dumps(obj).encode("utf-8")
    text = _STATUS.get(status, "OK")
    head = "HTTP/1.1 " + str(status) + " " + text + "\r\n"
    head += "Content-Type: application/json\r\n"
    head += "Content-Length: " + str(len(data)) + "\r\n"
    head += "Connection: close\r\n\r\n"
    conn.sendall(head.encode("utf-8") + data)


def _handle(conn):
    method, path, query, body_text = _recv_request(conn)
    if method is None:
        _respond(conn, 422, {"detail": "invalid request"})
        return
    fn, path_params = _match(method, path)
    if fn is None:
        _respond(conn, 404, {"detail": "not found", "path": path})
        return
    body = None
    if body_text is not None and body_text.strip() != "":
        try:
            body = json.loads(body_text)
        except Exception:
            _respond(conn, 422, {"detail": "invalid JSON body"})
            return
    try:
        kwargs = _build_kwargs(fn, path_params, query, body)
        result = fn(**kwargs)
    except HTTPError as e:
        _respond(conn, e.status, {"detail": e.detail})
        print(method, path, "->", e.status)
        return
    _respond(conn, 200, result)
    print(method, path, "-> 200")


def main():
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.bind((HOST, PORT))
    srv.listen(16)
    print("listening on http://" + HOST + ":" + str(PORT))
    while True:
        conn, addr = srv.accept()
        try:
            _handle(conn)
        finally:
            conn.close()


main()
