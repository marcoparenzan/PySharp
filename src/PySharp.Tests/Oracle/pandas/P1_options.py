import pandas as pd

print(pd.get_option('display.max_rows'), pd.get_option('display.width'), pd.get_option('display.precision'))
pd.set_option('display.max_rows', 6)
print(pd.Series(range(20)))
print(pd.DataFrame({'a': range(20)}))
pd.set_option('display.min_rows', 4)
print(pd.Series(range(20)))
pd.reset_option('display.max_rows')
pd.reset_option('display.min_rows')
pd.options.display.precision = 2
print(pd.DataFrame({'a': [1.23456, 2.34567]}))
pd.options.display.precision = 6
pd.set_option('display.max_colwidth', 10)
print(pd.DataFrame({'t': ['a very long string indeed', 'short']}))
pd.reset_option('display.max_colwidth')
print(pd.DataFrame({'t': ['a very long string indeed', 'short']}))
