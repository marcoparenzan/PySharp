import math
import torch
from torch.optim import lr_scheduler


class Optimizer:
    def __init__(self, params, defaults):
        self.defaults = defaults
        self.state = {}
        self._state = {}
        self.param_groups = []
        param_groups = list(params)
        if len(param_groups) == 0:
            raise ValueError("optimizer got an empty parameter list")
        if not isinstance(param_groups[0], dict):
            param_groups = [{"params": param_groups}]
        for group in param_groups:
            self.add_param_group(group)

    def add_param_group(self, param_group):
        params = param_group["params"]
        if isinstance(params, torch.Tensor):
            param_group["params"] = [params]
        else:
            param_group["params"] = list(params)
        for name, default in self.defaults.items():
            if name not in param_group:
                param_group[name] = default
        self.param_groups.append(param_group)

    def zero_grad(self, set_to_none=True):
        for group in self.param_groups:
            for p in group["params"]:
                if p.grad is not None:
                    if set_to_none:
                        p.grad = None
                    else:
                        p.grad.zero_()

    def _st(self, p):
        key = id(p)
        s = self._state.get(key)
        if s is None:
            s = {}
            self._state[key] = s
        return s

    def state_dict(self):
        return {"state": dict(self._state), "param_groups": [{k: v for k, v in g.items() if k != "params"} for g in self.param_groups]}

    def load_state_dict(self, state_dict):
        for g, sg in zip(self.param_groups, state_dict["param_groups"]):
            for k, v in sg.items():
                g[k] = v

    def step(self, closure=None):
        raise NotImplementedError


class SGD(Optimizer):
    def __init__(self, params, lr=1e-3, momentum=0, dampening=0, weight_decay=0, nesterov=False, *, maximize=False, foreach=None, differentiable=False, fused=None):
        if lr < 0.0:
            raise ValueError("Invalid learning rate: " + str(lr))
        defaults = dict(lr=lr, momentum=momentum, dampening=dampening, weight_decay=weight_decay, nesterov=nesterov, maximize=maximize)
        super().__init__(params, defaults)

    def step(self, closure=None):
        loss = None
        if closure is not None:
            with torch.enable_grad():
                loss = closure()
        with torch.no_grad():
            for group in self.param_groups:
                lr = group["lr"]
                momentum = group["momentum"]
                dampening = group["dampening"]
                weight_decay = group["weight_decay"]
                nesterov = group["nesterov"]
                for p in group["params"]:
                    if p.grad is None:
                        continue
                    grad = p.grad if not group["maximize"] else -p.grad
                    if weight_decay != 0:
                        grad = grad.add(p, alpha=weight_decay)
                    if momentum != 0:
                        st = self._st(p)
                        buf = st.get("momentum_buffer")
                        if buf is None:
                            buf = torch.clone(grad).detach()
                            st["momentum_buffer"] = buf
                        else:
                            buf.mul_(momentum).add_(grad, alpha=1 - dampening)
                        if nesterov:
                            grad = grad.add(buf, alpha=momentum)
                        else:
                            grad = buf
                    p.add_(grad, alpha=-lr)
        return loss


class Adam(Optimizer):
    _decoupled = False

    def __init__(self, params, lr=1e-3, betas=(0.9, 0.999), eps=1e-8, weight_decay=0, amsgrad=False, *, foreach=None, maximize=False, capturable=False, differentiable=False, fused=None):
        if lr < 0.0:
            raise ValueError("Invalid learning rate: " + str(lr))
        defaults = dict(lr=lr, betas=betas, eps=eps, weight_decay=weight_decay, amsgrad=amsgrad, maximize=maximize)
        super().__init__(params, defaults)

    def step(self, closure=None):
        loss = None
        if closure is not None:
            with torch.enable_grad():
                loss = closure()
        with torch.no_grad():
            for group in self.param_groups:
                lr = group["lr"]
                beta1, beta2 = group["betas"]
                eps = group["eps"]
                wd = group["weight_decay"]
                for p in group["params"]:
                    if p.grad is None:
                        continue
                    grad = p.grad if not group["maximize"] else -p.grad
                    st = self._st(p)
                    if len(st) == 0:
                        st["step"] = 0
                        st["exp_avg"] = torch.zeros_like(p)
                        st["exp_avg_sq"] = torch.zeros_like(p)
                        if group["amsgrad"]:
                            st["max_exp_avg_sq"] = torch.zeros_like(p)
                    st["step"] += 1
                    step = st["step"]
                    exp_avg = st["exp_avg"]
                    exp_avg_sq = st["exp_avg_sq"]
                    if wd != 0:
                        if self._decoupled:
                            p.mul_(1 - lr * wd)
                        else:
                            grad = grad.add(p, alpha=wd)
                    exp_avg.lerp_(grad, 1 - beta1)
                    exp_avg_sq.mul_(beta2).addcmul_(grad, grad, value=1 - beta2)
                    bias_correction1 = 1 - beta1 ** step
                    bias_correction2 = 1 - beta2 ** step
                    step_size = lr / bias_correction1
                    bias_correction2_sqrt = math.sqrt(bias_correction2)
                    if group["amsgrad"]:
                        mx = st["max_exp_avg_sq"]
                        mx.copy_(torch.maximum(mx, exp_avg_sq))
                        denom = (mx.sqrt() / bias_correction2_sqrt).add_(eps)
                    else:
                        denom = (exp_avg_sq.sqrt() / bias_correction2_sqrt).add_(eps)
                    p.addcdiv_(exp_avg, denom, value=-step_size)
        return loss


class AdamW(Adam):
    _decoupled = True

    def __init__(self, params, lr=1e-3, betas=(0.9, 0.999), eps=1e-8, weight_decay=1e-2, amsgrad=False, **kwargs):
        super().__init__(params, lr, betas, eps, weight_decay, amsgrad)


class RMSprop(Optimizer):
    def __init__(self, params, lr=1e-2, alpha=0.99, eps=1e-8, weight_decay=0, momentum=0, centered=False):
        defaults = dict(lr=lr, alpha=alpha, eps=eps, weight_decay=weight_decay, momentum=momentum, centered=centered)
        super().__init__(params, defaults)

    def step(self, closure=None):
        loss = None
        if closure is not None:
            with torch.enable_grad():
                loss = closure()
        with torch.no_grad():
            for group in self.param_groups:
                for p in group["params"]:
                    if p.grad is None:
                        continue
                    grad = p.grad
                    st = self._st(p)
                    if len(st) == 0:
                        st["step"] = 0
                        st["square_avg"] = torch.zeros_like(p)
                        if group["momentum"] > 0:
                            st["momentum_buffer"] = torch.zeros_like(p)
                        if group["centered"]:
                            st["grad_avg"] = torch.zeros_like(p)
                    st["step"] += 1
                    if group["weight_decay"] != 0:
                        grad = grad.add(p, alpha=group["weight_decay"])
                    sq = st["square_avg"]
                    sq.mul_(group["alpha"]).addcmul_(grad, grad, value=1 - group["alpha"])
                    if group["centered"]:
                        ga = st["grad_avg"]
                        ga.lerp_(grad, 1 - group["alpha"])
                        avg = sq.addcmul(ga, ga, value=-1).sqrt_().add_(group["eps"])
                    else:
                        avg = sq.sqrt().add_(group["eps"])
                    if group["momentum"] > 0:
                        buf = st["momentum_buffer"]
                        buf.mul_(group["momentum"]).addcdiv_(grad, avg)
                        p.add_(buf, alpha=-group["lr"])
                    else:
                        p.addcdiv_(grad, avg, value=-group["lr"])
        return loss


class Adagrad(Optimizer):
    def __init__(self, params, lr=1e-2, lr_decay=0, weight_decay=0, initial_accumulator_value=0, eps=1e-10):
        defaults = dict(lr=lr, lr_decay=lr_decay, weight_decay=weight_decay, initial_accumulator_value=initial_accumulator_value, eps=eps)
        super().__init__(params, defaults)

    def step(self, closure=None):
        loss = None
        if closure is not None:
            with torch.enable_grad():
                loss = closure()
        with torch.no_grad():
            for group in self.param_groups:
                for p in group["params"]:
                    if p.grad is None:
                        continue
                    grad = p.grad
                    st = self._st(p)
                    if len(st) == 0:
                        st["step"] = 0
                        st["sum"] = torch.full_like(p, group["initial_accumulator_value"])
                    st["step"] += 1
                    if group["weight_decay"] != 0:
                        grad = grad.add(p, alpha=group["weight_decay"])
                    clr = group["lr"] / (1 + (st["step"] - 1) * group["lr_decay"])
                    st["sum"].addcmul_(grad, grad, value=1)
                    std = st["sum"].sqrt().add_(group["eps"])
                    p.addcdiv_(grad, std, value=-clr)
        return loss
