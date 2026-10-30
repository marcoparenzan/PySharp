import numpy as np
for x in (np.array([3, -1, 2, -1], dtype=np.int8), np.array([2.5, -1.0, 2.5, 0.0], dtype=np.float32), np.array([[3, 1], [2, 0]])):
    r = np.sort_complex(x)
    print(r.dtype, r.shape, r.real.tolist(), r.imag.tolist())
c = np.array([1 + 2j, 1 - 2j, 0 + 5j])
print(np.sort_complex(c))
