import pandas as pd

df = pd.DataFrame({'a': [1, 2], 'b': [1.5, None], 'c': ['x', None], 'd': [True, False]}, index=pd.Index(['r1', 'r2'], name='idx'))
print(df._repr_html_().replace('\r', ''))
print(df.to_html().replace('\r', ''))
print(df.to_html(index=False).replace('\r', ''))
print(pd.DataFrame({'a': range(100)})._repr_html_().replace('\r', ''))
print(pd.DataFrame()._repr_html_().replace('\r', ''))
print(pd.DataFrame({'a<b': ['<i>&']}).to_html(index=False).replace('\r', ''))
print(pd.DataFrame({'x': [0.5, 1.25, 100.0]}, index=[10, 20, 30]).to_html().replace('\r', ''))
pd.set_option('display.max_columns', 4)
print(pd.DataFrame({'c%d' % j: [j, j * 2] for j in range(8)})._repr_html_().replace('\r', ''))
