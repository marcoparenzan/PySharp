import numpy as np
b = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
row = b[0]
print(row.shape)
print(str(row))

g = np.array([[1.0, 2.0], [3.0, 4.0]])
sub = g[0]
sub[0] = 999.0
print(str(g))
