import numpy as np
sq = np.array([[[1.0, 2.0, 3.0]]])
print(sq.shape)
print(sq.squeeze().shape)
print(np.array([[1.0], [2.0]]).squeeze(axis=1).shape)
try:
    np.array([[1.0, 2.0]]).squeeze(axis=1)
    print(False)
except ValueError:
    print(True)
