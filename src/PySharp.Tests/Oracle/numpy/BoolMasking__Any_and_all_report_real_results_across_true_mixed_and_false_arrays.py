import numpy as np
a = np.array([1.0, 2.0, 3.0, 4.0])
print((a > 3).any())
print((a > 3).all())
print((a > 0).any())
print((a > 0).all())
print((a > 10).any())
print((a > 10).all())
