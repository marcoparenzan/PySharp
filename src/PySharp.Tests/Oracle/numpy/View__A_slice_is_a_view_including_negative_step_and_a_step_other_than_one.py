import numpy as np
a = np.array([10.0, 20.0, 30.0, 40.0])
a[1:3][0] = 999.0
print(str(a))

b = np.array([1.0, 2.0, 3.0, 4.0])
b[::-1][0] = 100.0
print(str(b))

c = np.array([1.0, 2.0, 3.0, 4.0, 5.0, 6.0])
c[::2][1] = 999.0
print(str(c))
