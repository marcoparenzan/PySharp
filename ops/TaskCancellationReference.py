# Copyright (c) 2026 Marco Parenzan
# Licensed under the MIT License. See the LICENSE file in the project root.
"""Live CPython 3.11/3.12 cancellation-counter reference (PySharp advertises 3.12)."""
import asyncio
import sys

async def requests():
    async def worker():
        return 'done'
    task = asyncio.create_task(worker())
    print(task.cancelling(), task.cancel(), task.cancel(), task.cancelling())
    print(task.uncancel(), task.uncancel(), task.uncancel())
    try:
        print(await task, task.cancel(), task.cancelling())
    except asyncio.CancelledError:
        print('CancelledError', task.cancelled(), task.cancel(), task.cancelling())

async def delivered():
    async def worker():
        await asyncio.sleep(10)
    task = asyncio.create_task(worker())
    await asyncio.sleep(0)
    task.cancel()
    task.cancel()
    try:
        await task
    except asyncio.CancelledError:
        pass
    print(task.cancelled(), task.cancelling())
    print(task.uncancel(), task.uncancel(), task.cancelled())

async def suppressed():
    async def worker():
        try:
            await asyncio.sleep(10)
        except asyncio.CancelledError:
            return 'handled'
    task = asyncio.create_task(worker())
    await asyncio.sleep(0)
    task.cancel()
    print(await task, task.cancelled(), task.cancelling(), task.uncancel())

async def future():
    value = asyncio.get_running_loop().create_future()
    print(hasattr(value, 'cancelling'), hasattr(value, 'uncancel'))

print('CPython:', sys.version.split()[0])
for name, scenario in [('requests', requests), ('delivered', delivered), ('suppressed', suppressed), ('future', future)]:
    print(name)
    asyncio.run(scenario())
