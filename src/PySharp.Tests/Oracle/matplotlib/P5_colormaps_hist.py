import numpy as np
import matplotlib.pyplot as plt
import matplotlib.cm as cm

print(np.round(cm.viridis(0.0), 4), np.round(cm.viridis(0.5), 4), np.round(cm.viridis(1.0), 4))
print(np.round(plt.get_cmap("gray")(np.array([0.0, 0.25, 1.0])), 4))
t10 = plt.get_cmap("tab10")
print(t10.N, [tuple(round(c, 3) for c in t10.colors[i]) for i in (0, 3, 9)], np.round(t10(2), 3))
print(plt.get_cmap("hot").name, np.round(plt.get_cmap("coolwarm")(0.3), 4), np.round(plt.get_cmap("viridis_r")(0.0), 4))
data = np.array([1, 2, 2, 3, 3, 3, 4, 4, 4, 4, 5.5])
fig, ax = plt.subplots()
n, bins, patches = ax.hist(data, bins=5)
print(n, bins, len(patches))
n, bins, _ = ax.hist(data, bins=[0, 2, 4, 6], density=True)
print(np.round(n, 4), bins)
n, bins, _ = ax.hist(data, bins=3, range=(1, 4))
print(n, np.round(bins, 4))
fig, ax = plt.subplots()
ax.imshow(np.arange(6).reshape(2, 3), cmap="viridis")
ax.figure.canvas.draw()
print([float(v) for v in ax.get_xlim()], [float(v) for v in ax.get_ylim()], ax.get_aspect(), ax.get_xticks(), ax.get_yticks())
fig, ax = plt.subplots()
ax.imshow(np.arange(6).reshape(2, 3), origin="lower", extent=(0, 3, 0, 2), aspect="auto")
print([float(v) for v in ax.get_xlim()], [float(v) for v in ax.get_ylim()], ax.get_aspect())
