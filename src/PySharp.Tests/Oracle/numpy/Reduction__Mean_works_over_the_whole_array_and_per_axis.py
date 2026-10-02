import numpy as np
a = np.array([1.0, 2.0, 3.0, 4.0])
m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
print(a.mean())
print(str(m.mean(axis=0)))
print(str(m.mean(axis=1)))
