# Cipher(TM) Vault Icon Spec

## Goal
Create a single brand-consistent icon family that matches the new Cipher(TM) color system (`#00F5C4`) and stays clear at tiny Windows sizes.

## Visual Direction
- Keep: shield + keyhole security concept.
- Keep: strong geometric silhouette.
- Change: reduce heavy metallic/gold look for product consistency.
- Add: cyan brand accent tied to `#00F5C4`.

## Brand Tokens
- `CipherCyan`: `#00F5C4`
- `CipherCyanHover`: `#24FFD5`
- `CipherNavy900`: `#08121F`
- `CipherNavy700`: `#13263B`
- `CipherNavy500`: `#2D4A6C`
- `IconLight`: `#EAF4FF`
- `IconMid`: `#A7BED8`

## Icon Set (Windows)
Export one `.ico` containing these sizes:
- `16x16`
- `20x20`
- `24x24`
- `32x32`
- `40x40`
- `48x48`
- `64x64`
- `128x128`
- `256x256`

## Composition Rules
- Safe area: 10% padding on all sides.
- Primary form: shield background with centered keyhole motif.
- Optional monogram: simplified `C`/`V` implied shape only if it survives at `24x24`.
- No thin highlights below 2 px at `64x64` equivalent.

## Size-Specific Simplification
### 64+ px
- Full detail allowed (subtle gradients, soft shadow, cyan accent edge).
- Avoid bright gold streaks; replace with cyan/light accents.

### 32-48 px
- Remove micro texture and fine bevels.
- Keep one-level shading only.
- Keyhole and shield edge must remain high contrast.

### 16-24 px
- Flat 2-tone/3-tone version only.
- No glow, no texture, no hairline highlights.
- Increase keyhole and outline thickness so shape reads instantly.

## Contrast Requirements
- Icon must read on light and dark taskbar backgrounds.
- Minimum contrast target: ~4.5:1 for key shape against shield center.

## Deliverables
- Source vector (SVG/AI/Figma export).
- `app.ico` multi-size pack for app/tray/taskbar.
- `app_clean.png` at `512x512` for in-app display.
- `icon_size_preview.png` showing all final small sizes.

## Integration Targets In Repo
- `CipherVault.UI/Resources/app.ico`
- `CipherVault.UI/Resources/app_clean.png`
- `CipherVault.UI/Resources/icon_size_preview.png`

## QA Checklist
- Crisp at `16x16` in system tray.
- Recognizable at `24x24` in taskbar.
- No muddy edges on high DPI (`125%`, `150%`, `200%`).
- Visual style matches app cyan accent (`#00F5C4`).
