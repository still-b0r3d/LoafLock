# LoafLock

Keeps a cat lying on the keyboard from typing. LoafLock sits in the Windows taskbar corner
as Rukus. Lock the keyboard from there, and key presses go nowhere while the mouse keeps
working.

**Work in progress.** v0.1 isn't out yet. For now LoafLock only shows Rukus in the taskbar
corner; locking comes next.

What it will never do: connect to the internet, or keep what you type.

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
