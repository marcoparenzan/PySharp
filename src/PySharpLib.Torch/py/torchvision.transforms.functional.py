# Tensor-only port of torchvision.transforms.functional (the tensor code paths of torchvision 0.25: same size computation, same
# interpolate/antialias calls, same rounding), without the PIL backend.
import numbers
from enum import Enum

import torch
from torch import Tensor
from torch.nn.functional import interpolate


class InterpolationMode(Enum):
    NEAREST = "nearest"
    NEAREST_EXACT = "nearest-exact"
    BILINEAR = "bilinear"
    BICUBIC = "bicubic"
    BOX = "box"
    HAMMING = "hamming"
    LANCZOS = "lanczos"


def _interpolation_str(interpolation):
    if isinstance(interpolation, InterpolationMode):
        return interpolation.value
    return interpolation


def get_dimensions(img):
    channels = 1 if img.ndim == 2 else img.shape[-3]
    height, width = img.shape[-2:]
    return [channels, height, width]


def get_image_size(img):
    return [img.shape[-1], img.shape[-2]]


def _is_pil(img):
    return not isinstance(img, Tensor)


def pil_to_tensor(pic):
    import numpy as np
    arr = np.array(pic, copy=True)
    if arr.ndim == 2:
        arr = arr[:, :, None]
    return torch.from_numpy(arr).permute((2, 0, 1)).contiguous()


def to_tensor(pic):
    import numpy as np
    if isinstance(pic, Tensor):
        return pic
    arr = np.array(pic, copy=True)
    if arr.ndim == 2:
        arr = arr[:, :, None]
    img = torch.from_numpy(arr).permute((2, 0, 1)).contiguous()
    if img.dtype == torch.uint8:
        return img.to(torch.get_default_dtype()).div(255)
    return img


def _max_value(dtype):
    if dtype == torch.uint8:
        return 255
    if dtype == torch.int8:
        return 127
    if dtype == torch.int16:
        return 32767
    if dtype == torch.int32:
        return 2147483647
    return 9223372036854775807


def convert_image_dtype(image, dtype=torch.float):
    if image.dtype == dtype:
        return image
    if image.is_floating_point():
        if torch.tensor(0, dtype=dtype).is_floating_point():
            return image.to(dtype)
        # float -> integer
        eps = 1e-3
        max_val = float(_max_value(dtype))
        result = image.mul(max_val + 1.0 - eps)
        return result.to(dtype)
    # integer input
    if torch.tensor(0, dtype=dtype).is_floating_point():
        max_val = _max_value(image.dtype)
        image = image.to(dtype)
        return image / max_val
    input_max = _max_value(image.dtype)
    output_max = _max_value(dtype)
    if input_max > output_max:
        factor = int((input_max + 1) // (output_max + 1))
        image = torch.div(image, factor, rounding_mode="floor")
        return image.to(dtype)
    factor = int((output_max + 1) // (input_max + 1))
    image = image.to(dtype)
    return image * factor


def normalize(tensor, mean, std, inplace=False):
    if not isinstance(tensor, Tensor):
        raise TypeError("img should be Tensor Image. Got " + str(type(tensor)))
    if not tensor.is_floating_point():
        raise TypeError("Input tensor should be a float tensor. Got " + str(tensor.dtype) + ".")
    if tensor.ndim < 3:
        raise ValueError("Expected tensor to be a tensor image of size (..., C, H, W). Got tensor.size() = " + str(tensor.size()))
    if not inplace:
        tensor = tensor.clone()
    dtype = tensor.dtype
    mean = torch.as_tensor(mean, dtype=dtype)
    std = torch.as_tensor(std, dtype=dtype)
    if (std == 0).any():
        raise ValueError("std evaluated to zero after conversion to " + str(dtype) + ", leading to division by zero.")
    if mean.ndim == 1:
        mean = mean.view(-1, 1, 1)
    if std.ndim == 1:
        std = std.view(-1, 1, 1)
    return tensor.sub_(mean).div_(std)


def _compute_resized_output_size(image_size, size, max_size=None, allow_size_none=False):
    h, w = image_size
    short, long = (w, h) if w <= h else (h, w)
    if len(size) == 1:
        requested_new_short = size if isinstance(size, int) else size[0]
        new_short, new_long = requested_new_short, int(requested_new_short * long / short)
        if max_size is not None:
            if max_size <= requested_new_short:
                raise ValueError("max_size = " + str(max_size) + " must be strictly greater than the requested size for the smaller edge size = " + str(size))
            if new_long > max_size:
                new_short, new_long = int(max_size * new_short / new_long), max_size
        new_w, new_h = (new_short, new_long) if w <= h else (new_long, new_short)
    else:
        new_w, new_h = size[1], size[0]
    return [new_h, new_w]


def _cast_squeeze_in(img, req_dtypes):
    need_squeeze = False
    if img.ndim < 4:
        img = img.unsqueeze(dim=0)
        need_squeeze = True
    out_dtype = img.dtype
    need_cast = False
    if out_dtype not in req_dtypes:
        need_cast = True
        req_dtype = req_dtypes[0]
        img = img.to(req_dtype)
    return img, need_cast, need_squeeze, out_dtype


def _cast_squeeze_out(img, need_cast, need_squeeze, out_dtype):
    if need_squeeze:
        img = img.squeeze(dim=0)
    if need_cast:
        if out_dtype in (torch.uint8, torch.int8, torch.int16, torch.int32, torch.int64):
            img = torch.round(img)
        img = img.to(out_dtype)
    return img


def resize(img, size, interpolation=InterpolationMode.BILINEAR, max_size=None, antialias=True):
    if isinstance(size, int):
        size = [size]
    elif isinstance(size, tuple):
        size = list(size)
    if antialias is None:
        antialias = False
    mode = _interpolation_str(interpolation)
    _, image_height, image_width = get_dimensions(img)
    output_size = _compute_resized_output_size((image_height, image_width), size, max_size)
    if [image_height, image_width] == output_size:
        return img
    if antialias and mode not in ("bilinear", "bicubic"):
        raise ValueError("Antialias option is supported for bilinear and bicubic interpolation modes only")
    img, need_cast, need_squeeze, out_dtype = _cast_squeeze_in(img, [torch.float32, torch.float64])
    align_corners = False if mode in ("bilinear", "bicubic") else None
    img = interpolate(img, size=output_size, mode=mode, align_corners=align_corners, antialias=antialias)
    if mode == "bicubic" and out_dtype == torch.uint8:
        img = img.clamp(min=0, max=255)
    return _cast_squeeze_out(img, need_cast, need_squeeze, out_dtype)


def crop(img, top, left, height, width):
    h, w = img.shape[-2:]
    right = left + width
    bottom = top + height
    if left < 0 or top < 0 or right > w or bottom > h:
        padding_ltrb = [max(-left + min(0, right), 0), max(-top + min(0, bottom), 0), max(right - max(w, left), 0), max(bottom - max(h, top), 0)]
        return pad(img[..., max(top, 0):bottom, max(left, 0):right], padding_ltrb, fill=0)
    return img[..., top:bottom, left:right]


def pad(img, padding, fill=0, padding_mode="constant"):
    if isinstance(padding, int):
        padding = [padding] * 4
    elif len(padding) == 2:
        padding = [padding[0], padding[1], padding[0], padding[1]]
    left, top, right, bottom = padding
    return torch.nn.functional.pad(img, [left, right, top, bottom], mode=padding_mode, value=fill)


def center_crop(img, output_size):
    if isinstance(output_size, numbers.Number):
        output_size = (int(output_size), int(output_size))
    elif isinstance(output_size, (tuple, list)) and len(output_size) == 1:
        output_size = (output_size[0], output_size[0])
    _, image_height, image_width = get_dimensions(img)
    crop_height, crop_width = output_size
    if crop_width > image_width or crop_height > image_height:
        padding_ltrb = [(crop_width - image_width) // 2 if crop_width > image_width else 0,
                        (crop_height - image_height) // 2 if crop_height > image_height else 0,
                        (crop_width - image_width + 1) // 2 if crop_width > image_width else 0,
                        (crop_height - image_height + 1) // 2 if crop_height > image_height else 0]
        img = pad(img, padding_ltrb, fill=0)
        _, image_height, image_width = get_dimensions(img)
        if crop_width == image_width and crop_height == image_height:
            return img
    crop_top = int(round((image_height - crop_height) / 2.0))
    crop_left = int(round((image_width - crop_width) / 2.0))
    return crop(img, crop_top, crop_left, crop_height, crop_width)


def hflip(img):
    return img.flip(-1)


def vflip(img):
    return img.flip(-2)
