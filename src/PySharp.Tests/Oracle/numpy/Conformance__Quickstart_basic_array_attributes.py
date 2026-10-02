import numpy as np
a = np.arange(15, dtype=np.int64).reshape(3, 5)
print(str(a))
print(a.shape)
print(a.ndim)
print(a.dtype.name)
print(a.size)
print(type(a).__name__)
