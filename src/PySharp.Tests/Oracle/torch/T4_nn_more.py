import copy
import torch
import torch.nn as nn
import torch.nn.functional as F
from torch.utils.data import DataLoader, TensorDataset, random_split

torch.manual_seed(0)
mha = nn.MultiheadAttention(8, 2, batch_first=True)
q = torch.randn(2, 5, 8)
out, wts = mha(q, q, q)
print(out.shape, wts.shape, out.sum().item(), wts[0, 0])
layer = nn.TransformerEncoderLayer(8, 2, dim_feedforward=16, batch_first=True, dropout=0.0)
enc = nn.TransformerEncoder(layer, num_layers=2)
y = enc(q)
print(y.shape, round(y.abs().sum().item(), 3), len(list(enc.parameters())))
print(nn.LayerNorm(8)(q).std().item(), nn.GELU()(q).sum().item())
net = nn.Sequential(nn.Conv2d(1, 4, 3), nn.BatchNorm2d(4), nn.ReLU(), nn.Flatten(), nn.Linear(4 * 6 * 6, 3))
x = torch.randn(2, 1, 8, 8)
print(net(x).shape, net)
net.eval()
print(net(x).sum().item())
print([n for n, _ in net.named_modules()][:4], len(net.state_dict()), net.training, net[0].training)
sd = copy.deepcopy(net.state_dict())
net2 = nn.Sequential(nn.Conv2d(1, 4, 3), nn.BatchNorm2d(4), nn.ReLU(), nn.Flatten(), nn.Linear(4 * 6 * 6, 3))
print(net2.load_state_dict(sd), torch.allclose(net2.eval()(x), net(x)))
net3 = copy.deepcopy(net)
net3[0].weight.data.zero_()
print(net[0].weight.abs().sum().item() > 0, net3[0].weight.abs().sum().item())
print(F.dropout(torch.ones(4), 0.5, training=False), nn.Dropout(0.5).eval()(torch.ones(2)))
print(F.one_hot(torch.tensor([0, 2]), 3), F.normalize(torch.tensor([[3., 4.]])), F.pad(torch.ones(1, 1, 2, 2), (1, 1, 1, 1)).shape)
print(F.interpolate(torch.arange(4.).reshape(1, 1, 2, 2), scale_factor=2, mode="nearest").shape, F.avg_pool2d(torch.ones(1, 1, 4, 4), 2).shape)

torch.manual_seed(3)
ds = TensorDataset(torch.arange(10.).reshape(10, 1), torch.arange(10))
dl = DataLoader(ds, batch_size=4, shuffle=True)
print(len(dl), [b[1].tolist() for b in dl])
tr, va = random_split(ds, [7, 3])
print(len(tr), len(va), [ds[i][1].item() for i in tr.indices])
opt = torch.optim.SGD([torch.zeros(1, requires_grad=True)], lr=1.0)
sch = torch.optim.lr_scheduler.StepLR(opt, step_size=2, gamma=0.5)
lrs = []
for _ in range(6):
    lrs.append(opt.param_groups[0]["lr"])
    opt.step()
    sch.step()
print(lrs)
opt2 = torch.optim.Adam([torch.zeros(1, requires_grad=True)], lr=0.1)
cos = torch.optim.lr_scheduler.CosineAnnealingLR(opt2, T_max=4)
cl = []
for _ in range(6):
    cl.append(round(opt2.param_groups[0]["lr"], 6))
    opt2.step()
    cos.step()
print(cl)
