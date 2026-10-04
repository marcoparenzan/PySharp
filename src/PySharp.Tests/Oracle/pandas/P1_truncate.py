import pandas as pd

s = pd.Series(range(100))
print(s)
s2 = pd.Series([i * 0.5 for i in range(70)], name='half')
print(s2)
df = pd.DataFrame({'a': range(100), 'b': [i * 1.5 for i in range(100)], 'c': ['v%d' % i for i in range(100)]})
print(df)
print(df.head(61))
print(df.head(60))
print(pd.DataFrame({'a': range(11)}))
print(pd.DataFrame({'a': range(61)}))
