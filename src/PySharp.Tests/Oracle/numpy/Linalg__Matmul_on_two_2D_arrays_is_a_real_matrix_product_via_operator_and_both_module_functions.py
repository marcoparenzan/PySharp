import numpy as np
m1 = np.array([[1.0, 2.0], [3.0, 4.0]])
m2 = np.array([[5.0, 6.0], [7.0, 8.0]])
print(str(m1 @ m2))
print(str(np.matmul(m1, m2)))
print(str(np.dot(m1, m2)))
a = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
b = np.array([[1.0, 0.0], [0.0, 1.0], [1.0, 1.0]])
print(str(a @ b))
