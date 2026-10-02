import numpy as np
try:
    np.array([]).min()
    print(False)
except ValueError:
    print(True)
