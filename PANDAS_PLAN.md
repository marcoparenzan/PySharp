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

### Phase 2 — Computation  ✅
- [x] arithmetic / comparison / logic (`+ - * / // % **`, `== != < <= > >=`, `& | ^ ~`, unary, `abs`, `add/sub/mul/...` named forms) on Series and DataFrame with index alignment, scalar/list/ndarray/Series operands, pandas dtype rules (int/float/bool/str/object, weak scalars, `int // 0`, bool arithmetic)
- [x] reductions with NaN skipping (`sum prod mean median std var min max count nunique any all quantile idxmin idxmax`, `axis=0/1`, `skipna`, `numeric_only`, `min_count`, `ddof`) — float sums use NDSharp's pairwise summation, so results equal numpy's to the last digit; cumulative `cumsum cumprod cummax cummin`, `diff shift pct_change`
- [x] missing data: `isna notna isnull fillna ffill bfill dropna where mask replace clip round`, `inplace=`
- [x] ordering and uniqueness: `sort_values sort_index nlargest nsmallest unique value_counts duplicated drop_duplicates isin between`, `reset_index set_index rename T`, `select_dtypes`
- [x] `apply map agg/aggregate pipe` calling back into Python (Series; DataFrame `axis=0/1`, `map`), `items iterrows`
- [x] `.str` accessor: `lower upper title capitalize swapcase strip lstrip rstrip len contains startswith endswith match replace count find slice get split(expand) cat zfill pad center ljust rjust repeat removeprefix removesuffix is*`
- [x] `describe` (numeric and categorical), `info`, `corr`, `cov`
- [x] numpy interop: `np.sqrt(series)` / `np.add(df, 1)` return Series/DataFrame, `ndarray + series` defers to the Series operator (new hooks in `PySharp.Numpy`: `Conv.UfuncWrappers`, `Conv.DeferToOther`)
- [x] oracle snippets `Oracle/pandas/P2_*` (12 snippets, 1200+ lines of expected output) + 5 more native tests; samples `pandas_demo.py` and `pandas_analysis_demo.py` are byte-identical to CPython + pandas
- [x] **Milestone M2:** operations / missing data / string methods snippets equal pandas' output

### Phase 3 — Shaping and IO  ✅
- [x] **MultiIndex** (rows and columns): construction (`from_tuples/from_arrays/from_product`, `set_index([...])`, groupby/pivot results), pandas-identical printing (sparsified levels, level names, header rows), `loc` with full and partial keys, `get_level_values`, `droplevel`, `names`, `sort_index`, `reset_index`
- [x] **groupby**: one or several keys (labels, Series/arrays, levels, functions), `sort`, `as_index`, `dropna`; reductions (`sum mean median min max std var count prod first last nunique any all size quantile`), `agg` (name, callable, list, dict, named aggregation), `transform`, `apply`, `filter`, `cumsum/cumprod/cummax/cummin/cumcount/shift/diff/head/tail`, iteration, `get_group`, `groups`, column selection (`gb['x']`, `gb.x`)
- [x] **merge / join**: `pd.merge`, `DataFrame.merge`, `DataFrame.join` — inner/left/right/outer/cross, `on`/`left_on`/`right_on`/`left_index`/`right_index`, suffixes, `indicator`, NaN keys match, outer keys sorted like pandas
- [x] **concat**: Series and DataFrames, `axis=0/1`, `join`, `ignore_index`, `keys`/dict, column union in order of appearance
- [x] **reshape**: `pivot_table` (values/index/columns lists, several aggfuncs, dict, callables, `fill_value`, `margins`), `pivot`, `melt`, `crosstab` (counts, values+aggfunc, margins, normalize), `stack`/`unstack`, `get_dummies`
- [x] **IO**: `read_csv`/`read_table` (files, `StringIO`, quoting, `sep`, `header`, `names`, `index_col`, `usecols`, `dtype`, `na_values`, `keep_default_na`, `skiprows`, `nrows`, `comment`, `thousands`/`decimal`, duplicate and unnamed columns, pandas' type inference), `to_csv` (DataFrame and Series, files and strings, `sep`, `na_rep`, `float_format`, `columns`, `header`, `index`, `index_label`, MultiIndex rows), `to_dict` (all orients), `DataFrame.from_dict`, `from_records`
- [x] oracle snippets `Oracle/pandas/P3_*` (7 snippets: multiindex, groupby, merge_concat, reshape, io, edge) + 5 more native tests; sample `pandas_report_demo.py` identical to CPython + pandas
- [x] **Milestone M3:** every in-scope snippet equals pandas' output
- Fixes found by the sample: a `str` column that meets NaN placeholders stays `str`; `read_csv(index_col=...)` of a regular integer column gives a RangeIndex; bool column labels print like pandas' object index; `unstack(fill_value=)` keeps ints; `stack()` keeps NaN (pandas 3)

### Phase 4 — Close-out  ✅
- [x] **rolling / expanding / rank**: `rolling(window, min_periods, center)` and `expanding(min_periods)` with `sum mean min max count median std var apply agg` — the mean/sum/var
      kernels are ports of pandas' Kahan-compensated and online algorithms, so results equal pandas bit for bit on random data; `rank` (`average/min/max/first/dense`, `pct`, `na_option`)
- [x] **plotting**: `df.plot()` / `Series.plot()` / `plot.line|bar|barh|hist|scatter|area` / `hist()` through `PySharp.Matplotlib` (the drawing logic is a small embedded Python module that
      follows pandas' defaults); `pie`, `box`, `kde`, `hexbin` raise `NotImplementedError`; tests check that every supported kind saves a PNG
- [x] **notebooks**: `DataFrame._repr_html_` / `to_html` (pandas' HTML layout, verified), the JupyterNet kernel echoes the last expression of a cell (HTML for DataFrames, `repr` otherwise),
      `display()`, `max_columns = 20`; guide `JupyterNet/docs/pandas-in-notebooks.md`
- [x] README / ROADMAP / RELEASE_NOTES (v2.2.0) / PROJECT_LOG; version lockstep **2.2.0** for all `PySharp.*` / `NDSharp.*` packages; the 16 library packages are in `D:/Dev/NuGetLocalFeed`
      (a consumer project restored from the feed runs pandas + groupby + rolling). The `PySharp` global tool is **not** packed (about 410 MB because of libtorch)
- [x] samples `pandas_demo.py`, `pandas_analysis_demo.py`, `pandas_report_demo.py`, `pandas_rolling_demo.py` — byte-identical to CPython + pandas
- Bug found by the rolling oracle: `Generator.normal` used a ziggurat table recomputed from the layer areas, which differs from numpy's in the last bit for most draws; the exact `wi_double`
  table was recovered from numpy's own output and embedded (200000 draws now equal numpy's, tail included)

### Phase 5 — JSON, ewm, pie/box  ✅
- [x] **`to_json` / `read_json`**: DataFrame orients `columns index records split values` (+ `lines=True`, `indent`, `double_precision`, `force_ascii`), Series `index split records values`;
      the encoder is a port of pandas' ujson (escaped `/` and non-ASCII, fractional part rounded to `double_precision` digits with ujson's rounding rules, exponent form above 1e16, NaN/inf as `null`);
      `read_json` (strings, files, `StringIO`, `typ='series'`, `lines`) converts values and index labels like pandas (integral floats become int64, all-integer keys an int64 index, bool with null → float)
- [x] **`ewm`**: `ewm(com | span | halflife | alpha, min_periods, adjust, ignore_na).mean()/std()/var(bias=)` — ports of pandas' weighted-mean and weighted-covariance kernels (bit-exact on random data)
- [x] **`plot.pie` / `plot.box`** (and `kind='pie'|'box'`): drawn with polygons and lines in the embedded plotting module (pie: `autopct`, `startangle`, labels; box: quartiles, 1.5 IQR whiskers, fliers, `vert`)
- [x] oracle snippets `P5_json`, `P5_ewm`; plot tests extended; native tests unchanged
- Known divergence found: pandas' `ewm(alpha=0.5, adjust=False)` over data with gaps (NaN) returns `1-(1-alpha)^k` for the observation after a gap of k NaNs — a special case at alpha exactly 0.5 that its own
  documented formula does not give — while every other alpha follows the documented weights. PySharp follows the documented formula; the oracle snippet uses alpha 0.4 there.

### Phase 6 — Categorical  (planned)
- [ ] `Kind.Category` column: categories + codes (+ ordered), NaN as code -1; `dtype 'category'`, `pd.Categorical`, `astype('category')`, `CategoricalDtype`
- [ ] printing (`Categories (3, str): ['a', 'b', 'c']` footers, `dtype: category`), `.cat` accessor (`categories codes ordered add/remove/rename/reorder_categories as_ordered/unordered`)
- [ ] behaviour: ordered comparisons, sort by category order, `value_counts` (all categories, including empty), `groupby` (`observed=False` default shows empty categories), `unique`, `isin`, `fillna`, `get_dummies`
- [ ] `pd.cut` / `pd.qcut` (interval labels, `labels=`, `bins=`, `right=`, `include_lowest`, `retbins`) and `Interval` display
- [ ] oracle snippets `P6_*`

### Phase 7 — Datetime  (planned)
- [ ] `Kind.DateTime` (datetime64 with the unit pandas 3 infers), `Timestamp`/`Timedelta` scalars, `NaT`, `pd.to_datetime` (formats, `errors`, `dayfirst`, ISO parsing), `pd.to_timedelta`
- [ ] `DatetimeIndex`, `pd.date_range`, `pd.Timedelta`, arithmetic (datetime ± timedelta, differences), comparisons, printing of datetime/timedelta columns and indexes
- [ ] `.dt` accessor (`year month day hour minute second dayofweek day_name month_name date normalize floor/ceil/round strftime isocalendar`), `resample` (common rules and aggregations), `shift(freq=)`, `rolling('3D')`, `read_csv(parse_dates=)`, `to_csv`/`to_json` date formats, `groupby(pd.Grouper(freq=))`
- [ ] oracle snippets `P7_*`

---

## Known divergences (grows as phases land)

- `np.asarray(series_of_str)` / `.values` of `str` and `object` columns: NDSharp has no object arrays, so these raise `NotImplementedError` (numeric and bool columns convert).
- `Series.dtype` of a `str` column is a stand-in object (`str(dtype) == 'str'`, `repr` as pandas); there is no `StringDtype` class hierarchy.
- Nullable dtypes, `pd.NA`, datetime and categorical dtypes: out of scope (see above).
- A frame/Series element read from a `float32` column comes back as a Python float (pandas returns `np.float32`); reductions return plain Python `int`/`float` (the numpy binding's convention), not `np.int64`/`np.float64`.
- `Series.unique()` of a `str`/`object` Series returns a Python list (pandas returns an extension array).
- `sort_values` is always stable (pandas' default quicksort is not stable on large inputs, so tie order can differ there).
- `Series.corr` can differ from pandas in the last digit (BLAS rounding inside `np.corrcoef`); the oracle snippet rounds it. `describe(include=...)`, `ranking`, `rolling`, `interpolate`, `eval`/`query` are not implemented yet.
- A binary operation between two Series with *different* duplicate labels raises (pandas joins them).
- `Series.groups` / `DataFrameGroupBy.groups` values are lists of labels; `merge(indicator=True)` gives a `str` column (pandas: category); `validate=` is accepted and ignored.
- MultiIndex: `stack`/`unstack` of frames whose *columns* are a MultiIndex, `xs`, `swaplevel`, level-wise `loc` slicing are not implemented; `to_csv` with MultiIndex columns raises.
- `read_csv`: no `parse_dates`/`converters`/`chunksize`/URLs; `orient='table'` JSON and datetime-aware JSON are not implemented (Phase 7).
- Not implemented: `rolling(window='3D')` (offsets, Phase 7), `plot.kde`, `eval`/`query`, `describe(include=...)`, `interpolate`; plots are drawn by PySharp.Matplotlib, so pixels differ from Agg.

## Verification environment

`tools/oracle/.venv`: CPython 3.12, numpy 2.5.3, **pandas 3.0.6** (installed for this plan), plus the libraries of the other plans.
Golden files: `tools/oracle/make_golden.py pandas`.

## Open points

- PROJECT_LOG.md has the entry for Phases 1-3 (real `git log` metrics); Phase 4 is added once it is committed.
