import pandas as pd
import numpy as np

s = pd.Series(range(12), index=pd.date_range('2020-01-01 00:07', periods=12, freq='7min'))
for o in ['start_day', 'start', 'epoch', 'end', 'end_day', pd.Timestamp('2020-01-01 00:03')]:
    print(o)
    print(s.resample('10min', origin=o).sum())
print(s.resample('10min', offset='2min').sum())
print(s.resample('10min', offset=pd.Timedelta('2min'), origin='start').sum())
print(s.resample('1D', offset='3h').sum())
print(s.resample('10min', origin='end', closed='left').sum())
print(s.resample('10min', origin='end', label='left').sum())
print(s.resample('3h', origin='epoch').count())
try:
    s.resample('10min', origin='middle')
except Exception as e:
    print(type(e).__name__, e)

pi = pd.period_range('2020-01', periods=14, freq='M')
ps = pd.Series(np.arange(14.0), index=pi)
print(ps.resample('Y').sum())
print(ps.resample('Q').mean())
print(ps.resample('Q').agg(['min', 'max']))
d = pd.Series(range(10), index=pd.period_range('2020-01-29', periods=10, freq='D'))
print(d.resample('M').sum())
print(d.resample('W').sum())
gap = pd.Series([1, 2, 3], index=pd.PeriodIndex(['2020-01', '2020-04', '2020-09'], freq='M'))
print(gap.resample('Q').sum())
print(pd.DataFrame({'a': range(14), 'b': 1.5}, index=pi).resample('Y').sum())
