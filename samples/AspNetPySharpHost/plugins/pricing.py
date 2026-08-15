# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# A second real plugin, demonstrating the actual value proposition of this scenario: real business
# logic (a tiered discount rule) that ops/business users could tweak by editing this file alone --
# no C# recompile, no redeploy of the .NET host -- while still running inside a real production
# ASP.NET Core service.


def quote(unit_price: float, quantity: int) -> dict:
    if unit_price < 0 or quantity < 0:
        raise ValueError("unit_price and quantity must be non-negative")

    if quantity >= 100:
        discount = 0.20
    elif quantity >= 10:
        discount = 0.10
    else:
        discount = 0.0

    subtotal = unit_price * quantity
    total = subtotal * (1 - discount)
    return {
        "unit_price": unit_price,
        "quantity": quantity,
        "discount_pct": discount * 100,
        "subtotal": round(subtotal, 2),
        "total": round(total, 2),
    }
