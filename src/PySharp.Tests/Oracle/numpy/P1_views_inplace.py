import numpy as np
a = np.arange(10)
v = a[2:5]
v[0] = 99
print(a, v.base is a)
b = a.reshape(2, 5)
b[0, 0] = -1
print(a)
a += 1
print(a, b[0])
c = np.zeros((3, 3), dtype=np.uint8)
c[1:, 1:] = 200
c += 100
print(c)
d = np.arange(6).reshape(2, 3)
d *= 2
print(d)
e = np.array([1.5, 2.5])
e -= 1
print(e)
try:
    i = np.array([1, 2, 3])
    i += 1.5
except TypeError as ex:
    print(type(ex).__name__)
t = d.T
t[0, 1] = 777
print(d)
print(d.T.shape)
f = a.copy()
f[:] = 0
print(a.sum(), f.sum())
g = np.arange(6).reshape(2, 3)
g[g > 2] = 0
print(g)
g[:, 1] = [10, 20]
print(g)
g[0] = 5
print(g)
