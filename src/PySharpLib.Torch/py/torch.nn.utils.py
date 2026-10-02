import torch


def clip_grad_norm_(parameters, max_norm, norm_type=2.0, error_if_nonfinite=False, foreach=None):
    if isinstance(parameters, torch.Tensor):
        parameters = [parameters]
    grads = [p.grad for p in parameters if p.grad is not None]
    if len(grads) == 0:
        return torch.tensor(0.0)
    norm_type = float(norm_type)
    with torch.no_grad():
        if norm_type == float("inf"):
            total = max(g.abs().max() for g in grads)
        else:
            norms = torch.stack([g.norm(norm_type) for g in grads])
            total = norms.norm(norm_type)
        coef = max_norm / (total + 1e-6)
        coef = torch.clamp(coef, max=1.0)
        for g in grads:
            g.mul_(coef)
    return total


def clip_grad_value_(parameters, clip_value, foreach=None):
    if isinstance(parameters, torch.Tensor):
        parameters = [parameters]
    with torch.no_grad():
        for p in parameters:
            if p.grad is not None:
                p.grad.clamp_(-clip_value, clip_value)


def parameters_to_vector(parameters):
    return torch.cat([p.reshape(-1) for p in parameters])
