# https://stackoverflow.com/questions/47895765/use-asyncio-and-tkinter-or-another-gui-lib-together-without-freezing-the-gui
# https://stackoverflow.com/a/74583971

from tkinter import *
from tkinter import messagebox
import asyncio
import random
from async_tkinter_loop import async_handler, async_mainloop


def do_freezed():
    """ Button-Event-Handler to see if a button on GUI works. """
    messagebox.showinfo(message='Tkinter is reacting.')


async def one_url(url):
    """ One task. """
    sec = random.randint(1, 15)
    await asyncio.sleep(sec)
    return 'url: {}\tsec: {}'.format(url, sec)


async def do_urls():
    """ Creating and starting 10 tasks. """
    tasks = [
        asyncio.create_task(one_url(url))  # added create_task to remove warning "The explicit passing of coroutine objects to asyncio.wait() is deprecated since Python 3.8, and scheduled for removal in Python 3.11."
        for url in range(10)
    ]
    print("Started")
    completed, pending = await asyncio.wait(tasks)
    results = [task.result() for task in completed]
    print('\n'.join(results))
    print("Finished")


if __name__ == '__main__':
    root = Tk()

    # Wrap async function into async_handler to use it as a button handler or an event handler
    buttonT = Button(master=root, text='Asyncio Tasks', command=async_handler(do_urls))
    buttonT.pack()
    buttonX = Button(master=root, text='Freezed???', command=do_freezed)
    buttonX.pack()

    # Use async_mainloop(root) instead of root.mainloop()
    async_mainloop(root)