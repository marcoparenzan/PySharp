# pandas on PySharp — plan

**Goal.** `import pandas as pd` on PySharp for the everyday surface: build Series/DataFrames, select, clean, compute, group,
combine, read/write CSV, and print them exactly like pandas prints them. Same method as numpy / cv2 / matplotlib / torch
([NOTEBOOKS_PLAN.md](NOTEBOOKS_PLAN.md)): a **native .NET library with no Python dependency + a thin PySharp binding + oracle
verification against the real library**.

The real pandas cannot be loaded: it is mostly compiled code (Cython, numpy's C API) and PySharp does not load native Python extensions.

Not a promise of parity. The target is the surface below; everything else is listed under "Out of scope" and fails with a clear
`NotImplementedError`, not with wrong answers.

---

## Decisions

| # | Decision |
|---|---|
| D1 | **Names:** native `NDSharp.Frame` (`src/NDSharp.Frame`), binding `PySharp.Pandas` (`src/PySharpLib.Pandas`). Lockstep version with the other `PySharp.*` / `NDSharp.*` packages. |
| D2 | **Target semantics: pandas 3.0.x** (oracle: pandas 3.0.6 + numpy 2.5.3 in `tools/oracle/.venv`). That means the default string dtype `str` (missing = NaN), Copy-on-Write always on, `object` only for genuinely mixed data. |
| D3 | **Storage:** immutable typed columns held as plain .NET arrays (`long[]` with its numpy width, `double[]`, `bool[]`, `string?[]`, `object?[]`); conversion to/from NDSharp `NDArray` happens at the numpy interop boundary (`values`, `to_numpy`, `np.asarray`, `pd.DataFrame(ndarray)`). Because pandas 3 is always copy-on-write, a `DataFrame` is a mutable container of immutable columns: every update swaps a column, nothing is aliased, and no "view vs copy" rules are needed. |
| D4 | **Missing data:** float NaN; an integer column that gains a missing value becomes `float64`, a bool column becomes `object` (pandas' classic rules). Nullable extension dtypes (`Int64`, `boolean`, `pd.NA`) are out of scope at first. |
| D5 | **Printing is part of the contract.** `repr`/`str` of Series, DataFrame and Index are ports of pandas' `io/formats/format.py` rules (float precision 6 and its trimming, scientific switch, right alignment, truncation at `max_rows`/`min_rows`, column wrapping at `width`, the `[N rows x M columns]` footer). `pd.set_option`/`options` for the display options. |
| D6 | **Verification:** golden snippets `src/PySharp.Tests/Oracle/pandas/*.py` with `.expected` produced by real pandas (`tools/oracle/make_golden.py pandas`, `tools/oracle/diff.sh pandas <name>`); native tests in `src/NDSharp.Frame.Tests` for the engine. Acceptance snippets follow the structure of pandas' own "10 minutes to pandas". |
| D7 | **Interop:** `np.asarray(df)` / `df.to_numpy()` / `df.values` / `Series.values`, numpy ufuncs on Series, `pd.DataFrame(ndarray)`, `df.plot()` hands over to matplotlib (late phase). |

## Target surface (in scope)

Level 1 — the model:
- `Series`, `DataFrame`, `Index`, `RangeIndex`; construction from dict / list / list of dicts / list of lists / ndarray / scalar / Series.
- dtypes `int64`/`int32`/..., `float64`/`float32`, `bool`, `str`, `object`; `astype`.
- `shape size ndim dtypes columns index values T empty name`, `len`, iteration, `in`, `head tail copy info describe`.
- selection: `df[col]`, `df[[cols]]`, `df[mask]`, `df[a:b]`, `df.col`, `.loc`, `.iloc`, `.at`, `.iat`, assignment through all of them, new columns, `drop`, `rename`, `assign`, `insert`.
- `repr` / `str` exactly like pandas.

Level 2 — everyday work:
- arithmetic and comparison with index alignment; scalar and column broadcasting; boolean combination (`&`, `|`, `~`).
- reductions skipping NaN: `sum mean median std var min max count prod quantile cumsum cumprod idxmin idxmax nunique`, `axis=0/1`.
- missing data: `isna notna isnull fillna dropna`; `where mask replace clip round abs`.
- `sort_values sort_index reset_index set_index unique value_counts duplicated drop_duplicates isin between nlargest nsmallest`.
- `apply map applymap agg`, string accessor `.str` (the common 20 methods).
- `groupby` (single/multiple keys; `sum mean count min max std size agg apply transform`), `merge`/`join` (inner/left/right/outer), `concat`, `pivot`/`pivot_table`, `melt`, `crosstab`.
- IO: `read_csv`/`to_csv` (files and strings), `to_dict`, `to_list`, `from_records`, `to_string`, `to_markdown`-free.
- `pd.set_option`, `pd.get_option`, `pd.options.display.*`.

## Out of scope (for now)

Datetime/timedelta/period dtypes and everything time-based (`to_datetime`, `resample`, `DatetimeIndex`), `Categorical`, `MultiIndex` beyond what `groupby`/`pivot_table` need for their results, nullable extension dtypes and `pd.NA`, sparse, window functions beyond a basic `rolling`, Excel/Parquet/SQL/JSON/HTML IO, styling, `eval`/`query` strings (maybe late), `pd.plotting`.

## Phases

### Phase 0 — Audit & tracking
- [x] plan (this file), oracle installed (pandas 3.0.6), decisions D1-D7
- [ ] ROADMAP row, memory, project log entry at the end of each phase

### Phase 1 — Model, construction, selection, printing  ✅
- [x] `NDSharp.Frame`: typed immutable columns, `Index`/`RangeIndex`, `Series`, `DataFrame`
- [x] construction: dict / list / list of dicts / list of lists / ndarray / scalar / Series, `index=`, `columns=`, `dtype=`; pandas 3 dtype inference
- [x] selection and assignment: `[]`, attribute access, `loc`, `iloc`, `at`, `iat`, boolean masks, label and positional slices, enlargement (`s['new'] = v`, `df.loc[new] = row`, `df.loc[mask, 'new'] = v`); `head tail copy drop rename assign insert del astype`
- [x] printing: port of pandas' formatter (float formatting, alignment, `max_rows`/`min_rows` truncation, terminal-width column dropping, `max_columns` wrapping with `\`, footers, index/column names, Index reprs) for Series / DataFrame / Index; `to_string`
- [x] `PySharp.Pandas` binding and registration (CLI, kernel, tests, notebook runner); `pd.set_option/get_option/reset_option/options.display.*` (per-engine options)
- [x] oracle snippets `Oracle/pandas/P1_*` (11 snippets) + `NDSharp.Frame.Tests` (8 native tests)
- [x] **Milestone M1:** the "object creation / viewing data / selection" snippets print exactly what pandas 3.0.6 prints
- Interpreter fix found on the way: `str(KeyError('x'))` is `'x'` (the repr of a single key), as in CPython; one test expectation that encoded the old behavior was corrected.

### Phase 2 — Computation
- [ ] arithmetic/comparison with alignment, reductions, `describe`, `info`, missing data, sorting, uniqueness, `apply/map/agg`, `.str`
- [ ] **Milestone M2:** acceptance snippets "operations / missing data / string methods"

### Phase 3 — Shaping and IO
- [ ] `groupby`, `merge`/`join`, `concat`, `pivot`/`pivot_table`/`melt`/`crosstab`
- [ ] `read_csv` / `to_csv`, `to_dict`, `from_records`, numpy interop
- [ ] **Milestone M3:** all acceptance snippets (everything in scope) equal pandas' output

### Phase 4 — Close-out
- [ ] README / ROADMAP / RELEASE_NOTES / PROJECT_LOG, version lockstep, packages in the local feed, JupyterNet docs ("pandas in a notebook")
- [ ] `df.plot()` over `PySharp.Matplotlib` (line / bar / hist) if time allows

---

## Known divergences (grows as phases land)

- `np.asarray(series_of_str)` / `.values` of `str` and `object` columns: NDSharp has no object arrays, so these raise `NotImplementedError` (numeric and bool columns convert).
- `Series.dtype` of a `str` column is a stand-in object (`str(dtype) == 'str'`, `repr` as pandas); there is no `StringDtype` class hierarchy.
- Nullable dtypes, `pd.NA`, datetime and categorical dtypes: out of scope (see above).
- A frame/Series element read from a `float32` column comes back as a Python float (pandas returns `np.float32`).

## Verification environment

`tools/oracle/.venv`: CPython 3.12, numpy 2.5.3, **pandas 3.0.6** (installed for this plan), plus the libraries of the other plans.
Golden files: `tools/oracle/make_golden.py pandas`.

## Open points

- PROJECT_LOG.md gets its pandas entry (real `git log` metrics) once the phases are committed.
- Notebook display: DataFrames print as text in the kernel; an HTML `_repr_html_` is a possible Phase 4 addition.
