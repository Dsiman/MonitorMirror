# Monitor Mirror

Mirrors live center crop of one monitor onto another (DXGI Desktop Duplication + Direct2D). Published exe self-contained, no dotnet install needed.

## Usage

Run `MonitorMirror.exe`. Settings window opens:

1. **Monitor picker**: top row shows live thumbnails (refresh every second, pause while minimized). Left-click sets source (green). Right-click sets output (blue), monitor covered by mirror.
2. **Crop size slider**: percent of source monitor captured, centered. 10% = small box, 100% = full screen.
3. **Warp mode**, for aspect mismatch between crop and output:
   - None: scale to fit, no distortion, black bars on mismatched side(s).
   - Stretch: fills output, may distort.
   - Fill: crops overflow, fills output, no distortion, no bars.
4. **Toggle** (hotkey):
   - Enable: master on/off.
   - Hold (default on): mirror shows only while hotkey held, hides on release. Off: plain on/off switch, press once show, again hide.
   - Set Hotkey: click, then press key combo (Ctrl+F9) or mouse button (Right Click, Ctrl+Right Click). Works system-wide, active only while mirroring running.
5. **Start Mirroring**: starts capture (GPU duplication/swapchain, heavy step). Hotkey then just shows/hides instantly, no reinit lag. Stop Mirroring tears it down.

Crop size and warp mode change live while running. Changing source/output monitor needs stop/start.

Binding mouse button with no modifier (plain Left Click) triggers on every click of that button system-wide. Use modifier (Ctrl/Alt/Shift+click) to avoid that.

### Minimize to tray

Minimizing sends settings window to tray. Mirroring, if running, keeps going. Double-click tray icon, or right-click and pick "Show Settings", brings it back. Closing (X button) exits fully.

### Theme

Light/Dark slider, top-right. Defaults dark, persists.

### Settings saved

Source/output monitor, crop size, warp mode, hotkey, enable/hold, theme: all saved to `%AppData%\MonitorMirror\settings.json`, restored next launch. Monitors matched by device name, so unplug/reorder safe (falls back to none-selected if monitor gone).