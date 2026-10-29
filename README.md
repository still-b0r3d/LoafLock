# LoafLock

Keeps a cat lying on the keyboard from typing. LoafLock sits in the Windows taskbar corner
as Rukus. Lock the keyboard from there, and key presses go nowhere while the mouse keeps
working (or locks too, if you like).

**Work in progress.** Version 0.7, not released yet.

## Using it

1. Run `LoafLock.exe`. Its window opens, and the first time it asks for an **unlock
   word** (letters only, at least 4): a word a cat won't type.
2. Click **Lock keyboard**, in the window or from Rukus's menu (right-click him in the
   taskbar corner). He gets a gold padlock, and key presses go nowhere. The mouse keeps
   working. A small note in the corner says when the keyboard locks and unlocks; it never
   takes focus, so a fullscreen video stays fullscreen.
3. To get the keyboard back, type your unlock word, or click **Unlock keyboard**.

Left-click Rukus any time for the window: the paws blocked today and all time, and your
unlock word. **Options** (in its menu bar, or Rukus's menu) has:

- **a hotkey** that locks (off to start with; Ctrl+Alt+L unless you pick another). It only
  locks: it can't unlock, so a cat can't either;
- **Let the volume keys through** while locked: mute, volume up and down, a volume knob;
- **Locking by itself** after 5 to 60 minutes with nobody using the keyboard or mouse (off
  to start with). Watching a video without touching anything counts; a fullscreen game
  doesn't. It locks only the keyboard, so whoever comes back can click Unlock;
- the notes (on locking and unlocking, or on every blocked key). When the cat's been lying
  on the keyboard for half a minute, Rukus nods off in one: "Biscuit is loafing";
- **your cat's name**, for "Paws blocked by Biscuit";
- **Lock the mouse too** (off to start with): the pointer, the buttons and the wheel stop
  as well, and only your unlock word gets things back;
- **Look**: like Windows, light or dark;
- **Start minimized**, and **Start LoafLock when you sign in to Windows**.

From a shortcut, a script or a Stream Deck button:

```
LoafLock.exe lock      locks (and starts LoafLock first if it isn't running)
LoafLock.exe unlock    unlocks
```

If you're ever stuck: Ctrl+Alt+Del, Task Manager, end LoafLock. The keyboard comes back at
once. With the mouse locked too: Ctrl+Alt+Del, **Lock**, then sign back in, and the mouse
works again, so you can click Unlock. (Both are in the window's **Help** menu.)

To remove LoafLock: **Options → Remove my settings…** (its settings, its log and its
sign-in entry), then delete `LoafLock.exe`.

What it never does: connect to the internet, or keep what you type. While unlocked it
doesn't look at the keyboard or the mouse at all.

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
