import pandas as pd

df = pd.DataFrame({'a': [1, 2, 3], 'b': [4.0, 5.0, 6.0]})
df['c'] = [7, 8, 9]
print(df)
df['d'] = 'k'
print(df)
df['a'] = [10, 20, 30]
print(df)
df.loc[0, 'b'] = 99.5
print(df)
df.loc[df.index > 0, 'c'] = 0
print(df)
df.iloc[2, 0] = -1
print(df)
df.loc[1] = [1, 2.5, 3, 'q']
print(df)
df.loc[3] = [4, 5.5, 6, 'r']
print(df)
df['e'] = pd.Series([1, 2, 3, 4], index=[0, 1, 2, 3])
print(df)
del df['e']
print(df.columns.tolist())
print(df.drop(columns=['d']))
print(df.drop(index=[0, 1]))
print(df.drop('c', axis=1))
print(df.assign(f=1))
df.insert(1, 'ins', 0)
print(df)
s = pd.Series([1, 2, 3], index=['a', 'b', 'c'])
s['b'] = 20
s['d'] = 4
print(s)
s.iloc[0] = 100
print(s)
s2 = s.copy()
s2['a'] = -1
print(s['a'], s2['a'])
df3 = df[['a', 'b']]
df3.loc[0, 'a'] = 12345
print(df['a'].tolist(), df3['a'].tolist())
