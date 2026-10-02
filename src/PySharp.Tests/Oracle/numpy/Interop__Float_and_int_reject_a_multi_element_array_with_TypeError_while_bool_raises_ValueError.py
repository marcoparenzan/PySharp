import numpy as np
try:
    float(np.array([1.0, 2.0]))
    print(False)
except TypeError:
    print(True)
try:
    bool(np.array([1.0, 2.0]))
    print(False)
except ValueError:
    print(True)
