import numpy as np
s = np.array([42.0][0])
print(s.ndim)
print(s.size)
try:
    len(s)
    print(False)
except TypeError:
    print(True)
