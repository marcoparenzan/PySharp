class _Flag:
    def __init__(self):
        self.benchmark = False
        self.deterministic = False
        self.enabled = True

    def is_available(self):
        return False

    def is_built(self):
        return False


cudnn = _Flag()
mps = _Flag()
mkldnn = _Flag()
