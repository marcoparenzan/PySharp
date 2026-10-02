import torch


def is_available():
    return False


def device_count():
    return 0


def manual_seed(seed):
    pass


def manual_seed_all(seed):
    pass


def empty_cache():
    pass


def synchronize(device=None):
    pass


def current_device():
    raise RuntimeError("Torch not compiled with CUDA enabled")


class _GradScaler:
    def __init__(self, *args, **kwargs):
        pass

    def scale(self, loss):
        return loss

    def step(self, optimizer, *a, **k):
        return optimizer.step()

    def update(self, *a, **k):
        pass

    def unscale_(self, optimizer):
        pass

    def get_scale(self):
        return 1.0

    def is_enabled(self):
        return False


class _Autocast:
    def __init__(self, *args, **kwargs):
        pass

    def __enter__(self):
        return self

    def __exit__(self, *a):
        return False

    def __call__(self, fn):
        return fn


class _AmpModule:
    GradScaler = _GradScaler
    autocast = _Autocast


amp = _AmpModule()
