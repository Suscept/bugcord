import websockets
import asyncio
import json
from enum import Enum

CONNECTIONS = set()

class Packet(Enum):
    Message = 1
    Handshake = 2
    VoicePacket = 3

    def __eq__(self, __value: object) -> bool:
        return self.value == __value

async def server():
    stop = asyncio.Future()
    
    server = await websockets.serve(handle, "10.0.0.25", port=25987)
    print("open")
    await stop
    await server.close()

async def handle(websocket:websockets.WebSocketServerProtocol):
    #await websocket.send(json.dumps({"usr":"10.0.0.25", "pktpe":"servhndshke", "networkcount":len(CONNECTIONS), "motd":"Hello chat"}))
    await websocket.send(json.dumps({"pktpe":Packet.Message.value, "usr":"Server", "content":"Hello chat. " + str(len(CONNECTIONS)) + " other users connected."}))
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