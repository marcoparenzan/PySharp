from torch._C import *
import torch._C as _C
import math as _math


Size = _C.Size

__version__ = "2.10.0"


def var_mean(input, dim=None, unbiased=True, keepdim=False, correction=None):
    if dim is None:
        return input.var(unbiased=unbiased), input.mean()
    return input.var(dim, unbiased=unbiased, keepdim=keepdim), input.mean(dim, keepdim=keepdim)


def std_mean(input, dim=None, unbiased=True, keepdim=False, correction=None):
    if dim is None:
        return input.std(unbiased=unbiased), input.mean()
    return input.std(dim, unbiased=unbiased, keepdim=keepdim), input.mean(dim, keepdim=keepdim)


def diff(input, n=1, dim=-1):
    for _ in range(n):
        size = input.size(dim)
        input = input.narrow(dim, 1, size - 1) - input.narrow(dim, 0, size - 1)
    return input


def compile(model=None, *args, **kwargs):
    if model is None:
        return lambda m: m
    return model


def save(obj, f, *args, **kwargs):
    import pickle

    def conv(o):
        if isinstance(o, Tensor):
            return ("__tensor__", o.detach().numpy(), str(o.dtype))
        if isinstance(o, dict):
            return {k: conv(v) for k, v in o.items()}
        if isinstance(o, (list, tuple)):
            return type(o)(conv(v) for v in o)
        return o

    if isinstance(f, str) or hasattr(f, "__fspath__"):
        with open(f, "wb") as fh:
            pickle.dump(conv(obj), fh)
    else:
        pickle.dump(conv(obj), f)


def load(f, map_location=None, pickle_module=None, *, weights_only=None, mmap=None, **kwargs):
    import pickle
    import zipfile
    import os
    if isinstance(f, str) or hasattr(f, "__fspath__"):
        path = os.fspath(f)
        if zipfile.is_zipfile(path):
            return _C._load_zip(path)
        with open(path, "rb") as fh:
            head = fh.read(16)
        if head[:3] == b"":
            return _C._load_legacy(path)

    def unconv(o):
        if isinstance(o, tuple) and len(o) == 3 and o[0] == "__tensor__":
            return from_numpy(o[1])
        if isinstance(o, dict):
            return {k: unconv(v) for k, v in o.items()}
        if isinstance(o, (list, tuple)):
            return type(o)(unconv(v) for v in o)
        return o

    if isinstance(f, str) or hasattr(f, "__fspath__"):
        with open(f, "rb") as fh:
            return unconv(pickle.load(fh))
    return unconv(pickle.load(f))


def _assert(condition, message):
    if not condition:
        raise AssertionError(message)


def get_num_threads():
    return _C._get_num_threads()


def set_num_threads(n):
    _C._set_num_threads(n)


class autocast:
    def __init__(self, device_type="cpu", dtype=None, enabled=True, cache_enabled=None):
        pass

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False

    def __call__(self, fn):
        return fn


from torch import nn, optim, utils, autograd, cuda, backends, linalg, hub, fx, jit, compiler
from torch.nn import functional

grad = autograd.grad
