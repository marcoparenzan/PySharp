import numpy as np
try:
    np.array([1.0]) & np.array([1.0])
    print(False)
except TypeError:
    print(True)
