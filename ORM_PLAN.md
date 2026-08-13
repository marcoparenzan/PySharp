# SQLAlchemy support — a probe-driven plan

**Goal.** A real, generic ORM beyond Django's own (which needs the full Django framework). Real
**SQLAlchemy** (2.0.51, `py3-none-any` wheel, installs cleanly via `pysharp install sqlalchemy` — the
core/ORM has no C-extension dependency by default) — real ORM + Core query builder, dialect-based, so
the same code targets SQLite (already a real C# DB-API module in this project) and, later, Postgres
via the pure-Python `pg8000` driver (avoiding `psycopg2`'s C extension).

**Method**: same as FASTAPI_PLAN.md/NUMPY_PLAN.md/CTYPES_PLAN.md — probe with real, unmodified
SQLAlchemy against a real SQLite database, root-cause each real gap, fix it generally (not
SQLAlchemy-specific hacks), write a regression test per fix, keep the full suite green.

## Phase 0 — groundwork: `import sqlalchemy` (done)

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

## Phase 1 — a minimal real ORM round-trip against SQLite (done)

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

- [x] **A new, substantial capability (explicit author go-ahead after a size/risk flag): full real
  `class Foo(str): ...` subclassing.** Instances behave as real strings everywhere — all real str
  methods, concatenation (`+`/`+=` either direction), comparison operators, `len`/indexing/slicing/
  iteration/`in`, and (critically) value-based hashing/equality so a subclass instance and its plain
  `str` equivalent are the *same* dict key (`d[quoted_name("x")]` and `d["x"]` collide, matching real
  CPython). Implemented via `PyInstance.StrValue` (set once at construction, matching real string
  immutability) plus real dunders/methods installed on the "str" pseudo-base
  (`Interp.GetPseudoBaseClass`, reusing `StrModules.Table` from `TypeMethods.cs`), an `isinstance`
  fix, and `PyOps.PyHash`/`PyEquals` value-equality special cases. `TypeMethods.StrArg` (the shared
  helper nearly every str method's secondary argument goes through) was also fixed to unwrap a
  str-subclass instance, so passing one anywhere a plain string is expected just works. Found via
  real sqlalchemy's own `sql/elements.py` `class quoted_name(util.MemoizedSlots, str): ...`, used
  pervasively for column/table/identifier names throughout the ORM and SQL-compiler pipeline.
- [x] **A real, general interpreter bug this exposed: `super().__new__(cls, value)` incorrectly
  prepended an implicit extra argument.** Real CPython's `__new__` is implicitly a staticmethod —
  never auto-bound — but `PySuper`'s attribute dispatch wrapped *every* found function/builtin as a
  bound method regardless of name, silently shifting every explicit argument over by one position
  (`value` landing where `cls` was expected one level down). This had been invisible until now
  because the existing `object.__new__` fallback is lenient enough to mask it for the common
  zero-extra-arg case. Fixed by special-casing `__new__` in the `PySuper` case of
  `Interp.TryGetAttr` to stay unbound. Found via real sqlalchemy's own `quoted_name.__new__`:
  `super().__new__(cls, value)` was silently constructing a broken instance whose `str()`/`repr()`
  printed the *class itself* instead of the value.
- [x] **A second real, general bug in the same family: a plain class attribute referencing a builtin
  type or a factory-returned closure (e.g. `execute_sequence_format = tuple`,
  `schema_for_object = operator.attrgetter("schema")`) was incorrectly auto-bound to `self` on
  instance access**, exactly as if it were a real method — real CPython only auto-binds genuine
  `def`-defined functions; a plain value or a callable *object* (not a function) never does. Fixed
  two ways: (a) `BindClassAttr` now leaves a `PyBuiltinFunction` unbound when its name is a known
  builtin type constructor (`BuiltinTypeNames`); (b) `operator.attrgetter`/`itemgetter`/
  `methodcaller` now wrap their returned closures in `PyStaticMethod` (which never auto-binds, and
  is now itself directly callable — a new `CallCore` case delegates to the wrapped function, so
  `sorted(items, key=operator.attrgetter(...))` keeps working). Found via real sqlalchemy's own
  `engine/default.py` `dialect.execute_sequence_format()` (silently became
  `tuple(dialect_instance)`, "not iterable") and `sql/compiler.py`
  `self.schema_for_object(table)` (silently called with `table` shifted out and `self` in its place).
- [x] Real `dict.__missing__` support — a dict subclass overriding it gets it called instead of a
  raw `KeyError` on a miss (real CPython's mechanism behind `collections.defaultdict`). Found via
  real sqlalchemy's own `sql/base.py` `DialectKWArgs.dialect_options = util.PopulateDict(...)`
  (`util/_collections.py`'s own real `PopulateDict(dict)` using `__missing__` to lazily populate
  per-dialect option dicts on first access).
- [x] **A real, general parser/semantics gap: `typing.Literal[...]`'s own arguments were being
  ForwardRef-wrapped like every other generic subscript's string arguments** — but `Literal`'s
  arguments are literal *values*, never forward-referenced type names, so `Literal[...].__args__`
  held `ForwardRef('x')` objects instead of the plain strings, breaking any real `x in
  SomeLiteral.__args__` check. Found via real sqlalchemy's own `orm/session.py`
  `JoinTransactionMode = Literal["conditional_savepoint", ...]`, whose own `Session.__init__`
  validates its default value against `.__args__` this way — sqlalchemy's own default was being
  rejected by sqlalchemy's own validation. Fixed in `GenericAliasModule.Subscript`.
- [x] **Real "str enum" mixin support** (`class Color(str, Enum): RED = "red"`) — each member is a
  real `str` too (multiple inheritance, matching real CPython), not just an `Enum` member that
  happens to hold a string value: `Color.RED == "red"` is real str equality/hashing, usable directly
  as a dict key interchangeably with the plain string. `ConvertToEnum` now also sets
  `PyInstance.StrValue` on each member when the enum class derives from the "str" pseudo-base — found
  live via a real pydantic str-enum field crashing with `str.__eq__(): invalid argument type` (an
  enum member built directly by `ConvertToEnum`, bypassing `str.__new__`, never had `StrValue` set,
  yet the "str" pseudo-base's own `__eq__` was winning the MRO lookup ahead of `Enum`'s).
- [x] Real `BaseException.with_traceback(tb)` — sets `__traceback__` and returns the same instance,
  the common `raise value.with_traceback(traceback)` re-raise idiom. Found via real sqlalchemy's own
  `util/langhelpers.py` `reraise`-style `__exit__` handler (`pool/base.py`'s connection-creation
  error path).
- [x] **A real, general gap: `**expr` unpacking only ever accepted a literal `dict`**, rejecting any
  real mapping-protocol object — including a real `class Foo(dict): ...` subclass instance. Fixed in
  `Interp.EvalCall` to also accept anything satisfying the mapping protocol (reusing
  `PyOps.TryGetMappingItems`, the same real `keys()`+`__getitem__` duck-typing check `dict.update()`
  already used). Found via real sqlalchemy's own `pool/base.py` connection-creator call chain
  unpacking a real `immutabledict` of connect args with `**`.
- [x] `set.isdisjoint`/`.difference_update`/`.intersection_update`/`.symmetric_difference_update`
  didn't exist (only the non-mutating `union`/`intersection`/`difference`/`symmetric_difference` and
  plain `update` did) — added, plus the matching `frozenset.isdisjoint`. Found via real sqlalchemy's
  own `util/topological.py` `sort_as_subsets` (dependency-graph topological sort for DDL emission
  order).
- [x] `sqlite3.dbapi2`, `sqlite3.sqlite_version`/`sqlite_version_info`,
  and `sqlite3.Connection.create_function` didn't exist. `sqlite3.dbapi2` re-exports the real,
  already-imported `sqlite3` module (a common real idiom, `from sqlite3 import dbapi2 as sqlite`);
  `sqlite_version_info` reports the real underlying SQLite C library version (via a real
  `SELECT sqlite_version()`, not a hardcoded guess); `create_function` registers a real callable SQL
  function backed by Microsoft.Data.Sqlite's own `SqliteConnection.CreateFunction` (arities 0–4).
  Found via real sqlalchemy's own `dialects/sqlite/pysqlite.py` `import_dbapi`/`on_connect`
  (registers `regexp`/`floor` as real SQL functions on every new connection).

- [x] **A new, substantial capability (explicit author go-ahead after a size/risk flag): real
  `class Foo(int): ...` subclassing**, the same general mechanism as dict/list/set/str — instances
  behave as real ints (arithmetic `+ - * // % & | ^ << >>`, comparisons, `bool`/`hash`/`repr`, and
  value-based hashing/equality so a subclass instance and its plain `int` equivalent are the same
  dict key), via the *existing* `PyInstance.Dict["value"]` convention already shared by the
  httpx-style `int.__new__(cls, value)` special case and by `IntEnum` members (`PyOps.AsBigInt`
  already read either shape) — no new field needed. Scoped specifically to instances whose class
  derives from the "int" pseudo-base in `PyOps.PyHash`/`PyEquals`, so a plain non-int `Enum` member
  that merely happens to hold an int value does *not* become numerically hashable/equal-by-value at
  this level (only a real int subclass does in CPython). Found via real sqlalchemy's own
  `util/langhelpers.py` `class symbol(int): ...` (`util.symbol`, real int-valued sentinels combined
  with `&`/`|`, e.g. inside `FastIntFlag`'s own machinery).
- [x] **A real, general interpreter bug: `operator.eq`/`ne`/`lt`/`le`/`gt`/`ge` were wired to
  `Interp.BinaryOp`** (arithmetic dispatch, which has no concept of comparison at all and always
  raised "unsupported operand type(s)") **instead of `Interp.CompareRaw`** (which also preserves a
  non-bool `__eq__`-etc. return value, e.g. a real SQL expression object, matching what the `==`
  syntax itself already did). Found via real sqlalchemy's own expression-building internals, which
  call `operator.eq`/etc. as plain functions pervasively, not just via `==` syntax. `CompareRaw`
  widened from `private` to `internal` so `OperatorModule` can call it directly.
- [x] **A real, general interpreter gap: introspecting a builtin type *constructor* directly (`str`,
  `int`, ... — not a user class) via `.__mro__`/`.__bases__` fell all the way through to a generic
  AttributeError**, misreported as `'function' object has no attribute '__mro__'` (a builtin type
  constructor and a plain function share the same internal representation, both typed as
  `"function"`). Fixed by delegating to the same "str"/"int"/... pseudo-base `class Foo(str): ...`
  already uses. Found via real sqlalchemy's own `sql/coercions.py` `expect()`:
  `resolved.__class__.__mro__` where `resolved` is a plain string, i.e. `str.__mro__`.
- [x] **A real, general, and notably subtle interpreter gap: reassigning a function's
  `__defaults__`/`__kwdefaults__` was stored as an inert attribute, never actually consulted by
  argument binding** — real CPython's calling machinery always reads them fresh at call time, so
  `func.__defaults__ = new_tuple` genuinely changes what a *future* call resolves unfilled parameters
  to. `PyFunction.Defaults` (the dictionary argument-binding actually reads) is now updated in lock-
  step whenever `__defaults__`/`__kwdefaults__` is reassigned, mapping the tuple positionally onto
  the *trailing* N positional parameters (matching real CPython's own alignment rule) and merging
  `__kwdefaults__` entries directly by name. Found via real sqlalchemy's own
  `util/langhelpers.py` `decorator()` helper (a signature-preserving decorator factory used
  pervasively, e.g. by `@_generative`): it `exec()`-generates a wrapper whose own source only has
  dummy `None` placeholder defaults (to avoid embedding large default objects into generated code),
  then does `decorated.__defaults__ = fn.__defaults__` to give the wrapper the *real* target
  function's defaults — every one of these wrapped functions was silently losing its real default
  argument values and failing with spurious "missing required argument" errors on the very first
  call that relied on a default (e.g. `Engine`'s own `query_cache_size` default of 500 arriving as
  `None`, or `UpdateBase.return_defaults()`'s own keyword defaults going missing).
- [x] `re.compile(x)` didn't handle `x` already being a compiled `Pattern` (real CPython: idempotent,
  returns it as-is) — always tried to treat it as raw pattern text and failed. Found via real
  sqlalchemy's own `sql/compiler.py` bind-name-escaping logic re-passing a pre-compiled
  `_bind_translate_re` through a `re.compile`-shaped helper.
- [x] `re.match`/`search`/`fullmatch`/etc.'s subject-string coercion didn't accept a real
  `class Foo(str): ...` subclass instance (only a literal `str`/`bytes`) — fixed in `ReModule`'s
  shared `ToWorkingString` helper. Found via real sqlalchemy's own `sql/elements.py`
  `class quoted_name(..., str): ...` identifiers flowing into real regex validation
  (`_requires_quotes`'s `legal_characters.match(str(value))`).

- [x] **`PyCode.co_varnames` had the wrong real-CPython ordering**: built as `[positional, *args,
  kwonly, **kwargs]`, but real CPython's actual layout is `[positional, kwonly, *args, **kwargs]`
  (keyword-only names come *before* the `*args` name, not after). Broke any `inspect.
  getfullargspec`-style introspection reading `co_varnames[argcount + kwonlycount]` to find the
  var-args name. This was the actual root cause of the `return_defaults()` `'cols'` wall above:
  `compat.inspect_formatargspec` (sqlalchemy's own hand-rolled port of the function real CPython
  removed in 3.11) misread `*cols` as a required keyword-only parameter under the old ordering.
- [x] **`PyFunction.Code` (`fn.__code__`) built a fresh `PyCode` object on every access** instead of
  caching it — broke any code comparing two `__code__` reads by identity (`is`), a real, documented
  CPython pattern (e.g. real sqlalchemy's own `type_api.py` `_has_column_expression`: `self.
  __class__.column_expression.__code__ is not TypeEngine.column_expression.__code__`, an "was this
  method overridden" check that always reported "yes", even unmodified). Now lazily cached per
  `PyFunction`.
- [x] **`instance.__dict__ = newdict` (plain attribute assignment, not just the explicit `object.
  __setattr__` unbound form) must replace the whole instance namespace at once** — was instead
  storing the value under the literal key `"__dict__"`, silently losing every other attribute (reading
  `.__dict__` back looked correct by coincidence). This is the exact mechanism behind sqlalchemy's own
  `Generative._generate()`, used internally by every `@_generative`-decorated SQL-expression method.
- [x] **`object.__new__`'s "was this actually a `type.__new__(mcs, name, bases, ns)`-shaped call?"
  heuristic matched ANY call with a string then a tuple as the next two args**, regardless of whether
  the first argument was actually a metaclass — misfired for a real `typing.NamedTuple` whose first
  field is `str`-typed and second is `tuple`-typed, e.g. real sqlalchemy's own `sql/compiler.py class
  _InsertManyValuesBatch(NamedTuple): replaced_statement: str; replaced_parameters: ...` —
  constructing a real instance was misdetected as "build a brand-new class named after the SQL text"
  instead. Fixed by gating on the same "is `cls` actually a metaclass?" guard `Interp.Instantiate`
  already uses for calling a metaclass directly.
- [x] **Functions (and builtins) didn't support the descriptor protocol on themselves**
  (`func.__get__(obj, type)`) — real CPython functions ARE descriptors; this is the actual machinery
  behind "accessing a function through a class turns it into a bound method". `obj is None` (class-
  level access) returns the plain function unchanged, matching real Python 3 (no more "unbound
  method" wrapper). Found via real sqlalchemy's own `util/langhelpers.py` `hybridmethod.__get__`:
  `self.clslevel.__get__(owner, owner.__class__)`, explicitly re-invoking the descriptor protocol on
  a plain function to bind it to the class itself as if it were an instance — the final wall blocking
  `session.execute(select(...))`.
- [x] `str.join` rejected a real `class Foo(str): ...` subclass instance in the sequence (only a
  literal `str`), even though real CPython accepts any str subclass. Found via real sqlalchemy's own
  `quoted_name` (`class quoted_name(..., str): ...`) flowing straight into `", ".join([...])` while
  composing a FROM-clause's SQL text.
- [x] **A regression introduced by the `object.__new__` metaclass-shape guard fix above: real
  `abc.ABCMeta` (`class ABCMeta(type): ...` in real CPython) had no bases at all in PySharp's own stub**,
  so any real custom metaclass built on it (the exact pattern pydantic's own `class ModelMetaclass
  (ABCMeta): ...` uses) failed the "is this actually a metaclass?" check — `super().__new__(mcs, name,
  bases, namespace)` silently fell through to "build a blank instance of `mcs`" instead of building the
  real class, dropping every method (including a real `__init__`) the namespace carried. Fixed by
  giving `AbcModule.AbcMetaClass` the real "type" pseudo-base as a base. Caught by re-running the full
  regression suite (not sqlalchemy-specific — this broke pydantic's own `import pydantic` and three
  `MetaclassTests`/`IntrospectionTests` cases that had gone stale/unverified since the four fixes above
  were made without a full-suite run).

**Phase 1 is done.** The full insert + query round trip now runs end-to-end against a real,
unmodified sqlalchemy 2.0.51 + this project's own real `sqlite3` module: `declarative_base()`, a
mapped `User` class definition, `Base.metadata.create_all(engine)` (real `CREATE TABLE` DDL),
`Session(engine)`, `session.add(...)`, `session.commit()` (a full real INSERT flush, including the
`insertmanyvalues`/RETURNING-clause machinery), `session.execute(select(User).order_by(User.name))
.scalars().all()`, and `session.get(User, 1)` all produce exactly the expected real values against a
real SQLite in-memory database — verified end-to-end in `M22_Orm/OrmSmokeTests.cs`. Getting here took
~30 real, general interpreter fixes, none of them sqlalchemy-specific (see the itemized lists above).

## Phase 2 — docs (not started)

ROADMAP.md scenario entry, RELEASE_NOTES.md, README.md "Verified scenarios" update, once Phase 1 is
real and verified end to end.
