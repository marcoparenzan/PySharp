import pandas as pd

wide = pd.DataFrame({'col%02d' % j: [j + 0.5, j * 2.0, j - 1.0] for j in range(30)})
print(wide)
mid = pd.DataFrame({'column_number_%d' % j: [j * 1000.0, j * 2.0] for j in range(8)})
print(mid)
wider = pd.DataFrame({'c%d' % j: [1, 2] for j in range(120)})
print(wider)
print(wide.to_string())
