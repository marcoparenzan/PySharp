# PySharp — project activity log

Real, git-derived metrics only: commit timestamps and diff stats. **No token/cost estimates** —
that data isn't observable from within a conversation (no tool exposes a usage counter); check the
Claude Code UI's own session indicator or the Anthropic Console for that instead.

Started 2026-08-11 (project itself started 2026-07-24 — earlier history isn't backfilled here by
default; ask if you want it computed from `git log`).

## Format

One entry per calendar day with activity. Within a day, commits are grouped into **blocks**: a gap
of more than ~30 minutes between two consecutive commits splits a new block (an idle gap — sleep, a
break — isn't "active" time and isn't counted). A block's duration is its last commit timestamp
minus its first. `+ins/-del` and file-touch counts are `git log --shortstat`, summed across the
commits listed.

---

## 2026-08-11

**20 commits · +8304/-642 across 104 file-touches (summed across commits) · ~5h07m active**

### Block 1 — 00:02–01:46 (1h44m)

Scenarios 6-7 (ROADMAP.md): real hand-rolled MQTT 3.1.1 and AMQP 0-9-1 brokers, each verified live
against a real, unmodified PyPI client (paho-mqtt, pika). 8 real interpreter gaps found and fixed
for AMQP (`ast`/`numbers`/`heapq` new modules; `ABCMeta` 3-arg call, `defaultdict` mixins,
`bytes.split()`, `OSError.errno`/`.strerror`, `select`/`getsockopt` for raw fds). Full ROADMAP
scenario backlog (4-9) completed.

- `bbc0b26` scenario 8 (filesystem API) — carried over from the prior session, first commit of today
- `b5b26e9` scenario 6 (MQTT broker)
- `dee14a7` scenario 7 (AMQP broker) + backlog-complete note

### Block 2 — 08:50–11:34 (2h44m)

`NUMPY_PLAN.md` Phases 0 through 9 — a C# `numpy`-shaped shim: skeleton/import, `ndarray` core
(strides/attributes/repr), real construction (`array`/`zeros`/`arange`/`linspace`/...), indexing/
slicing (copies), elementwise ops + real broadcasting, `bool` dtype + comparisons + masking (needed
a real `Interp.cs` core fix so `a < b` returns the dunder's raw result instead of collapsing to
bool), reductions (`sum`/`mean`/`std`/`argmin`/`cumsum`/...), ufuncs (`sqrt`/`exp`/trig/`round`/...),
shape manipulation (`reshape`/`ravel`/`transpose`/`concatenate`/`stack`/`np.newaxis`/...), a real
`int64` dtype with arithmetic promotion (`float64`>`int64`>`bool`), `dtype=`/`astype`, Python-sign
`//`/`%`, and a unified bitwise `& | ^ ~` mechanism. Also fixed a real `PyOps.PyEquals` bug (NaN
incorrectly equaling itself via a reference-identity shortcut) and a latent `Concatenate` dtype bug.

- `0326164` roadmap cross-reference to NUMPY_PLAN.md
- `6046030` Phase 0 (skeleton)
- `214cbdf` Phase 1 (ndarray core)
- `d2c3ef7` Phase 2 (construction)
- `42e0394` Phase 3 (indexing/slicing)
- `479badb` Phase 4 (elementwise/broadcasting)
- `0a81654` Phase 5 (bool/comparisons/masking + core fix)
- `3baf944` Phase 6 (reductions)
- `dffa64f` NUMPY_PLAN.md doc update (reference sources, rejected alternatives)
- `184b379` Phase 7 (ufuncs + PyOps.PyEquals fix)
- `a5c44d6` project update
- `bb42adf` Phase 8 (shape manipulation)
- `36fe6bd` Phase 9 (dtypes & promotion)

### Block 3 — 12:43–13:06 (22m)

`NUMPY_PLAN.md` Phases 10-11 — basic linear algebra: `dot`/`matmul`/`@` for 1-D/2-D operands sharing
one `MatMul` core, `numpy.linalg` submodule (`norm`), `trace`/`diagonal` (N-D batched matmul and
`inv`/`solve`/`det` deliberately deferred and documented); interop & conveniences: `tolist()`,
`float`/`int`/`bool` coercion for size-1 arrays, seedable `np.random`, a two-way `.NET` array bridge.

- `46de024` Phase 10 (linear algebra)
- `18c2c51` Phase 11 (interop & conveniences)

### Block 4 — 14:14–14:31 (17m)

`NUMPY_PLAN.md` Phase 12 (views, performance, polish) — basic indexing/`.T`/`reshape`/`ravel`/
`expand_dims`/`squeeze` became real strided views (5 pre-existing latent bugs found and fixed along
the way), a measured ~7.4x contiguous-`float64` fast path, `samples/numpy_demo.py`, a numpy-quickstart
conformance sweep — closing out all 12 NUMPY_PLAN.md phases. Then `FASTAPI_PLAN.md` Phase 5 (docs):
scenario 2 flipped to done in ROADMAP.md (re-verified the underlying work was still green first —
1228/1228, 45/45 FastAPI tests — before documenting it as done), a RELEASE_NOTES entry, and several
stale "in progress"/"missing" claims in README.md fixed.

- `c63dd21` numpy Phase 12 (views/perf/polish) — 18 new tests
- `f53cd93` FastAPI Phase 5 (docs)
