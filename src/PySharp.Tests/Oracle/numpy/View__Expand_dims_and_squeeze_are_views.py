import numpy as np
g = np.array([1.0, 2.0, 3.0])
np.expand_dims(g, axis=0)[0, 1] = 444.0
print(str(g))

h = np.array([[1.0], [2.0], [3.0]])
h.squeeze()[1] = 555.0
print(str(h))
