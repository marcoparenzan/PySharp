import numpy as np
a = np.array([[1.0, 2.0], [3.0, 4.0]])
b = np.array([[5.0, 6.0], [7.0, 8.0]])
print(str(np.vstack((a, b))))
print(str(np.hstack((a, b))))
