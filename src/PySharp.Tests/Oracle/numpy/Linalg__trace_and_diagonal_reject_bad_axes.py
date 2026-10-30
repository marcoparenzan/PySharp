import numpy as np
a = np.zeros((2, 3, 4))
for f in (lambda: np.trace(a, axis1=1, axis2=1), lambda: np.diagonal(np.zeros(3)), lambda: np.trace(a, axis1=0, axis2=3)):
    try:
        f()
    except Exception as e:
        print(type(e).__name__)
