import numpy as np
rng = np.random.default_rng(8)
print(np.round(rng.normal(loc=[-2, -1], scale=0.8, size=(3, 2)), 9))
print(np.round(rng.normal([0, 10, 20], [1, 2, 3]), 9))
print(np.round(rng.uniform([0, 5], [1, 6], size=(2, 2)), 9))
c = rng.multivariate_normal([0, 0], [[3, 1.5], [1.5, 1]], 500)
print(c.shape, c.dtype, bool(abs(c.mean()) < 0.3), np.round(np.cov(c.T), 0))
