import numpy as np
i = np.array([1, 2, 3])
fi = i.astype(np.float64)
print(fi.dtype.name, str(fi))
back = fi.astype('int64')
print(back.dtype.name, str(back))
i2 = i.astype(np.int64)
i2[0] = 999
print(str(i))
