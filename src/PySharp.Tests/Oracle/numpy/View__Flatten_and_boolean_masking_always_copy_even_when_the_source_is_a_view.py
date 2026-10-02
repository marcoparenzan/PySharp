import numpy as np
f = np.array([[1.0, 2.0], [3.0, 4.0]]).T
flat = f.flatten()
flat[0] = 333.0
print(str(f))
print(str(flat))

l = np.array([1.0, 2.0, 3.0, 4.0])
masked = l[l > 2]
masked[0] = 999.0
print(str(l))
