import numpy as np
a = np.arange(15, dtype=np.int64).reshape(3, 5)
for row in a:
    print(str(row))
