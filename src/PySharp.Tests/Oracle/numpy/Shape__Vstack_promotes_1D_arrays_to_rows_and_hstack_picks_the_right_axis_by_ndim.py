import numpy as np
v1 = np.array([1.0, 2.0, 3.0])
v2 = np.array([4.0, 5.0, 6.0])
print(str(np.vstack([v1, v2])))
print(str(np.hstack([v1, v2])))
x = np.array([[1.0, 2.0], [3.0, 4.0]])
print(str(np.hstack([x, x])))
