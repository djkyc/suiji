# 随记（Suiji Vault）

**随记（Suiji Vault）** 是一个本地优先、端到端加密、支持跨平台自动同步的轻量账号/记事工具。

## 特性
- 一站多帐号（同一网站支持多个帐号）
- 表格化管理，独立复制按钮（📋）
- AES-256 + PBKDF2 加密（云端仅存密文）
- Windows / Android 数据完全通用
- WebDAV（推荐）/ Cloudflare KV（高级）
- 自动同步 + 冲突保留

## 仓库结构
```
EasyNoteVault
│
├── .github
│   └── workflows
│       └── build.yml          ← GitHub Actions 自动打包
│
├── EasyNoteVault
│   ├── EasyNoteVault.csproj
│   ├── App.xaml
│   ├── App.xaml.cs
│   ├── MainWindow.xaml
│   ├── MainWindow.xaml.cs
│   │
│   ├── Models
│   │   └── AccountItem.cs
│   │
│   ├── Services
│   │   ├── CryptoService.cs
│   │   ├── DataService.cs
│   │   └── Sync
│   │       └── ISyncService.cs   ← 安卓/WebDAV 预留
│
└── README.md

```

## 发布
推送 tag（如 v1.0.0）后自动构建：
- Windows x64 单文件 exe
- Android APK
