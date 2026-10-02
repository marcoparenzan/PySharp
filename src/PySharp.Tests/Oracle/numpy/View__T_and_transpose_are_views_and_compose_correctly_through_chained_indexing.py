import numpy as np
m = np.array([[1.0, 2.0], [3.0, 4.0]])
m.T[0, 1] = 999.0
print(str(m))

m2 = np.array([[1.0, 2.0], [3.0, 4.0]])
m2.transpose()[1, 0] = 555.0
print(str(m2))

j = np.array([[1.0, 2.0], [3.0, 4.0]])
j.T[0, 1] = 777.0
print(str(j))
