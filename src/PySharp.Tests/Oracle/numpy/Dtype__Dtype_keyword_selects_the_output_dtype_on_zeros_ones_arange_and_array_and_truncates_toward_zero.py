import numpy as np
z = np.zeros(3, dtype=np.int64)
print(z.dtype.name, str(z))
o = np.ones((2,), dtype='int64')
print(o.dtype.name, str(o))
ar = np.arange(5, dtype=np.int64)
print(ar.dtype.name, str(ar))
print(str(np.array([1.9, 2.1], dtype=np.int64)))
