# Asset attribution

## StadiaControllerPhoto.png

- Source: user-provided reference image in this Codex thread.
- Local changes: resized to 2048x1024 for the Stadia X controller test view and used as the source for the application icon.
- Note: replace this file with an owned/redistributable photo before broad public distribution if required by your release policy.

## StadiaControllerCutout.png

- Source: derived from `StadiaControllerPhoto.png`.
- Local changes: the original controller pixels and geometry are preserved; an AI-assisted silhouette mask removes the photographic background and watermark for the interactive mapping view.
- Note: the same redistribution consideration as the source photo applies.

## StadiaX-WindowsNative-icon.png / StadiaX-WindowsNative.ico

- Source: the Windows Native icon now incorporates the transparent controller reference from `StadiaControllerCutout.png`.
- Local changes: AI-assisted compositing replaces the previous illustrated controller while preserving the Windows badge and Stadia X branding; the ICO contains dedicated Windows sizes from 16 through 256 pixels.

## Bundled Windows Native dependencies

- HidHide 1.5.230: official signed installer, MIT license, https://github.com/nefarius/HidHide/releases/tag/v1.5.230.0
- ViGEmBus 1.22.0: official signed installer, BSD-3-Clause license, https://github.com/nefarius/ViGEmBus/releases/tag/v1.22.0
- The setup files are included unchanged and verified by pinned SHA-256 hashes during packaging.
