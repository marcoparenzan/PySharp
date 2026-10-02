import numpy as np
import pywt
rng = np.random.default_rng(4)
modes = ["symmetric", "zero", "constant", "periodic", "periodization", "reflect", "antisymmetric", "antireflect", "smooth"]
for name in ["haar", "db2", "db4", "sym5", "coif2", "bior2.2", "rbio3.1"]:
    w = pywt.Wavelet(name)
    for n in (8, 11):
        x = rng.normal(size=n)
        for mode in modes:
            a, d = pywt.dwt(x, w, mode=mode)
            r = pywt.idwt(a, d, w, mode=mode)
            print(name, n, mode, a.shape, bool(np.allclose(a, pywt.dwt(x, name, mode=mode)[0])), (np.round(a[:3], 6) + 0.0).tolist(), (np.round(d[-2:], 6) + 0.0).tolist(), r.shape, (np.round(r[: min(3, len(r))], 6) + 0.0).tolist())
w = pywt.Wavelet("db2")
print(w.name, w.dec_len, w.rec_len, w.orthogonal, np.round(w.dec_lo, 8).tolist(), np.round(w.rec_hi, 8).tolist(), len(w.filter_bank), pywt.Wavelet("haar").dec_hi)
print(pywt.dwt_coeff_len(10, 4, "symmetric"), pywt.dwt_coeff_len(10, w, "periodization"), pywt.dwt_coeff_len(7, 2, "zero"))
print(pywt.wavelist("haar"), pywt.wavelist("db")[:4], len(pywt.wavelist("sym")) > 5)
img = rng.integers(0, 256, (8, 10)).astype(np.float64)
for mode in ["symmetric", "periodization", "zero"]:
    LL, (LH, HL, HH) = pywt.dwt2(img, "db2", mode=mode)
    rec = pywt.idwt2((LL, (LH, HL, HH)), "db2", mode=mode)
    print(mode, LL.shape, (np.round(LL[:2, :2], 4) + 0.0).tolist(), (np.round(LH[:1, :3], 4) + 0.0).tolist(), (np.round(HL[:1, :3], 4) + 0.0).tolist(), (np.round(HH[:1, :3], 4) + 0.0).tolist(), rec.shape, bool(np.allclose(rec[:8, :10], img)))
print(pywt.dwt(np.arange(8, dtype=np.float32), "haar")[0].dtype, pywt.dwt(np.arange(8), "haar")[0].dtype, pywt.dwt(np.arange(12.0).reshape(3, 4), "haar", axis=0)[0].shape)
sig = np.sin(np.linspace(0, 6, 33))
a, d = pywt.dwt(sig, "db3", mode="periodization")
print(a.shape, np.round(a[:4], 6).tolist(), float(np.abs(pywt.idwt(a, d, "db3", mode="periodization")[:33] - sig).max()) < 1e-9)
