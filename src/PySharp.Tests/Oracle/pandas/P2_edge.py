import pandas as pd
import numpy as np

# empty frames and series
e = pd.Series([], dtype='float64')
print(e.sum(), e.max(), e.count(), e.mean())
print(pd.DataFrame({'a': []}).sum())
print(pd.DataFrame({'a': [1, 2]}).iloc[:0].mean())
print(e + 1)
# object dtype
o = pd.Series([1, 'a', 2.5, None])
print(o.isna(), o.count())
print(o == 1)
print(pd.Series([1, 2, None], dtype='object') + 1)
# integer column labels
df = pd.DataFrame([[1, 2], [3, 4]])
print(df.sum(), df.sum(axis=1), df[0] + df[1], df * 10)
print(df.loc[0], df.iloc[:, 1])
# apply returning a Series
d = pd.DataFrame({'a': [1, 2], 'b': [3, 4]})
print(d.apply(lambda col: pd.Series({'lo': col.min(), 'hi': col.max()})))
print(d.apply(lambda row: pd.Series([row['a'] + 1, row['b'] * 2], index=['x', 'y']), axis=1))
print(d.apply(np.sqrt))
print(d.apply(lambda col: col.sum()).tolist())
# where / mask with frame condition
print(d.where(d > 1, -d), d.mask(d > 1, other=0))
print(d[d['a'] > 1].shape, d[d > 5].shape)
# duplicates in labels
dd = pd.Series([1, 2, 3], index=['a', 'a', 'b'])
print(dd['a'], dd.loc['b'], dd.sum(), dd.index.is_unique)
print(dd.sort_index(), dd.reset_index())
# sorting mixed
print(pd.Series([3, None, 1, 2]).sort_values(ascending=False))
print(pd.Series(['b', None, 'a']).sort_values())
print(pd.DataFrame({'a': [2, 1, 2], 'b': [None, 5.0, 4.0]}).sort_values(['a', 'b']))
# rank-free ordering helpers
x = pd.Series([10, 20, 30, 40, 50])
print(x[x > x.mean()], x[x.between(20, 40)], x[(x > 10) & (x < 50)])
print(x.where(x > 20).dropna().tolist(), x.diff().dropna().tolist())
print((x - x.mean()) / x.std())
print((x / x.sum()).round(3))
print(x.rename('score').describe())
# arithmetic dtype outcomes
print((pd.Series([1, 2]) / pd.Series([2, 4])).dtype, (pd.Series([1, 2]) // pd.Series([2, 4])).dtype, (pd.Series([True, False]) * 2).dtype)
print((pd.Series([1, 2]) + True).dtype, (pd.Series([1.5]) + 1).dtype, (pd.Series([1], dtype='uint8') + 1).dtype)
print(pd.Series([1, 2, 3]).sum() / pd.Series([1, 2, 3]).count())
print(pd.DataFrame({'a': [1, 2], 'b': [3, 4]}).sum().sum(), pd.DataFrame({'a': [1, 2], 'b': [3, 4]}).max().max())
print(pd.DataFrame({'a': [1.5, 2.5], 'b': [3, 4]}).apply(lambda r: r['a'] * r['b'], axis=1).tolist())
print(pd.Series([1, 2, 3]).map(str).tolist(), pd.Series(['1', '2']).map(int).tolist())
print(pd.Series([3, 1, 2]).sort_values().index.tolist(), pd.Series([3, 1, 2]).idxmax())
print(pd.Series([1, 2, 3, 4]).rolling if False else 'norolling')
