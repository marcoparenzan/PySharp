import urllib.request
from pathlib import Path
import cv2
import torch
import torchvision

CACHE_DIR = Path.home() / '.cache' / 'cvintro'
IMG = CACHE_DIR / 'coco_sample.jpg'
if not IMG.exists():
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    urllib.request.urlretrieve('http://images.cocodataset.org/val2017/000000039769.jpg', IMG)

weights = torchvision.models.detection.FasterRCNN_ResNet50_FPN_Weights.COCO_V1
det = torchvision.models.detection.fasterrcnn_resnet50_fpn(weights=weights).eval()
cls = weights.meta['categories']
img = torch.tensor(cv2.cvtColor(cv2.imread(str(IMG)), cv2.COLOR_BGR2RGB) / 255.0, dtype=torch.float32).permute(2, 0, 1)
print(img.shape, sum(p.numel() for p in det.parameters()))
with torch.no_grad():
    r = det([img])[0]
print(sorted(r.keys()), r['boxes'].shape, r['labels'].shape, r['scores'].shape)
for b, l, s in list(zip(r['boxes'].tolist(), r['labels'].tolist(), r['scores'].tolist()))[:6]:
    print(cls[l], [round(v, 2) for v in b], round(s, 4))
print(int((r['scores'] > 0.5).sum()), [cls[l] for l, s in zip(r['labels'].tolist(), r['scores'].tolist()) if s > 0.5])
