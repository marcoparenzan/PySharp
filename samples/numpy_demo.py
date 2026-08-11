# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# numpy_demo.py — a realistic numpy session, driven end-to-end by PySharp.
#
# See NUMPY_PLAN.md for the full phased plan. `numpy` here is a real C# `numpy`-shaped shim over
# this repo's own `ndarray` type (real numpy is a compiled CPython C extension a from-scratch
# interpreter can't load) — not a reimplementation of numpy's actual algorithms, but real strides/
# broadcasting/dtype-promotion/views, verified against real numpy's own documented semantics.
# Exercises: construction, dtypes, real views (a slice/`.T` mutating the source), broadcasting,
# reductions, boolean masking, linear algebra, and `np.random`.
#
# Usage:  pysharp run samples/numpy_demo.py

import numpy as np

print("--- construction & dtypes ---")
ints = np.array([1, 2, 3, 4, 5])
floats = np.array([1.5, 2.5, 3.5])
print(f"ints:   {ints}  dtype={ints.dtype.name}")
print(f"floats: {floats}  dtype={floats.dtype.name}")
grid = np.arange(12.0).reshape(3, 4)
print("grid:")
print(grid)

print("\n--- real views (Phase 12.1) ---")
view_demo = grid.copy()
row = view_demo[1]
row[0] = 999.0
print(f"view_demo[1] is a view — mutating it changed view_demo:\n{view_demo}")
transposed = grid.T
print(f"grid.T is also a real view, shape {transposed.shape}, sharing the same buffer")

print("\n--- broadcasting ---")
column = np.array([[10.0], [20.0], [30.0]])
print(f"grid + column (broadcast {grid.shape} with {column.shape}):")
print(grid + column)

print("\n--- reductions ---")
print(f"grid.sum() = {grid.sum()}")
print(f"grid.sum(axis=0) = {grid.sum(axis=0)}")
print(f"grid.mean() = {grid.mean():.4f}")

print("\n--- boolean masking ---")
big = grid[grid > 5.0]
print(f"grid[grid > 5.0] = {big}")

print("\n--- linear algebra ---")
a = np.array([[1.0, 2.0], [3.0, 4.0]])
b = np.array([[5.0, 6.0], [7.0, 8.0]])
print(f"a @ b =\n{a @ b}")
print(f"np.linalg.norm([3, 4]) = {np.linalg.norm(np.array([3.0, 4.0]))}")
print(f"np.trace(a) = {np.trace(a)}")

print("\n--- np.random (seeded, reproducible within this shim) ---")
np.random.seed(42)
sample = np.random.rand(3)
print(f"np.random.rand(3) = {sample}")
dice = np.random.randint(1, 7, size=5)
print(f"np.random.randint(1, 7, size=5) = {dice}")

print("\n--- interop ---")
print(f"grid.tolist() = {grid.tolist()}")
