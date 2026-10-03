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

weights = torchvision.models.detection.MaskRCNN_ResNet50_FPN_Weights.COCO_V1
m = torchvision.models.detection.maskrcnn_resnet50_fpn(weights=weights).eval()
cls = weights.meta['categories']
img = torch.tensor(cv2.cvtColor(cv2.imread(str(IMG)), cv2.COLOR_BGR2RGB) / 255.0, dtype=torch.float32).permute(2, 0, 1)
print(sum(p.numel() for p in m.parameters()))
with torch.no_grad():
    r = m([img])[0]
print(sorted(r.keys()), r['boxes'].shape, r['masks'].shape, r['masks'].dtype)
for k in range(min(5, len(r['labels']))):
    print(cls[r['labels'][k].item()], [round(v, 2) for v in r['boxes'][k].tolist()], round(r['scores'][k].item(), 4), round(r['masks'][k].sum().item(), 3), int((r['masks'][k] > 0.5).sum()))
