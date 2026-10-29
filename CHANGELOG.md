# What changed

Each version's exe says its build (the commit it was made from) in the window, in
Rukus's menu and under Help → About.

## 0.8

- Help → **Check for updates…** opens the downloads page. LoafLock itself never goes on
  the internet.
- The license: MIT with the Commons Clause (free to use, change and share; not to sell).
- Each tagged version is built by GitHub and drafted as a release, with its SHA-256.
- The hotkey is Ctrl+Shift+L unless you pick another, and the picker turns down combos that
  get in the way: Ctrl with one key (Ctrl+C), Ctrl+Alt with a letter (on many keyboards
  that's AltGr, which types a character), and Space or Enter.
- Locking by itself waits while Windows is locked or switched to another sign-in, so you
  don't sign back in to a locked keyboard. If it ever can't lock, it only notes it in the log.
- With the note on every blocked key on, the loafing note stays up while the loaf goes on.

## 0.7

- On a long loaf (half a minute of paws without a break) Rukus nods off in the note:
  "Biscuit is loafing", with a "z Z Z". Once per loaf.

## 0.6

- **Locking by itself**, after 5 to 60 minutes with nobody using the keyboard or mouse.
  Not while a game has the whole screen to itself. It locks only the keyboard, so whoever
  comes back can click Unlock.

## 0.5

- **A hotkey** that locks. It can't unlock, so a cat can't either.
- **Let the volume keys through** while locked: mute, volume up and down, a volume knob.
- Options in two columns.

## 0.4

- Space no longer pauses YouTube while locked: a key's release is now dropped along
  with its press. Shift, Ctrl, Alt, Windows and keys held from before the lock still get
  their release, so nothing can stick down.
- `LoafLock.exe lock` and `LoafLock.exe unlock`, for shortcuts and Stream Deck buttons.
- Your cat's name: "Paws blocked by Biscuit".
- **Look**: like Windows, light or dark.
- **Lock the mouse too** (off to start with). Locking Windows (Ctrl+Alt+Del → Lock) lets
  go of the mouse.

## 0.3

- A menu bar on the window, and an Options window.
- Notes in the corner when the keyboard locks and unlocks; the note on every blocked key
  is optional.
- Start minimized.

## 0.2

- The first version: lock and unlock from Rukus in the taskbar corner, an unlock word,
  the LoafLock window, paws blocked today and all time, start with Windows.
