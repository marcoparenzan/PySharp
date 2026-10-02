import numpy as np
e1 = np.array([1.0, 2.0, 3.0, 4.0, 5.0, 6.0])
e1.reshape(2, 3)[0, 0] = 111.0
print(str(e1))

e2 = np.array([[1.0, 2.0], [3.0, 4.0]]).T
resh2 = e2.reshape(4)
resh2[0] = 222.0
print(str(e2))
print(str(resh2))

d2 = np.array([[1.0, 2.0], [3.0, 4.0]]).T
rav2 = d2.ravel()
rav2[0] = 888.0
print(str(d2))
print(str(rav2))
