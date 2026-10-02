import numpy as np
p = np.array([1.0, 2.0, 3.0, 4.0, 5.0, 6.0])
pv = p[::2]
print(str(pv + 1.0))
print(pv.sum())
print(str(pv))

ww = np.array([[1.0, 2.0], [3.0, 4.0]])
print(str(ww.T @ ww))

xx = np.array([[1.0, 2.0], [3.0, 4.0]]).T
print(str(np.diagonal(xx)))
print(np.trace(xx))
yy = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0], [7.0, 8.0, 9.0]]).T
print(str(np.diagonal(yy, offset=-1)))

vv = np.array([[1.0, 2.0], [3.0, 4.0]]).T
print(str(np.concatenate([vv, vv], axis=0)))

ab = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
row = ab[0]
row[row > 1] = 0.0
print(str(ab))
