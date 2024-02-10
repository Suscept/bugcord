from tkinter import *
from tkinter import messagebox
from async_tkinter_loop import async_handler, async_mainloop
from enum import Enum
import json
import uuid
import asyncio
import websockets
class Packet(Enum):
    Message = 1
    Handshake = 2
    VoicePacket = 3

client = None

def messageBox(title:str = None, message:str = None):
    if title == None:
        title = "Message"
    if message == None:
        message = "Not implemented"

    messagebox.showinfo(title, message)

async def cum(pen):
    print(pen.state)
    if pen.state == 8:
        msg = channelInput.get(1.0, 'end')[:-2] # message comes with \n\n for some reason so cut it out

        # channelContent.configure(state=NORMAL)
        # channelContent.insert('end', "Bussyman: " + msg)
        # channelContent.configure(state=DISABLED)
        channelInput.delete(1.0, 'end')

        await client.send(json.dumps({"pktpe":"msg", "usr":user["username"], "content":msg}))

        return 'break'
    
async def connect(uriHolder):
    global client
    
    if client != None:
        await client.close()
    
    uriParsed = uriHolder.get().split(":")
    if len(uriParsed) == 1:
        uriParsed.append("25987")

    formattedUri = "ws://{}:{}".format(uriParsed[0], uriParsed[1])
    print("Connecting to " + formattedUri + "...")
    client = await websockets.connect(formattedUri)
    print("Connected!")
    connectionStatus.configure(text="Connected")
    await consume_client(client)
    
    print("cloihg")
    await client.close()

async def consume_client(websocket:websockets.WebSocketServerProtocol):
    async for message in websocket:
        print(message)
        msgJson = json.loads(message)
        # if msgJson["usr"] == user["username"]:
        #     continue
        if msgJson["pktpe"] == "servhndshke":
            print_message("Server MOTD: " + msgJson["motd"])
        if msgJson["pktpe"] == "msg":
            print_message(msgJson["usr"] + ": " + msgJson["content"])

def print_message(message:str):
    channelContent.configure(state=NORMAL)
    channelContent.insert('end', "\n\n"+message)
    channelContent.configure(state=DISABLED)

if __name__ == '__main__':
    # Account creation step
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

    # Login
    user = json.loads(userRaw)
    print("Logging in as " + user["username"])
    #print("Connect to " + user["defaultConnect"] + "?\nPress enter to connect. Enter server IP to connect to other server")
    uri = user["defaultConnect"]

    client = None

    # UI
    root = Tk("Bugcord")
    root.wm_title("Bugcord")

    connectionStatus = Label(root, text="Not Connected")
    connectUrl = Entry(root)
    connectUrl.insert(0, uri)
    connectButton = Button(root, text="Connect", command=async_handler(connect, connectUrl))

    channelContent = Text(root, background="red", wrap=WORD, state=DISABLED)

    channelInput = Text(root, exportselection=0, wrap=WORD, undo=TRUE, height=1)
    fileButton = Button(root, text="Embed", command=messageBox)

    channelContent.grid(row=1, column=0, columnspan=5, sticky=NSEW)

    channelInput.grid(row=2, column=0, columnspan=4, sticky=NSEW)
    channelInput.bind(sequence="<Return>", func=async_handler(cum))
    fileButton.grid(row=2, column=4, sticky=E)

    connectUrl.grid(row=0, column=0)
    connectButton.grid(row=0, column=1)
    connectionStatus.grid(row=0, column=2)

    #root.columnconfigure(1, weight=1)
    root.columnconfigure(3, weight=1)
    root.rowconfigure(1, weight=1)

    async_mainloop(root)