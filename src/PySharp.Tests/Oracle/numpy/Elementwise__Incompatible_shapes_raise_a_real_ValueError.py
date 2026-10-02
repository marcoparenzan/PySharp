import numpy as np
try:
    np.array([1.0, 2.0, 3.0]) + np.array([1.0, 2.0])
    print(False)
except ValueError:
    print(True)
