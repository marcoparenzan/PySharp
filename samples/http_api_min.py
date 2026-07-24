# http_api_min.py — minimal synchronous HTTP API, run by PySharp.
#
# Roadmap phase 2.0 (see ROADMAP.md): the API "walking skeleton".
# No async, no external dependency: just the `socket` and `json` modules.
# Plain `def` handlers, routing by (method, path), JSON response.
#
# Usage:  pysharp run samples/http_api_min.py
#         then:  curl http://127.0.0.1:8080/hello?name=Marco

import json
import socket

HOST = "127.0.0.1"
PORT = 8080

# --- routing: (method, path) -> handler(req) -> (status, body_dict) ---
routes = {}


def route(method, path):
    def deco(fn):
        routes[(method, path)] = fn
        return fn
    return deco


@route("GET", "/")
def index(req):
    return 200, {"message": "PySharp HTTP API is alive", "engine": "PySharp"}


@route("GET", "/health")
def health(req):
    return 200, {"status": "ok"}


@route("GET", "/hello")
def hello(req):
    name = req["query"].get("name", "world")
    return 200, {"hello": name}


STATUS_TEXT = {200: "OK", 400: "Bad Request", 404: "Not Found"}


def parse_request(raw):
    text = raw.decode("utf-8")
    lines = text.split("\r\n")
    if len(lines) == 0 or lines[0] == "":
        return None
    parts = lines[0].split(" ")
    if len(parts) < 2:
        return None
    method = parts[0]
    target = parts[1]
    path = target
    query = {}
    if "?" in target:
        path, qs = target.split("?", 1)
        for pair in qs.split("&"):
            if "=" in pair:
                k, v = pair.split("=", 1)
                query[k] = v
    return {"method": method, "path": path, "query": query}


def build_response(status, body):
    data = json.dumps(body).encode("utf-8")
    text = STATUS_TEXT.get(status, "OK")
    head = "HTTP/1.1 " + str(status) + " " + text + "\r\n"
    head += "Content-Type: application/json\r\n"
    head += "Content-Length: " + str(len(data)) + "\r\n"
    head += "Connection: close\r\n"
    head += "\r\n"
    return head.encode("utf-8") + data


def handle(conn):
    raw = conn.recv(65536)
    req = parse_request(raw)
    if req is None:
        conn.sendall(build_response(400, {"error": "bad request"}))
        return
    handler = routes.get((req["method"], req["path"]))
    if handler is None:
        conn.sendall(build_response(404, {"error": "not found", "path": req["path"]}))
        return
    status, body = handler(req)
    print(req["method"], req["path"], "->", status)
    conn.sendall(build_response(status, body))


def main():
    srv = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    srv.bind((HOST, PORT))
    srv.listen(16)
    print("listening on http://" + HOST + ":" + str(PORT))
    while True:
        conn, addr = srv.accept()
        try:
            handle(conn)
        finally:
            conn.close()


main()
