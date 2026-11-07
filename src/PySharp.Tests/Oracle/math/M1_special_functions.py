import math

def g(x):
    return format(x, '.12g')

def attempt(label, f, *args):
    try:
        print(label, g(f(*args)))
    except Exception as e:
        print(label, type(e).__name__, e)

# error function: PySharp used Abramowitz-Stegun 7.1.26 (about 1e-7); now MathemaSharp.Core.SpecialFunctions
for x in [0.0, 1e-8, 0.1, 0.4375, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0, 5.5, 10.0, -0.5, -2.5]:
    print('erf', x, g(math.erf(x)), g(math.erfc(x)))
print('erfc tail', g(math.erfc(26.0)), g(math.erfc(-3.0)))

# gamma and lgamma
for x in [0.5, 1.5, 2.5, 5.5, 10.25, 22.0, 23.0, 24.0, 50.5, 100.5, 139.5, 140.5, 170.5, -0.5, -1.5, -10.25]:
    print('gamma', x, g(math.gamma(x)), g(math.lgamma(x)))
print('gamma factorials', [int(math.gamma(n)) for n in range(1, 12)])
attempt('gamma(0.0)', math.gamma, 0.0)
attempt('gamma(-3.0)', math.gamma, -3.0)
attempt('gamma(172.0)', math.gamma, 172.0)
attempt('gamma(-inf)', math.gamma, float('-inf'))
attempt('gamma(inf)', math.gamma, float('inf'))
attempt('lgamma(0.0)', math.lgamma, 0.0)
attempt('lgamma(-2.0)', math.lgamma, -2.0)
attempt('lgamma(inf)', math.lgamma, float('inf'))
print('nan', math.isnan(math.gamma(float('nan'))), math.isnan(math.erf(float('nan'))))

# expm1 and log1p keep the digits below 1e-16 and report the domain like CPython
for x in [1e-20, 1e-10, 1e-5, 0.5, -0.5, 20.0]:
    print('expm1', x, g(math.expm1(x)))
for x in [1e-20, 1e-10, 1e-5, 0.5, -0.5, 1e10]:
    print('log1p', x, g(math.log1p(x)))
attempt('expm1(1000)', math.expm1, 1000.0)
attempt('log1p(-1)', math.log1p, -1.0)
attempt('log1p(-2)', math.log1p, -2.0)

# hypot: no overflow in the squares
print('hypot', g(math.hypot(3.0, 4.0)), g(math.hypot(1e200, 1e200)), g(math.hypot(1e-200, 1e-200)), g(math.hypot(1.0, 2.0, 2.0)))
