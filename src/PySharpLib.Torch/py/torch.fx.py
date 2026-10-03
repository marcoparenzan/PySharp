def wrap(fn_or_name):
    """torch.fx.wrap marks a function as a leaf for symbolic tracing; there is no tracing here, so it is the identity."""
    return fn_or_name
