import io
import numpy as np
import pandas as pd

csv = """when,city,temp
2021-03-01,Rome,12.5
2021-03-02,Rome,13.0
2021-03-03,Oslo,-1.5
2021-03-04,Oslo,0.5
"""
df = pd.read_csv(io.StringIO(csv))
print(df.dtypes)
df = pd.read_csv(io.StringIO(csv), parse_dates=['when'])
print(df.dtypes)
print(df)
df = pd.read_csv(io.StringIO(csv), parse_dates=['when'], index_col='when')
print(df)
print(df.index)
df = pd.read_csv(io.StringIO(csv), index_col=0, parse_dates=True)
print(df.index.dtype)
print(df.loc['2021-03-02'])
mixed = pd.read_csv(io.StringIO('d,v\n2021-03-01,1\n2021-03-02 10:00,2\n'), parse_dates=['d'])
print(mixed.dtypes)
t2 = pd.read_csv(io.StringIO('d,v\n2021-03-01 10:00,1\n2021-03-02 11:30,2\n'), parse_dates=['d'])
print(t2.dtypes)
print(t2)

d2 = pd.DataFrame({'t': pd.date_range('2020-01-01 10:00', periods=3, freq='h'), 'dur': pd.to_timedelta([1, 2, 3], unit='h'), 'x': [1, 2, 3]})
print(d2.to_csv(index=False, lineterminator='\n'))
print(d2.to_csv(index=False, date_format='%d/%m/%Y %H:%M', lineterminator='\n'))
print(d2.set_index('t').to_csv(lineterminator='\n'))
print(d2.to_json())
print(d2.to_json(date_format='iso'))
print(d2.to_json(orient='records', date_format='iso'))
print(d2.to_json(orient='split', date_unit='s'))
print(pd.Series(pd.date_range('2020-01-01', periods=2)).to_json())
print(d2.to_dict(orient='records')[0])
print(d2.to_string())
print(d2.to_html())
print(d2.dtypes)

r = pd.read_json(io.StringIO('[{"d":"2020-01-02","v":1},{"d":"2020-02-03","v":2}]'), convert_dates=['d'])
print(r.dtypes)
print(r)

e = pd.DataFrame({'a': [pd.Timestamp('2020-01-01'), pd.NaT, pd.Timestamp('2020-01-03')], 'b': [pd.Timedelta('1D'), pd.NaT, pd.Timedelta('2h')]})
print(e)
print(e.isna())
print(e.fillna(pd.Timestamp('2000-01-01')).a.tolist())
print(e.a.dt.year)
print(e.to_csv(lineterminator='\n'))
print(e.describe())
print(e.count())
print(e.dropna())
print(pd.concat([e, e]).shape)
print(e.sort_values('a', ascending=False))
print(e['a'].map(lambda t: t.year if t is not pd.NaT else -1).tolist() if False else '')
print(e['a'].apply(lambda t: str(t)).tolist())
print(pd.Series([pd.Timestamp('2020-05-05'), pd.Timestamp('2020-05-06')], name='dates'))
print(pd.Series([pd.Timedelta('1D'), pd.Timedelta('2D')]))
print(pd.DataFrame({'d': [pd.Timestamp('2020-05-05')] * 2}).dtypes)
print(pd.Series(pd.to_datetime(['2020-01-01', '2020-01-02'])).values.dtype if False else '')
print(pd.Series(['2020-01-05', '2020-01-06']).astype('datetime64[ns]').dt.day.tolist())
print(pd.to_datetime(pd.Series(['2020-01-05', 'junk']), errors='coerce'))
print(pd.to_datetime(['2020-01-05T10:00:00', '2020-01-06T11:30:00']))
print(pd.to_datetime(['Jan 5, 2020', 'Feb 6, 2020'], format='%b %d, %Y'))
print(pd.to_datetime(['2020-01-05 10:00:00.123456789']))
print(pd.to_datetime('20200105', format='%Y%m%d'))
print(pd.Timestamp('2020-01-05').strftime('%A %d %B %Y, week %U, day %j'))
