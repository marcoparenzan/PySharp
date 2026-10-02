import numpy as np
import cv2
rng = np.random.default_rng(6)
a = rng.integers(0, 256, (4, 5), dtype=np.uint8)
b = rng.integers(0, 256, (4, 5), dtype=np.uint8)
print(cv2.add(a, b), cv2.subtract(a, b), cv2.absdiff(a, b))
print(cv2.add(a, 100), cv2.subtract(a, 100))
print(cv2.multiply(a, b), cv2.divide(a, b + 1), cv2.multiply(a, b, scale=0.01))
print(cv2.addWeighted(a, 0.7, b, 0.3, 10), cv2.addWeighted(a, 0.5, b, 0.5, 0, dtype=cv2.CV_32F))
print(cv2.bitwise_and(a, b), cv2.bitwise_or(a, b), cv2.bitwise_xor(a, b), cv2.bitwise_not(a))
print(cv2.normalize(a, None, 0, 255, cv2.NORM_MINMAX), cv2.normalize(a.astype(np.float32), None, 0, 1, cv2.NORM_MINMAX))
for t in (cv2.THRESH_BINARY, cv2.THRESH_BINARY_INV, cv2.THRESH_TRUNC, cv2.THRESH_TOZERO, cv2.THRESH_TOZERO_INV):
    r, d = cv2.threshold(a, 100, 255, t)
    print(t, r, d)
r, d = cv2.threshold(a, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
print(r, d, d.dtype)
print(type(r).__name__, cv2.threshold(a.astype(np.float32), 100.5, 1, cv2.THRESH_BINARY)[1])
