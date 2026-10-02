import numpy as np
sq = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0], [7.0, 8.0, 10.0]])
print(np.trace(sq))
print(str(np.diagonal(sq)))
print(str(np.diagonal(sq, offset=1)))
print(str(np.diagonal(sq, offset=-1)))
