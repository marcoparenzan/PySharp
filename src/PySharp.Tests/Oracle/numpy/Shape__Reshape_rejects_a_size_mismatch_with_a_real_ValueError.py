import numpy as np
try:
    np.arange(6).reshape(4, 2)
    print(False)
except ValueError:
    print(True)
