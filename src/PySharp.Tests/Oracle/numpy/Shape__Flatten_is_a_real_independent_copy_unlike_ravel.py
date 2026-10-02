import numpy as np
m = np.array([1.0, 2.0, 3.0, 4.0])
f = m.flatten()
f[0] = -1.0
print(str(m))
