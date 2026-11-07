# 第三方数据与许可 / Third-party data and licenses

本项目的 C# 源码、脚本与文档依 `LICENSE` 中的 MIT 许可发布。但 `data/` 目录收录的是第三方数据，**不在 MIT 授权范围内**，各自适用下列许可。若你要重新分发本仓库或安装包，请一并保留本文件。

The C# source, scripts and docs in this repo are released under the MIT license in `LICENSE`. The files under `data/` are third-party data and are **not covered by that MIT license**. Keep this notice if you redistribute the repo or the installer.

## 粤拼词典 / Jyutping dictionaries

- 文件 / Files: `data/chars.dict.yaml`, `data/words.dict.yaml`
- 来源 / Source: [CanCLID/rime-cantonese-upstream](https://github.com/CanCLID/rime-cantonese-upstream), 词典内标注版本 `2026.08.10`
- 原项目 / Upstream schema: [rime/rime-cantonese](https://github.com/rime/rime-cantonese), 由「粤语计算语言学基础建设组 (CanCLID)」维护
- 许可 / License: [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)
- 说明 / Notes: 在本仓库中改名并作为静态数据随程序分发。上游另有按 ODbL 1.0 发布的 `jyut6ping3.maps`，本仓库**未收录**。

## 简繁转换表 / OpenCC conversion tables

- 文件 / Files: `data/opencc/STCharacters.txt`, `data/opencc/STPhrases.txt`, `data/opencc/HKVariants.txt`
- 来源 / Source: [BYVoid/OpenCC](https://github.com/BYVoid/OpenCC)
- 版权 / Copyright: Copyright (c) 2010-present BYVoid and OpenCC contributors
- 许可 / License: [Apache License 2.0](https://www.apache.org/licenses/LICENSE-2.0)
- 说明 / Notes: 仅取原始文本对照表，转换逻辑由本项目 `csharp/S2Hk.cs` 自行实现。

## NuGet 依赖 / NuGet dependency

- `FlaUI.UIA3` 5.0.0: [MIT](https://github.com/FlaUI/FlaUI/blob/master/LICENSE)。以包引用方式使用，源码未 vendored 进本仓库。

## 演示素材 / Demo assets

`assets/Video/` 中的演示 GIF 为本项目实机录制，版权归项目作者所有，随 MIT 一同授权。源录屏只保留在本机，不纳入仓库。
The demo GIFs in `assets/Video/` are screen captures made for this project and are covered by the MIT license. Source recordings stay local and are not shipped.
