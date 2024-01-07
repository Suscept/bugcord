import websockets
import asyncio
import json
import uuid

print("Welcome to Bugcord!")
try:
    userRaw = open("user.txt").read()
except FileNotFoundError:
    print("No user.txt found. Creating new account")
    username = input("Enter username: ")
    uri = input("Enter IP of server to connect to: ")
    print("Creating account...")
    user = {"username":username, "defaultConnect":uri, "secretDONTSHARE":str(uuid.uuid4())}
    userRaw = json.dumps(user)
    try:
        open("user.txt", "w").write(userRaw)
    except Exception as e:
        print(e)
        input()
    while True:
        input("Created account!\nPlease close and reopen Bugcord.")
    

user = json.loads(userRaw)
print("Logging in as " + user["username"])
print("Connect to " + user["defaultConnect"] + "?\nPress enter to connect. Enter server IP to connect to other server")
serverInput = input()
if serverInput == "":
    uri = user["defaultConnect"]
else:
    uri = serverInput
    setDefault = input("Set this IP as your default? Y/N: ")
    if setDefault == "y":
        user["defaultConnect"] = uri
        print("Set " + uri + " as default ip")

async def client():
    uriParsed = uri.split(":")
    if len(uriParsed) == 1:
        uriParsed.append("25987")

    formattedUri = "ws://{}:{}".format(uriParsed[0], uriParsed[1])
    print("Connecting to " + formattedUri + "...")
    client = await websockets.connect(formattedUri)
    print("Connected!")
    await asyncio.gather(
        consume_client(client),
        produce(client)
        )

    print("cloihg")
    client.close()

async def consume_client(websocket:websockets.WebSocketServerProtocol):
    async for message in websocket:
        msgJson = json.loads(message)
        if msgJson["usr"] == user["username"]:
            continue
        if msgJson["pktpe"] == "servhndshke":
            print_message("Server MOTD: " + msgJson["motd"])
        if msgJson["pktpe"] == "msg":
            print_message(msgJson["usr"] + ": " + msgJson["content"])

async def produce(websocket:websockets.WebSocketServerProtocol):
    print("taking input")
    while True:
        print("You: ", end='')
        msg = await asyncio.to_thread(input)
        await websocket.send(json.dumps({"pktpe":"msg", "usr":user["username"], "content":msg}))

def print_message(message:str):
    print("\r" + message)

async def main():
    clientTask = asyncio.create_task(client())
    await clientTask
    

if __name__ == '__main__':
    asyncio.run(main())