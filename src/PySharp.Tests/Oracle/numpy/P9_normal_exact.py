import numpy as np

rng = np.random.default_rng(1)
x = rng.normal(size=200000)
print(repr(float(x[0])), repr(float(x[1])), repr(float(x[2])))
print(repr(float(x.sum())), repr(float(x.min())), repr(float(x.max())), repr(float(x[-1])))
print(repr(float(np.abs(x).max())), int((x > 3.6541528853610088).sum()))
rng2 = np.random.default_rng(42)
y = rng2.standard_normal(size=100000)
print(repr(float(y.mean())), repr(float(y.std())), repr(float(y[12345])))
print(rng2.normal(5.0, 2.0, size=3).tolist())
