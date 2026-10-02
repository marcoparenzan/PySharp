import numpy as np
import matplotlib.pyplot as plt

def pos(ax):
    return tuple(round(float(v), 2) for v in ax.get_position().bounds)

fig, ax = plt.subplots()
print(fig.get_size_inches(), fig.dpi, pos(ax))
fig, axes = plt.subplots(2, 2, figsize=(8, 6))
print([pos(a) for a in axes.ravel()], axes.shape)
fig, axes = plt.subplots(1, 3, figsize=(12, 4))
for a in axes:
    a.imshow(np.random.default_rng(0).random((10, 20)), cmap="gray")
    a.set_title("img")
print([pos(a) for a in axes])
fig.tight_layout()
print([pos(a) for a in axes])
fig, ax = plt.subplots()
ax.plot([1, 2, 3])
ax.set_xlabel("x label")
ax.set_ylabel("y label")
ax.set_title("title")
fig.tight_layout()
print(pos(ax))
fig, axes = plt.subplots(2, 3, figsize=(10, 6))
for a in axes.ravel():
    a.plot([1, 2, 3])
    a.set_title("t")
fig.suptitle("super")
fig.tight_layout()
print([pos(a) for a in axes.ravel()])
fig, ax = plt.subplots()
im = ax.imshow(np.arange(12).reshape(3, 4))
fig.colorbar(im, ax=ax)
fig.tight_layout()
print([round(v, 1) for v in pos(ax)], [round(pos(fig.axes[1])[0], 1), round(pos(fig.axes[1])[2], 1)], [float(v) for v in im.get_clim()], len(fig.axes))
fig = plt.figure(figsize=(5, 5))
a1 = fig.add_subplot(2, 1, 1)
a2 = fig.add_subplot(2, 1, 2)
print(pos(a1), pos(a2))
sp = fig.subplotpars
fig, axes = plt.subplots(1, 3, figsize=(12, 4))
for a in axes:
    a.imshow(np.random.default_rng(0).random((10, 20)), cmap="gray")
    a.set_title("img")
fig.tight_layout()
sp = fig.subplotpars
print([round(float(v), 2) for v in (sp.left, sp.right, sp.bottom, sp.top, sp.wspace, sp.hspace)][:1] + [round(float(v), 2) for v in (sp.right, sp.bottom, sp.top, sp.wspace, sp.hspace)])
