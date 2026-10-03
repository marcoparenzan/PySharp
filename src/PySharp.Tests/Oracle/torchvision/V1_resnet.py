import torch
import torchvision
from torchvision.models import resnet18, ResNet18_Weights

torch.manual_seed(0)
m = resnet18()
print(sum(p.numel() for p in m.parameters()), len(m.state_dict()), list(m.state_dict().keys())[:3])
m.eval()
x = torch.randn(1, 3, 64, 64)
print(m(x)[0, :5])
w = ResNet18_Weights.IMAGENET1K_V1
mp = resnet18(weights=w).eval()
tf = w.transforms()
img = (torch.arange(3 * 100 * 120).reshape(3, 100, 120) % 255).to(torch.uint8)
t = tf(img)
print(t.shape, t.dtype, t.mean().item(), t[0, 0, :3])
y = mp(t.unsqueeze(0))
print(y.argmax().item(), round(y.max().item(), 3), w.meta["categories"][y.argmax().item()], y[0, :4])
print(tf)
