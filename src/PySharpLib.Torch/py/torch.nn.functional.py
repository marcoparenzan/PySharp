import math as _math
import torch
from torch._F import *


def scaled_dot_product_attention(query, key, value, attn_mask=None, dropout_p=0.0, is_causal=False, scale=None, enable_gqa=False):
    L, S = query.size(-2), key.size(-2)
    scale_factor = 1 / _math.sqrt(query.size(-1)) if scale is None else scale
    attn_bias = torch.zeros(L, S, dtype=query.dtype)
    if is_causal:
        temp_mask = torch.ones(L, S, dtype=torch.bool).tril(diagonal=0)
        attn_bias.masked_fill_(temp_mask.logical_not(), float("-inf"))
    if attn_mask is not None:
        if attn_mask.dtype == torch.bool:
            attn_bias.masked_fill_(attn_mask.logical_not(), float("-inf"))
        else:
            attn_bias = attn_mask + attn_bias
    attn_weight = query @ key.transpose(-2, -1) * scale_factor
    attn_weight = attn_weight + attn_bias
    attn_weight = torch.softmax(attn_weight, dim=-1)
    if dropout_p > 0.0:
        attn_weight = dropout(attn_weight, dropout_p, True)
    return attn_weight @ value


def hardswish(input, inplace=False):
    return input * torch.clamp(input + 3, 0, 6) / 6


def hardsigmoid(input, inplace=False):
    return torch.clamp(input + 3, 0, 6) / 6


def mish(input, inplace=False):
    return input * torch.tanh(softplus(input))


def logsigmoid(input):
    return -softplus(-input)


def glu(input, dim=-1):
    a, b = input.chunk(2, dim)
    return a * torch.sigmoid(b)


def dropout2d(input, p=0.5, training=True, inplace=False):
    return dropout(input, p, training)


def pairwise_distance(x1, x2, p=2.0, eps=1e-6, keepdim=False):
    return (x1 - x2 + eps).norm(p, -1, keepdim)


def grid_sample(*args, **kwargs):
    raise NotImplementedError("grid_sample is not supported yet")
