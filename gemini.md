# 粤语拼音弹窗（JyutPingPopup）

Windows 托盘小工具：在任意窗口里**划选中文**，立刻弹一张置顶小卡片，逐字显示粤拼（Jyutping）。
另带**截图识字**：拖框选屏幕区域，离线 OCR 读出图里的华文并注音。目标用户是想边读边查粤语读音的人，
也作为个人 GitHub portfolio 展示一个完整的 Windows 桌面小工具。

## 它做什么

- **划词注音**：选中一段中文（简体/繁体都行），鼠标松开即弹卡片；每个字上面汉字、下面粤拼，
  多音字用红色标出并在底部列出来源。
- **截图识字（OCR）**：`Ctrl+Shift+L` 或托盘菜单「截图识字」触发，全屏拖框选区域 →
  松手即离线识别图里的中文 → 弹注音面板。适合无法高亮的场景（图片、PDF 扫描件、视频画面）。
- **常驻托盘**：单实例，启动后缩在托盘；可设开机自启、可暂停取词。

## 目录结构

```
jyutping/
├── csharp/                 # 主程序（C# / WPF）
│   ├── App.xaml.cs         # 程序入口；热键、托盘、各浮层（弹窗/OCR 面板）的编排
│   ├── InputHooks.cs       # 全局鼠标/键盘低层钩子（含热键捕获模式）
│   ├── CaptureService.cs   # 取词：UIA 优先，剪贴板 Ctrl+C 兜底；队列带「世代号」
│   ├── ClipboardBackup.cs  # 借剪贴板前快照、用后还原，防丢用户数据（原子还原）
│   ├── Jyutping.cs         # 粤拼词典查找与注音（词优先、字兜底）
│   ├── S2Hk.cs             # 简体 → 香港繁体（OpenCC 数据 + 自写最长匹配）
│   ├── PopupWindow.xaml(.cs)    # 主粤拼卡片
│   ├── RegionSelector.xaml(.cs) # 截图拖框遮罩（冻结快照 + 变暗 + 蓝框）
│   ├── OcrPanel.xaml(.cs)       # OCR 结果面板
│   ├── Ocr.cs              # WinRT Windows.Media.Ocr 封装
│   ├── Native.cs           # Win32 P/Invoke（钩子、剪贴板、窗口查询）
│   ├── SelfTest*.cs        # 七套自测（--selftest* 系列）
│   └── JyutPingPopup.csproj
├── data/                   # 运行时词典（随 exe 发布，CopyToOutputDirectory）
│   ├── chars.dict.yaml     # 单字粤拼词典
│   ├── words.dict.yaml     # 词语粤拼词典
│   └── opencc/             # OpenCC 繁简表（STCharacters / STPhrases / HKVariants）
├── assets/logo.ico         # 托盘/程序图标
├── installer/
│   └── JyutPingPopup.iss   # Inno Setup 安装脚本（改完代码要 F9 重编）
└── *.py                    # 早期 Python 原型（探索用，非运行所需）
```

## 构建与运行

- 要求：Windows + .NET 9 SDK
- 调试构建：`dotnet build -c Release`
- 发布单文件 exe：`dotnet publish -c Release -p:PublishSingleFile=true --self-contained false -r win-x64 -o publish`
- 安装包：用 Inno Setup 打开 `installer/JyutPingPopup.iss`，按 **F9** 重编（本机无 `iscc` 命令行）
- 自测：`./publish/JyutPingPopup.exe --selftest`（另有 `--selftest-clip` / `-footer` / `-ui` /
  `-ocr` / `-cliprace` / `-region`，全部 `EXIT=0` 为通过）

## 关键约定与已知坑

- **剪贴板是敏感区**：借剪贴板取词（`ReadByCopy`）前后必须快照并还原，**绝不能覆盖用户正在复制的内容**。
  改动此逻辑前后务必跑 `--selftest-clip` 与 `--selftest-cliprace`。
- **弹窗世代号 `_popupEpoch`**：`DismissPopup()` 推进，取词请求带上 epoch，结果回来对不上就丢弃，
  避免过期结果糊回用户已关掉的卡片。**顺序必须「先 Dismiss 再 Request」**，反了卡片永远不弹。
- **多显示器**：虚拟屏幕原点可能为负（副屏在主屏左侧/上方），坐标一律用物理像素 + `TransformFromDevice` 换算。
- **备份目录 `_bak_YYYYMMDD/` 必须放在 `csharp/` 外面**：否则 SDK 把备份的 `.cs` 当源码编译，报一堆假的 CS0101。
- **不要主动「优化」已验证的剪贴板/取词逻辑**，先问再做；判断错了就明确认错并回滚。
- **单实例**：`Mutex` 防多开；重复启动的实例用 `EventWaitHandle` 戳一下托盘提示。

---

## Tech Stack (brief)

- **Language / Framework:** C# 11 on .NET 9, TFM `net9.0-windows10.0.22621.0`. WPF for UI,
  Windows Forms interop (`Screen`) for multi-monitor DPI handling.
- **Text capture:** FlaUI.UIA3 5.0.0 reads the selected text from other apps (primary path).
  When UIA returns nothing, the tool synthesizes `Ctrl+C` and reads the clipboard instead.
- **OCR:** Offline WinRT `Windows.Media.Ocr` (language `zh-Hans`). No cloud, no network call.
- **Clipboard safety:** Before borrowing the clipboard it snapshots every format via Win32
  `OpenClipboard` / `SetClipboardData`, and restores atomically afterwards so user copies are never lost.
- **Cantonese data:** Custom `chars.dict.yaml` / `words.dict.yaml`. Simplified → Hong-Kong
  Traditional conversion uses OpenCC source tables (STCharacters / STPhrases / HKVariants) with a
  hand-rolled longest-match converter, deliberately avoiding a jieba dependency (~18 MB).
- **Low-level hooks:** Win32 `SetWindowsHookEx` for global mouse/keyboard; `Mutex` for single instance;
  `EventWaitHandle` for second-instance activation.
- **Distribution:** Single-file `dotnet publish`; Inno Setup (`JyutPingPopup.iss`) builds the installer.
- **Self-tests:** Seven `--selftest*` modes run against the built exe; clipboard behavior has dedicated
  race tests (`--selftest-cliprace`) proving user data is not clobbered.

## Notes for AI assistants

- Treat the clipboard / capture / popup-epoch logic as sensitive. Run the matching `--selftest*` after
  any change there, and confirm with the user before "optimizing" something already verified working.
- The `_bak_*` / `_probe` / `_trash*` folders are pre-git rollback backups, excluded by `.gitignore`.
  They are NOT source; do not compile against or delete them.
