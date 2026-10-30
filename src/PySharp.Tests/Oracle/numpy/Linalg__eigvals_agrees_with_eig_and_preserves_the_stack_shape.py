import numpy as np
rng = np.random.default_rng(5)
a = rng.standard_normal((3, 4, 4))
w, v = np.linalg.eig(a)
w2 = np.linalg.eigvals(a)
print(w.shape, v.shape, w2.shape)
print(np.allclose(np.sort(np.abs(w), axis=1), np.sort(np.abs(w2), axis=1)))
print(np.allclose(w.sum(axis=1).real, [np.trace(m) for m in a]))
