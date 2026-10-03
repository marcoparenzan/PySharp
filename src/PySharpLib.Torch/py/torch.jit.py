def is_scripting():
    return False


def is_tracing():
    return False


def unused(fn):
    return fn


def ignore(drop=False, **kwargs):
    if callable(drop):
        return drop
    return lambda fn: fn


def export(fn):
    return fn


def script(obj=None, *args, **kwargs):
    return obj


def trace(fn, *args, **kwargs):
    return fn


def annotate(the_type, the_value):
    return the_value


def _overload(fn):
    return fn


def _overload_method(fn):
    return fn


class ScriptModule:
    pass


def _script_if_tracing(fn):
    return fn


def _unwrap_optional(x):
    return x


Final = None
