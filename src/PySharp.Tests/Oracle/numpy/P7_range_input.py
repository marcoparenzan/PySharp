import numpy as np

print(np.array(range(4)), np.array(range(1, 10, 3)), np.asarray(range(3)).sum(), np.array(range(0)).shape)
print(np.array(range(5))[::2] * 2, np.zeros(3)[list(range(2))], (np.arange(4) + np.array(range(4))).tolist())
