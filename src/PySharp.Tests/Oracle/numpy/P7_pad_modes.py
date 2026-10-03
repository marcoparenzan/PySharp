import numpy as np

a = np.arange(1, 6)
for mode in ["edge", "reflect", "symmetric", "wrap"]:
    print(mode, np.pad(a, 2, mode=mode), np.pad(a, (1, 7), mode=mode))
m = np.arange(6).reshape(2, 3)
for mode in ["edge", "reflect", "symmetric", "wrap"]:
    print(mode, np.pad(m, ((1, 2), (2, 1)), mode=mode).tolist())
print(np.pad(np.array([4.5]), 3, mode="edge"), np.pad(np.array([4.5]), 3, mode="reflect"), np.pad(m.astype(np.float32), 1, mode="edge").dtype)
