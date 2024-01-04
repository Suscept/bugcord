import websockets
import asyncio

CONNECTIONS = set()

async def client():
    uri = "ws://localhost:25987"

    async with websockets.connect(uri) as client:
        async for message in client:
            await consume_client(message)
        await produce(client)

async def server():
    stop = asyncio.Future()

    server = await websockets.serve(handle, "localhost", port=25987)

    await stop
    await server.close()

async def handle(websocket:websockets.WebSocketServerProtocol):
    asyncio.gather(register_connection(websocket), consume_server(websocket))

async def register_connection(websocket:websockets.WebSocketServerProtocol):
    CONNECTIONS.add(websocket)
    print("added connection")
    try:
        await websocket.wait_closed()
    finally:
        CONNECTIONS.remove(websocket)

async def consume_server(websocket:websockets.WebSocketServerProtocol):
    print("echoiung")
    async for message in websocket:
        print("casting")
        websockets.broadcast(CONNECTIONS, message)

async def consume_client(message):
    print(message)

async def produce(websocket:websockets.WebSocketServerProtocol):
    print("taking input")
    while True:
        await websocket.send(input("message: "))

async def main():
    serverTask = asyncio.create_task(server())
    clientTask = asyncio.create_task(client())
    if input("host? y/n ") == "y":
        await serverTask
    await clientTask
    

if __name__ == '__main__':
    asyncio.run(main())