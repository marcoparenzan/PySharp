import numpy as np
orig = np.array([1.0, 2.0, 3.0])
dup = orig.copy()
dup_module = np.copy(orig)
print(str(dup))
print(str(dup_module))
print(orig is dup)
