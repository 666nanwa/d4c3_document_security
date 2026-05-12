# D4C3 File Encryption v1.0.0

First downloadable Windows x64 release.

## Download

Download `D4C3Jiami-WPF.zip`, unzip it, then run:

```text
D4C3Jiami.exe
```

## Features

- Local offline encryption for any file.
- Supports 1 to 3 encryption layers.
- Each layer can use an independent password and algorithm.
- Supports AES-256-GCM, ChaCha20-Poly1305, and AES-256-CBC + HMAC-SHA256.
- Encrypted files can use `.enc`, `.jpg`, `.txt`, or `.mp4` extensions.
- Decryption restores the original file name, original extension, and original bytes.

## Notes

- The `.jpg`, `.txt`, and `.mp4` output options only disguise the extension. They are not real image, text, or video files.
- The app does not save plaintext passwords. Keep every layer password safe.
- Back up important files before encrypting them.
