from tkinter import *
from async_tkinter_loop import async_handler, async_mainloop

def cum(pen):
    print(pen.state)
    if pen.state == 8:
        channelContent.configure(state=NORMAL)
        channelContent.insert('end', "\nBussyman: " + channelInput.get(1.0, 'end'))
        channelContent.configure(state=DISABLED)
        channelInput.delete(1.0, 'end')
        return 'break'

root = Tk("Bugcord")
root.wm_title("Bugcord")

connectionStatus = Label(root, text="Connected")
connectButton = Button(root, text="Connect")
connectUrl = Entry(root)

channelContent = Text(root, background="red", wrap=WORD, state=DISABLED)

channelInput = Text(root, exportselection=0, wrap=WORD, undo=TRUE, height=1)
fileButton = Button(root, text="Embed")

channelContent.grid(row=1, column=0, columnspan=5, sticky=NSEW)

channelInput.grid(row=2, column=0, columnspan=4, sticky=NSEW)
channelInput.bind(sequence="<Return>", func=cum)
fileButton.grid(row=2, column=4, sticky=E)

connectUrl.grid(row=0, column=0)
connectButton.grid(row=0, column=1)
connectionStatus.grid(row=0, column=2)

#root.columnconfigure(1, weight=1)
root.columnconfigure(3, weight=1)
root.rowconfigure(1, weight=1)

root.mainloop()