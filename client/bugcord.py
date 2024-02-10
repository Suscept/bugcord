from tkinter import *
from tkinter import messagebox
from async_tkinter_loop import async_handler, async_mainloop
from enum import Enum
import json
import uuid
import asyncio
import websockets
import webrunner
import pyaudio
import base64

# mobile version
# messages save
# pins
# notifacations
# online indacators
# specific pings
# screen share
# camera
# mute/deafen
# keybinds
# gifs embeds and file uploads
# change volume / mute specific users

class Packet(Enum):
    Message = 1
    Handshake = 2
    VoicePacket = 3

    def __eq__(self, __value: object) -> bool:
        return self.value == __value

class User():
    def __init__(self, username, defaultServer, secret) -> None:
        self.username = username
        self.defaultServer = defaultServer
        self.secret = secret

client = None
voiceStream = None

def messageBox(title:str = None, message:str = None):
    if title == None:
        title = "Message"
    if message == None:
        message = "Not implemented"

    messagebox.showinfo(title, message)

async def sendPacket(type:Packet, *args, **kwargs):
    match type:
        case Packet.Message:
            await client.socketProtocol.send(json.dumps({
                "pktpe":type.value,
                "usr":client.user.username,
                "content":kwargs["msg"]
                }))
        case Packet.VoicePacket:
            await client.socketProtocol.send(json.dumps({
                "pktpe":type.value,
                "usr":client.user.username,
                "data":kwargs["data"]
                }))


async def sendMessage(event):
    if event.state == 8: # If shift was not held when enter was pressed
        msg = channelInput.get(1.0, 'end')[:-2] # message comes with \n\n for some reason so cut it out

        channelInput.delete(1.0, 'end')

        await sendPacket(Packet.Message, client.socketProtocol, msg=msg)
        print_message(client.user.username + ": " + msg)
        #await client.send(json.dumps({"pktpe":"msg", "usr":user.username, "content":msg}))

        return 'break'

async def consume_client():
    p = pyaudio.PyAudio()
    voiceStream = p.open(
        rate=10000,
        channels=1,
        output_device_index=8,
        output=True,
        format=pyaudio.paInt16,
        frames_per_buffer=2048)

    async for message in client.socketProtocol:
        msgJson = json.loads(message)
        # if msgJson["usr"] == user["username"]: # Ignore own packets
        #     continue
        if msgJson["pktpe"] == Packet.Handshake:
            print_message("Server MOTD: " + msgJson["motd"])
        if msgJson["pktpe"] == Packet.Message:
            print_message(msgJson["usr"] + ": " + msgJson["content"])
        if msgJson["pktpe"] == Packet.VoicePacket:
            print("recv")
            voiceStream.write(base64.b64decode(msgJson["data"]))

    voiceStream.stop_stream()
    p.terminate()

def print_message(message:str):
    channelContent.configure(state=NORMAL)
    channelContent.insert('end', "\n\n"+message)
    channelContent.configure(state=DISABLED)

async def startConnect():
    await client.connect(client.uriCleaner(connectUrl.get()), consume_client)
    
async def startVoiceStream():
    # if client == None or voiceStream != None:
    #     return
    
    p = pyaudio.PyAudio()
    voiceStream = p.open(
        rate=10000,
        channels=1,
        input_device_index=1,
        #output_device_index=8,
        input=True,
        #output=True,
        format=pyaudio.paInt16,
        frames_per_buffer=2048)
    
    while True:
        print(voiceStream.get_read_available())
        voiceBase64 = base64.b64encode(voiceStream.read(4096)).decode()
        print("sending")
        await sendPacket(type=Packet.VoicePacket, data=voiceBase64)
        await asyncio.sleep(0.1)
    voiceStream.stop_stream()
    p.terminate()

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
    userJson = json.loads(userRaw)
    user = User(userJson["username"], userJson["defaultConnect"], userJson["secretDONTSHARE"])
    print("Logging in as " + user.username)

    client = webrunner.Client(user)
    voiceStream = None

    # UI
    root = Tk("Bugcord")
    root.wm_title("Bugcord")

    connectionStatus = Label(root, text="Not Connected")
    connectUrl = Entry(root)
    connectUrl.insert(0, user.defaultServer)
    connectButton = Button(root, text="Connect", command=async_handler(startConnect))
    connectVoiceButton = Button(root, text="Connect to voice", command=async_handler(startVoiceStream))

    channelContent = Text(root, background="red", wrap=WORD, state=DISABLED)

    channelInput = Text(root, exportselection=0, wrap=WORD, undo=TRUE, height=1)
    fileButton = Button(root, text="Embed", command=messageBox)

    channelContent.grid(row=1, column=0, columnspan=5, sticky=NSEW)

    channelInput.grid(row=2, column=0, columnspan=4, sticky=NSEW)
    channelInput.bind(sequence="<Return>", func=async_handler(sendMessage))
    fileButton.grid(row=2, column=4, sticky=E)

    connectUrl.grid(row=0, column=0)
    connectButton.grid(row=0, column=1)
    connectionStatus.grid(row=0, column=2)
    connectVoiceButton.grid(row=0, column=3)

    root.columnconfigure(3, weight=1)
    root.rowconfigure(1, weight=1)

    async_mainloop(root)