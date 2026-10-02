import numpy as np
b = np.array([3.0, 1.0, 4.0, 1.0, 5.0])
m = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
print(b.argmin())
print(b.argmax())
print(str(m.argmin(axis=0)))
print(str(m.argmax(axis=1)))
