import numpy as np
a = np.array([[2.0, -1.0, 0.0], [1.0, 3.0, 1.0], [0.0, -2.0, 4.0]])
w, v = np.linalg.eig(a)
print(w.dtype, v.dtype, w.shape, v.shape)
print(np.sort(np.round(w.real, 6)))
print(np.sort(np.round(w.imag, 6)))
print(np.allclose(a @ v, v * w))
print(np.allclose(np.linalg.norm(v, axis=0), 1.0))
