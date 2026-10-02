import numpy as np
import cv2
rng = np.random.default_rng(5)
img = rng.integers(0, 256, (6, 8, 3), dtype=np.uint8)
print(img.shape, img.dtype)
gray = cv2.cvtColor(img, cv2.COLOR_BGR2GRAY)
print(gray.shape, gray.dtype, gray)
print(cv2.cvtColor(img, cv2.COLOR_BGR2RGB)[0, :3], cv2.cvtColor(gray, cv2.COLOR_GRAY2BGR).shape, cv2.cvtColor(gray, cv2.COLOR_GRAY2RGB)[0, 0])
print(cv2.cvtColor(img, cv2.COLOR_BGR2HSV)[:2, :3], cv2.cvtColor(img, cv2.COLOR_BGR2LAB)[:2, :3], cv2.cvtColor(img, cv2.COLOR_BGR2YCrCb)[:2, :3])
f = img.astype(np.float32) / 255
print(cv2.cvtColor(f, cv2.COLOR_BGR2GRAY)[:2, :3], cv2.cvtColor(f, cv2.COLOR_BGR2HSV)[:2, :3])
print(cv2.resize(img, (4, 3)).shape, cv2.resize(img, (4, 3))[:, :, 0], cv2.resize(img, (16, 12), interpolation=cv2.INTER_NEAREST)[:2, :4, 0])
print(cv2.resize(gray, None, fx=2, fy=2)[:3, :5], cv2.resize(gray, (3, 2), interpolation=cv2.INTER_AREA), cv2.resize(gray, (12, 9), interpolation=cv2.INTER_CUBIC)[:2, :6])
print(cv2.flip(gray, 0)[:2], cv2.flip(gray, 1)[:2], cv2.flip(gray, -1)[:2])
b, g, r = cv2.split(img)
print(b.shape, np.array_equal(cv2.merge([b, g, r]), img), type(cv2.split(img)).__name__)
print(cv2.copyMakeBorder(gray, 1, 2, 3, 1, cv2.BORDER_CONSTANT, value=7)[:4, :6])
print(cv2.copyMakeBorder(gray, 2, 2, 2, 2, cv2.BORDER_REFLECT)[:3, :5], cv2.copyMakeBorder(gray, 2, 2, 2, 2, cv2.BORDER_REFLECT_101)[:3, :5], cv2.copyMakeBorder(gray, 1, 1, 1, 1, cv2.BORDER_REPLICATE)[:3, :5], cv2.copyMakeBorder(gray, 1, 1, 1, 1, cv2.BORDER_WRAP)[:3, :5])
