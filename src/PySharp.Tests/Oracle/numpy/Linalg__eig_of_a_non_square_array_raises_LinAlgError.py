import numpy as np
try:
    np.linalg.eig(np.zeros((2, 3)))
except np.linalg.LinAlgError as e:
    print("LinAlgError:", e)
