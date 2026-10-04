# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# pandas_categorical_demo.py — categorical data, pd.cut and pd.qcut on PySharp.Pandas (Phase 6).
#
# See PANDAS_PLAN.md. The output is identical to real pandas 3.0 (checked by running the script under CPython +
# pandas and diffing). Covers ordered and unordered categoricals, the .cat accessor, binning numbers into labelled
# or interval categories, and how groupby / value_counts / crosstab treat categories that never occur.
#
# Usage:  pysharp run samples/pandas_categorical_demo.py

import numpy as np
import pandas as pd

rng = np.random.default_rng(11)
n = 40
survey = pd.DataFrame({
    "age": rng.integers(16, 80, size=n),
    "satisfaction": rng.choice(["very low", "low", "ok", "high"], size=n, p=[0.1, 0.2, 0.4, 0.3]),
    "channel": rng.choice(["web", "shop", "phone"], size=n),
    "spend": np.abs(rng.normal(90.0, 60.0, size=n)).round(2),
})
print(survey.head(6))

print("\n--- ordered categories ---")
levels = pd.CategoricalDtype(["very low", "low", "ok", "high", "very high"], ordered=True)
survey["satisfaction"] = survey["satisfaction"].astype(levels)
survey["channel"] = survey["channel"].astype("category")
print(survey.dtypes)
print(survey["satisfaction"].cat.categories.tolist(), survey["satisfaction"].cat.ordered)
print(survey["satisfaction"].value_counts())
print(survey["satisfaction"].min(), survey["satisfaction"].max())
print(survey[survey["satisfaction"] >= "high"].shape[0], "customers at least 'high'")
print(survey.sort_values(["satisfaction", "spend"], ascending=[False, False]).head(3)[["satisfaction", "spend"]])

print("\n--- grouping keeps unused categories on request ---")
print(survey.groupby("satisfaction", observed=True)["spend"].mean().round(2))
print(survey.groupby("satisfaction", observed=False)["spend"].agg(["count", "mean"]).round(2))
print(pd.crosstab(survey["satisfaction"], survey["channel"]))

print("\n--- binning ---")
survey["age_group"] = pd.cut(survey["age"], [15, 25, 40, 60, 80], labels=["16-25", "26-40", "41-60", "61-80"])
survey["spend_band"] = pd.qcut(survey["spend"], 4, labels=["Q1", "Q2", "Q3", "Q4"])
survey["age_bin"] = pd.cut(survey["age"], 3)
print(survey["age_bin"].cat.categories)
print(survey["age_group"].value_counts(sort=False))
print(survey.groupby("age_group", observed=True)["spend"].median().round(2))
print(survey.pivot_table(index="age_group", columns="spend_band", values="age", aggfunc="count", observed=False))
print(pd.qcut(survey["spend"], 3).value_counts().sort_index())

print("\n--- the .cat accessor ---")
tidy = survey["channel"].cat.rename_categories({"web": "Web", "shop": "Shop", "phone": "Phone"})
print(tidy.cat.categories.tolist(), tidy.value_counts().to_dict())
print(tidy.cat.add_categories("Kiosk").cat.remove_unused_categories().cat.categories.tolist())
print(pd.get_dummies(survey["channel"]).sum().to_dict())
