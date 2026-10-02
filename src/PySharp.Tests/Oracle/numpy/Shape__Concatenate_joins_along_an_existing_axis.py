import numpy as np
x = np.array([[1.0, 2.0], [3.0, 4.0]])
y = np.array([[5.0, 6.0]])
print(str(np.concatenate([x, y], axis=0)))
print(str(np.concatenate([x, x], axis=1)))
