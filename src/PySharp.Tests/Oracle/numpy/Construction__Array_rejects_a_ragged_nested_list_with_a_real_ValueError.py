import numpy as np
try:
    np.array([[1, 2], [3]])
    print(False)
except ValueError:
    print(True)
