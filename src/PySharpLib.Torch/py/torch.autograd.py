import torch
from torch._C import _autograd_grad as grad
from torch._C import no_grad, enable_grad, set_grad_enabled, inference_mode


def backward(tensors, grad_tensors=None, retain_graph=None, create_graph=False, inputs=None):
    if isinstance(tensors, torch.Tensor):
        tensors = [tensors]
    if grad_tensors is None:
        grad_tensors = [None] * len(tensors)
    elif isinstance(grad_tensors, torch.Tensor):
        grad_tensors = [grad_tensors]
    for t, g in zip(tensors, grad_tensors):
        if g is None:
            t.backward(retain_graph=bool(retain_graph), create_graph=create_graph)
        else:
            t.backward(g, retain_graph=bool(retain_graph), create_graph=create_graph)


class Variable:
    def __new__(cls, data, requires_grad=False, **kwargs):
        t = data.detach() if isinstance(data, torch.Tensor) else torch.tensor(data)
        return t.requires_grad_(requires_grad)
