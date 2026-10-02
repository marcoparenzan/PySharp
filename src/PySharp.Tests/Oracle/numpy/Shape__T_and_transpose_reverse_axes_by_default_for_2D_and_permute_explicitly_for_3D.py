import numpy as np
m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
print(str(m.T))
print(m.transpose().shape)
t3 = np.arange(24.0).reshape(2, 3, 4)
print(t3.transpose(1, 0, 2).shape)
