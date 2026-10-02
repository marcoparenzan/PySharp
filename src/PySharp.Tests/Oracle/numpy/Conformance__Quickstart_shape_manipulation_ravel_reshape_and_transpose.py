import numpy as np
a = np.array([[3.0, 7.0, 3.0, 4.0], [1.0, 4.0, 2.0, 2.0], [7.0, 2.0, 4.0, 9.0]])
print(str(a.ravel()))
print(str(a.reshape(6, 2)))
print(str(a.T))
print(a.T.shape)
