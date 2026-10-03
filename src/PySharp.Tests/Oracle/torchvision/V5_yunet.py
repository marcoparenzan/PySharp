# Needs the cvintro course photo (env CVINTRO_DIR, default D:/clones/sbirchfield.github.io/cvintro) and downloads the 230 KB YuNet model once.
import os
import urllib.request
from pathlib import Path
import cv2

CACHE = Path.home() / '.cache' / 'cvintro'
MODEL = CACHE / 'face_detection_yunet_2023mar.onnx'
if not MODEL.exists():
    CACHE.mkdir(parents=True, exist_ok=True)
    urllib.request.urlretrieve('https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_detection_yunet/face_detection_yunet_2023mar.onnx', MODEL)
photo = cv2.imread(os.path.join(os.environ.get('CVINTRO_DIR', 'D:/clones/sbirchfield.github.io/cvintro'), 'img', 'apollo11_crew.jpg'))
h, w = photo.shape[:2]
y = cv2.FaceDetectorYN_create(str(MODEL), '', (w, h))
ok, faces = y.detect(photo)
print(photo.shape, ok, None if faces is None else (faces.shape, faces.dtype))
for f in faces:
    print([round(float(v), 2) for v in f[:4]], round(float(f[14]), 4), [round(float(v), 1) for v in f[4:8]])
y2 = cv2.FaceDetectorYN.create(str(MODEL), '', (w, h), 0.5, 0.3, 100)
ok, f2 = y2.detect(photo)
print(None if f2 is None else f2.shape, y2.getScoreThreshold(), y2.getTopK(), y2.getInputSize())
