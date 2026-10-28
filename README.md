# LoafLock

Keeps a cat lying on the keyboard from typing. LoafLock sits in the Windows taskbar corner
as Rukus. Lock the keyboard from there, and key presses go nowhere while the mouse keeps
working.

**Work in progress.** Version 0.3, not released yet.

## Using it

1. Run `LoafLock.exe`. Its window opens, and the first time it asks for an **unlock
   word** (letters only, at least 4): a word a cat won't type.
2. Click **Lock keyboard**, in the window or from Rukus's menu (right-click him in the
   taskbar corner). He gets a gold padlock, and key presses go nowhere. The mouse keeps
   working. A small note in the corner says when the keyboard locks and unlocks; it never
   takes focus, so a fullscreen video stays fullscreen.
3. To get the keyboard back, type your unlock word, or click **Unlock keyboard**.

Left-click Rukus any time for the window: the paws blocked today and all time, and your
unlock word. Its **Options** menu has the notes (on locking and unlocking, or on every
blocked key), **Start minimized**, and **Start LoafLock when you sign in to Windows**.

If you're ever stuck: Ctrl+Alt+Del, Task Manager, end LoafLock. The keyboard comes back at
once. (It's in the window's **Help** menu too.)

To remove LoafLock: in its window, **Options → Remove my settings…** (its settings, its
log and its sign-in entry), then delete `LoafLock.exe`.

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
