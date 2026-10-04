import pandas as pd

df = pd.DataFrame({'a': [10, 20, 30, 40], 'b': [1.5, 2.5, 3.5, 4.5], 'c': list('wxyz')}, index=['r1', 'r2', 'r3', 'r4'])
print(df['a'])
print(df[['a', 'c']])
print(df.b)
print(df[[False, True, True, False]])
print(df['r2':'r3'])
print(df[1:3])
print(df.loc['r2'])
print(df.loc['r2', 'b'])
print(df.loc[['r1', 'r4'], ['a', 'c']])
print(df.loc['r1':'r3', 'a'])
print(df.loc[[True, False, False, True]])
print(df.iloc[0])
print(df.iloc[1, 2])
print(df.iloc[[0, 2], [0, 1]])
print(df.iloc[1:3, 0:2])
print(df.iloc[-1])
print(df.at['r3', 'a'], df.iat[0, 1])
s = df['a']
print(s['r2'], s.loc['r3'], s.iloc[0])
print(s[['r1', 'r2']])
print(s['r1':'r2'])
print(s[1:3])
print('a' in df, 'z' in df, 'r1' in s)
try:
    df['zz']
except KeyError as e:
    print('KeyError', e)
try:
    s['nope']
except KeyError as e:
    print('KeyError', e)
try:
    df.iloc[10]
except IndexError as e:
    print('IndexError', e)
