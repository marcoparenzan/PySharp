import torch
from torchvision.models.segmentation import fcn_resnet50, FCN_ResNet50_Weights

torch.manual_seed(0)
m = fcn_resnet50(weights=None, weights_backbone=None, num_classes=21, aux_loss=True)
print(sum(p.numel() for p in m.parameters()), len(m.state_dict()))
m.eval()
with torch.no_grad():
    out = m(torch.randn(1, 3, 64, 64))
print(out["out"].shape, out["aux"].shape, out["out"].flatten()[:4], out["out"].abs().sum().item())

w = FCN_ResNet50_Weights.COCO_WITH_VOC_LABELS_V1
fcn = fcn_resnet50(weights=w).eval()
tf = w.transforms()
img = ((torch.arange(3 * 90 * 120).reshape(3, 90, 120) * 7) % 255).to(torch.uint8)
x = tf(img).unsqueeze(0)
print(x.shape, tf)
with torch.no_grad():
    o = fcn(x)["out"]
pred = o.argmax(1)
print(o.shape, pred.shape, torch.unique(pred).tolist(), o[0, :3, 0, 0], [w.meta["categories"][i] for i in torch.unique(pred).tolist()])
