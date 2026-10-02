import numpy as np
a = np.arange(24).reshape(2, 3, 4)
print(a[1, 2, 3], a[1][2][3], a[-1, -1, -1])
print(a[0])
print(a[:, 1])
print(a[..., 0])
print(a[:, :, 1:3].shape)
print(a[::-1, ::2, ::-2])
print(a[[0, 1], [1, 2]])
print(a[[0, 1], :, [1, 2]].shape)
print(a[:, [0, 2], :].shape)
m = a % 5 == 0
print(m.sum(), a[m])
print(a[a > 20])
b = np.arange(10)
print(b[[1, 3, -1]], b[b % 2 == 0], b[np.array([True] * 5 + [False] * 5)])
print(b[np.array([[0, 1], [2, 3]])])
print(np.where(b > 5, b, -b))
print(np.where(b > 5))
print(b[None, :].shape, b[:, None].shape)
print(b[3:], b[:-3], b[-3:], b[::3], b[7:2:-2])
try:
    b[10]
except IndexError as ex:
    print(ex)
try:
    a[0, 0, 0, 0]
except IndexError as ex:
    print(ex)
x = np.zeros((3, 3))
x[[0, 1, 2], [0, 1, 2]] = 1
print(x)
x[1:, :2] = np.array([[5, 6], [7, 8]])
print(x)
print(x[x > 4])
print(np.argmax(x))
