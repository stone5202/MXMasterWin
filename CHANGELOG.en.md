# Changelog

<p>
  <a href="CHANGELOG.md">繁體中文</a> · <strong>English</strong>
</p>

## 2.4 — 2026-11-14

- New Receiver page (between Keyboard and Advanced): pair a new mouse or keyboard with a Logi Bolt receiver (including the passkey typed on a keyboard or clicked on a mouse), list the paired devices and unpair them, without installing Logi Bolt or Logi Options+.
- When the channel a mouse or keyboard uses for this computer stores a different name (a channel paired through Logi Bolt is stored as "Logitech Bolt receiver", for one), this computer's name is written to the device, so MX Flow shows the right computer and the keyboard-follows matching is more accurate (as Logi Options+ does; only the current channel is written).
- Opening the MX Flow page reads the channel names again (before, only when the mouse connected).
- Pointer & Wheel adds ratchet torque (1–100) when the device advertises support; confirmed values are saved and restored after reconnecting.
- The Keyboard page adds individual switches for the keys the device can disable, preserving other key states and confirming changes by reading back.
- The Keyboard page adds the OS layout for the current Easy-Switch channel, using the platform list reported by the device and confirming changes by reading back.
- Backlight v3 adds hands-away, hands-near and external-power delays (1–600 seconds, rounded up to 5-second units), preserving other backlight fields and confirming changes by reading back.
- Pointer & Wheel adds device-side thumb-wheel inversion, preserving the existing left/right action mappings; confirmed values are saved and restored after reconnecting.
- Pointer & Wheel adds high resolution for the main wheel (scrolling may become faster), preserving other mode bits; confirmed values are saved and restored after reconnecting.
- Layout: the Keyboard page's sub-pages use a segmented control, and every row has its title and hint on the left and the setting on the right, stacked from the top with only what the keyboard supports; disabling keys uses switches in two columns. Pointer & Wheel shows connection, battery and firmware in three columns with the wheel settings aligned, and thumb-wheel inversion and high resolution are switches. On the Receiver page the paired devices sit right under the pairing hints while no pairing is going on.
- One more item in the sidebar, so the items sit slightly closer together.
- Fix: with "Switch the keyboard with the mouse" turned off, a switch queued while the keyboard slept could still happen. Turning it off now cancels it, and a cancel during the wait also wins.
- Fix: a keyboard that slept on Logi Bolt and came back over Bluetooth stayed "asleep" in the app. While it does not answer, the other connections are searched again every 60 seconds (at once on Reload), keeping the last reading meanwhile.
- Fix: with the mouse on Bluetooth and the keyboard on a Logi Bolt receiver, keyboard traffic could still count as mouse activity and make the app check a sleeping mouse.
- The engineering log no longer writes Logi Bolt pairing passkeys, device addresses or serial numbers.

## 2.3.1 — 2026-11-14

- Fix: a keyboard on a Logi Bolt receiver sleeps after a while idle, and the Keyboard page and the bottom-left corner then lost it for a while. The last reading is now kept and marked as not answering, and it comes back as soon as a key is pressed.
- Fix: when the mouse moved to another computer while the keyboard was asleep, the keyboard stayed behind. The switch is now queued and sent as soon as the keyboard wakes (any key) within 60 seconds; it is cancelled if the mouse comes back first.
- Fix: with the mouse and a Logitech keyboard that supports button diversion (an MX Mechanical, for one) on the same Logi Bolt receiver and the keyboard in an earlier slot, the app took the keyboard for the mouse, showed "The device reported no divertable C3 / 1A0 controls", left the special buttons dead, and kept sending requests to the keyboard, so the Keyboard page often lost it too. The device type is now checked and slots that are not a mouse are skipped.
- Fix: with the mouse and the keyboard on the same Logi Bolt receiver, the keyboard took a long time to be found (sometimes a minute or two), and with the Keyboard page open it kept coming and going as not answering although it typed fine. An idle Bolt device answers its first request only after about a second, longer than the app waited, and the keyboard's traffic was taken for mouse activity, so the app kept reconnecting to the mouse. The app now waits long enough, moves on as soon as the receiver reports a slot empty, skips the mouse's slot, and counts only the mouse's own reports as mouse activity; when the keyboard comes back from another computer or wakes up, it is read as soon as the receiver announces it.
- The battery chip in the bottom-left corner shows a lightning bolt next to the battery while charging instead of the word "charging", so the connection text stays inside the box (and is cut off with an ellipsis if still too long).

## 2.3 — 2026-11-13

- New Keyboard page (between Battery and Advanced) for a Logitech keyboard, connected over Bluetooth or sharing the Logi Bolt receiver with the mouse. It shows connection, battery and firmware, and sets the backlight (automatic, manual brightness or off) and whether the top row sends media keys or F1–F12. Settings live on the keyboard and count as applied once it confirms them.
- The keyboard follows the mouse: when the mouse moves to another computer (MX Flow, a Switch-to-channel action or the Switch button), the keyboard moves to the matching channel, only after the mouse switch went out. Each mouse channel can pick a keyboard channel; Automatic uses the channel with the same computer name, otherwise the same number, and a channel can also leave the keyboard where it is. Set on the Keyboard page.
- While a keyboard is connected, the battery chip in the bottom-left corner gets a second row with the keyboard's level.

## 2.2.1 — 2026-10-23

- Fix: quitting right after leaving precision mode could leave the mouse at the precision speed until the next start or reconnect.
- Fix: when writing the precision-mode speed failed, the hint still said "Pointer speed restored". The hint now waits for the mouse to confirm. A failed write is retried once; if restoring still fails, "Pointer speed not restored" shows and the speed is written again on quit (the saved speed is also restored on reconnect). A failed switch into precision mode shows "Precision mode failed" and tries to write the normal speed back instead of staying on.
- Fix: switching computers or reconnecting while in precision mode, or applying a new speed on the Pointer & Wheel page, could in rare timing leave the speed unrestored or overwritten by an older value.
- Fix: turning the low-battery alert on while the battery was already below the threshold never alerted.

## 2.2 — 2026-10-17

- New Haptics page (between Pointer & Wheel and MX Flow). The haptic strength moves here from Pointer & Wheel, and four haptic moments can each be switched on or off, given a waveform and tried:
  - Actions Ring slices (moved from the Gestures & Ring page; the setting carries over)
  - Gesture recognized (new, on by default, soft bump)
  - MX Flow switch, right before the mouse leaves (new, on by default, crisp bump)
  - Mode changes: precision mode on/off and ratchet / free spin (new, on by default, crisp)
- New precision mode: hold the chosen button to drop the pointer to a slower DPI (400 by default) for detailed work; release to go back. Set the speed and pick the trigger on the Pointer & Wheel page.
- New Mouse category in the action menu: Precision mode (hold), Precision mode (toggle), Toggle ratchet / free spin. Available on buttons, gestures and the ring; from the ring or a gesture, "hold" works as a toggle.
- On-screen hint: when precision mode or the wheel mode changes, the new state shows briefly at the bottom of the screen (can be turned off on the right of Pointer & Wheel).
- The "Subtle (default)" waveform is now just "Subtle", since each haptic moment has its own default.
- New Switch to channel 1 / 2 / 3 (Easy-Switch) actions: a button, gesture or Actions Ring slice moves the mouse straight to that Easy-Switch channel. The ring shows "Channel N". Nothing happens on the current channel or an unpaired one.
- New Battery page (between MX Flow and Advanced): the current level, whether it is charging and the connection, with a Reload button.
- Low-battery alert (Battery page, on by default): one tray notification when the level drops to the threshold (15% by default, 5–30%) while not charging; it alerts again only after charging.
- Pause haptics on low battery (Haptics page, on by default): below the threshold and not charging, no automatic taps play; the Try buttons still do. The current level is shown next to it while paused.
- Gesture sensitivity (Customize gestures… dialog, 50% = the original distance): higher means a shorter swipe picks a direction.
- The Haptics page's "MX Flow switch" is now "Computer switch" and also covers the Switch to channel actions.
- Smaller default Actions Ring: the new Overall size 50% (the default) equals the old 44%. Saved percentages are kept, so an existing ring shows about 6% smaller.

## 2.1 — 2026-10-15

- Actions Ring: new "Haptic tick on each slice" (on by default) — the mouse taps lightly as the pointer moves onto another slice. Choose from six waveforms (picking one plays it at once, and there is a Try button); the strength follows the haptic feedback setting on the Pointer & Wheel page.
- Actions Ring: new White color option ("Ring color" on the Gestures & Ring page; dark stays the default).
- New action "MX Settings": a button, gesture or Actions Ring slice can open this app's settings window.

## 2.0 — 2026-10-13

- The app is now called **MX Master Win** (formerly MX Master Portable) and the executable is `MXMasterWin.exe`. It is the Windows edition of the same product as MX Master Mac for macOS, so the version number moves to 2.0 with it.
  The settings file is now `mxmaster-win.ini`: put the new EXE in the same folder and the old `mxmaster-portable.ini` is renamed and reused on first run. The old `MXMasterPortable.exe` can be deleted.
- Fixed: after the mouse switched to another computer and back (or reconnected), the gesture button and the Actions Ring button stopped responding. The app now checks when the mouse becomes active again, re-enables the special buttons and re-applies the saved device settings.
- If the mouse was not connected when the app started, the special buttons are now enabled once it connects, without restarting the app.
- Fixed: the mouse was not found after it moved from the Logi Bolt receiver to Bluetooth (or back) while the receiver stayed plugged in. When the current connection stops answering, the app now looks for the mouse on the other connections.
- Actions Ring: new Bubbles style, with one round icon per position and the name shown in the centre only for the one under the pointer.
- Actions Ring: adjustable overall size, bubble size and opacity (Gestures & Ring page). The default size is smaller than in 1.1, matching the macOS version; raise Overall size to get the old size back.
- All four thumb gesture directions are now customizable (Customize gestures…).
- Advanced page: new "Start when I sign in to Windows" switch, off by default. Turning it on puts a shortcut in your Startup folder and turning it off removes it; no administrator rights and no registry change.
- Scroll direction: Inverted is now called Natural (Standard is unchanged); applied settings are unaffected.
- New: **MX Flow** (its own page). Keep pushing the pointer against a screen edge and the mouse moves to the chosen Easy-Switch channel. Each of the four
  edges can target its own channel and the trigger time is adjustable (0.1–2.0 s); with several monitors only the outer edge of the outermost monitor
  triggers, and a held key (Ctrl, Alt, Shift, Win or a mouse button) can be required. Off by default; no clipboard or files are transferred.
- Shows the computer name and connection type stored for each of the three Easy-Switch channels, with a button to switch directly.
- The tray icon menu has an MX Flow item (ticked while on).

## 1.1 — 2026-10-13

- Pointer & Wheel page: added wheel scroll direction (Standard / Inverted); the applied value is saved and re-applied at the next start.
- Actions Ring: added a click-to-choose mode. Press the Actions Ring button once to show the ring, then left-click a slice, with no holding; clicking the centre, clicking outside, right-clicking or pressing the button again cancels.
- Added 30 built-in actions for buttons and the ring: previous / next track, File Explorer, Windows Settings, Calculator, Task Manager, Windows Search, Start menu, new / close / reopen tab, refresh, zoom in / out, screenshot, emoji panel, clipboard history, lock PC, quick settings, minimize / maximize / close / snap window, select all, save, find, Delete, type today's date, type the current time.
- The Customize ring dialog now has a picture guide: a ring numbered 1–8 on the left and matching numbers on the rows; hovering either side highlights both, and clicking a slice opens that position.
- Fixed: with Logi Options+ running at the same time, one press of a special button could be handled twice.

## 1.0 — 2026-10-13

First public release.

### Buttons and actions

- Map the middle button, gesture button, both side buttons, thumb wheel left and right, the Actions Ring button and the wheel mode button.
- Built-in actions: back / forward, copy / paste / cut, undo / redo, Task View, show desktop, switch virtual desktop, Alt+Tab, play / pause, volume, mute, Enter, Esc.
- Custom shortcuts (any key combination, including the Win key) and launching a program or opening a file, folder or URL.

### Gestures and ring

- Actions Ring: hold to show a translucent eight-way ring with an icon and label per slice; all eight directions are customizable.
- Four-way gestures on the gesture button: left / right for back / forward, up for Task View, down for show desktop.

### Device

- Works over Bluetooth LE and the Logi Bolt receiver.
- No Logi Options+ needed: the app sets temporary HID++ diversion for the gesture and Actions Ring buttons itself, and releases it when paused or closed.
- Shows battery level and firmware version.
- Adjusts pointer speed (DPI), SmartShift and haptic feedback strength; applied values are saved and re-applied at the next start.

### Interface

- Four pages: Buttons (with a mouse diagram), Gestures & Ring (with a live ring preview), Pointer & Wheel, and Advanced.
- Interface language switches between Traditional Chinese and English, following the system by default.
- Starts minimized to the system tray by default; this can be turned off on the Advanced page.
- Every setting is saved automatically to `mxmaster-portable.ini` next to the EXE.
- Single instance: launching it again brings up the running window.
- Engineering mode window: view raw HID events and write them to a log file.

### Other

- A single EXE with no installer and no administrator rights; installs no driver, service or start-up entry and makes no network connections.
