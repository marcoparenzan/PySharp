// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

namespace PySharp.Tests.M6_Stdlib;

/// <summary>Real, general interpreter/stdlib gaps found while probing real SQLAlchemy's own
/// `postgresql+psycopg2://` dialect against a live Azure Postgres server (ORM_PLAN.md's Postgres
/// phase) — each independently reachable by other real packages too, not SQLAlchemy/Postgres-
/// specific. The full round trip (connect, real DDL, `session.add()`/`.commit()` — a real INSERT
/// flush including the `insertmanyvalues` machinery — and `session.execute(select(...))`) is now
/// verified end to end against the live server; these are the real fixes made getting there.
/// </summary>
public class OrmPostgresGapsTests
{
    private static string Run(string body) => Py.Run(body).TrimEnd('\n');

    // Real, general, and severe interpreter bug: `SomeClass.__hash__` (unbound, class-level
    // attribute access) returned a closure permanently bound to hash `SomeClass` itself, ignoring
    // whatever argument it was actually called with. This is *correct* for `hash(SomeClass)` (which
    // really does call `type(SomeClass).__hash__(SomeClass)`, landing here with `SomeClass` as the
    // argument) but silently wrong for the equally real `__hash__ = SomeBase.__hash__` class-body
    // idiom (reusing a base's hash implementation on a *different*, unrelated class) — every
    // instance of the class doing the reassignment hashed identically (to the value of the class
    // where the lookup first happened), corrupting anything relying on real per-instance hash
    // identity: set/dict membership, and any `x in some_collection` check backed by `__eq__`
    // returning a non-bool object whose own `__bool__` falls back to hash comparison (a real,
    // documented CPython idiom precisely so such objects can be membership-checked without an
    // ambient boolean-context error). Found live via real SQLAlchemy's own `sql/operators.py`
    // `__hash__ = Operators.__hash__` (reused by every `ColumnOperators` subclass, including
    // `Column`) — every real `Column` instance hashed identically, so `col not in
    // some_tuple_of_other_columns` always silently reported "found", corrupting the sentinel-column
    // filtering deep inside `insertmanyvalues` batch compilation and manifesting five call frames
    // away as a `ZeroDivisionError` (an empty parameter list divided a batch-size computation) —
    // this was the wall that finally blocked a real end-to-end `session.commit()` against Postgres.
    [Fact]
    public void A_class_hash_reassigned_from_a_base_class_hashes_each_instance_independently()
        => Assert.Equal("True\nTrue\nFalse", Run("""
            class Base:
                pass

            class Derived:
                __hash__ = Base.__hash__

            a = Derived()
            b = Derived()
            print(hash(a) == hash(a))
            print(hash(a) != hash(b))
            print(a in (b,))
            """));

    // Real, general concurrency bug: `threading.Condition` wrapped .NET's `Monitor` directly — a
    // genuinely OS-thread-affine construct (the same real thread must Enter/Exit/Wait/Pulse it).
    // PySharp's own execution model runs every generator/coroutine body on its own dedicated OS
    // thread, so a single *logical* Python thread's execution routinely spans several real OS
    // threads over its lifetime — found live via real SQLAlchemy's own connection pool
    // (`sqlalchemy.pool`, a real `threading.Condition`-backed queue), reached once a dialect
    // defaulting to `QueuePool` (unlike sqlite3's own default) actually blocked on it. Rewritten to
    // the same algorithm real CPython's own `Condition` uses: a reentrant lock plus an explicit
    // per-waiter list of single-token semaphores (no thread affinity at all).
    [Fact]
    public void Condition_notify_and_wait_work_correctly_across_different_worker_threads()
        => Assert.Equal("producer done\nconsumer got: 42", Run("""
            import threading

            cond = threading.Condition()
            result = []

            def consumer():
                with cond:
                    while not result:
                        cond.wait()
                    print("consumer got:", result[0])

            def producer():
                import time
                time.sleep(0.05)
                with cond:
                    result.append(42)
                    cond.notify()
                print("producer done")

            t1 = threading.Thread(target=consumer)
            t2 = threading.Thread(target=producer)
            t1.start()
            t2.start()
            t1.join()
            t2.join()
            """));

    // Real, general interpreter bug: zero-arg `super()`'s implicit `__class__` cell was only ever
    // set by post-hoc walking the finished class namespace for PySharp's own known wrapper shapes
    // (staticmethod/classmethod/property) — an arbitrary *third-party* decorator (e.g. a library's
    // own `@memoized_property`) hides the underlying function from that walk entirely, so
    // `super()` inside it raised "no __class__ cell found". Fixed generally: every function is now
    // recorded at the moment its `def` statement runs inside a class body (before any decorator
    // sees it) — real CPython's own `__class__` cell is a property of the function object itself,
    // baked in at definition time, regardless of later decoration. Found live via real SQLAlchemy's
    // own `sql/type_api.py` `_static_cache_key` (wrapped by `@util.memoized_property`, a real
    // third-party descriptor class), reachable from any `postgresql+psycopg2://` engine.
    [Fact]
    public void Zero_arg_super_works_inside_a_function_wrapped_by_a_third_party_decorator()
        => Assert.Equal("24", Run("""
            class custom_property:
                def __init__(self, func):
                    self.func = func
                def __get__(self, obj, owner=None):
                    if obj is None:
                        return self
                    return self.func(obj)

            class Base:
                def value(self):
                    return 12

            class Derived(Base):
                @custom_property
                def value(self):
                    return super().value() * 2

            print(Derived().value)
            """));

    // Real CPython: `%`-formatting is inherited by any str subclass with no override needed (real
    // code never redefines `__mod__`) — found via real SQLAlchemy's own `sql/elements.py`
    // `_anonymous_label` (a `quoted_name`/str subclass) doing `"%%(%d %s)s" % (seed, body)`-style
    // formatting on `self`.
    [Fact]
    public void Percent_formatting_works_on_a_real_str_subclass_instance()
        => Assert.Equal("(1 hi)", Run("""
            class MyLabel(str):
                pass

            label = MyLabel("(%d %s)")
            print(label % (1, "hi"))
            """));

    // Real CPython: a `%(name)s`-shaped placeholder accepts any mapping-*protocol* right-hand side
    // (anything supporting `__getitem__`), not just a literal `dict` — found via real SQLAlchemy's
    // own `sql/cache_key.py` `self.key % anon_map`, where `anon_map` is a custom class with its own
    // `__getitem__`, not a `dict`.
    [Fact]
    public void Percent_formatting_accepts_a_mapping_protocol_object_not_just_a_literal_dict()
        => Assert.Equal("value is 42", Run("""
            class FakeMapping:
                def __getitem__(self, key):
                    return 42

            print("value is %(x)s" % FakeMapping())
            """));

    [Fact]
    public void Functools_singledispatch_dispatches_on_runtime_type_including_stacked_registrations()
        => Assert.Equal("default: 1\nlist: [1, 2]\nbytesish: b'x'\nbytesish: bytearray(b'y')\nnone!", Run("""
            from functools import singledispatch

            @singledispatch
            def f(val):
                return f"default: {val}"

            @f.register
            def _(val: list):
                return f"list: {val}"

            @f.register(bytes)
            @f.register(bytearray)
            def _(val):
                return f"bytesish: {val!r}"

            @f.register
            def _(val: None):
                return "none!"

            print(f(1))
            print(f([1, 2]))
            print(f(b"x"))
            print(f(bytearray(b"y")))
            print(f(None))
            """));

    [Fact]
    public void Ipaddress_ip_network_and_ip_interface_pick_the_right_version()
        => Assert.Equal("192.168.1.0/24 4\n::1/128 6\n192.168.1.5/24 4", Run("""
            import ipaddress
            n = ipaddress.ip_network("192.168.1.0/24")
            print(n, n.version)
            n6 = ipaddress.ip_network("::1/128")
            print(n6, n6.version)
            i = ipaddress.ip_interface("192.168.1.5/24")
            print(i, i.version)
            """));

    [Fact]
    public void Calendar_monthrange_matches_real_cpython()
        // Real CPython: calendar.monthrange(2026, 8) -> (5, 31) (August 2026 starts on a Saturday,
        // weekday 5 with Monday=0, and has 31 days).
        => Assert.Equal("(5, 31)", Run("""
            import calendar
            print(calendar.monthrange(2026, 8))
            """));

    [Fact]
    public void Future_module_exposes_the_real_full_feature_list()
        => Assert.Equal("ok", Run("""
            from __future__ import unicode_literals, nested_scopes, generators, absolute_import, with_statement, barry_as_FLUFL
            print("ok")
            """));

    [Fact]
    public void Types_moduletype_has_a_real_constructor_settable_via_subclassing()
        => Assert.Equal("<module 'mymod'>\nmymod", Run("""
            import types

            class MyModule(types.ModuleType):
                pass

            m = MyModule("mymod")
            print(m)
            print(m.__name__)
            """));

    [Fact]
    public void Itertools_product_computes_the_real_cartesian_product_including_repeat()
        => Assert.Equal("[(1, 'a'), (1, 'b'), (2, 'a'), (2, 'b')]\n[(0, 0), (0, 1), (1, 0), (1, 1)]", Run("""
            import itertools
            print(list(itertools.product([1, 2], ["a", "b"])))
            print(list(itertools.product([0, 1], repeat=2)))
            """));

    [Fact]
    public void Deque_extendleft_prepends_each_element_in_reverse_order()
        => Assert.Equal("deque([6, 5, 4, 1, 2, 3])", Run("""
            from collections import deque
            d = deque([1, 2, 3])
            d.extendleft([4, 5, 6])
            print(d)
            """));

    // Real CPython: `spec_from_loader(name, loader, origin=None)` builds a real (if minimal)
    // ModuleSpec-shaped object from an explicit loader. Found via real `six`'s own
    // `_SixMetaPathImporter` (`if PY34: from importlib.util import spec_from_loader`), reachable
    // once installed as a transitive dependency.
    [Fact]
    public void Importlib_util_spec_from_loader_builds_a_real_spec_object()
        => Assert.Equal("thing.mod None", Run("""
            import importlib.util

            class L:
                pass

            spec = importlib.util.spec_from_loader("thing.mod", L())
            print(spec.name, spec.origin)
            """));

    // Real PEP 302 legacy meta-path-finder protocol (find_module/load_module), a real fallback in
    // the core importer for a name that isn't backed by a real file or a registered builtin —
    // consulted for real, in order, before raising ModuleNotFoundError. Found via real `six`'s own
    // `_SixMetaPathImporter` (`six.moves` and its submodules registered exactly this way) — note
    // this fallback only completes the load when `load_module()` returns a real PyModule (as any
    // finder returning an *already-imported* real module — the common "alias into an existing
    // module" pattern — naturally does); a finder building a *new* module via `types.ModuleType(...)`
    // is a separate, deeper gap (that constructor doesn't yet produce this interpreter's native
    // module representation) not covered by this fix.
    [Fact]
    public void A_custom_sys_meta_path_finder_is_consulted_for_an_otherwise_unresolvable_name()
        => Assert.Equal("<module 're'>", Run("""
            import sys

            class MyFinder:
                def find_module(self, fullname, path=None):
                    return self if fullname == "myvirtualpkg" else None

                def load_module(self, fullname):
                    import re
                    sys.modules[fullname] = re
                    return re

            sys.meta_path.append(MyFinder())

            import myvirtualpkg
            print(myvirtualpkg)
            """));
}
