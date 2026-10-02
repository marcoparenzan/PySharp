import numpy as np
c = np.array([1.0, 2.0, 3.0, 4.0])
c[c > 2] = 0.0
print(str(c))

d = np.array([1.0, 2.0, 3.0, 4.0])
d[d > 2] = np.array([100.0, 200.0])
print(str(d))
