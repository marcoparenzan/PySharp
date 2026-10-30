import numpy as np
nan = float("nan")
a = np.array([complex(nan, 0), complex(1, nan), 1 + 0j, complex(nan, nan), 2 + 0j, 1 + 0j, 1 + 1j])
u, idx, inv, cnt = np.unique(a, return_index=True, return_inverse=True, return_counts=True)
print(u.shape, u.real.tolist()[:3], u.imag.tolist()[:3])
print(idx.tolist(), inv.tolist(), cnt.tolist())
