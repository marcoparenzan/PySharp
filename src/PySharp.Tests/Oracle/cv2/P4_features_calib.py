import numpy as np
import cv2

rng = np.random.default_rng(21)
noise = rng.integers(0, 256, (140, 180), dtype=np.uint8)
tex = cv2.normalize(cv2.GaussianBlur(noise, (0, 0), 2.0), None, 0, 255, cv2.NORM_MINMAX)
print(tex.shape, tex.dtype, int(tex.sum()))

sift = cv2.SIFT_create()
kp, des = sift.detectAndCompute(tex, None)
print(len(kp), des.shape, des.dtype, type(kp).__name__, type(kp[0]).__name__)
for q in kp[:3]:
    print(tuple(round(v, 3) for v in q.pt), round(q.size, 3), round(q.angle, 3), round(q.response, 6), q.octave, q.class_id)
print(np.round(des[0][:10], 3), int(des.sum()))
print(len(cv2.SIFT_create(nfeatures=20).detect(tex, None)), len(cv2.SIFT_create(contrastThreshold=0.01).detect(tex, None)))

M = cv2.getRotationMatrix2D((90, 70), 15, 0.9)
rot = cv2.warpAffine(tex, M, (180, 140))
kp2, des2 = sift.detectAndCompute(rot, None)
bf = cv2.BFMatcher()
raw = bf.knnMatch(des, des2, k=2)
good = [m for m, n in raw if m.distance < 0.75 * n.distance]
print(len(kp2), len(raw), len(good), good[0].queryIdx, good[0].trainIdx, round(good[0].distance, 3), type(good[0]).__name__)
bf2 = cv2.BFMatcher(cv2.NORM_L2, crossCheck=True)
mm = bf2.match(des, des2)
mm = sorted(mm, key=lambda x: x.distance)
print(len(mm), mm[0].queryIdx, mm[0].trainIdx, round(mm[0].distance, 3))
vis = cv2.drawMatches(tex, kp, rot, kp2, good[:10], None, flags=cv2.DrawMatchesFlags_NOT_DRAW_SINGLE_POINTS)
print(vis.shape, vis.dtype, int(vis.sum()))
kv = cv2.drawKeypoints(tex, kp[:20], None, flags=cv2.DRAW_MATCHES_FLAGS_DRAW_RICH_KEYPOINTS)
print(kv.shape, int(kv.sum()))
src = np.float32([kp[m.queryIdx].pt for m in good])
dst = np.float32([kp2[m.trainIdx].pt for m in good])
H, mask = cv2.findHomography(src, dst, cv2.RANSAC, 3.0)
print(np.round(H, 4), mask.shape, mask.dtype, int(mask.sum()))
H0, m0 = cv2.findHomography(src[:4], dst[:4])
print(np.round(H0, 3), m0)

corners = cv2.goodFeaturesToTrack(tex, 25, 0.05, 8)
shifted = np.roll(tex, (2, 3), axis=(0, 1))
nxt, status, err = cv2.calcOpticalFlowPyrLK(tex, shifted, corners, None, winSize=(15, 15), maxLevel=2)
good_pts = status.ravel() == 1
print(nxt.shape, status.shape, status.dtype, err.dtype, int(good_pts.sum()), np.round((nxt - corners)[good_pts].reshape(-1, 2).mean(axis=0), 3))
flow = cv2.calcOpticalFlowFarneback(tex, shifted, None, pyr_scale=0.5, levels=3, winsize=15, iterations=3, poly_n=5, poly_sigma=1.2, flags=0)
print(flow.shape, flow.dtype, np.round(flow.reshape(-1, 2).mean(axis=0), 3))

left = tex.copy()
right = np.roll(tex, -4, axis=1)
stereo = cv2.StereoBM_create(numDisparities=32, blockSize=9)
disp = stereo.compute(left, right)
print(disp.shape, disp.dtype, int(disp.min()), int(disp.max()), int(np.median(disp[40:100, 50:150])))
stereo.setDisp12MaxDiff(1)
print(stereo.getDisp12MaxDiff(), stereo.getBlockSize(), stereo.getNumDisparities(), int(stereo.compute(left, right).max()))

K = np.array([[500.0, 0, 320], [0, 500.0, 240], [0, 0, 1]])
dist = np.array([0.1, -0.05, 0, 0, 0])
grid = np.zeros((35, 3), np.float32)
grid[:, :2] = np.mgrid[0:7, 0:5].T.reshape(-1, 2) * 0.03
objs, imgs = [], []
for rv, tv in [((0.1, 0.2, 0.0), (0, 0, 0.8)), ((-0.2, 0.1, 0.05), (0.02, -0.01, 0.9)), ((0.15, -0.25, 0.1), (-0.03, 0.02, 0.7)), ((0.0, 0.3, -0.1), (0.0, 0.0, 1.0)), ((0.3, 0.0, 0.2), (0.01, 0.01, 0.85))]:
    ip, jac = cv2.projectPoints(grid, np.array(rv), np.array(tv), K, dist)
    objs.append(grid)
    imgs.append(ip.astype(np.float32))
print(ip.shape, ip.dtype, np.round(ip[:3, 0], 4), jac is None)
rms, K2, d2, rvecs, tvecs = cv2.calibrateCamera(objs, imgs, (640, 480), None, None)
print(round(rms, 6), np.round(K2, 2), np.round(d2, 4), len(rvecs), rvecs[0].shape, type(rvecs).__name__)
und = cv2.undistortPoints(imgs[0].reshape(-1, 1, 2).astype(np.float64), K, dist).reshape(-1, 2)
print(np.round(und[:3], 6))

pts3 = rng.uniform(-1, 1, (40, 3)) + np.array([0, 0, 5.0])
Rt = cv2.Rodrigues(np.array([0.0, 0.1, 0.0]))[0] if hasattr(cv2, "Rodrigues") else np.eye(3)
t = np.array([[0.5], [0.0], [0.0]])
x1 = (K @ pts3.T).T
x1 = (x1[:, :2] / x1[:, 2:]).astype(np.float64)
cam2 = (Rt @ pts3.T + t).T
x2 = (K @ cam2.T).T
x2 = (x2[:, :2] / x2[:, 2:]).astype(np.float64)
F, fmask = cv2.findFundamentalMat(x1, x2, cv2.FM_8POINT)
print(np.round(F / F[2, 2], 6), fmask is None or fmask.shape)
E, emask = cv2.findEssentialMat(x1, x2, K, method=cv2.RANSAC, threshold=1.0)
print(np.round(E / np.linalg.norm(E), 4), emask.shape, int(emask.sum()))
n, R, tt, pmask = cv2.recoverPose(E, x1, x2, K)
print(n, np.round(R, 4), np.round(tt.ravel(), 4), pmask.shape)
P1 = K @ np.hstack([np.eye(3), np.zeros((3, 1))])
P2 = K @ np.hstack([R, tt])
X = cv2.triangulatePoints(P1, P2, x1.T, x2.T)
print(X.shape, X.dtype, np.round((X[:3] / X[3]).T[:2], 3))

big = cv2.normalize(cv2.GaussianBlur(rng.integers(0, 256, (200, 320), dtype=np.uint8), (0, 0), 3.0), None, 0, 255, cv2.NORM_MINMAX)
a, b = big[:, :200], big[:, 120:]
status, pano = cv2.Stitcher_create(cv2.Stitcher_SCANS).stitch([a, b])
print(status == cv2.Stitcher_OK, status, None if pano is None else pano.shape)
