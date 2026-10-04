import numpy as np

rng = np.random.default_rng(5)
print(rng.choice(5, size=10, p=[0.1, 0.2, 0.3, 0.2, 0.2]).tolist())
print([str(v) for v in rng.choice(['a', 'b', 'c'], size=8, p=[0.5, 0.3, 0.2])])
print(rng.choice([10, 20, 30, 40], size=3, replace=False, p=[0.4, 0.3, 0.2, 0.1]).tolist())
print(rng.choice(4, p=[0.25, 0.25, 0.25, 0.25]), str(rng.choice(['x', 'y'], p=[0.9, 0.1])))
print(rng.choice(6, size=(2, 3), p=[0.05, 0.05, 0.1, 0.2, 0.3, 0.3]).tolist())
print(rng.choice(np.array([1.5, 2.5, 3.5]), size=5, p=[0.2, 0.3, 0.5]).tolist())
try:
    rng.choice(3, p=[0.5, 0.2, 0.2])
except ValueError as e:
    print('ValueError', e)
