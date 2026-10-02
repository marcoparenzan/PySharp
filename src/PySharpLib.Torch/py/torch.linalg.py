import torch


def norm(A, ord=None, dim=None, keepdim=False):
    if ord is None or ord == "fro":
        return A.norm(2, dim, keepdim) if dim is not None else A.norm(2)
    return A.norm(ord, dim, keepdim) if dim is not None else A.norm(ord)


def vector_norm(x, ord=2, dim=None, keepdim=False):
    return x.norm(ord, dim, keepdim) if dim is not None else x.norm(ord)


inv = torch.inverse
det = torch.det
cross = torch.cross
matmul = torch.matmul
