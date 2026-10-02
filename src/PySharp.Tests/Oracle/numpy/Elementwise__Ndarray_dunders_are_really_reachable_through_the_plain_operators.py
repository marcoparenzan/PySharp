import numpy as np
a = np.array([1.0])
print(hasattr(a, "__add__"))
print((a + a).shape == (1,))
