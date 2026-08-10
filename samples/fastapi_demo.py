# Copyright (c) 2026 Marco Parenzan
#
# Licensed under the MIT License. See the LICENSE file in the project
# root for full license information.

# fastapi_demo.py — a real, unmodified FastAPI app, served over a real HTTP/1.1 connection
# by PySharp's own ASGI server (asgi_server.py), run entirely by PySharp.
#
# Scenario 2 of the roadmap (phase 4.2): the first real target FastAPI app, wired to a real
# ASGI server rather than starlette's in-process TestClient (FASTAPI_PLAN.md Phase 4.1.10/
# 4.1.11 verified the full request/response stack through TestClient; this sample is the
# live, curl-able version of the same milestone). Real fastapi builds real routes, real
# pydantic validates the request body and shapes the JSON response, real starlette's
# exception handling turns a raised HTTPException into the right status code — none of it
# stubbed or reimplemented, this is the actual PyPI package.
#
# Prerequisite (from the repo root, so ./site-packages matches where `run` looks for it):
#   pysharp install fastapi==0.99.1
#   pysharp install starlette==0.27.0
#   pysharp install pydantic==1.10.13
#   pysharp install typing_extensions
#   pysharp install anyio
#   pysharp install annotated_doc
#
# Usage:  pysharp run samples/fastapi_demo.py
#   curl http://127.0.0.1:8000/
#   curl http://127.0.0.1:8000/items/42
#   curl "http://127.0.0.1:8000/search?q=hello&limit=5"
#   curl -X POST http://127.0.0.1:8000/items -H "Content-Type: application/json" -d "{\"name\": \"widget\", \"price\": 9.5}"
#   curl -X POST http://127.0.0.1:8000/items -H "Content-Type: application/json" -d "{\"name\": \"bad\"}"     # 422, missing price
#   curl -X PUT http://127.0.0.1:8000/items/1 -H "Content-Type: application/json" -d "{\"name\": \"widget\", \"price\": 9.5}"
#   curl -X DELETE http://127.0.0.1:8000/items/1
#   curl http://127.0.0.1:8000/items/1     # 404 after the delete above
#   (WebSocket: connect to ws://127.0.0.1:8000/ws and send text — echoes back "echo: ...",
#    driven by real starlette's own WebSocket class, not asgi_server.py's dependency-free demo)

import asyncio

from asgi_server import serve
from fastapi import FastAPI, HTTPException, WebSocket, WebSocketDisconnect
from pydantic import BaseModel

app = FastAPI()
items: dict = {}


class Item(BaseModel):
    name: str
    price: float


@app.get("/")
async def index():
    return {"message": "hello from a real FastAPI app served by PySharp"}


@app.get("/items/{item_id}")
async def get_item(item_id: int):
    if item_id not in items:
        raise HTTPException(status_code=404, detail="Item not found")
    return items[item_id]


@app.put("/items/{item_id}")
async def put_item(item_id: int, item: Item):
    items[item_id] = item
    return {"item_id": item_id, "item": item}


@app.delete("/items/{item_id}")
async def delete_item(item_id: int):
    if item_id not in items:
        raise HTTPException(status_code=404, detail="Item not found")
    del items[item_id]
    return {"deleted": item_id}


@app.post("/items")
async def create_item(item: Item):
    return {"name": item.name, "price": item.price, "total": item.price * 2}


@app.get("/search")
async def search(q: str = "", limit: int = 10):
    return {"q": q, "limit": limit}


@app.websocket("/ws")
async def websocket_endpoint(websocket: WebSocket):
    await websocket.accept()
    try:
        while True:
            data = await websocket.receive_text()
            await websocket.send_text("echo: " + data)
    except WebSocketDisconnect:
        pass


if __name__ == "__main__":
    asyncio.run(serve(app))
