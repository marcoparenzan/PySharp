# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# pandas_periods_tz_demo.py — periods, time zones and query/eval on PySharp.Pandas (Phase 8).
#
# See PANDAS_PLAN.md. The output is identical to real pandas 3.0 (checked by running the script under CPython +
# pandas and diffing). Covers fiscal Periods and PeriodIndex, tz-aware timestamps across a daylight-saving change,
# DataFrame.query / DataFrame.eval, and numpy's poisson / exponential / gamma generators feeding a call-centre dataset.
#
# Usage:  pysharp run samples/pandas_periods_tz_demo.py

import numpy as np
import pandas as pd

rng = np.random.default_rng(31)
hours = pd.date_range("2024-03-30 18:00", periods=36, freq="h", tz="Europe/Rome")
calls = pd.DataFrame({
    "calls": rng.poisson(14, size=len(hours)),
    "wait": rng.exponential(3.0, size=len(hours)).round(2),
    "value": rng.gamma(2.0, 40.0, size=len(hours)).round(2),
}, index=hours)
calls.index.name = "when"
print(calls.head(6))
print(calls.index.dtype, calls.index.tz, calls.index[0], calls.index[-1])

print("\n--- the clocks go forward on 2024-03-31 ---")
steps = calls.index.to_series().diff().dropna().value_counts().sort_index()
print(steps)
print(calls.index[calls.index.hour == 2].tolist() or "no 02:00 local hour exists that night")
print(calls.index[5:9])
print(calls.index.tz_convert("UTC")[5:9])
print(calls.index.tz_convert("America/New_York")[:3])
daily = calls.resample("D").agg({"calls": "sum", "wait": "mean", "value": "sum"}).round(2)
print(daily)
print(calls["calls"].groupby(calls.index.date).sum())
print(calls.index.min().strftime("%Y-%m-%d %H:%M %Z (%z)"), calls.index.max().tz_convert("UTC").isoformat())

print("\n--- query and eval ---")
busy = calls.query("calls > 16 and wait < 2")
print(busy)
threshold = 15
print(calls.query("calls >= @threshold").shape[0], "busy hours")
print(calls.query("`value` > 100 or calls == 0").index.hour.tolist())
tagged = calls.eval("per_call = value / calls")
print(tagged["per_call"].replace([np.inf, -np.inf], np.nan).describe().round(2))
print(calls.eval("calls * 2 + 1").head(4).tolist())
print(calls.eval("score = value - 10 * wait").query("score > 60").shape[0])

print("\n--- fiscal periods ---")
orders = pd.DataFrame({
    "day": pd.date_range("2023-11-20", periods=8, freq="21D"),
    "amount": [120.0, 80.5, 99.9, 150.0, 60.0, 210.5, 75.25, 130.0],
})
orders["month"] = orders["day"].dt.to_period("M")
orders["fiscal_q"] = orders["day"].dt.to_period("Q-MAR")
print(orders)
print(orders.dtypes)
print(orders.groupby("fiscal_q")["amount"].agg(["count", "sum"]))
print(orders.groupby("month")["amount"].sum())
q = pd.period_range("2023Q1", periods=5, freq="Q-MAR")
print(q, q.start_time.tolist()[:2], q.end_time.tolist()[:2])
p = pd.Period("2024-02", "M")
print(p, p + 1, p - pd.Period("2023-11", "M"), p.start_time, p.end_time, p.asfreq("D", "end"), p.asfreq("Y"))
print(orders["month"].dt.to_timestamp().dt.strftime("%b %Y").tolist())
print((orders["month"].max() - orders["month"].min()).n, orders["month"].nunique())
