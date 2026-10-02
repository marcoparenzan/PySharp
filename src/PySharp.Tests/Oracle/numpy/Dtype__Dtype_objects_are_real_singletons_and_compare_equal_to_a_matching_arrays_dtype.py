import numpy as np
print(np.int64 is np.int64)
print(np.array([1, 2]).dtype == np.int64)
print(np.array([1.0]).dtype == np.float64)
print(np.array([True]).dtype == np.bool_)
print(np.dtype(np.int64).name)
