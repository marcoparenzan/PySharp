# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# pandas_rolling_demo.py — rolling windows, ranking and plotting on PySharp.Pandas (Phase 4).
#
# See PANDAS_PLAN.md. The printed output is identical to real pandas 3.0 and numpy 2.x (checked by running the
# script under CPython and diffing; the random walk comes from numpy's default_rng, bit for bit). Covers
# rolling/expanding statistics, rank, and df.plot() — which draws through PySharp.Matplotlib and writes PNG files.
#
# Usage:  pysharp run samples/pandas_rolling_demo.py

import os
import tempfile

import matplotlib
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd

rng = np.random.default_rng(7)
steps = rng.normal(0.1, 1.0, size=60)
price = pd.Series(100 + steps.cumsum(), name="price")
print(price.head(5).round(3).tolist())

print("\n--- rolling and expanding ---")
frame = pd.DataFrame({
    "price": price,
    "ma5": price.rolling(5).mean(),
    "ma20": price.rolling(20, min_periods=10).mean(),
    "vol10": price.rolling(10).std(),
    "hi10": price.rolling(10).max(),
})
print(frame.iloc[[4, 9, 19, 29, 59]].round(3))
print(frame.rolling(3).mean().iloc[[5, 6]].round(4))
print(price.expanding().mean().iloc[[0, 9, 59]].round(4).tolist())
print(price.rolling(5, center=True).median().iloc[[2, 30]].round(4).tolist())
print(price.diff().rolling(5).sum().iloc[[5, 6]].round(4).tolist())
print(price.rolling(7).apply(lambda w: w.max() - w.min()).dropna().round(3).describe().round(3))

print("\n--- rank ---")
returns = price.pct_change().dropna()
ranked = returns.rank(ascending=False)
print(returns.round(4).nlargest(3).index.tolist(), ranked.nsmallest(3).index.tolist())
scores = pd.DataFrame({"team": list("abcdef"), "pts": [30, 25, 30, 10, 25, 40]})
scores["rank"] = scores["pts"].rank(method="min", ascending=False).astype(int)
scores["dense"] = scores["pts"].rank(method="dense", ascending=False).astype(int)
scores["pct"] = scores["pts"].rank(pct=True).round(3)
print(scores.sort_values("rank"))

print("\n--- plotting ---")
out = tempfile.gettempdir()
ax = frame[["price", "ma5", "ma20"]].plot(title="Random walk and its averages", grid=True)
plt.savefig(os.path.join(out, "pysharp_walk.png"))
plt.close("all")
scores.set_index("team")[["pts"]].plot.bar(legend=False, title="Points")
plt.savefig(os.path.join(out, "pysharp_points.png"))
plt.close("all")
price.plot.hist(bins=8, title="Distribution of price")
plt.savefig(os.path.join(out, "pysharp_hist.png"))
sizes = [os.path.getsize(os.path.join(out, n)) > 1000 for n in ("pysharp_walk.png", "pysharp_points.png", "pysharp_hist.png")]
print("plots written:", sizes)
