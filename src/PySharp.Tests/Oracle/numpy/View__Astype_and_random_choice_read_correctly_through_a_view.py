import numpy as np
zz = np.array([1, 2, 3, 4])[1:3]
zc = zz.astype(np.float64)
print(zc.dtype.name, str(zc))

np.random.seed(1)
pool = np.array([10.0, 20.0, 30.0, 40.0, 50.0])[1:4]
picked = np.random.choice(pool, size=10)
print(all(v in (20.0, 30.0, 40.0) for v in picked.tolist()))
