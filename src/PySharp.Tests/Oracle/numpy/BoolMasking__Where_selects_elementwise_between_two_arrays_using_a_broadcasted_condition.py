import numpy as np
a = np.array([1.0, 2.0, 3.0, 4.0])
print(str(np.where(a > 2, a, 0.0)))
print(str(np.where(np.array([True, False]), np.array([1.0, 2.0]), np.array([10.0, 20.0]))))
