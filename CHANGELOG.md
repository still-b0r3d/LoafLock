# What changed

Each version's exe says its build (the commit it was made from) in the window, in
Ruckus's menu and under Help → About.

## 0.83

The first release since 0.8, so 0.81's changes (below) come with it.

- **Unlocking by itself** (Options → By itself, off to start with): once the cat's been gone
  for 30 seconds to 5 minutes, the keyboard unlocks. Gone means nothing pressed or held down
  for that long, once the lock has blocked a paw, so a lock you set before the cat comes
  stays on until it's been and gone. Before trusting the quiet, LoafLock puts its keyboard
  watch in afresh and listens a moment more, in case Windows had quietly taken the old one
  out. The note says "Keyboard unlocked by itself", and the log says how long nothing was
  pressed.
- **The hotkey can unlock too** (Options → Locking → It unlocks too, off to start with). It
  unlocks as you let go, and only counts with just its keys down and nothing else pressed
  before you let go, so a cat lying across the keyboard can't do it, nor a paw landing on the
  hotkey and the keys next to it. A key on its own (Pause, Scroll Lock, F13 to F24) still
  only locks. While it's on, the window, the note on locking and the help for a stuck
  keyboard name it.
- With locking by itself and unlocking by itself both on, Options says what that means.
- **Cat detection's numbers** can be changed: how many keys (3 to 6) and how long they stay
  down (0.1, 0.25 or 0.5 s). The numbers it used before, 4 keys and 0.25 s, are the
  defaults, and the log gives the numbers when they change.
- **Options in pages**, like TVFollow's settings: Locking, By itself, Your cat, Notes, Look
  and Starting, listed down the left with an icon each. In every row of choices, the one
  LoafLock starts with says "(default)".

## 0.81

- **Cat detection** (experimental, off to start with), in Options under "Your cat". It locks
  the keyboard when several keys go down at once, the way a paw does: 4 together, 3 landing
  at the same moment, or 3 put down within a second and held for a second and a half. It
  can't spot a cat on a keyboard that senses only 2 keys at once, and holding three keys
  together in a game could set it off. It locks only the keyboard, and the log says what it
  saw (how many keys, never which). While it's on, LoafLock watches the keyboard while
  unlocked too.
- **Options…** is in the window's LoafLock menu now, so the menu bar is just LoafLock and
  Help. Look moved to the left side of the Options window to make room.
- The cat's name is spelled Ruckus now (it was Rukus), in the window's help, the unlock
  word window, Options and the README.

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

- On a long loaf (half a minute of paws without a break) Ruckus nods off in the note:
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

- The first version: lock and unlock from Ruckus in the taskbar corner, an unlock word,
  the LoafLock window, paws blocked today and all time, start with Windows.
