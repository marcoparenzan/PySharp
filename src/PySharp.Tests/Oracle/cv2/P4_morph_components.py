import numpy as np
import cv2
bw = np.zeros((9, 12), np.uint8)
bw[1:4, 1:4] = 255
bw[5:8, 6:11] = 255
bw[2, 8] = 255
print(cv2.getStructuringElement(cv2.MORPH_RECT, (3, 3)), cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (5, 5)), cv2.getStructuringElement(cv2.MORPH_CROSS, (3, 3)))
k = np.ones((3, 3), np.uint8)
print(cv2.erode(bw, k), cv2.dilate(bw, k), cv2.erode(bw, k, iterations=2))
print(cv2.morphologyEx(bw, cv2.MORPH_OPEN, k), cv2.morphologyEx(bw, cv2.MORPH_CLOSE, k), cv2.morphologyEx(bw, cv2.MORPH_GRADIENT, k), cv2.morphologyEx(bw, cv2.MORPH_TOPHAT, k))
print(cv2.dilate(bw, None).sum(), cv2.erode(bw, np.ones((1, 3), np.uint8)).sum())
n, labels = cv2.connectedComponents(bw)
print(n, labels, labels.dtype)
n, labels, stats, cent = cv2.connectedComponentsWithStats(bw, connectivity=4)
print(n, stats, np.round(cent, 4), stats.dtype, cent.dtype)
print(cv2.connectedComponents(bw, connectivity=4)[0])
img = np.zeros((7, 7), np.uint8)
img[2:5, 2:5] = 200
fill = img.copy()
ret, out, mask, rect = cv2.floodFill(fill, None, (3, 3), 77)
print(ret, rect, out is fill, fill)
mask = np.zeros((9, 9), np.uint8)
ret, out, mask, rect = cv2.floodFill(img.copy(), mask, (0, 0), 255, flags=4 | cv2.FLOODFILL_MASK_ONLY | (1 << 8))
print(ret, rect, mask)
