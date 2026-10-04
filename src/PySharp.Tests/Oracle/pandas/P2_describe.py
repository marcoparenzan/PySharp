import pandas as pd

s = pd.Series([1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
print(s.describe())
print(pd.Series([1.5, None, 2.5]).describe())
print(s.describe(percentiles=[0.1, 0.9]))
print(pd.Series(['a', 'b', 'a', None]).describe())
print(pd.Series([True, False, True]).describe())
df = pd.DataFrame({'a': [1, 2, 3, 4], 'b': [0.5, 1.5, None, 4.5], 's': ['x', 'y', 'x', 'z']})
print(df.describe())
print(df[['s']].describe())
print(df.describe(percentiles=[0.5]))
print(pd.DataFrame({'a': range(1000), 'b': [i * 0.001 for i in range(1000)]}).describe())
