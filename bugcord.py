import websockets
import asyncio

CONNECTIONS = set()

async def client():
    uri = "ws://10.0.0.25:25987"

    client = await websockets.connect(uri)

    async for message in client:
        await consume_client(message)
    await produce(client)

async def consume_client(message):
    print(message)

async def produce(websocket:websockets.WebSocketServerProtocol):
    print("taking input")
    while True:
        await websocket.send(input("message: "))

async def main():
    clientTask = asyncio.create_task(client())
    await clientTask
    

if __name__ == '__main__':
    asyncio.run(main())