import numpy as np
c = np.array([3 + 4j, 0j])
print(np.linalg.norm(c))
m = np.array([[3 + 4j, 1j], [0j, 1 + 0j]])
print(np.round(np.linalg.norm(m, axis=0), 6))
print(np.round(np.linalg.norm(m, axis=1), 6))
print(round(float(np.linalg.norm(m)), 6))
print(np.linalg.norm(np.array([3 + 4j, -2j]), 1), np.linalg.norm(np.array([3 + 4j, -2j]), np.inf))
