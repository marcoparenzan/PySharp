import math
import copy
import torch
from torch._C import _Parameter as Parameter
import torch.nn.init as init
from torch.nn import functional as F


def _pair(v):
    if isinstance(v, (tuple, list)):
        return tuple(v)
    return (v, v)


def _calculate_fan_in_and_fan_out(tensor):
    if tensor.dim() < 2:
        raise ValueError("Fan in and fan out can not be computed for tensor with fewer than 2 dimensions")
    num_input_fmaps = tensor.size(1)
    num_output_fmaps = tensor.size(0)
    receptive_field_size = 1
    if tensor.dim() > 2:
        for s in tensor.shape[2:]:
            receptive_field_size *= s
    return num_input_fmaps * receptive_field_size, num_output_fmaps * receptive_field_size


init._calculate_fan_in_and_fan_out = _calculate_fan_in_and_fan_out


class Module:
    training = True

    def __init__(self, *args, **kwargs):
        object.__setattr__(self, "training", True)
        object.__setattr__(self, "_parameters", {})
        object.__setattr__(self, "_buffers", {})
        object.__setattr__(self, "_non_persistent_buffers_set", set())
        object.__setattr__(self, "_modules", {})
        object.__setattr__(self, "_forward_hooks", {})
        object.__setattr__(self, "_forward_pre_hooks", {})
        object.__setattr__(self, "_hook_counter", 0)

    def forward(self, *args, **kwargs):
        raise NotImplementedError('Module [' + type(self).__name__ + '] is missing the required "forward" function')

    def __call__(self, *args, **kwargs):
        for hook in list(self._forward_pre_hooks.values()):
            result = hook(self, args)
            if result is not None:
                args = result if isinstance(result, tuple) else (result,)
        result = self.forward(*args, **kwargs)
        for hook in list(self._forward_hooks.values()):
            hook_result = hook(self, args, result)
            if hook_result is not None:
                result = hook_result
        return result

    def __getattr__(self, name):
        if name in ("_parameters", "_buffers", "_modules", "_forward_hooks", "_forward_pre_hooks", "_non_persistent_buffers_set"):
            raise AttributeError(name)
        params = self._parameters
        if name in params:
            return params[name]
        buffers = self._buffers
        if name in buffers:
            return buffers[name]
        modules = self._modules
        if name in modules:
            return modules[name]
        raise AttributeError("'" + type(self).__name__ + "' object has no attribute '" + name + "'")

    def __setattr__(self, name, value):
        try:
            params = self._parameters
        except AttributeError:
            params = None
        if isinstance(value, Parameter):
            if params is None:
                raise AttributeError("cannot assign parameters before Module.__init__() call")
            self._buffers.pop(name, None)
            self._modules.pop(name, None)
            self._drop_plain(name)
            params[name] = value
        elif params is not None and name in params:
            if value is not None:
                raise TypeError("cannot assign '" + type(value).__name__ + "' as parameter '" + name + "' (torch.nn.Parameter or None expected)")
            params[name] = value
        elif isinstance(value, Module):
            if params is None:
                raise AttributeError("cannot assign module before Module.__init__() call")
            params.pop(name, None)
            self._buffers.pop(name, None)
            self._drop_plain(name)
            self._modules[name] = value
        elif params is not None and name in self._modules:
            if value is not None:
                raise TypeError("cannot assign '" + type(value).__name__ + "' as child module '" + name + "' (torch.nn.Module or None expected)")
            self._modules[name] = value
        elif params is not None and name in self._buffers:
            if value is not None and not isinstance(value, torch.Tensor):
                raise TypeError("cannot assign '" + type(value).__name__ + "' as buffer '" + name + "' (torch.Tensor or None expected)")
            self._buffers[name] = value
        else:
            object.__setattr__(self, name, value)

    def _drop_plain(self, name):
        # a plain attribute of the same name (e.g. `self.head = None` in __init__) must not shadow the registered one
        try:
            object.__delattr__(self, name)
        except AttributeError:
            pass

    def __delattr__(self, name):
        if name in self._parameters:
            del self._parameters[name]
        elif name in self._buffers:
            del self._buffers[name]
            self._non_persistent_buffers_set.discard(name)
        elif name in self._modules:
            del self._modules[name]
        else:
            object.__delattr__(self, name)

    def register_parameter(self, name, param):
        if param is not None and not isinstance(param, Parameter):
            raise TypeError("cannot assign '" + type(param).__name__ + "' object to parameter '" + name + "' (torch.nn.Parameter or None required)")
        self._parameters[name] = param

    def register_buffer(self, name, tensor, persistent=True):
        if tensor is not None and not isinstance(tensor, torch.Tensor):
            raise TypeError("cannot assign '" + type(tensor).__name__ + "' object to buffer '" + name + "' (torch Tensor or None required)")
        self._buffers[name] = tensor
        if persistent:
            self._non_persistent_buffers_set.discard(name)
        else:
            self._non_persistent_buffers_set.add(name)

    def add_module(self, name, module):
        if module is not None and not isinstance(module, Module):
            raise TypeError(str(type(module)) + " is not a Module subclass")
        self._modules[name] = module

    register_module = add_module

    def get_submodule(self, target):
        if target == "":
            return self
        mod = self
        for item in target.split("."):
            mod = getattr(mod, item)
        return mod

    def get_parameter(self, target):
        module_path, _, name = target.rpartition(".")
        return getattr(self.get_submodule(module_path), name)

    def _named_modules_list(self, memo, prefix, remove_duplicate):
        out = []
        if id(self) not in memo:
            if remove_duplicate:
                memo.add(id(self))
            out.append((prefix, self))
            for name, module in self._modules.items():
                if module is None:
                    continue
                submodule_prefix = prefix + ("." if prefix else "") + name
                out.extend(module._named_modules_list(memo, submodule_prefix, remove_duplicate))
        return out

    # The traversals build lists (not generators): every generator in this interpreter runs on its own OS thread.
    def named_modules(self, memo=None, prefix="", remove_duplicate=True):
        if memo is None:
            memo = set()
        return iter(self._named_modules_list(memo, prefix, remove_duplicate))

    def modules(self):
        return iter([m for _, m in self._named_modules_list(set(), "", True)])

    def named_children(self):
        memo = set()
        out = []
        for name, module in self._modules.items():
            if module is not None and id(module) not in memo:
                memo.add(id(module))
                out.append((name, module))
        return iter(out)

    def children(self):
        return iter([m for _, m in self.named_children()])

    def _named_members_list(self, get_members_fn, prefix="", recurse=True, remove_duplicate=True):
        memo = set()
        out = []
        modules = self._named_modules_list(set(), prefix, remove_duplicate) if recurse else [(prefix, self)]
        for module_prefix, module in modules:
            for k, v in get_members_fn(module):
                if v is None or id(v) in memo:
                    continue
                if remove_duplicate:
                    memo.add(id(v))
                name = module_prefix + ("." if module_prefix else "") + k
                out.append((name, v))
        return out

    def named_parameters(self, prefix="", recurse=True, remove_duplicate=True):
        return iter(self._named_members_list(lambda module: module._parameters.items(), prefix, recurse, remove_duplicate))

    def parameters(self, recurse=True):
        return iter([p for _, p in self._named_members_list(lambda module: module._parameters.items(), "", recurse, True)])

    def named_buffers(self, prefix="", recurse=True, remove_duplicate=True):
        return iter(self._named_members_list(lambda module: module._buffers.items(), prefix, recurse, remove_duplicate))

    def buffers(self, recurse=True):
        return iter([b for _, b in self._named_members_list(lambda module: module._buffers.items(), "", recurse, True)])

    def train(self, mode=True):
        object.__setattr__(self, "training", mode)
        for module in list(self.children()):
            module.train(mode)
        return self

    def eval(self):
        return self.train(False)

    def requires_grad_(self, requires_grad=True):
        for p in self.parameters():
            p.requires_grad_(requires_grad)
        return self

    def zero_grad(self, set_to_none=True):
        for p in self.parameters():
            if p.grad is not None:
                if set_to_none:
                    p.grad = None
                else:
                    p.grad.zero_()

    def apply(self, fn):
        for module in self.children():
            module.apply(fn)
        fn(self)
        return self

    def _apply(self, fn):
        for module in self.children():
            module._apply(fn)
        for key, p in list(self._parameters.items()):
            if p is not None:
                self._parameters[key] = Parameter(fn(p.detach()), p.requires_grad)
        for key, b in list(self._buffers.items()):
            if b is not None:
                self._buffers[key] = fn(b)
        return self

    def to(self, *args, **kwargs):
        dtype = kwargs.get("dtype")
        for a in args:
            if isinstance(a, torch.dtype):
                dtype = a
        device = kwargs.get("device")
        for a in args:
            if isinstance(a, str):
                device = a
        if device is not None and str(device).startswith("cuda"):
            raise RuntimeError("Torch not compiled with CUDA enabled")
        if dtype is not None:
            return self._apply(lambda t: t.to(dtype) if t.is_floating_point() else t)
        return self

    def float(self):
        return self._apply(lambda t: t.float() if t.is_floating_point() else t)

    def double(self):
        return self._apply(lambda t: t.double() if t.is_floating_point() else t)

    def half(self):
        return self._apply(lambda t: t.half() if t.is_floating_point() else t)

    def cpu(self):
        return self

    def cuda(self, device=None):
        raise RuntimeError("Torch not compiled with CUDA enabled")

    def type(self, dst_type):
        return self.to(dst_type)

    def register_forward_hook(self, hook, *, prepend=False, with_kwargs=False, always_call=False):
        object.__setattr__(self, "_hook_counter", self._hook_counter + 1)
        key = self._hook_counter
        self._forward_hooks[key] = hook
        return _RemovableHandle(self._forward_hooks, key)

    def register_forward_pre_hook(self, hook, *, prepend=False, with_kwargs=False):
        object.__setattr__(self, "_hook_counter", self._hook_counter + 1)
        key = self._hook_counter
        self._forward_pre_hooks[key] = hook
        return _RemovableHandle(self._forward_pre_hooks, key)

    def register_full_backward_hook(self, hook, prepend=False):
        return _RemovableHandle({}, 0)

    def state_dict(self, destination=None, prefix="", keep_vars=False):
        if destination is None:
            destination = {}
        for name, p in self._parameters.items():
            if p is not None:
                destination[prefix + name] = p if keep_vars else p.detach()
        for name, b in self._buffers.items():
            if b is not None and name not in self._non_persistent_buffers_set:
                destination[prefix + name] = b if keep_vars else b.detach()
        for name, module in self._modules.items():
            if module is not None:
                module.state_dict(destination, prefix + name + ".", keep_vars)
        return destination

    def _load_from_state_dict(self, state_dict, prefix, local_metadata, strict, missing_keys, unexpected_keys, error_msgs):
        persistent_buffers = {k: v for k, v in self._buffers.items() if k not in self._non_persistent_buffers_set}
        local_state = {k: v for k, v in list(self._parameters.items()) + list(persistent_buffers.items()) if v is not None}
        for name, param in local_state.items():
            key = prefix + name
            if key in state_dict:
                input_param = state_dict[key]
                if not isinstance(input_param, torch.Tensor):
                    error_msgs.append("While copying the parameter named \"" + key + "\", expected torch.Tensor or Tensor-like object from checkpoint but received " + str(type(input_param)))
                    continue
                if tuple(input_param.shape) != tuple(param.shape):
                    error_msgs.append("size mismatch for " + key + ": copying a param with shape " + str(input_param.shape) + " from checkpoint, the shape in current model is " + str(param.shape) + ".")
                    continue
                with torch.no_grad():
                    param.copy_(input_param)
            elif strict:
                missing_keys.append(key)
        if strict:
            for key in state_dict.keys():
                if key.startswith(prefix):
                    input_name = key[len(prefix):].split(".", 1)
                    if len(input_name) > 1:
                        if input_name[0] not in self._modules:
                            unexpected_keys.append(key)
                    elif input_name[0] not in local_state:
                        unexpected_keys.append(key)

    def load_state_dict(self, state_dict, strict=True, assign=False):
        if not isinstance(state_dict, dict):
            raise TypeError("Expected state_dict to be dict-like, got " + str(type(state_dict)) + ".")
        metadata = getattr(state_dict, "_metadata", None)
        state_dict = dict(state_dict)
        missing_keys = []
        unexpected_keys = []
        error_msgs = []

        def load(module, local_state_dict, prefix=""):
            local_metadata = {} if metadata is None else metadata.get(prefix[:-1], {})
            module._load_from_state_dict(local_state_dict, prefix, local_metadata, True, missing_keys, unexpected_keys, error_msgs)
            for name, child in module._modules.items():
                if child is not None:
                    child_prefix = prefix + name + "."
                    child_state_dict = {k: v for k, v in local_state_dict.items() if k.startswith(child_prefix)}
                    load(child, child_state_dict, child_prefix)

        load(self, state_dict)
        if strict:
            if len(unexpected_keys) > 0:
                error_msgs.insert(0, "Unexpected key(s) in state_dict: " + ", ".join('"' + k + '"' for k in unexpected_keys) + ". ")
            if len(missing_keys) > 0:
                error_msgs.insert(0, "Missing key(s) in state_dict: " + ", ".join('"' + k + '"' for k in missing_keys) + ". ")
        if len(error_msgs) > 0:
            raise RuntimeError("Error(s) in loading state_dict for " + type(self).__name__ + ":\n\t" + "\n\t".join(error_msgs))
        return _IncompatibleKeys(missing_keys, unexpected_keys)

    def extra_repr(self):
        return ""

    def __repr__(self):
        extra_lines = []
        extra_repr = self.extra_repr()
        if extra_repr:
            extra_lines = extra_repr.split("\n")
        child_lines = []
        for key, module in self._modules.items():
            mod_str = repr(module)
            mod_str = _addindent(mod_str, 2)
            child_lines.append("(" + key + "): " + mod_str)
        lines = extra_lines + child_lines
        main_str = type(self).__name__ + "("
        if lines:
            if len(extra_lines) == 1 and not child_lines:
                main_str += extra_lines[0]
            else:
                main_str += "\n  " + "\n  ".join(lines) + "\n"
        main_str += ")"
        return main_str


def _addindent(s_, numSpaces):
    s = s_.split("\n")
    if len(s) == 1:
        return s_
    first = s.pop(0)
    s = [(numSpaces * " ") + line for line in s]
    return first + "\n" + "\n".join(s)


class _RemovableHandle:
    def __init__(self, d, key):
        self._d = d
        self._key = key

    def remove(self):
        self._d.pop(self._key, None)


class _IncompatibleKeys:
    def __init__(self, missing_keys, unexpected_keys):
        self.missing_keys = missing_keys
        self.unexpected_keys = unexpected_keys

    def __repr__(self):
        if not self.missing_keys and not self.unexpected_keys:
            return "<All keys matched successfully>"
        return "_IncompatibleKeys(missing_keys=" + repr(self.missing_keys) + ", unexpected_keys=" + repr(self.unexpected_keys) + ")"


# ------------------------------------------------------------------------------------------ containers

class Sequential(Module):
    def __init__(self, *args):
        super().__init__()
        if len(args) == 1 and hasattr(args[0], "items"):
            for key, module in args[0].items():
                self.add_module(key, module)
        else:
            for idx, module in enumerate(args):
                self.add_module(str(idx), module)

    def __getitem__(self, idx):
        if isinstance(idx, slice):
            return Sequential(dict(list(self._modules.items())[idx]))
        mods = list(self._modules.values())
        return mods[idx]

    def __setitem__(self, idx, module):
        key = list(self._modules.keys())[idx]
        self._modules[key] = module

    def __len__(self):
        return len(self._modules)

    def __iter__(self):
        return iter(list(self._modules.values()))

    def append(self, module):
        self.add_module(str(len(self)), module)
        return self

    def forward(self, input):
        for module in self._modules.values():
            input = module(input)
        return input


class ModuleList(Module):
    def __init__(self, modules=None):
        super().__init__()
        if modules is not None:
            self.extend(modules)

    def __getitem__(self, idx):
        if isinstance(idx, slice):
            return ModuleList(list(self._modules.values())[idx])
        return list(self._modules.values())[idx]

    def __setitem__(self, idx, module):
        key = list(self._modules.keys())[idx]
        self._modules[key] = module

    def __len__(self):
        return len(self._modules)

    def __iter__(self):
        return iter(list(self._modules.values()))

    def append(self, module):
        self.add_module(str(len(self)), module)
        return self

    def extend(self, modules):
        for module in modules:
            self.append(module)
        return self

    def insert(self, index, module):
        mods = list(self._modules.values())
        mods.insert(index, module)
        self._modules.clear()
        for i, m in enumerate(mods):
            self._modules[str(i)] = m


class ModuleDict(Module):
    def __init__(self, modules=None):
        super().__init__()
        if modules is not None:
            self.update(modules)

    def __getitem__(self, key):
        return self._modules[key]

    def __setitem__(self, key, module):
        self.add_module(key, module)

    def __len__(self):
        return len(self._modules)

    def __iter__(self):
        return iter(list(self._modules.keys()))

    def __contains__(self, key):
        return key in self._modules

    def keys(self):
        return self._modules.keys()

    def values(self):
        return self._modules.values()

    def items(self):
        return self._modules.items()

    def update(self, modules):
        items = modules.items() if hasattr(modules, "items") else modules
        for k, v in items:
            self[k] = v


class ParameterList(Module):
    def __init__(self, values=None):
        super().__init__()
        self._n = 0
        if values is not None:
            for v in values:
                self.append(v)

    def append(self, value):
        if not isinstance(value, Parameter):
            value = Parameter(value)
        self.register_parameter(str(self._n), value)
        self._n += 1
        return self

    def __getitem__(self, idx):
        return self._parameters[str(idx if idx >= 0 else self._n + idx)]

    def __len__(self):
        return self._n

    def __iter__(self):
        return iter([self._parameters[str(i)] for i in range(self._n)])


# ------------------------------------------------------------------------------------------ layers

class Identity(Module):
    def __init__(self, *args, **kwargs):
        super().__init__()

    def forward(self, input):
        return input


class Linear(Module):
    def __init__(self, in_features, out_features, bias=True, device=None, dtype=None):
        super().__init__()
        self.in_features = in_features
        self.out_features = out_features
        self.weight = Parameter(torch.empty((out_features, in_features), dtype=dtype))
        if bias:
            self.bias = Parameter(torch.empty(out_features, dtype=dtype))
        else:
            self.register_parameter("bias", None)
        self.reset_parameters()

    def reset_parameters(self):
        init.kaiming_uniform_(self.weight, a=math.sqrt(5))
        if self.bias is not None:
            fan_in, _ = _calculate_fan_in_and_fan_out(self.weight)
            bound = 1 / math.sqrt(fan_in) if fan_in > 0 else 0
            init.uniform_(self.bias, -bound, bound)

    def forward(self, input):
        return F.linear(input, self.weight, self.bias)

    def extra_repr(self):
        return "in_features=" + str(self.in_features) + ", out_features=" + str(self.out_features) + ", bias=" + str(self.bias is not None)


NonDynamicallyQuantizableLinear = Linear


class Bilinear(Module):
    def __init__(self, in1_features, in2_features, out_features, bias=True):
        super().__init__()
        self.weight = Parameter(torch.empty((out_features, in1_features, in2_features)))
        self.bias = Parameter(torch.empty(out_features)) if bias else None
        bound = 1 / math.sqrt(self.weight.size(1))
        init.uniform_(self.weight, -bound, bound)
        if self.bias is not None:
            init.uniform_(self.bias, -bound, bound)

    def forward(self, input1, input2):
        out = torch.einsum("bi,oij,bj->bo", input1, self.weight, input2)
        if self.bias is not None:
            out = out + self.bias
        return out


class _ConvNd(Module):
    def __init__(self, in_channels, out_channels, kernel_size, stride, padding, dilation, transposed, output_padding, groups, bias, padding_mode, ndim):
        super().__init__()
        if in_channels % groups != 0:
            raise ValueError("in_channels must be divisible by groups")
        if out_channels % groups != 0:
            raise ValueError("out_channels must be divisible by groups")
        self.in_channels = in_channels
        self.out_channels = out_channels
        self.kernel_size = kernel_size
        self.stride = stride
        self.padding = padding
        self.dilation = dilation
        self.transposed = transposed
        self.output_padding = output_padding
        self.groups = groups
        self.padding_mode = padding_mode
        if transposed:
            shape = (in_channels, out_channels // groups) + tuple(kernel_size)
        else:
            shape = (out_channels, in_channels // groups) + tuple(kernel_size)
        self.weight = Parameter(torch.empty(shape))
        if bias:
            self.bias = Parameter(torch.empty(out_channels))
        else:
            self.register_parameter("bias", None)
        self.reset_parameters()

    def reset_parameters(self):
        init.kaiming_uniform_(self.weight, a=math.sqrt(5))
        if self.bias is not None:
            fan_in, _ = _calculate_fan_in_and_fan_out(self.weight)
            if fan_in != 0:
                bound = 1 / math.sqrt(fan_in)
                init.uniform_(self.bias, -bound, bound)

    def extra_repr(self):
        s = str(self.in_channels) + ", " + str(self.out_channels) + ", kernel_size=" + str(self.kernel_size) + ", stride=" + str(self.stride)
        if self.padding != (0,) * len(self.padding) and not isinstance(self.padding, str):
            s += ", padding=" + str(self.padding)
        if isinstance(self.padding, str):
            s += ", padding=" + self.padding
        if self.dilation != (1,) * len(self.dilation):
            s += ", dilation=" + str(self.dilation)
        if self.groups != 1:
            s += ", groups=" + str(self.groups)
        if self.bias is None:
            s += ", bias=False"
        if self.padding_mode != "zeros":
            s += ", padding_mode=" + self.padding_mode
        return s


class Conv2d(_ConvNd):
    def __init__(self, in_channels, out_channels, kernel_size, stride=1, padding=0, dilation=1, groups=1, bias=True, padding_mode="zeros", device=None, dtype=None):
        padding_ = padding if isinstance(padding, str) else _pair(padding)
        super().__init__(in_channels, out_channels, _pair(kernel_size), _pair(stride), padding_, _pair(dilation), False, (0, 0), groups, bias, padding_mode, 2)

    def forward(self, input):
        if self.padding_mode != "zeros":
            p = self.padding
            input = F.pad(input, (p[1], p[1], p[0], p[0]), mode=self.padding_mode)
            return F.conv2d(input, self.weight, self.bias, self.stride, (0, 0), self.dilation, self.groups)
        return F.conv2d(input, self.weight, self.bias, self.stride, self.padding, self.dilation, self.groups)


class Conv1d(_ConvNd):
    def __init__(self, in_channels, out_channels, kernel_size, stride=1, padding=0, dilation=1, groups=1, bias=True, padding_mode="zeros", device=None, dtype=None):
        k = (kernel_size,) if not isinstance(kernel_size, tuple) else kernel_size
        super().__init__(in_channels, out_channels, k, (stride,), (padding,), (dilation,), False, (0,), groups, bias, padding_mode, 1)

    def forward(self, input):
        return F.conv1d(input, self.weight, self.bias, self.stride, self.padding, self.dilation, self.groups)


class ConvTranspose2d(_ConvNd):
    def __init__(self, in_channels, out_channels, kernel_size, stride=1, padding=0, output_padding=0, groups=1, bias=True, dilation=1, padding_mode="zeros", device=None, dtype=None):
        super().__init__(in_channels, out_channels, _pair(kernel_size), _pair(stride), _pair(padding), _pair(dilation), True, _pair(output_padding), groups, bias, padding_mode, 2)

    def forward(self, input, output_size=None):
        return F.conv_transpose2d(input, self.weight, self.bias, self.stride, self.padding, self.output_padding, self.groups, self.dilation)


def _triple(v):
    if isinstance(v, (tuple, list)):
        return tuple(v)
    return (v, v, v)


class Conv3d(_ConvNd):
    def __init__(self, in_channels, out_channels, kernel_size, stride=1, padding=0, dilation=1, groups=1, bias=True, padding_mode="zeros", device=None, dtype=None):
        super().__init__(in_channels, out_channels, _triple(kernel_size), _triple(stride), _triple(padding), _triple(dilation), False, (0, 0, 0), groups, bias, padding_mode, 3)

    def forward(self, input):
        raise NotImplementedError("Conv3d forward is not supported yet")


class _MaxPoolNd(Module):
    def __init__(self, kernel_size, stride=None, padding=0, dilation=1, return_indices=False, ceil_mode=False):
        super().__init__()
        self.kernel_size = kernel_size
        self.stride = stride if (stride is not None) else kernel_size
        self.padding = padding
        self.dilation = dilation
        self.return_indices = return_indices
        self.ceil_mode = ceil_mode

    def extra_repr(self):
        return "kernel_size=" + str(self.kernel_size) + ", stride=" + str(self.stride) + ", padding=" + str(self.padding) + ", dilation=" + str(self.dilation) + ", ceil_mode=" + str(self.ceil_mode)


class MaxPool2d(_MaxPoolNd):
    def forward(self, input):
        return F.max_pool2d(input, self.kernel_size, self.stride, self.padding, self.dilation, self.ceil_mode, self.return_indices)


class AvgPool2d(Module):
    def __init__(self, kernel_size, stride=None, padding=0, ceil_mode=False, count_include_pad=True, divisor_override=None):
        super().__init__()
        self.kernel_size = kernel_size
        self.stride = stride if (stride is not None) else kernel_size
        self.padding = padding
        self.ceil_mode = ceil_mode
        self.count_include_pad = count_include_pad
        self.divisor_override = divisor_override

    def forward(self, input):
        return F.avg_pool2d(input, self.kernel_size, self.stride, self.padding, self.ceil_mode, self.count_include_pad, self.divisor_override)

    def extra_repr(self):
        return "kernel_size=" + str(self.kernel_size) + ", stride=" + str(self.stride) + ", padding=" + str(self.padding)


class AdaptiveAvgPool2d(Module):
    def __init__(self, output_size):
        super().__init__()
        self.output_size = output_size

    def forward(self, input):
        return F.adaptive_avg_pool2d(input, self.output_size)

    def extra_repr(self):
        return "output_size=" + str(self.output_size)


class AdaptiveMaxPool2d(Module):
    def __init__(self, output_size, return_indices=False):
        super().__init__()
        self.output_size = output_size

    def forward(self, input):
        return F.adaptive_max_pool2d(input, self.output_size)

    def extra_repr(self):
        return "output_size=" + str(self.output_size)


class _NormBase(Module):
    def __init__(self, num_features, eps=1e-5, momentum=0.1, affine=True, track_running_stats=True, device=None, dtype=None):
        super().__init__()
        self.num_features = num_features
        self.eps = eps
        self.momentum = momentum
        self.affine = affine
        self.track_running_stats = track_running_stats
        if affine:
            self.weight = Parameter(torch.ones(num_features))
            self.bias = Parameter(torch.zeros(num_features))
        else:
            self.register_parameter("weight", None)
            self.register_parameter("bias", None)
        if track_running_stats:
            self.register_buffer("running_mean", torch.zeros(num_features))
            self.register_buffer("running_var", torch.ones(num_features))
            self.register_buffer("num_batches_tracked", torch.tensor(0, dtype=torch.long))
        else:
            self.register_buffer("running_mean", None)
            self.register_buffer("running_var", None)
            self.register_buffer("num_batches_tracked", None)

    def _load_from_state_dict(self, state_dict, prefix, local_metadata, strict, missing_keys, unexpected_keys, error_msgs):
        version = local_metadata.get("version", None)
        if (version is None or version < 2) and self.track_running_stats:
            num_batches_tracked_key = prefix + "num_batches_tracked"
            if num_batches_tracked_key not in state_dict:
                state_dict[num_batches_tracked_key] = self.num_batches_tracked if self.num_batches_tracked is not None else torch.tensor(0, dtype=torch.long)
        super()._load_from_state_dict(state_dict, prefix, local_metadata, strict, missing_keys, unexpected_keys, error_msgs)

    def reset_running_stats(self):
        if self.track_running_stats:
            self.running_mean.zero_()
            self.running_var.fill_(1)
            self.num_batches_tracked.zero_()

    def extra_repr(self):
        return str(self.num_features) + ", eps=" + str(self.eps) + ", momentum=" + str(self.momentum) + ", affine=" + str(self.affine) + ", track_running_stats=" + str(self.track_running_stats)


class _BatchNorm(_NormBase):
    def forward(self, input):
        exponential_average_factor = 0.0 if self.momentum is None else self.momentum
        if self.training and self.track_running_stats:
            if self.num_batches_tracked is not None:
                self.num_batches_tracked.add_(1)
                if self.momentum is None:
                    exponential_average_factor = 1.0 / float(self.num_batches_tracked)
                else:
                    exponential_average_factor = self.momentum
        if self.training:
            bn_training = True
        else:
            bn_training = (self.running_mean is None) and (self.running_var is None)
        return F.batch_norm(
            input,
            self.running_mean if (not self.training or self.track_running_stats) else None,
            self.running_var if (not self.training or self.track_running_stats) else None,
            self.weight, self.bias, bn_training, exponential_average_factor, self.eps)


class BatchNorm1d(_BatchNorm):
    pass


class BatchNorm2d(_BatchNorm):
    pass


class BatchNorm3d(_BatchNorm):
    pass


class LayerNorm(Module):
    def __init__(self, normalized_shape, eps=1e-5, elementwise_affine=True, bias=True, device=None, dtype=None):
        super().__init__()
        if isinstance(normalized_shape, int):
            normalized_shape = (normalized_shape,)
        self.normalized_shape = tuple(normalized_shape)
        self.eps = eps
        self.elementwise_affine = elementwise_affine
        if elementwise_affine:
            self.weight = Parameter(torch.ones(self.normalized_shape))
            if bias:
                self.bias = Parameter(torch.zeros(self.normalized_shape))
            else:
                self.register_parameter("bias", None)
        else:
            self.register_parameter("weight", None)
            self.register_parameter("bias", None)

    def forward(self, input):
        return F.layer_norm(input, self.normalized_shape, self.weight, self.bias, self.eps)

    def extra_repr(self):
        return str(self.normalized_shape) + ", eps=" + str(self.eps) + ", elementwise_affine=" + str(self.elementwise_affine)


class GroupNorm(Module):
    def __init__(self, num_groups, num_channels, eps=1e-5, affine=True):
        super().__init__()
        self.num_groups = num_groups
        self.num_channels = num_channels
        self.eps = eps
        self.affine = affine
        if affine:
            self.weight = Parameter(torch.ones(num_channels))
            self.bias = Parameter(torch.zeros(num_channels))
        else:
            self.register_parameter("weight", None)
            self.register_parameter("bias", None)

    def forward(self, input):
        return F.group_norm(input, self.num_groups, self.weight, self.bias, self.eps)


class Dropout(Module):
    def __init__(self, p=0.5, inplace=False):
        super().__init__()
        if p < 0 or p > 1:
            raise ValueError("dropout probability has to be between 0 and 1, but got " + str(p))
        self.p = p
        self.inplace = inplace

    def forward(self, input):
        return F.dropout(input, self.p, self.training, self.inplace)

    def extra_repr(self):
        return "p=" + str(self.p) + ", inplace=" + str(self.inplace)


Dropout1d = Dropout
Dropout2d = Dropout


class Embedding(Module):
    def __init__(self, num_embeddings, embedding_dim, padding_idx=None, max_norm=None, norm_type=2.0, scale_grad_by_freq=False, sparse=False, _weight=None, _freeze=False, device=None, dtype=None):
        super().__init__()
        self.num_embeddings = num_embeddings
        self.embedding_dim = embedding_dim
        self.padding_idx = padding_idx
        if _weight is None:
            self.weight = Parameter(torch.empty((num_embeddings, embedding_dim)), requires_grad=not _freeze)
            self.reset_parameters()
        else:
            self.weight = Parameter(_weight, requires_grad=not _freeze)

    def reset_parameters(self):
        init.normal_(self.weight)
        if self.padding_idx is not None:
            with torch.no_grad():
                self.weight[self.padding_idx].fill_(0)

    def forward(self, input):
        return F.embedding(input, self.weight, self.padding_idx)

    @classmethod
    def from_pretrained(cls, embeddings, freeze=True, padding_idx=None, **kwargs):
        rows, cols = embeddings.shape
        return cls(rows, cols, padding_idx=padding_idx, _weight=embeddings, _freeze=freeze)

    def extra_repr(self):
        return str(self.num_embeddings) + ", " + str(self.embedding_dim)


class Flatten(Module):
    def __init__(self, start_dim=1, end_dim=-1):
        super().__init__()
        self.start_dim = start_dim
        self.end_dim = end_dim

    def forward(self, input):
        return input.flatten(self.start_dim, self.end_dim)

    def extra_repr(self):
        return "start_dim=" + str(self.start_dim) + ", end_dim=" + str(self.end_dim)


class Unflatten(Module):
    def __init__(self, dim, unflattened_size):
        super().__init__()
        self.dim = dim
        self.unflattened_size = unflattened_size

    def forward(self, input):
        return input.unflatten(self.dim, self.unflattened_size)


class Upsample(Module):
    def __init__(self, size=None, scale_factor=None, mode="nearest", align_corners=None, recompute_scale_factor=None):
        super().__init__()
        self.size = size
        self.scale_factor = scale_factor
        self.mode = mode
        self.align_corners = align_corners

    def forward(self, input):
        return F.interpolate(input, self.size, self.scale_factor, self.mode, self.align_corners)

    def extra_repr(self):
        return "scale_factor=" + str(self.scale_factor) + ", mode='" + self.mode + "'"


# ------------------------------------------------------------------------------------------ activations

class ReLU(Module):
    def __init__(self, inplace=False):
        super().__init__()
        self.inplace = inplace

    def forward(self, input):
        return F.relu(input)

    def extra_repr(self):
        return "inplace=True" if self.inplace else ""


class ReLU6(Module):
    def __init__(self, inplace=False):
        super().__init__()

    def forward(self, input):
        return F.relu6(input)


class LeakyReLU(Module):
    def __init__(self, negative_slope=0.01, inplace=False):
        super().__init__()
        self.negative_slope = negative_slope
        self.inplace = inplace

    def forward(self, input):
        return F.leaky_relu(input, self.negative_slope)

    def extra_repr(self):
        return "negative_slope=" + str(self.negative_slope)


class ELU(Module):
    def __init__(self, alpha=1.0, inplace=False):
        super().__init__()
        self.alpha = alpha

    def forward(self, input):
        return F.elu(input, self.alpha)

    def extra_repr(self):
        return "alpha=" + str(self.alpha)


class SELU(Module):
    def __init__(self, inplace=False):
        super().__init__()

    def forward(self, input):
        return F.selu(input)


class GELU(Module):
    def __init__(self, approximate="none"):
        super().__init__()
        self.approximate = approximate

    def forward(self, input):
        return F.gelu(input, self.approximate)

    def extra_repr(self):
        return "approximate='" + self.approximate + "'"


class SiLU(Module):
    def __init__(self, inplace=False):
        super().__init__()

    def forward(self, input):
        return F.silu(input)


class Sigmoid(Module):
    def forward(self, input):
        return torch.sigmoid(input)


class Tanh(Module):
    def forward(self, input):
        return torch.tanh(input)


class Softplus(Module):
    def __init__(self, beta=1.0, threshold=20.0):
        super().__init__()
        self.beta = beta
        self.threshold = threshold

    def forward(self, input):
        return F.softplus(input, self.beta, self.threshold)


class Softmax(Module):
    def __init__(self, dim=None):
        super().__init__()
        self.dim = dim

    def forward(self, input):
        return F.softmax(input, self.dim)

    def extra_repr(self):
        return "dim=" + str(self.dim)


class LogSoftmax(Module):
    def __init__(self, dim=None):
        super().__init__()
        self.dim = dim

    def forward(self, input):
        return F.log_softmax(input, self.dim)

    def extra_repr(self):
        return "dim=" + str(self.dim)


class Hardtanh(Module):
    def __init__(self, min_val=-1.0, max_val=1.0, inplace=False):
        super().__init__()
        self.min_val = min_val
        self.max_val = max_val

    def forward(self, input):
        return F.hardtanh(input, self.min_val, self.max_val)


class Hardswish(Module):
    def __init__(self, inplace=False):
        super().__init__()

    def forward(self, input):
        return F.hardswish(input)


class Hardsigmoid(Module):
    def __init__(self, inplace=False):
        super().__init__()

    def forward(self, input):
        return F.hardsigmoid(input)


class PReLU(Module):
    def __init__(self, num_parameters=1, init=0.25):
        super().__init__()
        self.num_parameters = num_parameters
        self.weight = Parameter(torch.full((num_parameters,), init))

    def forward(self, input):
        return torch.where(input >= 0, input, self.weight * input)


# ------------------------------------------------------------------------------------------ losses

class _Loss(Module):
    def __init__(self, size_average=None, reduce=None, reduction="mean"):
        super().__init__()
        self.reduction = reduction


class MSELoss(_Loss):
    def forward(self, input, target):
        return F.mse_loss(input, target, reduction=self.reduction)


class L1Loss(_Loss):
    def forward(self, input, target):
        return F.l1_loss(input, target, reduction=self.reduction)


class SmoothL1Loss(_Loss):
    def __init__(self, size_average=None, reduce=None, reduction="mean", beta=1.0):
        super().__init__(reduction=reduction)
        self.beta = beta

    def forward(self, input, target):
        return F.smooth_l1_loss(input, target, reduction=self.reduction, beta=self.beta)


class HuberLoss(_Loss):
    def __init__(self, reduction="mean", delta=1.0):
        super().__init__(reduction=reduction)
        self.delta = delta

    def forward(self, input, target):
        return F.huber_loss(input, target, self.reduction, self.delta)


class CrossEntropyLoss(_Loss):
    def __init__(self, weight=None, size_average=None, ignore_index=-100, reduce=None, reduction="mean", label_smoothing=0.0):
        super().__init__(reduction=reduction)
        self.weight = weight
        self.ignore_index = ignore_index
        self.label_smoothing = label_smoothing

    def forward(self, input, target):
        return F.cross_entropy(input, target, weight=self.weight, ignore_index=self.ignore_index, reduction=self.reduction, label_smoothing=self.label_smoothing)


class NLLLoss(_Loss):
    def __init__(self, weight=None, size_average=None, ignore_index=-100, reduce=None, reduction="mean"):
        super().__init__(reduction=reduction)
        self.weight = weight
        self.ignore_index = ignore_index

    def forward(self, input, target):
        return F.nll_loss(input, target, weight=self.weight, ignore_index=self.ignore_index, reduction=self.reduction)


class BCELoss(_Loss):
    def __init__(self, weight=None, size_average=None, reduce=None, reduction="mean"):
        super().__init__(reduction=reduction)
        self.weight = weight

    def forward(self, input, target):
        return F.binary_cross_entropy(input, target, weight=self.weight, reduction=self.reduction)


class BCEWithLogitsLoss(_Loss):
    def __init__(self, weight=None, size_average=None, reduce=None, reduction="mean", pos_weight=None):
        super().__init__(reduction=reduction)
        self.weight = weight
        self.pos_weight = pos_weight

    def forward(self, input, target):
        return F.binary_cross_entropy_with_logits(input, target, self.weight, reduction=self.reduction, pos_weight=self.pos_weight)


class KLDivLoss(_Loss):
    def __init__(self, size_average=None, reduce=None, reduction="mean", log_target=False):
        super().__init__(reduction=reduction)
        self.log_target = log_target

    def forward(self, input, target):
        return F.kl_div(input, target, reduction=self.reduction, log_target=self.log_target)


# ------------------------------------------------------------------------------------------ attention / transformer

class MultiheadAttention(Module):
    def __init__(self, embed_dim, num_heads, dropout=0.0, bias=True, add_bias_kv=False, add_zero_attn=False, kdim=None, vdim=None, batch_first=False, device=None, dtype=None):
        super().__init__()
        self.embed_dim = embed_dim
        self.kdim = kdim if kdim is not None else embed_dim
        self.vdim = vdim if vdim is not None else embed_dim
        self._qkv_same_embed_dim = self.kdim == embed_dim and self.vdim == embed_dim
        self.num_heads = num_heads
        self.dropout = dropout
        self.batch_first = batch_first
        self.head_dim = embed_dim // num_heads
        if self.head_dim * num_heads != self.embed_dim:
            raise AssertionError("embed_dim must be divisible by num_heads")
        if not self._qkv_same_embed_dim:
            self.q_proj_weight = Parameter(torch.empty((embed_dim, embed_dim)))
            self.k_proj_weight = Parameter(torch.empty((embed_dim, self.kdim)))
            self.v_proj_weight = Parameter(torch.empty((embed_dim, self.vdim)))
            self.register_parameter("in_proj_weight", None)
        else:
            self.in_proj_weight = Parameter(torch.empty((3 * embed_dim, embed_dim)))
            self.register_parameter("q_proj_weight", None)
            self.register_parameter("k_proj_weight", None)
            self.register_parameter("v_proj_weight", None)
        if bias:
            self.in_proj_bias = Parameter(torch.empty(3 * embed_dim))
        else:
            self.register_parameter("in_proj_bias", None)
        self.out_proj = Linear(embed_dim, embed_dim, bias=bias)
        self.bias_k = None
        self.bias_v = None
        self._reset_parameters()

    def _reset_parameters(self):
        if self._qkv_same_embed_dim:
            init.xavier_uniform_(self.in_proj_weight)
        else:
            init.xavier_uniform_(self.q_proj_weight)
            init.xavier_uniform_(self.k_proj_weight)
            init.xavier_uniform_(self.v_proj_weight)
        if self.in_proj_bias is not None:
            init.constant_(self.in_proj_bias, 0.0)
            init.constant_(self.out_proj.bias, 0.0)

    def forward(self, query, key, value, key_padding_mask=None, need_weights=True, attn_mask=None, average_attn_weights=True, is_causal=False):
        is_batched = query.dim() == 3
        if self.batch_first and is_batched:
            query = query.transpose(1, 0)
            key = key.transpose(1, 0)
            value = value.transpose(1, 0)
        if not is_batched:
            query = query.unsqueeze(1)
            key = key.unsqueeze(1)
            value = value.unsqueeze(1)
            if key_padding_mask is not None:
                key_padding_mask = key_padding_mask.unsqueeze(0)
        tgt_len, bsz, embed_dim = query.shape
        src_len = key.shape[0]
        H = self.num_heads
        hd = self.head_dim
        if self._qkv_same_embed_dim:
            w_q, w_k, w_v = self.in_proj_weight.chunk(3)
        else:
            w_q, w_k, w_v = self.q_proj_weight, self.k_proj_weight, self.v_proj_weight
        if self.in_proj_bias is not None:
            b_q, b_k, b_v = self.in_proj_bias.chunk(3)
        else:
            b_q = b_k = b_v = None
        q = F.linear(query, w_q, b_q)
        k = F.linear(key, w_k, b_k)
        v = F.linear(value, w_v, b_v)
        mask = None
        if is_causal and attn_mask is None:
            mask = torch.zeros(tgt_len, src_len, dtype=q.dtype).masked_fill(torch.ones(tgt_len, src_len, dtype=torch.bool).triu(1), float("-inf"))
        if attn_mask is not None:
            if attn_mask.dtype == torch.bool:
                mask = torch.zeros(attn_mask.shape, dtype=q.dtype).masked_fill(attn_mask, float("-inf"))
            else:
                mask = attn_mask
            if mask.dim() == 2:
                mask = mask.unsqueeze(0)
        if key_padding_mask is not None:
            if key_padding_mask.dtype == torch.bool:
                kpm = torch.zeros(key_padding_mask.shape, dtype=q.dtype).masked_fill(key_padding_mask, float("-inf"))
            else:
                kpm = key_padding_mask
            kpm = kpm.view(bsz, 1, 1, src_len).expand(-1, H, -1, -1).reshape(bsz * H, 1, src_len)
            mask = kpm if mask is None else mask + kpm
        q = q.reshape(tgt_len, bsz * H, hd).transpose(0, 1)
        k = k.reshape(src_len, bsz * H, hd).transpose(0, 1)
        v = v.reshape(src_len, bsz * H, hd).transpose(0, 1)
        q_scaled = q * math.sqrt(1.0 / float(hd))
        scores = torch.bmm(q_scaled, k.transpose(-2, -1))
        if mask is not None:
            scores = scores + mask
        weights = F.softmax(scores, dim=-1)
        if self.dropout > 0.0 and self.training:
            weights = F.dropout(weights, self.dropout, True)
        attn_output = torch.bmm(weights, v)
        attn_output = attn_output.transpose(0, 1).contiguous().view(tgt_len * bsz, embed_dim)
        attn_output = F.linear(attn_output, self.out_proj.weight, self.out_proj.bias)
        attn_output = attn_output.view(tgt_len, bsz, embed_dim)
        out_w = None
        if need_weights:
            out_w = weights.view(bsz, H, tgt_len, src_len)
            if average_attn_weights:
                out_w = out_w.mean(dim=1)
            if not is_batched:
                out_w = out_w.squeeze(0)
        if not is_batched:
            attn_output = attn_output.squeeze(1)
        elif self.batch_first:
            attn_output = attn_output.transpose(1, 0)
        return attn_output, out_w


def _get_activation_fn(activation):
    if activation == "relu":
        return F.relu
    if activation == "gelu":
        return F.gelu
    raise RuntimeError("activation should be relu/gelu, not " + str(activation))


def _get_clones(module, N):
    return ModuleList([copy.deepcopy(module) for i in range(N)])


class TransformerEncoderLayer(Module):
    def __init__(self, d_model, nhead, dim_feedforward=2048, dropout=0.1, activation=F.relu, layer_norm_eps=1e-5, batch_first=False, norm_first=False, bias=True, device=None, dtype=None):
        super().__init__()
        self.self_attn = MultiheadAttention(d_model, nhead, dropout=dropout, bias=bias, batch_first=batch_first)
        self.linear1 = Linear(d_model, dim_feedforward, bias=bias)
        self.dropout = Dropout(dropout)
        self.linear2 = Linear(dim_feedforward, d_model, bias=bias)
        self.norm_first = norm_first
        self.norm1 = LayerNorm(d_model, eps=layer_norm_eps, bias=bias)
        self.norm2 = LayerNorm(d_model, eps=layer_norm_eps, bias=bias)
        self.dropout1 = Dropout(dropout)
        self.dropout2 = Dropout(dropout)
        if isinstance(activation, str):
            activation = _get_activation_fn(activation)
        self.activation = activation

    def forward(self, src, src_mask=None, src_key_padding_mask=None, is_causal=False):
        x = src
        if self.norm_first:
            x = x + self._sa_block(self.norm1(x), src_mask, src_key_padding_mask, is_causal)
            x = x + self._ff_block(self.norm2(x))
        else:
            x = self.norm1(x + self._sa_block(x, src_mask, src_key_padding_mask, is_causal))
            x = self.norm2(x + self._ff_block(x))
        return x

    def _sa_block(self, x, attn_mask, key_padding_mask, is_causal=False):
        x = self.self_attn(x, x, x, attn_mask=attn_mask, key_padding_mask=key_padding_mask, need_weights=False, is_causal=is_causal)[0]
        return self.dropout1(x)

    def _ff_block(self, x):
        x = self.linear2(self.dropout(self.activation(self.linear1(x))))
        return self.dropout2(x)


class TransformerEncoder(Module):
    def __init__(self, encoder_layer, num_layers, norm=None, enable_nested_tensor=True, mask_check=True):
        super().__init__()
        self.layers = _get_clones(encoder_layer, num_layers)
        self.num_layers = num_layers
        self.norm = norm

    def forward(self, src, mask=None, src_key_padding_mask=None, is_causal=None):
        output = src
        for mod in self.layers:
            output = mod(output, src_mask=mask, src_key_padding_mask=src_key_padding_mask, is_causal=bool(is_causal))
        if self.norm is not None:
            output = self.norm(output)
        return output


from torch.nn import utils
