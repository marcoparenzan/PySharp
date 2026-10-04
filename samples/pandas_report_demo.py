# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# pandas_report_demo.py — building a report with groupby, merge, pivot_table and CSV on PySharp.Pandas (Phase 3).
#
# See PANDAS_PLAN.md. The output is identical to real pandas 3.0 (checked by running the script under
# CPython + pandas and diffing). Covers read_csv/to_csv, groupby (agg, transform, apply, named aggregation),
# merge/join/concat, pivot_table, melt, crosstab, MultiIndex results, unstack and to_dict.
#
# Usage:  pysharp run samples/pandas_report_demo.py

import io

import pandas as pd

ORDERS_CSV = """order_id,customer_id,product,qty,unit_price,express
1001,1,keyboard,2,49.90,True
1002,2,monitor,1,229.00,False
1003,1,mouse,3,19.50,False
1004,3,monitor,2,229.00,True
1005,2,keyboard,1,49.90,False
1006,4,cable,10,4.25,False
1007,3,mouse,1,19.50,True
1008,1,monitor,1,229.00,False
"""
CUSTOMERS_CSV = """customer_id,name,country
1,Anna,IT
2,Bruno,DE
3,Chloe,FR
5,Dario,IT
"""

orders = pd.read_csv(io.StringIO(ORDERS_CSV))
customers = pd.read_csv(io.StringIO(CUSTOMERS_CSV))
print(orders.dtypes)
orders["amount"] = (orders["qty"] * orders["unit_price"]).round(2)

print("\n--- merge ---")
full = orders.merge(customers, on="customer_id", how="left")
print(full[["order_id", "name", "country", "amount"]])
print(full["name"].isna().sum(), "orders without a known customer")
print(customers.merge(orders, on="customer_id", how="left").groupby("name")["order_id"].count())

print("\n--- groupby ---")
by_country = full.groupby("country")["amount"].agg(["count", "sum", "mean"]).round(2)
print(by_country.sort_values("sum", ascending=False))
print(full.groupby(["country", "product"])["amount"].sum())
summary = full.groupby("name", as_index=False).agg(orders=("order_id", "count"), revenue=("amount", "sum"), biggest=("amount", "max"))
print(summary.sort_values("revenue", ascending=False).reset_index(drop=True))
full["share"] = (full["amount"] / full.groupby("name")["amount"].transform("sum")).round(3)
print(full[["name", "product", "amount", "share"]].head(4))
print(full.groupby("product").apply(lambda g: g.loc[g["amount"].idxmax(), "name"]))

print("\n--- pivot ---")
table = full.pivot_table(values="amount", index="name", columns="product", aggfunc="sum", fill_value=0, margins=True)
print(table)
print(pd.crosstab(full["country"], full["express"]))
tidy = table.drop(index="All").drop(columns="All").reset_index().melt(id_vars="name", var_name="product", value_name="amount")
print(tidy[tidy["amount"] > 0].sort_values(["name", "product"]).reset_index(drop=True))
print(full.groupby(["name", "product"])["qty"].sum().unstack(fill_value=0))

print("\n--- concat and export ---")
extra = pd.DataFrame({"order_id": [1009], "customer_id": [5], "product": ["cable"], "qty": [4], "unit_price": [4.25], "express": [False]})
extra["amount"] = extra["qty"] * extra["unit_price"]
all_orders = pd.concat([orders, extra], ignore_index=True)
print(all_orders.tail(2))
print(all_orders.groupby("customer_id")["amount"].sum().to_dict())
out = all_orders[["order_id", "product", "amount"]].head(3).to_csv(index=False, lineterminator="\n")
print(out, end="")
print(pd.read_csv(io.StringIO(out)).to_dict("records"))
