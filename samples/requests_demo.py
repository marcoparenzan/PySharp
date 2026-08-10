# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# requests_demo.py — the real, unmodified `requests` package, run entirely by PySharp,
# talking real HTTPS to a real public server.
#
# Scenario 4 of the roadmap (HTTP client). `requests` pulls in `urllib3`, which needed a
# genuine HTTP/1.1 client engine (`http.client.HTTPConnection`/`HTTPResponse`, subclassable —
# urllib3 literally does `class HTTPConnection(http.client.HTTPConnection)` and drives it via
# `super()`) — the largest new C# module this scenario added. Nothing about `requests`/
# `urllib3` itself is stubbed or reimplemented; every byte on the wire goes through this
# project's own real TCP sockets (`socket`) and real TLS (`ssl`, backed by .NET's SslStream).
#
# Prerequisite (from the repo root, so ./site-packages matches where `run` looks for it):
#   pysharp install requests
#   pysharp install urllib3
#   pysharp install certifi
#   pysharp install idna
#   pysharp install charset_normalizer
#
# Usage:  pysharp run samples/requests_demo.py

import requests

print("--- GET with query params ---")
r = requests.get("https://httpbin.org/get", params={"scenario": "4", "lib": "requests"}, timeout=10)
print("status:", r.status_code, r.reason)
print("url   :", r.url)
data = r.json()
print("args  :", data["args"])

print("\n--- POST with a real JSON body ---")
r = requests.post(
    "https://httpbin.org/post",
    json={"project": "PySharp", "scenario": 4},
    timeout=10,
)
print("status:", r.status_code)
print("echoed:", r.json()["json"])

print("\n--- a Session: real redirect-following + real cookie persistence ---")
s = requests.Session()
r = s.get("https://httpbin.org/redirect/2", timeout=10)
print("final status :", r.status_code, "after", len(r.history), "redirect(s)")
print("final url    :", r.url)

s.get("https://httpbin.org/cookies/set/pysharp/works", timeout=10)
r = s.get("https://httpbin.org/cookies", timeout=10)
print("session cookie round-trip:", r.json()["cookies"])

print("\n--- a real, catchable requests exception ---")
try:
    requests.get("https://httpbin.org/status/404", timeout=10).raise_for_status()
except requests.exceptions.HTTPError as e:
    print("HTTPError caught as expected:", e)

print("\nrequests demo: ok")
