from tkinter import *
from tkinter import messagebox
from async_tkinter_loop import async_handler, async_mainloop
import json
import uuid
import asyncio
import websockets
import pyaudio

class Client():
    def __init__(self, user) -> None:
        self.socketProtocol = None
        self.user = user

    def uriCleaner(self, rawUri:str) -> str:
        """Takes an ip address or url and adds all needed information.
        
        If no port is provided the default of ``25987`` will be added."""

        if rawUri[:5] == "ws://": # Remove this so port can be detected
            rawUri = rawUri[5:]

        uriParsed = rawUri.split(":") # Used to find if port was specified
        if len(uriParsed) == 1: # If port was not specifed
            uriParsed.append("25987")

        return "ws://{}:{}".format(
            uriParsed[0], # Host / IP
            uriParsed[1]) # Port
    
    async def connect(self, uri, task):
        """Attempt to connect and remain connected to the provided uri"""
        if self.socketProtocol != None:
            print("already connected")
            await self.socketProtocol.close()

        async for websocket in websockets.connect(uri):
            try:
                self.socketProtocol = websocket
                await asyncio.gather(task())
                await self.socketProtocol.close()
            except websockets.ConnectionClosed:
                print("cock loop")
                continue
            break
