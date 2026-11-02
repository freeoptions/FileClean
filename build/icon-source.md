# 图标来源

- 原图：Find whatever 的 `src/assets/img/popup.png`（128×128 透明 PNG）。
- 来源：https://github.com/ImSwordTooth/findwhatever/blob/b8b33338fd40a4455d246740e99a46d1d90f20fd/src/assets/img/popup.png
- 上游许可证：GPL-3.0-only，原文见同目录 `LICENSE.findwhatever`。
- 保留原图的颜色、透明度和光晕，使用 app-icon-resources 生成 Windows 多尺寸 ICO 及 PNG；没有重绘。
- 为增大显示主体，原图四边各裁去 10px 外围区域，使用中心 108×108 区域等比生成资源；雷达主体完整保留，外围光晕随裁切收紧，主体较初版放大约 18.5%。
- `icon.ico` 用于 EXE、窗口、任务栏和托盘；`icon.png` 用于界面品牌图标。
- `tools/Generate-AppIcon.py` 是旧图标的生成工具，不适用于这份原图。
