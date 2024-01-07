import websockets
import asyncio
import json

CONNECTIONS = set()

async def server():
    stop = asyncio.Future()
    
    server = await websockets.serve(handle, "10.0.0.25", port=25987)

    await stop
    await server.close()

async def handle(websocket:websockets.WebSocketServerProtocol):
    await websocket.send(json.dumps({"usr":"10.0.0.25", "pktpe":"servhndshke", "networkcount":len(CONNECTIONS), "motd":"Hello chat"}))
    await asyncio.gather(
        register_connection(websocket),
        consume_server(websocket)
        )

async def register_connection(websocket:websockets.WebSocketServerProtocol):
    CONNECTIONS.add(websocket)
    print("added connection")
    try:
        await websocket.wait_closed()
    finally:
        print("removed conectino")
        CONNECTIONS.remove(websocket)

async def consume_server(websocket:websockets.WebSocketServerProtocol):
    print("echoiung")
    async for message in websocket:
        print("casting " + message)
        websockets.broadcast(CONNECTIONS, message)

async def main():
    await server()
    

if __name__ == '__main__':
    asyncio.run(main())