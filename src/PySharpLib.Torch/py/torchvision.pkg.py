__version__ = "0.25.0"


def _is_tracing():
    return False


def get_image_backend():
    return "PIL"


from torchvision import utils
from torchvision import transforms
from torchvision import ops
from torchvision import models
