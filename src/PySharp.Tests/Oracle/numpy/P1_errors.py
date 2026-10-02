import numpy as np


def show(f):
    try:
        f()
    except Exception as ex:
        print(type(ex).__name__ + ": " + str(ex))


show(lambda: np.zeros((2, 3)) + np.zeros((4,)))
show(lambda: np.zeros((2, 3)).reshape(4, 2))
show(lambda: np.array([[1, 2], [3]]))
show(lambda: np.arange(6).reshape(2, 3)[2])
show(lambda: np.zeros(3)[5])
show(lambda: np.array([1, 2]) @ np.array([1, 2, 3]))
show(lambda: np.sum(np.zeros(3), axis=1))
show(lambda: np.array([1, 2, 3], dtype=np.uint8) + 300)
show(lambda: np.array([True]) - np.array([True]))
show(lambda: np.concatenate([np.zeros((2, 2)), np.zeros((3, 3))]))
show(lambda: np.max(np.array([])))
show(lambda: float(np.array([1.0, 2.0])))
show(lambda: bool(np.array([1, 2])))
show(lambda: len(np.array(5)))
show(lambda: np.stack([np.zeros(2), np.zeros(3)]))
show(lambda: np.array([1, 2]).reshape(-1, -1))
show(lambda: np.arange(3) ** -1)
