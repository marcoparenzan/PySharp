import numpy as np
v = np.array([1.0, 2.0, 3.0])
print(np.expand_dims(v, axis=0).shape)
print(np.expand_dims(v, axis=1).shape)
