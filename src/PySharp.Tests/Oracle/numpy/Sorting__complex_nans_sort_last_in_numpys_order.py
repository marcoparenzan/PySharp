import numpy as np
nan = float("nan")
a = np.array([complex(nan, 0), complex(1, nan), 1 + 0j, complex(nan, nan), complex(0, nan), complex(nan, 2), 2 + 0j])
s = np.sort(a)
print(s.real)
print(s.imag)
print(np.argsort(a, kind="stable"))
