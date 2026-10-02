import torch


class Dataset:
    def __getitem__(self, index):
        raise NotImplementedError

    def __add__(self, other):
        return ConcatDataset([self, other])


class IterableDataset(Dataset):
    pass


class TensorDataset(Dataset):
    def __init__(self, *tensors):
        for t in tensors:
            if t.size(0) != tensors[0].size(0):
                raise AssertionError("Size mismatch between tensors")
        self.tensors = tensors

    def __getitem__(self, index):
        return tuple(tensor[index] for tensor in self.tensors)

    def __len__(self):
        return self.tensors[0].size(0)


class Subset(Dataset):
    def __init__(self, dataset, indices):
        self.dataset = dataset
        self.indices = list(indices)

    def __getitem__(self, idx):
        if isinstance(idx, list):
            return self.dataset[[self.indices[i] for i in idx]]
        return self.dataset[self.indices[idx]]

    def __len__(self):
        return len(self.indices)


class ConcatDataset(Dataset):
    def __init__(self, datasets):
        self.datasets = list(datasets)
        self.cumulative_sizes = []
        s = 0
        for d in self.datasets:
            s += len(d)
            self.cumulative_sizes.append(s)

    def __len__(self):
        return self.cumulative_sizes[-1]

    def __getitem__(self, idx):
        if idx < 0:
            idx += len(self)
        prev = 0
        for d, c in zip(self.datasets, self.cumulative_sizes):
            if idx < c:
                return d[idx - prev]
            prev = c
        raise IndexError("index out of range")


def random_split(dataset, lengths, generator=None):
    n = len(dataset)
    if sum(lengths) <= 1 and all(isinstance(x, float) for x in lengths):
        sizes = [int(n * frac) for frac in lengths]
        rem = n - sum(sizes)
        for i in range(rem):
            sizes[i % len(sizes)] += 1
        lengths = sizes
    if sum(lengths) != n:
        raise ValueError("Sum of input lengths does not equal the length of the input dataset!")
    indices = torch.randperm(sum(lengths), generator=generator).tolist()
    out = []
    offset = 0
    for length in lengths:
        out.append(Subset(dataset, indices[offset:offset + length]))
        offset += length
    return out


def default_collate(batch):
    elem = batch[0]
    if isinstance(elem, torch.Tensor):
        return torch.stack(list(batch), 0)
    if isinstance(elem, (bool, int)):
        return torch.tensor(list(batch))
    if isinstance(elem, float):
        return torch.tensor(list(batch), dtype=torch.float64)
    if isinstance(elem, (str, bytes)):
        return list(batch)
    if isinstance(elem, dict):
        return {key: default_collate([d[key] for d in batch]) for key in elem}
    if isinstance(elem, (tuple, list)):
        transposed = list(zip(*batch))
        res = [default_collate(list(samples)) for samples in transposed]
        return res if isinstance(elem, list) else tuple(res)
    try:
        return torch.stack([torch.as_tensor(b) for b in batch], 0)
    except Exception:
        return list(batch)


class DataLoader:
    def __init__(self, dataset, batch_size=1, shuffle=False, sampler=None, batch_sampler=None, num_workers=0, collate_fn=None, pin_memory=False, drop_last=False, timeout=0, worker_init_fn=None, generator=None, persistent_workers=False, prefetch_factor=None):
        self.dataset = dataset
        self.batch_size = batch_size
        self.shuffle = shuffle
        self.drop_last = drop_last
        self.generator = generator
        self.sampler = sampler
        self.collate_fn = collate_fn if collate_fn is not None else default_collate
        self.num_workers = num_workers

    def _indices(self):
        n = len(self.dataset)
        # like the real iterator: one draw for the (unused) worker base seed before the sampler draws its own
        torch.empty((), dtype=torch.int64).random_(generator=self.generator)
        if self.sampler is not None:
            return list(self.sampler)
        if self.shuffle:
            if self.generator is None:
                seed = int(torch.empty((), dtype=torch.int64).random_().item())
                g = torch.Generator()
                g.manual_seed(seed)
            else:
                g = self.generator
            return torch.randperm(n, generator=g).tolist()
        return list(range(n))

    def __len__(self):
        n = len(self.dataset)
        if self.drop_last:
            return n // self.batch_size
        return (n + self.batch_size - 1) // self.batch_size

    def __iter__(self):
        idx = self._indices()
        bs = self.batch_size
        if bs is None:
            for i in idx:
                yield self.dataset[i]
            return
        for start in range(0, len(idx), bs):
            chunk = idx[start:start + bs]
            if len(chunk) < bs and self.drop_last:
                break
            yield self.collate_fn([self.dataset[i] for i in chunk])


class RandomSampler:
    def __init__(self, data_source, replacement=False, num_samples=None, generator=None):
        self.data_source = data_source
        self.generator = generator

    def __iter__(self):
        return iter(torch.randperm(len(self.data_source), generator=self.generator).tolist())

    def __len__(self):
        return len(self.data_source)


class SequentialSampler:
    def __init__(self, data_source):
        self.data_source = data_source

    def __iter__(self):
        return iter(range(len(self.data_source)))

    def __len__(self):
        return len(self.data_source)
