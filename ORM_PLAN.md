# SQLAlchemy support — a probe-driven plan

**Goal.** A real, generic ORM beyond Django's own (which needs the full Django framework). Real
**SQLAlchemy** (2.0.51, `py3-none-any` wheel, installs cleanly via `pysharp install sqlalchemy` — the
core/ORM has no C-extension dependency by default) — real ORM + Core query builder, dialect-based, so
the same code targets SQLite (already a real C# DB-API module in this project) and, later, Postgres
via the pure-Python `pg8000` driver (avoiding `psycopg2`'s C extension).

**Method**: same as FASTAPI_PLAN.md/NUMPY_PLAN.md/CTYPES_PLAN.md — probe with real, unmodified
SQLAlchemy against a real SQLite database, root-cause each real gap, fix it generally (not
SQLAlchemy-specific hacks), write a regression test per fix, keep the full suite green.

## Phase 0 — groundwork: `import sqlalchemy` in progress (not yet complete)

Real gaps found and fixed so far, each with its own root cause (not SQLAlchemy-specific patches),
found via probing `import sqlalchemy` round by round:

- [x] `functools.update_wrapper` didn't exist at all (only the `@wraps` decorator form did) — added
  as a real, general function (works for both `PyFunction` and `PyBuiltinFunction` on either side),
  and `wraps` itself rewritten to share the same implementation instead of its own narrower one.
- [x] `typing.ValuesView`/`KeysView`/`ItemsView`/`MappingView` didn't exist — added as placeholders
  (same treatment as the rest of `typing`'s non-deeply-wired names).
- [x] `sysconfig` module didn't exist at all — added a minimal one (just `get_config_var`, always
  `None`, which is what real CPython itself returns for an unset variable — correct here since this
  interpreter has no such build-time flags at all).
- [x] `aiter`/`anext` builtins didn't exist — added as real, general builtins (`aiter(x)` →
  `type(x).__aiter__(x)`; `anext(x)` (1-arg only — the 2-arg default form is itself async in real
  CPython and isn't implemented yet, a documented gap) → `type(x).__anext__(x)`, letting the
  caller's own `await` do the actual suspension, exactly like calling any `async def` without
  awaiting it).
- [x] `contextlib.nullcontext` didn't exist — added as a real do-nothing context manager, both sync
  and async (`__enter__`/`__exit__`/`__aenter__`/`__aexit__`).
- [x] `itertools.filterfalse` didn't exist — added.
- [x] **A real, general interpreter gap**: `f.__init__` (a plain function's own `__init__`) raised
  `AttributeError` — every real object inherits `object.__init__`, even a function, but the
  attribute-lookup fallback that already existed for *classes* (`SomeClass.__init__` when not
  overridden) didn't extend to plain function/builtin-function *instances*. Fixed by adding the same
  `object.__init__` fallback to both the `PyFunction` and `PyBuiltinFunction` attribute-lookup cases.
- [x] `PendingDeprecationWarning` and the rest of real CPython's stdlib `Warning` subclasses
  (`SyntaxWarning`, `FutureWarning`, `ImportWarning`, `UnicodeWarning`, `BytesWarning`,
  `ResourceWarning`, `EncodingWarning`) didn't exist — added together.
- [x] **A real, general parser gap (PEP 701, Python 3.12)**: a triple-quoted f-string's `{...}`
  expression spanning multiple physical lines with no enclosing parens of its own (real CPython
  tokenizes f-string brace content as if already inside a bracket pair, so embedded newlines never
  end the expression) raised a confusing, mis-located `SyntaxError`. Root-caused via manual bisection
  of the 3392-line real source file that triggered it (`sqlalchemy/engine/base.py`) — the reported
  error location was itself wrong (a parser sub-bug: `expected EndOfFile` errors report a stale
  line/col), found by truncating the file at increasing line counts until the failure mode changed.
  Fixed in `ParseFStringParts`: the extracted `{...}` expression text is normalized (embedded
  newlines outside of nested string literals replaced with spaces) before being re-parsed as its own
  standalone expression, reproducing PEP 701's "never ends the logical line" effect without needing
  to thread an initial bracket-depth into the lexer.
- [x] `inspect.iscode` didn't exist — added.
- [x] `PyCode.co_flags` didn't exist (only `co_varnames`/`co_argcount`/`co_kwonlyargcount`/
  `co_posonlyargcount`/`co_name`) — added, currently just the two bits anything reachable needs
  (`CO_VARARGS`/`CO_VARKEYWORDS`, real `*args`/`**kwargs` presence) plus the two every real function
  always has (`CO_OPTIMIZED`/`CO_NEWLOCALS`). `inspect.CO_VARARGS`/`CO_VARKEYWORDS`/`CO_OPTIMIZED`/
  `CO_NEWLOCALS` constants added alongside.
- [x] `inspect.get_annotations` didn't exist — added (a real copy of `obj.__annotations__`;
  `eval_str=`/`globals=`/`locals=` not implemented yet, nothing reachable needs them so far).
- [x] A real `NamedTuple` subclass's `__getitem__` only supported a single int index, not a slice
  (`spec[1:]`) — real CPython: a NamedTuple genuinely is a `tuple` subclass, so slicing it is real
  tuple slicing (returns a plain `tuple`). Fixed in `Interp.ConvertToNamedTuple`.
- [x] **Real `exec(source, globals=None, locals=None)` implemented** (author go-ahead) — full
  statement-level dynamic execution (unlike `eval()`, expression-only), mirroring `eval()`'s own
  three call shapes exactly: no args runs in the caller's current scope; a single `globals` dict runs
  like real module-level code (`Interp.RunModule` reused directly); a separate `locals` dict gets
  new/updated bindings written back into it afterward, matching real CPython. This unblocked real
  sqlalchemy's own `util/langhelpers.py` `_exec_code_in_env` (`exec(code, env); return
  env[fn_name]`), a common real-world metaprogramming idiom (dynamically generating a wrapper
  function's source to preserve the original's real signature for introspection) well beyond just
  this one package. 6 new tests in `M4_Functions/ExecTests.cs`.
- [x] `operator.inv`/`__inv__` (the older pre-2.0 names for `invert`/`__invert__`) didn't exist —
  added as aliases.
- [x] **A second real, general interpreter gap in the same family as the earlier `__init__`
  fallback**: `SomeClass.__hash__` (accessed on a *class* itself, not an instance) raised
  `AttributeError` — every class is a real, hashable object (`type` doesn't override `__hash__`).
  Fixed by adding the same `__hash__` fallback the `PyFunction`/`PyBuiltinFunction` cases already had
  to the `PyClass`-accessed-directly case too.
- [x] **A third, broader real gap in the same family, and the actually-important one**: an
  *instance* of a class with no `__init__`/`__new__`/`__setattr__`/`__hash__` anywhere in its own
  bases (not just a bare function, and not just a class accessed directly — a real constructed
  object) raised `AttributeError` for all four. Real CPython: every class implicitly derives from
  `object`, so every instance always has these; this interpreter's classes don't carry a real
  `object` `PyClass` in their own `Bases`/`Mro` to inherit them from. Fixed by adding the same
  four-case fallback already used for direct class/function access to the `PyInstance` attribute-
  lookup case too. Found via real sqlalchemy's own singleton-construction idiom (`sql/base.py`'s
  `SingletonConstant._create_singleton`: `obj = object.__new__(cls); obj.__init__()` on a class with
  no `__init__` of its own) — root-caused via a minimal repro after the real traceback's error
  message (`missing required argument: 'fget'`) turned out to be a red herring pointing at an
  unrelated descriptor class, not the real cause.

- [x] **Root-caused the `missing required argument: 'fget'` wall**: not a `Null`-specific
  multiple-inheritance/Generic[T] bug at all — a real, general ordering bug in `PyInstance` attribute
  lookup. The object-fallback for `__init__`/`__new__`/`__setattr__`/`__hash__` was checked *after*
  a class's own `__getattr__`, but real CPython always resolves these via the type's own (real) MRO
  — which genuinely includes `object` — before `__getattr__` (a last-resort hook) ever gets a chance
  to intercept them. Real sqlalchemy's own `ColumnElement.__getattr__` (`sql/elements.py`,
  `getattr(self.comparator, key)`) was incorrectly intercepting `Null`'s `__init__` lookup and
  cascading into an unrelated `memoized_attribute` descriptor's own constructor. Fixed by moving the
  four-dunder fallback switch before the `__getattr__` check in `Interp.TryGetAttr`'s `PyInstance`
  case. Root-caused via temporary env-var-gated diagnostics in `PyClass.TryLookup` (since removed).
- [x] **Real traceback frames showed `"<string>"` for every imported module**, regardless of depth —
  `Importer.ExecuteFile` built each module's `PyModule` with only its dotted name, never setting
  `PyModule.FileName` (default `"<string>"`). A completely general bug (every `import`, not
  sqlalchemy-specific) that made a 12-deep real traceback impossible to read. Fixed by setting
  `module.FileName = filePath` in `ExecuteFile`, matching what top-level script execution already did.
- [x] `datetime.fromtimestamp`/`utcfromtimestamp` didn't exist — added (naive local time by default,
  aware when a `tz=` is given, matching real CPython). Found via real sqlalchemy's own
  `sql/sqltypes.py` epoch constant (`dt.datetime.fromtimestamp(0, dt.timezone.utc)`).
- [x] `enum.Enum`/`IntEnum` (the base classes themselves, with no members of their own) didn't carry
  a real `__members__` at all — real CPython's do (empty). Found via real sqlalchemy's own
  `sql/sqltypes.py` module-level `Enum(enum.Enum)` (a "template" Enum type), whose
  `_parse_into_values` branches on `hasattr(enums[0], "__members__")`.
- [x] **A real enum-alias gap**: two names assigned the same value (e.g.
  `LABEL_STYLE_DEFAULT = LABEL_STYLE_DISAMBIGUATE_ONLY`) each got their own distinct member instead
  of the second becoming a real CPython *alias* (the same member object, excluded from
  `list(EnumClass)`/iteration but still listed in `__members__`). Found via real sqlalchemy's own
  `sql/selectable.py` `SelectLabelStyle`, whose `list(SelectLabelStyle)` a later 4-target unpacking
  assignment expects to match exactly. Fixed in `Interp.ConvertToEnum` (dedup by resolved value) and
  `PyOps.GetIter`'s enum case (reference-identity `Distinct()`).
- [x] **A new, substantial capability (author go-ahead): real `class Foo(dict): ...` subclassing.**
  Instances now behave as real dicts (indexing, `len`, iteration, `isinstance`) while unbound
  `dict.__init__`/`dict.update`/etc. calls on such an instance also work (real sqlalchemy's own
  `util/_py_collections.py` `immutabledict(ImmutableDictBase)`, built via
  `ImmutableDictBase.__new__(cls)` + `dict.__init__(new, *args)` + `dict.update(new, __d)`).
  Implemented via `PyInstance.Mapping` (a lazily-allocated backing `PyDict`), real dunders/methods
  installed on the "dict" pseudo-base class (`Interp.GetPseudoBaseClass`, reusing `DictMethods.Table`
  from `TypeMethods.cs`), and an `isinstance(x, dict)` fix for such instances. Also fixed a related
  ordering bug: a builtin type name's own `Table["__init__"]` (e.g. `dict.__init__`) was being
  shadowed by the generic `object.__init__` fallback on `PyBuiltinFunction` attribute access.
- [x] `cls.__subclasses__()` didn't exist — added (`PyClass.DirectSubclasses`, appended to at class
  construction time). Found via real sqlalchemy's own `util/langhelpers.py` `walk_subclasses`.
- [x] **PEP 487, `__init_subclass__`, implemented** — called automatically for every new subclass on
  the nearest base defining it, with any extra class keyword arguments forwarded, plus a real
  `object.__init_subclass__` no-op fallback reachable via `super()`. Found via real sqlalchemy's own
  event system (`event/base.py`'s `Events.__init_subclass__`), which populates a global event-name
  registry this way — without it, `event.listen(...)` can never find a real target class.
- [x] **A new, substantial capability (author go-ahead): the general descriptor protocol
  (`__get__`/`__set__`) for arbitrary user-defined classes**, on both class-level (`Class.attr`) and
  instance-level (`instance.attr`) attribute access — previously only the hardcoded
  property/staticmethod/classmethod cases worked at all. Found via real sqlalchemy's own event system
  (a `dispatcher(...)` descriptor accessed directly on a target *class*, e.g.
  `PrimaryKeyConstraint.dispatch`).
- [x] **A real, general parser/semantics gap: `Flag`/`IntFlag` didn't really exist** (aliased straight
  to `Enum`/`IntEnum`, so `auto()` generated sequential ints instead of real CPython's powers of two,
  and `|`/`&`/`^`/`~`/`in` were entirely unsupported for a plain `Flag`). Also fixed: a same-class-body
  expression referencing an earlier `auto()`-assigned name (e.g.
  `ANY_VIEW = VIEW | MATERIALIZED_VIEW`) needs the already-resolved int, not the raw `auto()`
  sentinel — real CPython resolves `auto()` eagerly at class-body *assignment* time (`_EnumDict.
  __setitem__`), not in one pass after the whole body finishes. Implemented via a new
  `Env.EnumAuto`/`EnumAutoState` (eager resolution, power-of-two vs. sequential) plus a real, separate
  `FlagClass`/`IntFlagClass` with composite-value bitwise operators. Found via real sqlalchemy's own
  `engine/reflection.py` `class ObjectKind(Flag): ...`.

**Phase 0 complete**: `import sqlalchemy` succeeds end to end against the real, unmodified 2.0.51
package (`print(sqlalchemy.__version__)` → `2.0.51`).

## Phase 1 — a minimal real ORM round-trip against SQLite (in progress)

Target: real `declarative_base()`/`DeclarativeBase`, a mapped class, `Session`, `add`/`commit`/
`query`/`select` — a real end-to-end insert+query against this project's own real `sqlite3` module,
verified with known-correct values (not just "didn't crash").

Real gaps found and fixed so far, probing with a real `declarative_base()` + mapped `User` class:

- [x] Real `class Foo(list): ...` / `class Foo(set): ...` subclassing — the same general mechanism
  just built for `dict` (author go-ahead covered this: mechanically identical, not a new decision),
  extended to `list`/`set`. `PyInstance.Sequence`/`SetItems` backing fields;
  `ListMethods.Table`/`SetMethods.Table` dunders installed on the "list"/"set" pseudo-base classes;
  `isinstance`/`issubclass` fixes. A real `frozenset` also gained its own (immutable-only) method
  table (`FrozenSetMethods`) — it had none at all before. Found via real sqlalchemy's own
  `orm/collections.py` `InstrumentedList(list)`/`InstrumentedSet(set)` and `orm/util.py`'s real
  `frozenset(...).difference(...)`.
- [x] `PyBuiltinFunction.__doc__` didn't exist — added (always `None`, the same "always-present
  default, no real text captured" simplification already accepted for class `__doc__`). Found via
  real sqlalchemy's own `orm/collections.py` `_tidy` helper copying `list.append.__doc__` onto its
  own instrumented wrapper.
- [x] **A real, general metaclass gap: a custom metaclass's own `__init__` was never dispatched at
  all** (only `__new__` was) — real `type.__call__(mcs, name, bases, ns, **kwds)` runs both. Found
  via real sqlalchemy's own `util/langhelpers.py` `_IntFlagMeta.__init__` (a `FastIntFlag`
  metaclass, an `enum.IntFlag` stand-in avoiding its overhead), which computes `cls._items`/
  `cls.__members__` from the completed namespace — silently never ran before, so any `FastIntFlag`
  subclass ended up with no real `__members__`. Fixed in `ExecClassDef`'s metaclass path; the same
  extra class keyword arguments (`initSubclassKwargs`) are forwarded to it too.
- [x] `itertools.count`/`itertools.groupby` didn't exist — added (`count`: infinite arithmetic
  sequence; `groupby`: consecutive-equal-key grouping, eagerly buffered per group rather than lazily
  coupled to outer-iterator advancement — matches every real call site seen so far). Found via real
  sqlalchemy's own `util/langhelpers.py` `counter()` and `orm/persistence.py`.
- [x] **A real, general gap: binary operators on a class *itself* (not an instance) never checked the
  class's own metaclass for the dunder** — real CPython's `SomeClass + other` dispatches to
  `type(SomeClass).__add__`. Found via real sqlalchemy's own `sql/base.py` `_MetaOptions.__add__` (a
  `class Options(metaclass=_MetaOptions)` whose subclasses combine via class-level `+`, e.g.
  `QueryContext.default_load_options + {...}`). Fixed in `Interp.BinaryOp`.
- [x] **Real docstring capture implemented, for both functions and classes** — a bare string-literal
  expression as a function/class body's first statement is now actually captured as `__doc__`
  (previously always discarded, `__doc__` was unconditionally `None`/absent). Needed for real
  correctness, not just cosmetics: real sqlalchemy's own `event/legacy.py` `_augment_fn_docs` does
  `assert fn.__doc__` for a listener method marked `_omit_standard_example`, which a permanently-`None`
  docstring would fail for real (not just print wrong).
- [x] **A real, general gap: calling a metaclass directly (`SomeMetaclass(name, bases, ns)`, real
  code's own hand-rolled equivalent of a `class X(metaclass=SomeMetaclass): ...` statement) built a
  blank instance of the metaclass instead of a real new class.** Fixed in `Interp.Instantiate`: when
  the callee class itself derives from the "type" pseudo-base (i.e. really is a metaclass), build a
  real class via the metaclass's own `__new__`/`__init__` (or `TypeConstructorMethods.BuildClass` as
  the default), exactly like the `class` statement path already does. Found via real sqlalchemy's
  own `orm/decl_api.py` `generate_base`: `return metaclass(name, bases, class_dict)`.
- [x] **A real, general gap: `type(SomeClass)` (called on a class itself, not an instance) silently
  downgraded to the generic `type` builtin function** (no `__mro__`, no real metaclass identity)
  instead of returning the class's own real metaclass. Found via real sqlalchemy's own
  `inspection.py` `inspect()`, which walks `type(subject).__mro__` to recognize a class whose
  *metaclass* was registered (e.g. any class built via `DeclarativeMeta`) — completely broken by the
  downgrade. Fixed in the `type()` builtin's 1-arg form.

**Current wall**: real `class quoted_name(util.MemoizedSlots, str): ...` — a genuine `str` subclass,
used pervasively for column/table/identifier names throughout the ORM and SQL-compiler pipeline
(`"..." + a_quoted_name_instance` inside `orm/mapper.py`'s own logging helper is where it first
surfaces, but this is not a one-off — quoted_name instances flow through comparison, hashing,
dict-key use, and the full string-method surface elsewhere). This is a substantially bigger and
riskier undertaking than the earlier `dict`/`list`/`set` subclassing work: those are already
boxed/wrapped C# reference types (`PyDict`/`PyList`/`PySet`), so giving a `PyInstance` a lazily-
allocated backing store and installing real dunders was a contained, mechanical extension. Real
Python strings are represented here as raw, unboxed C# `string` values threaded natively through
nearly the entire interpreter (literals, dict keys, f-strings, the `TypeMethods.TryGetBuiltinAttr`
`obj switch { string => StrModules.Table, ... }` dispatch, countless internal `is string`/`(string)`
casts) — there is no existing seam to hang a "real string subclass instance" off of without either
(a) a much larger refactor giving every such internal site a `PyInstance`-aware fallback, or (b) a
narrower, admittedly-incomplete shim (e.g. only `__new__`/`__add__`/`__radd__`/`__str__`/`__eq__`/
`__hash__`, falling short of full str-method parity) that would likely keep hitting the same class of
wall deeper in the compiler pipeline. Flagged for an explicit decision on how to proceed rather than
silently picking either option.

## Phase 2 — docs (not started)

ROADMAP.md scenario entry, RELEASE_NOTES.md, README.md "Verified scenarios" update, once Phase 1 is
real and verified end to end.
