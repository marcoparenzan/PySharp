def _log_api_usage_once(obj):
    return None


def _is_tracing():
    return False


def _make_ntuple(x, n):
    try:
        t = tuple(x)
        return t
    except TypeError:
        return tuple(x for _ in range(n))
