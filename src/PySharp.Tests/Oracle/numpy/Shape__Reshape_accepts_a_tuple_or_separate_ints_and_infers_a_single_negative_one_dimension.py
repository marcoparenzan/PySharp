import numpy as np
a = np.arange(6)
print(str(a.reshape(2, 3)))
print(str(a.reshape((3, 2))))
print(str(a.reshape(2, -1)))
