import numpy as np
a = np.array([1.0, 2.0, 3.0, 4.0])
m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
print(str(a.cumsum()))
print(str(a.cumprod()))
print(str(m.cumsum(axis=0)))
print(str(m.cumsum(axis=1)))
