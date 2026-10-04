# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# pandas_timeseries_demo.py — dates and time series on PySharp.Pandas (Phase 7).
#
# See PANDAS_PLAN.md. The output is identical to real pandas 3.0 (checked by running the script under CPython +
# pandas and diffing). Covers Timestamp/Timedelta, to_datetime, date_range, the .dt accessor, partial-string
# indexing, resample, rolling windows over time, shift, date offsets, Grouper and reading dates from CSV.
#
# Usage:  pysharp run samples/pandas_timeseries_demo.py

import io

import numpy as np
import pandas as pd

rng = np.random.default_rng(2024)
days = pd.date_range("2024-01-01", "2024-06-30", freq="D")
weekday_boost = np.where(days.dayofweek < 5, 1.0, 0.45)
orders = (rng.integers(18, 43, size=len(days)) * weekday_boost).round().astype(int)
revenue = np.round(orders * rng.uniform(18.0, 26.0, size=len(days)), 2)
sales = pd.DataFrame({"orders": orders, "revenue": revenue}, index=days)
sales.index.name = "day"
print(sales.head(8))
print(sales.index.freq, sales.index.dtype, len(sales))

print("\n--- selecting by date strings ---")
print(sales.loc["2024-03-14"])
print(sales.loc["2024-02", "revenue"].sum().round(2))
print(sales.loc["2024-05-27":"2024-06-02"])
print(sales.loc["2024-06"].describe().round(2))

print("\n--- the .dt accessor ---")
frame = sales.reset_index()
frame["weekday"] = frame["day"].dt.day_name()
frame["month"] = frame["day"].dt.month_name()
frame["week"] = frame["day"].dt.isocalendar().week
frame["month_end"] = frame["day"].dt.is_month_end
print(frame.iloc[[10, 45, 91, 120, 181]])
print(frame.groupby("weekday")["orders"].mean().round(1).reindex(
    ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"]))

print("\n--- resample ---")
monthly = sales.resample("ME").agg({"orders": "sum", "revenue": "sum"})
monthly["avg_ticket"] = (monthly["revenue"] / monthly["orders"]).round(2)
print(monthly)
print(sales["orders"].resample("W").sum().head(5))
print(sales["revenue"].resample("QE").agg(["sum", "max"]).round(2))
print(sales["orders"].resample("2W-MON", closed="left", label="left").mean().round(1).head(4))

print("\n--- windows over time ---")
roll = sales["orders"].rolling("7D").mean().round(2)
print(roll.iloc[[0, 3, 6, 7, 30, 100]])
print(sales["orders"].rolling("30D", min_periods=30).max().dropna().head(3))
print(sales["revenue"].shift(7).iloc[5:10])
growth = (sales["revenue"].resample("ME").sum().pct_change() * 100).round(1)
print(growth)
print(sales["orders"].shift(1, freq="D").head(3))

print("\n--- parsing and arithmetic ---")
raw = pd.Series(["2024-03-01 08:15", "2024-03-02 17:40", "2024-03-05 09:05", "not a date"])
when = pd.to_datetime(raw, errors="coerce")
print(when)
print(when.dt.hour.tolist(), when.isna().sum())
shipped = pd.to_datetime(["2024-03-04", "2024-03-03", "2024-03-12"])
placed = pd.to_datetime(["2024-03-01", "2024-03-02", "2024-03-05"])
lead = shipped - placed
print(lead)
print(lead.days.tolist(), lead.max(), lead.mean())
print(pd.Timestamp("2024-03-29") + pd.offsets.BDay(2), pd.Timestamp("2024-01-31") + pd.offsets.MonthEnd(1))
print(pd.Timestamp("2024-02-29") + pd.DateOffset(years=1), pd.Timestamp("2024-07-15 13:45").floor("h"))
print(pd.date_range("2024-03-25", periods=5, freq="B").strftime("%a %d %b").tolist())
print(pd.Timedelta("2 days 03:30:00") * 3, pd.Timedelta(hours=90).round("D"))

print("\n--- grouping by calendar periods ---")
by_month_end = frame.groupby(pd.Grouper(key="day", freq="ME"))["orders"].agg(["count", "sum"])
print(by_month_end)
quarters = frame.set_index("day").groupby(pd.Grouper(freq="QE"))["revenue"].sum().round(2)
print(quarters)

print("\n--- dates in CSV ---")
csv = """shipped,carrier,days_in_transit
2024-03-01,ACME,2
2024-03-02,FAST,1
2024-03-04,ACME,3
2024-03-07,FAST,2
"""
ship = pd.read_csv(io.StringIO(csv), parse_dates=["shipped"])
ship["arrival"] = ship["shipped"] + pd.to_timedelta(ship["days_in_transit"], unit="D")
print(ship.dtypes)
print(ship)
print(ship.set_index("shipped").resample("3D")["days_in_transit"].mean())
print(ship.to_csv(index=False, date_format="%d/%m/%Y", lineterminator="\n"), end="")
