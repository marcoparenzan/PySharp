import numpy as np
f = np.array([1.0, 2.0, 3.0, 4.0])
f[1:3] = np.array([100.0, 200.0])
print(str(f))
try:
    f[1:3] = np.array([1.0, 2.0, 3.0])
    print(False)
except ValueError:
    print(True)
