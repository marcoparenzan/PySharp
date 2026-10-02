import numpy as np
a = np.array([10.0, 20.0, 30.0, 40.0])
try:
    a[10]
    print(False)
except IndexError:
    print(True)
