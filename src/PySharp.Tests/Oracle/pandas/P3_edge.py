import io
import pandas as pd
import numpy as np

sales = pd.DataFrame({
    'region': ['N', 'S', 'N', 'E', 'S', 'N', 'E', 'S'],
    'rep': ['ann', 'bob', 'ann', 'cy', 'bob', 'dee', 'cy', 'eve'],
    'q': [1, 1, 2, 2, 3, 3, 4, 4],
    'rev': [100.0, 80.5, 120.0, 60.25, 90.0, 70.0, 65.5, 55.0],
    'units': [10, 8, 12, 6, 9, 7, 6, 5],
})
# flattening multi-level aggregation columns
agg = sales.groupby('region').agg({'rev': ['sum', 'mean'], 'units': 'max'})
print(agg)
agg.columns = ['_'.join(c) for c in agg.columns]
print(agg)
print(agg.reset_index())
print(sales.groupby(['region', 'rep'])['rev'].sum().unstack(fill_value=0))
print(sales.groupby('region')['rev'].agg(['count', 'sum']).sort_values('sum', ascending=False))
print(sales.groupby('region').apply(lambda g: g.nlargest(1, 'rev')))
print(sales.groupby('region').apply(lambda g: g['rev'].sum() / g['units'].sum()))
print(sales.groupby('q')['rep'].nunique())
print(sales.groupby('region')['rep'].agg(lambda s: ', '.join(sorted(set(s)))))
print(sales.groupby('region')[['rev', 'units']].agg(['min', 'max']).loc['N'])
print(sales.groupby('region', as_index=False).agg(total=('rev', 'sum'), n=('rep', 'count')))
print(sales.groupby('region').rev.sum().idxmax(), sales.groupby('region').rev.sum().max())
by = sales.groupby('region')
print((by['rev'].transform('sum') / sales['rev']).round(3).tolist())
print(sales.assign(share=sales['rev'] / by['rev'].transform('sum')).round(3).head(3))
print(by['rev'].apply(lambda s: s.rank().tolist()) if False else 'norank')
print(sales.groupby(sales['rev'] > 70)['units'].sum())
print(sales.sort_values(['region', 'rev'], ascending=[True, False]).groupby('region').head(1))
# merge outcomes and dtypes
left = pd.DataFrame({'k': [1, 2, 3], 'a': [1.5, 2.5, 3.5]})
right = pd.DataFrame({'k': [2, 3, 4], 'b': ['x', 'y', 'z']})
m = left.merge(right, on='k', how='outer')
print(m, m.dtypes)
print(left.merge(right, on='k', how='left').dtypes)
print(left.merge(right, how='inner', on='k').set_index('k'))
cust = pd.DataFrame({'cid': [1, 2], 'name': ['ann', 'bob']})
orders = pd.DataFrame({'oid': [10, 11, 12], 'cid': [1, 1, 3], 'amt': [5.0, 7.5, 9.0]})
print(orders.merge(cust, on='cid', how='left'))
print(orders.merge(cust, on='cid', how='left').groupby('name', dropna=False)['amt'].sum())
print(cust.merge(orders, on='cid').groupby('name')['amt'].agg(['sum', 'count']))
# concat extras
print(pd.concat([sales.head(2), sales.tail(2)]))
print(pd.concat([sales[['region']].head(2), sales[['rev']].head(2)], axis=1))
print(pd.concat([pd.Series([1, 2], name='a'), pd.Series([3.5], name='a')]).dtype)
print(pd.concat([pd.DataFrame({'a': [1]}), pd.DataFrame({'a': ['x']})]).dtypes)
print(pd.concat([pd.DataFrame({'a': [1, 2]}), pd.DataFrame({'b': [3]})]).dtypes)
# csv corner cases
tricky = 'a,b\n"line1\nline2",1\n"say ""hi""",2\n'
t = pd.read_csv(io.StringIO(tricky))
print(t, t['a'].tolist())
print(t.to_csv(index=False, lineterminator='\n'), end='')
print(pd.read_csv(io.StringIO('x,y\n1,2\n3,4\n'), index_col=None).sum().tolist())
print(pd.read_csv(io.StringIO('n,v\na,1\nb,2\n'), index_col='n')['v'].to_dict())
print(pd.read_csv(io.StringIO('a,b\r\n1,2\r\n3,4\r\n')).values.tolist())
print(pd.read_csv(io.StringIO('a,b\n 1 , x \n2,y\n')).dtypes.tolist(), pd.read_csv(io.StringIO('a,b\n 1 , x \n2,y\n'))['b'].tolist())
# reshape roundtrip
wide = sales.pivot_table(values='rev', index='region', columns='q', aggfunc='sum')
print(wide)
print(wide.stack())
print(wide.reset_index().melt(id_vars='region', var_name='q', value_name='rev').dropna().sort_values(['region', 'q']).reset_index(drop=True))
print(sales.pivot_table(values='rev', index='region', columns='q', aggfunc='sum', fill_value=0, margins=True))
