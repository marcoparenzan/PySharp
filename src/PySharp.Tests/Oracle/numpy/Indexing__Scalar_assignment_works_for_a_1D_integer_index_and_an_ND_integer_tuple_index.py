import numpy as np
c = np.array([1.0, 2.0, 3.0])
c[1] = 99.0
print(str(c))

d = np.array([[1.0, 2.0], [3.0, 4.0]])
d[0, 1] = 42.0
print(str(d))
