import numpy as np
a = np.array([1.0, 2.0, 3.0])
print(np.array(a.tolist()).tolist() == a.tolist())
