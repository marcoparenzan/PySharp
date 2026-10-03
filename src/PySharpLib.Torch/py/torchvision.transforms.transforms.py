import torch
from torch import nn
from torchvision.transforms import functional as F
from torchvision.transforms.functional import InterpolationMode


class Compose:
    def __init__(self, transforms):
        self.transforms = transforms

    def __call__(self, img):
        for t in self.transforms:
            img = t(img)
        return img

    def __repr__(self):
        s = self.__class__.__name__ + "("
        for t in self.transforms:
            s += "\n    " + repr(t)
        return s + "\n)"


class Resize(nn.Module):
    def __init__(self, size, interpolation=InterpolationMode.BILINEAR, max_size=None, antialias=True):
        super().__init__()
        self.size = size
        self.max_size = max_size
        self.interpolation = interpolation
        self.antialias = antialias

    def forward(self, img):
        return F.resize(img, self.size, self.interpolation, self.max_size, self.antialias)


class CenterCrop(nn.Module):
    def __init__(self, size):
        super().__init__()
        self.size = size

    def forward(self, img):
        return F.center_crop(img, self.size)


class Normalize(nn.Module):
    def __init__(self, mean, std, inplace=False):
        super().__init__()
        self.mean = mean
        self.std = std
        self.inplace = inplace

    def forward(self, tensor):
        return F.normalize(tensor, self.mean, self.std, self.inplace)


class ToTensor:
    def __call__(self, pic):
        return F.to_tensor(pic)


class ConvertImageDtype(nn.Module):
    def __init__(self, dtype):
        super().__init__()
        self.dtype = dtype

    def forward(self, image):
        return F.convert_image_dtype(image, self.dtype)


class RandomHorizontalFlip(nn.Module):
    def __init__(self, p=0.5):
        super().__init__()
        self.p = p

    def forward(self, img):
        if torch.rand(1) < self.p:
            return F.hflip(img)
        return img
