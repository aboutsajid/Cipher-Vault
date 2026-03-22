# Cipher™ Vault Browser Capture + Autofill (Chrome/Edge)

This extension captures submitted login credentials and sends them to local Cipher™ Vault.
By default, Cipher™ Vault asks for confirmation before saving.

## Install (Free)

1. Open `chrome://extensions` (or `edge://extensions`).
2. Turn on **Developer mode**.
3. Click **Load unpacked**.
4. Select this folder (the folder containing `manifest.json`).

## Use

1. Unlock Cipher™ Vault desktop app.
2. Ensure Browser Capture is active (shown in app status).
3. Log in to a website in Chrome/Edge.
4. Cipher™ Vault will show a save prompt (or auto-save if Silent Mode / auto-save domains are enabled in Settings).
5. For autofill: focus username/password field and select account from the Cipher™ Vault popup.

## Privacy Notes

- Data is sent only to local endpoints:
  - `http://127.0.0.1:47633/session/token`
  - `http://127.0.0.1:47633/capture`
  - `http://127.0.0.1:47633/autofill/query`
- The app issues a short-lived in-memory session token after unlock; capture/autofill requests require that token.
- No external cloud service is used.
- Default behavior is approval-based; auto-save can be enabled explicitly in app settings.



