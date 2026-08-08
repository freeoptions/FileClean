# FileClean 项目专属规则

通用规则见：`E:\@imFile-Download\AI-Useful-Prompt\通用开发工作规则.md`。

- 本项目是 Windows WPF C# 文件清理工具，项目文件为 `FileClean.csproj`。
- 核心功能是扫描图片/视频重复文件、分组展示、勾选并移动到系统回收站；不得改成永久删除。
- 涉及扫描、重复判断、回收站移动、排除目录或进度展示时，优先保护现有安全规则，确保每组重复文件至少保留一个。
- 项目名与交付映射：`FileClean -> FileClean.exe`。
- 获得构建授权后只按 Release 配置生成最终 EXE，并复制到 `D:\@Software\FileClean\FileClean.exe`；不要复制 `bin`、`obj`、`build`、PDB 或其他中间产物。
- UI 修改完成后先预览和验证，未经“11”“构建”或“打包”授权不要构建最终 EXE。
