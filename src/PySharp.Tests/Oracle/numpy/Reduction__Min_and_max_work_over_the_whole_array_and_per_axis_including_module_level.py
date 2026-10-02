import numpy as np
a = np.array([1.0, 2.0, 3.0, 4.0])
m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
print(a.min())
print(a.max())
print(np.min(a))
print(np.max(a))
print(str(m.min(axis=0)))
print(str(m.max(axis=1)))
