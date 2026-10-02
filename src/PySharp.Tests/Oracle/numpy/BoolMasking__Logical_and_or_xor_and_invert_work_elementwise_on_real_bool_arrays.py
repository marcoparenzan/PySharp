import numpy as np
m1 = np.array([True, True, False, False])
m2 = np.array([True, False, True, False])
print(str(m1 & m2))
print(str(m1 | m2))
print(str(m1 ^ m2))
print(str(~m1))
