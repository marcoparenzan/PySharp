import numpy as np
nan, inf = float("nan"), float("inf")
a = np.array([1 + 0j, complex(inf, 0), complex(0, -inf), complex(nan, inf), 2 + 3j, complex(nan, 0), complex(0, nan)])
print(np.isnan(a).tolist())
print(np.isinf(a).tolist())
print(np.isfinite(a).tolist())
