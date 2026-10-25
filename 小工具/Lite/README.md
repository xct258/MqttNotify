# 小工具 Lite（ProjectRecorderLite）

从完整版 `小工具` 精简而来：**去掉登录、密码、加密、无操作自动退出，以及工作量 / 工序 / 笔记 / 思维导图 / 加密文件 / Excel 导出等功能**，只保留：

- **添加项目**：新增、重命名（同名会提示）、上移 / 下移、删除（连同其下快捷路径一起删除）
- **快捷路径**：每个项目独立维护自己的快捷路径
  - 文件夹：浏览选择，单击用资源管理器打开
  - 网址：自动补 `https://`，单击打开手动复制窗口（不会自动写入剪贴板）

数据以**明文 JSON** 保存在 exe 同级 `Data/data.json`，不再加密、无需密码。

## 1. 运行环境

- Windows 10 / 11（自带 .NET Framework 4.8，无需安装运行时）
- 产物：`publish/小工具Lite.exe`（单文件，直接双击）

## 2. 编译发布

推荐在 **Windows** 上编译：

1. 安装 .NET SDK（8.x 或 10.x，仅编译用）：
   https://dotnet.microsoft.com/download
2. 验证：`dotnet --list-sdks`
3. 在 `Lite/` 目录双击 `publish.bat`，或在命令行执行：

   ```
   dotnet publish src/ProjectRecorderLite/ProjectRecorderLite.csproj -c Release -o publish
   ```

Linux / macOS（仅编译，exe 仍需到 Windows 运行）：

```bash
chmod +x publish.sh && ./publish.sh
```

## 3. 使用

1. 双击 `小工具Lite.exe`，左侧点 `＋ 添加项目` 输入项目名。
2. 选中项目后，右侧点 `＋ 添加`，选择类型：
   - **文件夹**：点「浏览…」选目录（也可手输），确定后单击卡片即可打开。
   - **网址**：输入网址（可不写协议，自动补 `https://`），确定后单击卡片打开手动复制窗口，全选后按 `Ctrl+C` 复制。
3. 文件夹 / 网址卡片右键可删除（网址可右键「手动复制网址」）。
4. 左侧项目右键可 `上移` / `下移` / `删除该项目`。

## 4. 数据

```
Data/data.json      # 明文 JSON：{ "Projects": [...], "Shortcuts": [...] }
```

- 每次增删改立即落盘；写入采用「临时文件 + 覆盖」，尽量避免中断导致文件损坏。
- 若 `data.json` 损坏，启动时会自动备份为 `Data/data.corrupt-<时间>.json` 并以空数据启动。
- 窗口尺寸记忆在注册表 `HKCU\Software\ProjectRecorderLite`（与数据文件无关）。

## 5. 目录结构

```
Lite/
  ProjectRecorderLite.sln
  publish.bat / publish.sh
  src/ProjectRecorderLite/
    App.xaml(.cs)                  启动、单实例、全局异常日志
    MainWindow.xaml(.cs)           左：项目列表   右：快捷路径
    AddProjectDialog.xaml(.cs)     添加项目
    PathShortcutDialog.xaml(.cs)   添加快捷路径（文件夹 / 网址）
    ManualCopyDialog.xaml(.cs)     手动复制窗口（已全选，按 Ctrl+C 复制）
    Models/PathShortcut.cs         快捷路径模型
    Services/LiteStore.cs          明文 JSON 存储（项目 + 快捷路径）
    Views/ShortcutView.xaml(.cs)   快捷路径面板
    Helpers/VisualTreeHelpers.cs   视觉树辅助
```

## 6. 与完整版的差异

| 功能 | 完整版 | Lite 版 |
|---|---|---|
| 登录 / 密码 | 有 | 无 |
| AES 加密存储 | 有 | 无（明文 JSON） |
| 无操作自动退出 | 有（120 秒） | 无 |
| 添加项目 | 有 | 有 |
| 快捷路径 | 有（通用模块含网址，项目仅文件夹） | 有（每个项目都有文件夹 + 网址） |
| 工序 / 工作量 / 笔记 / 思维导图 / 加密文件 / Excel 导出 | 有 | 无 |
