import numpy as np
k = np.array([1.0, 2.0, 3.0, 4.0])
print(k.base is None)
print(k[1:3].base is not None)
print(k.copy().base is None)
print(k.reshape(2, 2).base is not None)
