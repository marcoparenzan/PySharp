import numpy as np
n = np.array([1.0, 2.0, 3.0, 4.0])
nv = n[1:3]
nc = nv.copy()
nc[0] = 999.0
print(str(nv))
print(str(n))
