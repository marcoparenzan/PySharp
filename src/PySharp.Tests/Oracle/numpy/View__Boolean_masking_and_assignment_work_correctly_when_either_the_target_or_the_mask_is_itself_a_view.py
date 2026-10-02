import numpy as np
ss = np.array([1.0, 2.0, 3.0, 4.0, 5.0])
full_mask = np.array([True, False, True, False, True, False, True])
sliced_mask = full_mask[0:5]
print(str(ss[sliced_mask]))

tt = np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]])
row0 = tt[0]
row0[1:3] = np.array([100.0, 200.0])
print(str(tt))
