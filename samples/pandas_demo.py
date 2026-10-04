# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# pandas_demo.py — a pandas session driven end-to-end by PySharp (PySharp.Pandas over NDSharp.Frame).
#
# See PANDAS_PLAN.md. The output of this script is identical to real pandas 3.0 (verified by running
# it under CPython + pandas and diffing). Covers the Phase 1 surface: building Series/DataFrames,
# dtypes and missing values, selection (loc/iloc/masks/slices), assignment, copy-on-write,
# display options and pandas' truncated printing.
#
# Usage:  pysharp run samples/pandas_demo.py

import pandas as pd

print("--- a Series ---")
temps = pd.Series([21.5, 23.0, None, 19.25], index=["mon", "tue", "wed", "thu"], name="temp")
print(temps)
print(temps.dtype, temps.shape, temps["tue"], temps.iloc[-1])

print("\n--- a DataFrame from a dict ---")
df = pd.DataFrame({
    "city": ["Milano", "Roma", "Napoli", "Torino"],
    "pop_k": [1370, 2750, 910, 855],
    "area": [181.8, 1285.0, 119.0, 130.0],
    "north": [True, False, False, True],
})
print(df)
print(df.dtypes)

print("\n--- selection ---")
print(df["city"])
print(df[["city", "pop_k"]])
print(df[df["north"] if False else [True, False, False, True]])
print(df.loc[1:2, ["city", "area"]])
print(df.iloc[[0, 3], :2])
print(df.at[2, "city"], df.iat[0, 1])

print("\n--- assignment and copy-on-write ---")
df["density"] = [round(p * 1000 / a, 1) for p, a in zip(df["pop_k"], df["area"])]
df.loc[df.index > 1, "north"] = False
subset = df[["city", "pop_k"]]
subset.loc[0, "pop_k"] = -1
print(df["pop_k"].tolist(), subset["pop_k"].tolist())
df.loc[4] = ["Bari", 316, 117.4, False, 2692.2]
print(df)
print(df.drop(columns=["north"]).tail(2))

print("\n--- records and ragged data ---")
print(pd.DataFrame([{"a": 1, "b": 2}, {"a": 3, "c": 4.5}]))
print(pd.DataFrame([[1, "x"], [2, None]], columns=["n", "s"]))

print("\n--- printing: truncation and options ---")
big = pd.DataFrame({"n": range(100), "sq": [i * i / 7 for i in range(100)]})
print(big)
pd.set_option("display.max_rows", 6)
pd.set_option("display.precision", 2)
print(big)
pd.reset_option("display.max_rows")
pd.reset_option("display.precision")
wide = pd.DataFrame({"col%02d" % j: [j * 0.5, j * 2.0] for j in range(30)})
print(wide)
