import numpy as np
print(str(np.floor(np.array([1.2, -1.2, 2.7]))))
print(str(np.ceil(np.array([1.2, -1.2, 2.7]))))
print(str(np.sign(np.array([-5.0, 0.0, 5.0]))))
print(str(np.clip(np.array([-5.0, 0.0, 5.0, 10.0]), 0.0, 5.0)))
