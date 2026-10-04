import numpy as np
import pandas as pd

df = pd.DataFrame({
    'a': [1, 2, 3, 4, 5, 6],
    'b': [10.0, 20.0, 30.0, 40.0, 50.0, 60.0],
    'name': ['ann', 'bob', 'cy', 'dee', 'eve', 'fay'],
    'ok': [True, False, True, True, False, True],
    'my col': [6, 5, 4, 3, 2, 1],
}, index=list('uvwxyz'))
df.index.name = 'key'

print(df.query('a > 2'))
print(df.query('a > 2 and b < 60'))
print(df.query('a < 2 or a > 5'))
print(df.query('not ok'))
print(df.query('ok and a != 3'))
print(df.query('2 < a <= 5'))
print(df.query('name == "bob"'))
print(df.query('name in ["ann", "cy", "zed"]'))
print(df.query('name not in ["ann", "cy"]'))
print(df.query('a == [1, 3, 5]'))
print(df.query('a != [1, 3, 5]'))
print(df.query('a + b > 40'))
print(df.query('a * 10 == b'))
print(df.query('b / a >= 10'))
print(df.query('a % 2 == 0'))
print(df.query('`my col` > 3'))
print(df.query('`my col` > a'))
print(df.query('index == "w"'))
print(df.query('key in ["u", "z"]'))
limit = 3
names = ['bob', 'eve']
print(df.query('a > @limit'))
print(df.query('name in @names'))
print(df.query('a > @limit and name != "fay"'))
print(df.query('abs(a - 3) <= 1'))
print(df.query('sqrt(b) > 6'))
print(df.query('name.str.startswith("d")', engine='python'))
print(df.query('a > 100'))
print(df.query('ok == True'))
print(df.query('ok'))
print(df.query('(a > 1) & (a < 4)'))
print(df.query('a > 1 & a < 4'))
print(df.query('a >= b / 10'))
d2 = df.copy()
d2.query('a > 4', inplace=True)
print(d2)

print(df.eval('a + b'))
print(df.eval('a * 2'))
print(df.eval('a'))
print(df.eval('a > 3'))
print(df.eval('a + b > 40 and ok'))
print(df.eval('(a + b) / 2'))
print(df.eval('b - a ** 2'))
print(df.eval('-a'))
print(df.eval('`my col` + a'))
print(df.eval('a + @limit'))
print(df.eval('sqrt(b) + log(a)').round(4))
print(df.eval('c = a + b'))
print(df.eval('c = a + b; d = c * 2') if False else '')
print(df.eval('''
c = a + b
d = c / a
'''))
print(df.columns.tolist())
d3 = df.copy()
d3.eval('z = a * b', inplace=True)
print(d3.head(3))
print(df.eval('a.sum() + 1') if False else '')
print(df.eval('b - b.mean()') if False else '')
print(pd.eval('3 + 4 * 2'))
print(pd.eval('limit * 2'))
try:
    df.query('nosuch > 1')
except Exception as e:
    print(type(e).__name__, e)
try:
    df.eval('a + @nosuch')
except Exception as e:
    print(type(e).__name__, e)
