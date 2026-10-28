# LoafLock

Keeps a cat lying on the keyboard from typing. LoafLock sits in the Windows taskbar corner
as Rukus. Lock the keyboard from there, and key presses go nowhere while the mouse keeps
working.

**Work in progress.** v0.1 isn't out yet.

## Using it

1. Run `LoafLock.exe`. The first time, it asks for an **unlock word** (letters only, at
   least 4): a word a cat won't type.
2. Right-click Rukus in the taskbar corner and pick **Lock keyboard**. He gets a gold
   padlock, and key presses go nowhere. The mouse keeps working.
3. To get the keyboard back, type your unlock word, or right-click Rukus and pick
   **Unlock keyboard**.

If you're ever stuck: Ctrl+Alt+Del, Task Manager, end LoafLock. The keyboard comes back at
once.

What it never does: connect to the internet, or keep what you type. While unlocked it
doesn't look at the keyboard at all.

## Build it yourself

Nothing to install: `build.cmd` uses the C# compiler that comes with Windows
(.NET Framework 4.8).

```
build.cmd         builds bin\LoafLock.exe
build.cmd test    builds it and runs the tests
```

The tray icons are made from Rukus by `tools\make-icons.ps1`.

---

Made by [b0r3d.org](https://b0r3d.org). Rukus is b0r3d.org's cat.
