# GitHub Publish Checklist (CipherVault)

Use this when publishing for the first time.

## 1) Create repository on GitHub
- Repository name: `CipherVault`
- Description: `Local-first encrypted password manager for Windows (.NET 8 + WPF + AES-256-GCM + Argon2id)`
- Visibility: choose `Public` (portfolio/open-source) or `Private` (internal)
- Initialize with README: `No` (this repo already has one)
- Add .gitignore in GitHub UI: `No` (already included locally)
- Add license in GitHub UI: optional (recommended if public)

Suggested topics:
`password-manager`, `dotnet`, `wpf`, `windows`, `sqlite`, `argon2`, `aes-gcm`, `mvvm`, `cybersecurity`

## 2) Put screenshot in the repo
Save your main window screenshot to:
`docs/images/main-window.png`

## 3) Initialize git and make first commit
Run in `D:\CipherVault`:

```powershell
git init
git branch -M main
git add .
git commit -m "Initial release: CipherVault"
```

## 4) Connect remote and push
Replace `<YOUR_GITHUB_USERNAME>`:

```powershell
git remote add origin https://github.com/<YOUR_GITHUB_USERNAME>/CipherVault.git
git push -u origin main
```

If remote already exists:

```powershell
git remote set-url origin https://github.com/<YOUR_GITHUB_USERNAME>/CipherVault.git
git push -u origin main
```

## 5) Optional but recommended after push
- Create first tag/release: `v1.0.0`
- Upload `artifacts/installer/CipherVault-Setup.exe` to GitHub Releases (not the main repo)
- Enable branch protection on `main`
- Add repository About website link and social preview image
