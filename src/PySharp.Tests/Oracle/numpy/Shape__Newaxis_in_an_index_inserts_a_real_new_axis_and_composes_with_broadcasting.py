import numpy as np
row = np.array([1.0, 2.0, 3.0])
print(row[:, np.newaxis].shape)
print(row[np.newaxis, :].shape)
print(str(row[:, None]))
print(str(row[:, None] + row[None, :]))
