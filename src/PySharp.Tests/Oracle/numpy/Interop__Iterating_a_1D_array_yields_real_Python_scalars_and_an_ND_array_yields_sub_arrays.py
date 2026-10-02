import numpy as np
for x in np.array([1.0, 2.0]):
    print(x, isinstance(x, np.ndarray))
for row in np.array([[1.0, 2.0], [3.0, 4.0]]):
    print(str(row))
