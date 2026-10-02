import math


class LRScheduler:
    def __init__(self, optimizer, last_epoch=-1):
        self.optimizer = optimizer
        if last_epoch == -1:
            for group in optimizer.param_groups:
                group.setdefault("initial_lr", group["lr"])
        self.base_lrs = [group["initial_lr"] for group in optimizer.param_groups]
        self.last_epoch = last_epoch
        self._step_count = 0
        self._initial_step()

    def _initial_step(self):
        self._step_count = 0
        self.step()

    def get_last_lr(self):
        return self._last_lr

    def get_lr(self):
        raise NotImplementedError

    def step(self, epoch=None):
        self._step_count += 1
        self.last_epoch += 1
        values = self.get_lr()
        for param_group, lr in zip(self.optimizer.param_groups, values):
            param_group["lr"] = lr
        self._last_lr = [group["lr"] for group in self.optimizer.param_groups]


_LRScheduler = LRScheduler


class StepLR(LRScheduler):
    def __init__(self, optimizer, step_size, gamma=0.1, last_epoch=-1):
        self.step_size = step_size
        self.gamma = gamma
        super().__init__(optimizer, last_epoch)

    def get_lr(self):
        if (self.last_epoch == 0) or (self.last_epoch % self.step_size != 0):
            return [group["lr"] for group in self.optimizer.param_groups]
        return [group["lr"] * self.gamma for group in self.optimizer.param_groups]


class MultiStepLR(LRScheduler):
    def __init__(self, optimizer, milestones, gamma=0.1, last_epoch=-1):
        self.milestones = list(milestones)
        self.gamma = gamma
        super().__init__(optimizer, last_epoch)

    def get_lr(self):
        if self.last_epoch not in self.milestones:
            return [group["lr"] for group in self.optimizer.param_groups]
        n = self.milestones.count(self.last_epoch)
        return [group["lr"] * self.gamma ** n for group in self.optimizer.param_groups]


class ExponentialLR(LRScheduler):
    def __init__(self, optimizer, gamma, last_epoch=-1):
        self.gamma = gamma
        super().__init__(optimizer, last_epoch)

    def get_lr(self):
        if self.last_epoch == 0:
            return [group["lr"] for group in self.optimizer.param_groups]
        return [group["lr"] * self.gamma for group in self.optimizer.param_groups]


class LambdaLR(LRScheduler):
    def __init__(self, optimizer, lr_lambda, last_epoch=-1):
        self.optimizer = optimizer
        if not isinstance(lr_lambda, (list, tuple)):
            self.lr_lambdas = [lr_lambda] * len(optimizer.param_groups)
        else:
            self.lr_lambdas = list(lr_lambda)
        super().__init__(optimizer, last_epoch)

    def get_lr(self):
        return [base_lr * lmbda(self.last_epoch) for lmbda, base_lr in zip(self.lr_lambdas, self.base_lrs)]


class CosineAnnealingLR(LRScheduler):
    def __init__(self, optimizer, T_max, eta_min=0.0, last_epoch=-1):
        self.T_max = T_max
        self.eta_min = eta_min
        super().__init__(optimizer, last_epoch)

    def get_lr(self):
        if self.last_epoch == 0:
            return [group["lr"] for group in self.optimizer.param_groups]
        elif self._step_count == 1 and self.last_epoch > 0:
            return [self.eta_min + (base_lr - self.eta_min) * (1 + math.cos((self.last_epoch) * math.pi / self.T_max)) / 2 for base_lr, group in zip(self.base_lrs, self.optimizer.param_groups)]
        elif (self.last_epoch - 1 - self.T_max) % (2 * self.T_max) == 0:
            return [group["lr"] + (base_lr - self.eta_min) * (1 - math.cos(math.pi / self.T_max)) / 2 for base_lr, group in zip(self.base_lrs, self.optimizer.param_groups)]
        return [(1 + math.cos(math.pi * self.last_epoch / self.T_max)) / (1 + math.cos(math.pi * (self.last_epoch - 1) / self.T_max)) * (group["lr"] - self.eta_min) + self.eta_min for group in self.optimizer.param_groups]


class LinearLR(LRScheduler):
    def __init__(self, optimizer, start_factor=1.0 / 3, end_factor=1.0, total_iters=5, last_epoch=-1):
        self.start_factor = start_factor
        self.end_factor = end_factor
        self.total_iters = total_iters
        super().__init__(optimizer, last_epoch)

    def get_lr(self):
        if self.last_epoch == 0:
            return [group["lr"] * self.start_factor for group in self.optimizer.param_groups]
        if self.last_epoch > self.total_iters:
            return [group["lr"] for group in self.optimizer.param_groups]
        return [group["lr"] * (1.0 + (self.end_factor - self.start_factor) / (self.total_iters * self.start_factor + (self.last_epoch - 1) * (self.end_factor - self.start_factor))) for group in self.optimizer.param_groups]


class OneCycleLR(LRScheduler):
    def __init__(self, optimizer, max_lr, total_steps=None, epochs=None, steps_per_epoch=None, pct_start=0.3, anneal_strategy="cos", div_factor=25.0, final_div_factor=1e4, last_epoch=-1, **kwargs):
        self.total_steps = total_steps if total_steps is not None else epochs * steps_per_epoch
        self.pct_start = pct_start
        self.anneal_strategy = anneal_strategy
        n = len(optimizer.param_groups)
        self.max_lrs = list(max_lr) if isinstance(max_lr, (list, tuple)) else [max_lr] * n
        for g, m in zip(optimizer.param_groups, self.max_lrs):
            g["initial_lr"] = m / div_factor
            g["max_lr"] = m
            g["min_lr"] = g["initial_lr"] / final_div_factor
        super().__init__(optimizer, last_epoch)

    def _anneal(self, start, end, pct):
        if self.anneal_strategy == "cos":
            return end + (start - end) / 2.0 * (math.cos(math.pi * pct) + 1)
        return (end - start) * pct + start

    def get_lr(self):
        step_num = self.last_epoch
        lrs = []
        up = float(self.pct_start * self.total_steps) - 1
        for group in self.optimizer.param_groups:
            if step_num <= up:
                lrs.append(self._anneal(group["initial_lr"], group["max_lr"], step_num / up))
            else:
                lrs.append(self._anneal(group["max_lr"], group["min_lr"], (step_num - up) / (self.total_steps - 1 - up)))
        return lrs
