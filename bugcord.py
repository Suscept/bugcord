import websockets
import asyncio

CONNECTIONS = set()

async def client():
    uri = "ws://10.0.0.25:25987"

    client = await websockets.connect(uri)
    await asyncio.gather(
        consume_client(client),
        produce(client)
        )

    print("cloihg")
    client.close()

async def consume_client(websocket:websockets.WebSocketServerProtocol):
    async for message in websocket:
        print("resc " + message)

async def produce(websocket:websockets.WebSocketServerProtocol):
    print("taking input")
    while True:
        msg = await asyncio.to_thread(input)
        print("msg: " + msg)
        await websocket.send(msg)

async def main():
    clientTask = asyncio.create_task(client())
    await clientTask
    

if __name__ == '__main__':
    asyncio.run(main())