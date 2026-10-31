# Retail365 BLE kiosk for Windows

Windows kiosk agent scans Bluetooth LE advertisements and sends token/RSSI observations to Manage365. The kiosk computer needs Windows 10+, Bluetooth LE, network access to the API, and a signed-in Windows account. Use HTTPS outside local development.

## One-time owner setup

1. On the store PC, open the web attendance page, select its kiosk and click **Kích hoạt BLE trên máy này**. Download `Retail365-BLE-Setup.exe`; the file already contains the selected kiosk and the web server's address. No API address entry or ZIP extraction is needed.
2. Open that EXE and confirm installation. It installs under the current user's `%LOCALAPPDATA%\Retail365\BLE\installs`, adds a per-kiosk Windows Startup shortcut and starts the scanner without a console window. Windows may ask you to trust the unsigned installer; this project does not bypass Windows security policies.
3. Keep Bluetooth on. On subsequent visits, use **Bật chấm công BLE** on the web. The agent polls authenticated control every five seconds, stops the physical watcher when disabled and stays connected to accept a resume command. The web distinguishes the requested setting from confirmed scanning. No new download/token rotation is needed for daily toggling.
4. Keep the kiosk PC awake and Bluetooth enabled. If the PC signs out, sleeps, loses network, or its Bluetooth adapter stops, attendance is not verified; use QR while restoring it.

The installer contains a kiosk-specific `ble-scanner.json`. Downloading a replacement rotates its secret and invalidates the previous package. Treat both as device credentials. Install on the PC that opened the web page; `localhost` works only on the backend PC. For remote kiosk PCs, open the website through its reachable LAN/HTTPS address first. The web reports the selected kiosk's connection, not proof that an agent is installed on the current browser's PC.

If control requests fail, the agent stops scanning and retries. If the PC sleeps, disconnects or exits, the web reports a stale/offline connection rather than claiming the scanner is working. Remove the matching `Retail365 BLE *.lnk` from `shell:startup` to stop launching at sign-in. Logs are in `%LOCALAPPDATA%\Retail365\BLE\logs`. Legacy ZIP/start scripts remain developer recovery tools; an old agent must be upgraded once before the new switch can control it.

## Employee setup

On Android, sign in, open Attendance once, and tap **Bật tự động**. Grant Bluetooth and notification permissions. The app displays a persistent notification while BLE attendance is enabled. Later launches restore the setting and start advertising without requiring the attendance screen to remain open. To stop it, tap **Tắt tự động** or sign out.

iPhone advertising works while the app is open. iOS background advertising changes service UUID visibility, so this Windows scanner cannot reliably detect it in the background. Use QR when the iPhone app is hidden or stopped.

## Attendance behavior

The API checks assigned shifts, account state, leave, stable dwell, and signal before creating check-in. After five minutes without the employee's beacon, a healthy kiosk records the last observed time as check-out and marks the row `NeedsReview`; the owner must review it. A failed kiosk or a beacon still present does not trigger that checkout. Configure the grace duration with `Attendance:Ble:AutoCheckoutAbsenceMinutes` (2–30 minutes; default 5).

Beacon absence can also mean a phone battery, permission, app, or Bluetooth problem. The check-out time is therefore the last confirmed observation, and the attendance is explicitly flagged for review. RSSI is not a precise distance measurement and BLE advertisements can be relayed.

## Build and developer setup

Before publishing the web application, run `tools/BleScannerAgent/build-package.ps1` to publish both the agent ZIP and installer template. Apply SQL migrations 013 through `018_ble_scan_session.sql` before starting the new server. A Manager's new assignments automatically belong to their shop; an Admin selects the shop when assigning. Historical assignments without a shop must be reviewed and saved in scheduling before BLE can recognize them. Never infer their shop from an old kiosk binding.

Agent 2.0 starts a new scan session after resume, Bluetooth failure, or a stale connection. Automatic absence checkout requires an employee observation from this session; observations from before the pause cannot close a shift after resuming. If the employee never returns a beacon after recovery, finish/review the open attendance using the QR workflow.

For source development, the agent also accepts `MANAGE365_API_BASE`, `BLE_SCANNER_ID`, and `BLE_SCANNER_TOKEN` environment variables. Never commit a real scanner token. The agent sends a heartbeat every 20 seconds, reports `running`/`error`/`stopped`, and the server makes attendance decisions. QR + GPS remains available as fallback.
