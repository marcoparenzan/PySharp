# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# pandas_analysis_demo.py — a small data-analysis session on PySharp.Pandas (Phase 2: computation).
#
# See PANDAS_PLAN.md. The output is identical to real pandas 3.0 (checked by running the script under
# CPython + pandas and diffing). Covers arithmetic with index alignment, comparisons and boolean
# filtering, reductions, missing data, sorting and ranking helpers, apply/map/agg and the .str accessor.
#
# Usage:  pysharp run samples/pandas_analysis_demo.py

import numpy as np
import pandas as pd

orders = pd.DataFrame({
    "customer": ["  anna ", "BOB", "carla", None, "dario", "Elena"],
    "units": [3, 10, 4, 7, None, 2],
    "price": [19.99, 5.5, 120.0, 12.25, 8.0, 64.9],
    "express": [True, False, False, True, False, True],
})
print(orders)
orders.info()

print("\n--- cleaning with .str ---")
orders["customer"] = orders["customer"].str.strip().str.title()
print(orders["customer"].tolist())
print(orders["customer"].str.len().tolist())
print(orders[orders["customer"].str.contains("a", case=False)])

print("\n--- missing data ---")
print(orders.isna().sum())
orders["units"] = orders["units"].fillna(orders["units"].median())
orders["customer"] = orders["customer"].fillna("(unknown)")
print(orders)

print("\n--- arithmetic and filtering ---")
orders["total"] = orders["units"] * orders["price"]
orders["total"] = orders["total"].round(2)
orders["shipping"] = (orders["express"] * 4.5).where(orders["total"] < 100, 0.0)
orders["grand"] = orders["total"] + orders["shipping"]
print(orders[["customer", "total", "shipping", "grand"]])
big = orders[(orders["grand"] > 50) & ~orders["express"]]
print(big["customer"].tolist())

print("\n--- reductions ---")
print(orders[["units", "total", "grand"]].sum())
print(orders["grand"].describe())
print(orders[["units", "price"]].agg(["min", "max", "mean"]))
print(orders["total"].idxmax(), orders.loc[orders["total"].idxmax(), "customer"])
print((orders["grand"] / orders["grand"].sum()).round(3).tolist())

print("\n--- sorting ---")
print(orders.sort_values("grand", ascending=False)[["customer", "grand"]].head(3))
print(orders.nlargest(2, "units")[["customer", "units"]])
print(orders["express"].value_counts())

print("\n--- apply / map ---")
tiers = orders["grand"].apply(lambda g: "high" if g > 100 else "mid" if g > 30 else "low")
print(tiers.value_counts())
print(orders["express"].map({True: "fast", False: "normal"}).tolist())
print(orders[["units", "price"]].apply(lambda col: col.max() - col.min()))
print(np.sqrt(orders["units"]).round(2).tolist())
print(orders.set_index("customer")["grand"].sort_index())
