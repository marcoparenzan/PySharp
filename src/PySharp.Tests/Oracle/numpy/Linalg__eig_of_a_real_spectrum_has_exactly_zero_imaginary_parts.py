import numpy as np
a = np.array([[4.0, 1.0, 2.0], [0.0, 3.0, 5.0], [0.0, 0.0, -1.0]])
w, v = np.linalg.eig(a)
print(w.dtype, v.dtype)
print(np.sort(np.round(w.real, 6)))
print(np.abs(w.imag).max(), np.abs(v.imag).max())
print(np.allclose(a @ v, v * w))
