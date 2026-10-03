import os
import sys
import urllib.request
import torch


def _torch_home():
    return os.environ.get("TORCH_HOME") or os.path.join(os.path.expanduser("~"), ".cache", "torch")


def get_dir():
    return os.path.join(_torch_home(), "hub")


def load_state_dict_from_url(url, model_dir=None, map_location=None, progress=True, check_hash=False, file_name=None, weights_only=False):
    """Downloads a checkpoint into torch's hub cache (the same ~/.cache/torch/hub/checkpoints real torch uses) and loads it."""
    if model_dir is None:
        model_dir = os.path.join(get_dir(), "checkpoints")
    os.makedirs(model_dir, exist_ok=True)
    fname = file_name or os.path.basename(url.split("?")[0])
    cached = os.path.join(model_dir, fname)
    if not os.path.exists(cached):
        sys.stderr.write('Downloading: "' + url + '" to ' + cached + "\n")
        part = cached + ".part"
        urllib.request.urlretrieve(url, part)
        os.rename(part, cached)
    return torch.load(cached, map_location=map_location)
