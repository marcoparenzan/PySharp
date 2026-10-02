import numpy as np
a = np.arange(6)
b = a.reshape(2, 3)
b[0, 0] = 999.0
print(str(a))

m = np.array([1.0, 2.0, 3.0, 4.0])
r = m.ravel()
r[0] = 100.0
print(str(m))
