# HTTP client — scenario 4 — a step-by-step plan

**Goal.** Get a real, unmodified `requests` (→ `urllib3` → this project's own `http.client`)
running real HTTPS requests end-to-end under PySharp, following the same scenario-driven method as
everywhere else: real script, real gap, real fix, real test, repeat. See ROADMAP.md's "Method:
scenario-driven development".

**Status: ✅ done (2026-08-10).** `import requests` works; real `GET`/`POST` with query params and
JSON bodies, `Session`-based redirect-following and cookie persistence, and
`response.raise_for_status()` all verified live against `https://httpbin.org`. Full blow-by-blow
below.

---

## Key architecture decision: build a real `http.client`, not a `requests`-shaped shim

The original plan (per ROADMAP.md) was cautious: "full `http.client`/`urllib.request`, ... maybe
pure `requests`". The first real probe (`import requests`) immediately needed `urllib3`, and
reading `urllib3/connection.py` showed why a lightweight shim wouldn't work: urllib3's own
`HTTPConnection`/`HTTPSConnection` are real Python subclasses of `http.client.HTTPConnection`/
`HTTPSConnection`, calling `super().putrequest()`/`super().getresponse()` directly and managing
`self.sock` (a real socket object, sometimes swapped in from outside via urllib3's own
`_new_conn()`, built on the real `socket` module) itself. A shim that only *behaved like* requests
at the Python-visible surface would break the moment urllib3's own code called `super()` into it.

So `http.client.HTTPConnection`/`HTTPSConnection`/`HTTPResponse` were built for real:
[HttpClientModule.cs](src/PySharpLib/Modules/HttpClientModule.cs) — subclassable
`putrequest`/`putheader`/`endheaders`/`send`/`request`/`getresponse`, all operating on `self.sock`
via the *existing* `socket`/`ssl` modules' own Python-visible objects (`self.sock.sendall(...)`/
`self.sock.recv(...)`), not a separate raw-socket layer — so it works identically whether `self.sock`
was set by this module's own `.connect()` or swapped in by urllib3's `_new_conn()`. A small internal
`SocketReader` (buffered line/chunk reading over repeated `recv()` calls) backs `getresponse()`'s
status-line/header/body parsing, including real chunked Transfer-Encoding decoding.

`HTTPMessage` reuses `email.message.Message` directly — real CPython's own `http.client.HTTPMessage`
literally *is* `email.message.Message`, not a separate implementation.

---

## Verification method

No local Python interpreter is available, so every fix below was verified one of two ways:

1. **Live, against a real server** — `pysharp install requests`/`urllib3`/`certifi`/`idna`/
   `charset_normalizer` into a scratch site-packages, then `pysharp run` a probe script making a
   real `requests.get()`/`.post()` call against `https://httpbin.org`, reading the *actual* Python
   traceback each real error produced to find the next real gap — the same "run it, see what
   breaks, fix, repeat" loop used for every other scenario.
2. **Reading the installed package's own source** — once a traceback pointed at a specific
   function (`urllib3/util/url.py`'s `parse_url`, `requests/models.py`'s `prepare_url`, ...), the
   real site-packages source (not documentation, not memory) was read to find the *exact* real
   CPython/stdlib feature being exercised, before deciding how to implement it.

25 distinct real gaps were found and fixed this way (grouped below by area). Every fix that touched
shared/core interpreter code (not scoped to one new module) was verified against the **full existing
1065-test suite** immediately after, before moving to the next gap — some of these turned out to be
real, general interpreter bugs unrelated to HTTP specifically, just never exercised by any prior
scenario.

---

## What was found and fixed

### The `http.client` module itself
- **`Microsoft.Data.Sqlite`-style discovery, but for TCP/TLS wiring, not a driver.** No new NuGet
  package needed — `http.client` is built entirely on this project's own already-real `socket`/`ssl`
  modules.
- **`socket.socket(family, type)` ignored its own `family`/`type` arguments**, always creating an
  IPv4/TCP socket regardless of what was asked for. Found live: `urllib3.util.connection`'s own
  `_has_ipv6("::1")` probe (`socket.socket(socket.AF_INET6)` then `.bind(("::1", 0))`) crashed with
  a raw, uncaught .NET `SocketException` ("An address incompatible with the requested protocol was
  used") instead of the real `OSError` Python code (`_has_ipv6`'s own `try/except Exception:`)
  expected to catch and handle gracefully. Two real fixes: (a) `socket.socket()` now honors
  `AF_INET`/`AF_INET6` and `SOCK_STREAM`/`SOCK_DGRAM`; (b) `socket.bind()` (which had no exception
  translation at all) now routes through the same `SocketModule.Translate` every other socket
  operation already used.
- **`socket.getdefaulttimeout`/`setdefaulttimeout`** didn't exist (`urllib3.util.timeout` imports
  the getter at module load time).
- **A real `pyodbc`-adjacent gap, but for real TLS certs this time**: `SSLSocket.getpeercert()`
  always returned an empty dict. `SslStream` itself already validates the certificate/hostname
  during the handshake, but urllib3 does its *own* independent hostname check
  (`ssl_match_hostname.py`, a vendored backport) against `getpeercert()`'s `subjectAltName` — an
  empty dict made every real HTTPS request fail with "empty or no certificate". Fixed with a real
  extraction via .NET's `X509SubjectAlternativeNameExtension.EnumerateDnsNames()`.
- **`ssl.SSLContext` was missing several real attributes** urllib3's own `create_urllib3_context`
  reads/writes unconditionally: `OPENSSL_VERSION`(+`_INFO`/`_NUMBER`), `TLSVersion` (a real, if
  plain, class — not a full IntEnum, nothing needs iteration/isinstance on it), `OP_NO_COMPRESSION`/
  `OP_NO_TICKET`/`VERIFY_X509_PARTIAL_CHAIN`/`VERIFY_X509_STRICT`/`HAS_NEVER_CHECK_COMMON_NAME`,
  and per-instance `options`/`verify_flags`/`minimum_version`/`maximum_version` defaults.
- **`sys.audit`/`sys.addaudithook`** (PEP 578) didn't exist — urllib3 calls `sys.audit(...)`
  unconditionally on every new connection. Real no-ops (nothing registers a hook).

### Real stdlib gaps (found via `email.errors`, `email.message`, `codecs`, `zipfile`, `calendar`, ...)
- **`email.errors`** (the real MIME-parsing exception/"defect" hierarchy) didn't exist at all —
  urllib3's `exceptions.py` imports `MessageDefect` unconditionally.
- **`email.message.Message`** was missing real `keys()`/`items()`/`values()`/`__contains__`/
  `__len__`/`__iter__` — needed both directly (urllib3's `HTTPHeaderDict(headers)` constructor uses
  the real "has a `keys()` method → treat as a mapping" protocol check) and via `http.client`'s own
  reuse of `Message` as `HTTPMessage`.
- **`codecs.lookup(name)` returned a bespoke, non-tuple `CodecInfo`-like object** — real CPython's
  `CodecInfo` is a `tuple` subclass, and real code indexes into it directly
  (`codecs.lookup("utf-8")[3]` for the stream writer, in urllib3's `filepost.py`). Rebuilt as a real
  4-tuple-shaped object (`encode`/`decode`/`streamreader`/`streamwriter`, all real, functioning
  implementations, not placeholders) plus attribute access.
- **`zipfile`** didn't exist (a real, if read-only-scoped, module now backed directly by
  `System.IO.Compression.ZipArchive`) — `requests/utils.py`'s `extract_zipped_paths` imports it
  unconditionally, even though nothing in the reachable path actually calls it for a normal request.
- **`calendar.timegm`** and **`time.strptime`** didn't exist — `requests/cookies.py`'s real
  `expires=` cookie-attribute parsing needs both. `strptime` translates common Python
  strftime-style directives to .NET custom date-format tokens (with individual-character literal
  escaping, so a literal "GMT" in a format string isn't misread as .NET's own month/AM-PM
  specifiers).
- **`http.cookiejar.CookiePolicy`** didn't exist, and **`CookieJar` had no real `_policy` attribute**
  — real requests' own `RequestsCookieJar.get_policy()` reads `self._policy` directly (bypassing
  this module's `get_policy()` method entirely), matching real CPython's own
  `CookieJar.__init__(self, policy=None)`.
- **`os.path` wasn't importable as its own dotted module** (`import os.path` directly, not just
  reached via `os.path` after `import os`) — found via `requests/certs.py`.
- **`encodings`/`encodings.aliases`/`encodings.idna`** (the real codec-registry package) didn't
  exist. `encodings.aliases.aliases` is a practical subset (~50 common real aliases, not CPython's
  full ~600-entry table) covering realistic HTTP `charset=` values. `encodings.idna` is real,
  backed by .NET's `System.Globalization.IdnMapping` — and while implementing it, the same real
  IDNA support was wired into `str.encode('idna')`/`bytes.decode('idna')` directly (found via
  urllib3's own `host.encode("idna")`), which previously would have raised the wrong exception type
  (`ModuleNotFoundError` instead of a catchable `UnicodeError`) had it ever actually run.
- **`json.JSONDecodeError`** only ever carried a single pre-formatted string in `.args` — real
  CPython's real `.msg`/`.doc`/`.pos`/`.lineno`/`.colno` attributes didn't exist. Found via real
  requests' own `models.py` (`raise RequestsJSONDecodeError(e.msg, e.doc, e.pos)`).
- **`binascii.hexlify`/`unhexlify`**, **`itertools.takewhile`/`dropwhile`**, **`random`** (the whole
  module — real, `System.Random`-backed, not bit-exact with CPython's Mersenne Twister for a given
  seed, which nothing in scope needs), **`importlib.metadata.version`/`PackageNotFoundError`** (real
  `.dist-info/METADATA` scanning over the same search paths `import` itself uses), **`IOError`/
  `EnvironmentError`** (real CPython aliases for `OSError`) — each didn't exist at all; found one
  `ModuleNotFoundError`/`NameError` at a time down the real `import requests` chain.
- **`urllib.parse`**: `urlparse()` returned a plain tuple instead of a real `ParseResult` (attribute
  *and* index access, matching the already-correct `SplitResult`) — found via real requests' own
  `cookies.py` (`urlparse(url).scheme`). Rewritten to reuse the same `SplitUrl()` algorithm already
  backing `urlsplit()`/`urljoin()` rather than keep a second, less-thorough ad-hoc parser.
  `urlunparse`/`urlunsplit` (missing entirely) were added, but initially cast every component to
  `string` unconditionally — real CPython's own implementation only ever *tests* a falsy component
  before conditionally appending it, so a real `None` fragment/query (urllib3's own `Url` namedtuple
  leaves unset fields as `None`) crashed instead of silently contributing nothing. `urlencode()`
  quoted a `bytes` key/value via Python's `str(bytes)` *repr* ("b'foo'") instead of its raw content —
  found live as the actual root cause of `params={...}` silently vanishing from request URLs.
  `urldefrag`, `urllib.request.getproxies_environment`/`proxy_bypass`/`proxy_bypass_environment`
  (real `NO_PROXY`-environment-variable-driven, portable subset — not the Windows-registry/macOS
  paths real CPython also has) were added; `getproxies()` was upgraded from an always-empty stub to
  real environment-variable reading.

### Real, general interpreter bugs (not new-module gaps — pre-existing, just never exercised)
- **`str(bytes_obj, encoding, errors='strict')`** — the real *second* overload of the `str()`
  builtin (decode bytes, distinct from the single-arg repr-ish form) — was never implemented; only
  `args[0]` was ever read. Found live as the root cause of `response.text` silently containing the
  Python *repr* of the raw response bytes ("b'{...}'") instead of the decoded JSON text, itself
  masking a *second* bug (`json.loads` on that garbled text was somehow still reachable) until this
  was fixed. Found via real requests' own `models.py` (`Response.text`: `str(self.content, encoding
  or "utf-8", errors="replace")`).
- **`hasattr(builtin_container, "__iter__"/"__len__"/"__contains__")` always returned False** for
  every builtin dict/list/tuple/set/bytes/etc. — the real iteration/len/contains protocols were
  always implemented (`for x in d`, `len(d)`, `x in d` all work), just never *lookupable* as real
  attributes, since no per-type method table had entries for these names. A universal fallback in
  `TypeMethods.TryGetBuiltinAttr` now answers `hasattr`/`getattr` truthfully for these three names on
  any builtin value that structurally supports the operation. Found live via real requests' own
  `models.py` (`_encode_params`: `hasattr(data, "__iter__")` on a plain `dict`, deciding whether to
  urlencode it) — this is exactly the kind of duck-typing idiom real-world code leans on constantly.
- **`typing.Protocol` + `@runtime_checkable` had no real structural `isinstance()` support** —
  `runtime_checkable` was a pure no-op identity decorator. Real requests' own `_types.py` defines
  `@runtime_checkable class SupportsItems(Protocol[...]): def items(self)...`, and
  `requests.utils.to_key_val_list` uses `isinstance(value, SupportsItems)` to decide whether to call
  `.items()` or iterate a value as raw (key, value) pairs — always returning `False` silently
  produced wrong (and confusingly *not immediately crashing*) results rather than an error. Now
  `runtime_checkable` tags the class with its own directly-declared method names, and
  `Builtins.IsInstance` checks structurally (does the target have every one of those methods —
  checked via the instance's own class MRO for a `PyInstance`, or the matching builtin Table dict
  for a raw dict/list/etc.) instead of the normal nominal class-hierarchy check for that one class.
- **`collections.abc.Mapping`/`MutableMapping` were missing real `update`/`__contains__`/`keys`/
  `items`/`values` mixin methods** — only `get`/`pop`/`popitem`/`setdefault`/`clear` existed. Found
  live via real requests' own `structures.py`
  (`CaseInsensitiveDict(MutableMapping)`, whose `__init__` calls the inherited `self.update(data,
  **kwargs)`, and `requests.sessions.merge_setting` calling `.items()` on one directly).
- **`class Foo(typing.NamedTuple): ...` inherited a `__new__` meant only for the *other*, functional
  calling convention** (`NamedTuple("Name", [...])`) — since both jobs share the same underlying
  `NamedTuple` `PyClass` object, and only the functional-syntax `__new__` was ever registered on it.
  Calling `Foo(a=1, b=2)` (no positional args) hit that same `__new__`, immediately indexed a
  nonexistent `a[1]` expecting a typename string, and surfaced as a baffling "NamedTuple.__new__()
  missing required argument". Found live via real urllib3's own `poolmanager.py`
  (`class PoolKey(typing.NamedTuple): ...` then `PoolKey(**context)`) — real CPython's own
  `class Foo(NamedTuple): ...` never actually keeps `NamedTuple` as a real runtime base for exactly
  this reason (`Foo.__bases__` is really `(tuple,)`); `ConvertToNamedTuple` now explicitly
  re-overrides `__new__` back to ordinary blank-instance construction on every class it converts,
  shadowing the inherited one.
- **`str.isascii()`** didn't exist. Found via real urllib3's own `unicode_is_ascii` helper.
- **Every `PyModule` had no `__doc__` global bound at all** (not even `None`) — referencing the bare
  name `__doc__` at module scope raised `NameError`. Found via real requests' own `models.py`
  module-level code. Now defaults to `None` in `PyModule`'s constructor (the same "real default
  present, not byte-identical docstring capture" simplification already accepted for class
  `__doc__` elsewhere in this project).
- **`__import__` (the real builtin the `import` statement itself desugars to) didn't exist.** Found
  via real requests' own `packages.py` (`locals()[package] = __import__(package)`, a
  backwards-compatibility shim aliasing `requests.packages.urllib3` to the real top-level
  `urllib3`). Scoped to absolute imports with a plain (no-dot) name and no `fromlist` — every real
  reachable call site.
- **`HTTPConnection.__init__`'s `host` argument was positional-only** — real urllib3 calls
  `super().__init__(host=host, port=port, ...)` entirely by keyword.

**Deliberately out of scope for v1** (practical-subset philosophy, matching every other module in
this project): HTTP proxy tunneling (`set_tunnel` raises `NotImplementedError`), HTTP/2 (urllib3's
own optional `h2`/`hpack` extras genuinely aren't installed, so `importlib.metadata.version("h2")`
correctly raising `PackageNotFoundError` is real, accurate behavior, not a gap), the Windows-registry/
macOS-native paths of `urllib.request.proxy_bypass`, `bytes.decode(..., errors=...)` beyond the
default strict mode (nothing reachable hit a malformed byte sequence), `doseq=True` sequence-value
expansion in `urlencode()`.

---

## Deliverables

- **Module**: [HttpClientModule.cs](src/PySharpLib/Modules/HttpClientModule.cs) (`http.client`,
  ~600 lines: HTTPConnection/HTTPSConnection/HTTPResponse/SocketReader/exception hierarchy), plus
  the dozen-plus smaller real stdlib modules/fixes listed above, spread across `EmailModule.cs`,
  `CodecsModule.cs`, `ZipfileModule.cs` (new), `CalendarModule.cs` (new),
  `EncodingsAliasesModule.cs`/`EncodingsIdnaModule.cs` (new), `HttpCookiejarModule.cs`,
  `UrllibModule.cs`, `SocketModule.cs`, `SslModule.cs`, `JsonModule.cs`, `BinasciiModule.cs`,
  `RandomModule.cs` (new), `ImportlibMetadataModule.cs` (new), `ItertoolsModule.cs`,
  `TimeModule.cs`, `Builtins.cs`, `TypeMethods.cs`, `CollectionsModule.cs`, `MiscModules.cs`,
  `Interp.cs`, `Env.cs`.
- **Sample**: [samples/requests_demo.py](samples/requests_demo.py) — real GET with query params,
  real POST with a JSON body, a `Session` following real redirects and persisting real cookies, and
  a caught `requests.exceptions.HTTPError` from `raise_for_status()` — every line run live against
  `https://httpbin.org`, output verified by hand before trusting it.
- **Tests**: [HttpClientTests.cs](src/PySharp.Tests/M18_Http/HttpClientTests.cs), 9 tests — a real
  local TCP server (hand-rolled in the test file, no external dependency) plays the far end, so
  these exercise this project's own request/response framing (status/headers/body, chunked
  decoding, low-level `putrequest`/`putheader`/`endheaders`/`send`, keep-alive connection reuse,
  `RemoteDisconnected` on an unexpected clean close, case-insensitive header lookup) without
  re-testing `requests`/`urllib3` themselves — those were verified live, separately, above.
- Full suite green at **1072/1072**, confirmed via 7 consecutive full-suite runs (touching this much
  shared/core interpreter code — `str()`, `isinstance()`, attribute dispatch, `NamedTuple`,
  `Mapping`/`MutableMapping` — warranted more than the usual handful).
