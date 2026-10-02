import numpy as np
row = np.array([1.0, 2.0])
mat = np.array([[1.0, 2.0], [3.0, 4.0]])
print(str(row @ mat))
print((row @ mat).shape)
col = np.array([1.0, 2.0])
print(str(mat @ col))
print((mat @ col).shape)
