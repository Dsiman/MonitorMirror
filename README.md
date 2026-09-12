# Monitor Mirror

Mirrors a center crop of one monitor onto another, live (GPU capture via DXGI Desktop Duplication, scaled with Direct2D). No dotnet install needed if you're using the published exe — it bundles the runtime.

## Using it

Run `MonitorMirror.exe`. A settings window opens:

1. **Monitor picker** — top row shows a live thumbnail of every monitor (refreshes every second, pauses while the window is minimized). **Left-click** a monitor to set it as the **source** (highlighted green). **Right-click** a monitor to set it as the **output** (highlighted blue) — this is the monitor that gets covered by the mirrored image.
2. **Crop size slider** — how much of the source monitor to capture, centered, as a percentage of its own width/height (10% = a small centered box, 100% = the whole screen).
3. **Warp mode** — what to do when the cropped region's aspect ratio doesn't match the output monitor's:
   - **None** — scale to fit, no distortion, no cropping. Leaves black bars on the mismatched side(s).
   - **Stretch** — fills the entire output, may distort (non-uniform scaling).
   - **Fill** — scales up and crops off whatever sticks out, so it fills the output with no distortion and no bars.
4. **Toggle** (hotkey) —
   - **Enable** — master on/off for the hotkey feature.
   - **Hold** — checked (default): the mirror is only visible while the hotkey is held down, and hides the instant you release it. Unchecked: it behaves as a plain on/off switch — press once to show, press again to hide.
   - **Set Hotkey** — click it, then either press a key combo (e.g. Ctrl+F9) or click/hold a mouse button (e.g. Right Click, or Ctrl+Right Click). Works globally, system-wide, even when this window isn't focused — it only takes effect while mirroring is running (see below).
5. **Start Mirroring** — actually starts the capture (creates the GPU duplication/swapchain — this is the "heavy" step). Once running, the hotkey just shows/hides the already-running mirror, so it responds instantly with no reinit lag. Click "Stop Mirroring" to fully tear it down.

Crop size and warp mode can be changed live while mirroring is running. Changing the source/output monitor selection requires stopping and starting again.

Note: binding a mouse button with no modifier (e.g. plain Left Click) means every click of that button anywhere on your system will trigger it — use a modifier (Ctrl/Alt/Shift+click) if that's too broad.

### Minimize to tray

**Minimizing** the settings window sends it to the system tray instead of the taskbar (mirroring, if running, keeps going). Double-click the tray icon, or right-click it and choose "Show Settings", to bring the window back. **Closing** the window (the X button) fully exits the app.

### Dark theme

Light/Dark slider, top-right of the window. Defaults to dark, persists across runs.

### Settings are remembered

Source/output monitor, crop size, warp mode, hotkey binding, enable/hold, and theme are all saved to `%AppData%\MonitorMirror\settings.json` and restored next launch. Monitors are matched by their device name, so if you unplug/replug or reorder monitors, the saved source/output selection still finds the right one (or falls back to "none selected" if that monitor's gone).
