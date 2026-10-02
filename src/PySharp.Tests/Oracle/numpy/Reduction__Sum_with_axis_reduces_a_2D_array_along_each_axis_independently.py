import numpy as np
m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
print(str(m.sum(axis=0)))
print(str(m.sum(axis=1)))
print(str(np.sum(m, axis=0)))
