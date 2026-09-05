# UnMosic

> .NET 游戏马赛克去除 / Return 值修改器 — 基于 dnlib（dnSpy 底层库）实现 IL 字节级原位修改

## 简介

**UnMosic** 是一款专为去除 .NET 游戏马赛克而生的工具，核心能力是定位指定函数的最后一个 `ret` 指令，原位修改其前面的取值指令，让方法返回你指定的值。

典型用途：绕过游戏中的马赛克绘制逻辑（关闭 `FnDrawMosaic`、`DrawMosaic` 等返回 `false`），也可用于任何需要修改 .NET 方法返回值的场景（调试、解锁、补丁等）。

> ⚠️ 本工具仅供学习与研究 .NET IL 修改技术使用。请在合法授权范围内使用，不要用于侵犯他人版权或绕过 DRM 的活动。

## 特性

- **最后一个 return 修改**：精确定位方法 IL 序列中最后一个 `ret`，只改这一条之前的取值指令，不影响方法体内其他早返回分支
- **原位修改 IL**：直接修改目标指令的 `OpCode` + `Operand`，保留指令对象身份，分支跳转目标不丢失，**不会**触发 dnlib 的 `Found some other method's instruction` 错误
- **类型自动适配**：按方法 `ReturnType` 自动选择对应 IL 指令
  - `Boolean` → `ldc.i4.0` / `ldc.i4.1`
  - `Int32/Int64/Byte/Char/...` → `ldc.i4` / `ldc.i8`
  - `Single/Double` → `ldc.r4` / `ldc.r8`
  - 引用类型 / `String` → `ldnull` / `ldstr`
- **安全备份**：每次修补前自动生成 `<原DLL>.bak`，已存在则保留最早一份
- **临时文件策略**：先写临时文件，确认成功后再覆盖原 DLL，避免文件句柄占用导致损坏
- **预设系统**：内置 8 条常见马赛克补丁预设，`presets.json` 可用记事本编辑后重启生效
- **同名方法消歧**：支持 `类型过滤` 参数，按声明类型全名（或 `.类型名` 后缀）过滤
- **🔍 自动扫描**：浏览游戏文件夹，遍历所有 `.dll` 自动识别候选马赛克函数
  - 强关键词命中（`mosaic`、`censor`、`drawglonly` 等）100 分
  - 普通关键词命中（`blur`、`pixelat`、`hider`、`mask` 等）50 分
  - 跳过非 .NET 程序集与 `void` 返回方法
  - 按相关度排序，强关键词 ★ 标记
  - 选中后自动套用 DLL/函数/类型，并按返回类型推荐补丁值

## 使用方式

### 🪟 UnMosic GUI — `PatchReturnUI.exe`

WinForms 图形界面，所有功能按钮化，适合不熟悉命令行的用户。

- 拖放 / 浏览选择 DLL
- 预设下拉框一键填入
- **🔍 扫描文件夹** 按钮：自动遍历游戏目录所有 .dll，识别马赛克相关函数
- 双击扫描结果即可套用，按返回类型自动推荐补丁值
- 彩色状态栏 + 可滚动日志区
- 支持备份 / 一键还原

体积约 177 MB（含完整 .NET 8 + WinForms 运行时，双击即用，无依赖）。

## 构建方法

### 前置依赖

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 或更高版本
- Windows 10/11 / Windows Server 2019+ （x64）
- 任意代码编辑器（VSCode / Visual Studio / Rider / 记事本均可）

### 项目结构

| 项目 | 目标框架 | 输出类型 |
|------|---------|---------|
| `PatchReturnUI/PatchReturnUI.csproj` | `net8.0-windows` | WinExe |

### 发布为单文件独立 exe

```powershell
dotnet publish PatchReturnUI/PatchReturnUI.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:IncludeAllContentForSelfExtract=true -o .\dist
```

发布后 `dist/PatchReturnUI.exe` 可单独拷贝分发，目标机无需安装 .NET 运行时。

## 工作原理

UnMosic 修补一个方法返回值的步骤：

1. 用 `ModuleDefMD.Load` 从字节数组加载目标 DLL（避开文件锁）
2. 遍历所有类型所有方法，按名字 + 可选类型过滤匹配目标
3. 在方法 IL 序列中**从后向前**找第一个 `ret` 指令（即最后一个 return）
4. 检查 `ret` 前一条指令是否为取值指令（`ldc.*`、`ldnull`、`ldstr`）
5. 按方法 `ReturnType` 构造新取值指令（如 `false` → `Instruction.CreateLdcI4(0)`）
6. **关键**：原位修改 `ret` 前指令的 `OpCode` 和 `Operand`，**不替换 Instruction 对象**
   - 原因：方法体内可能有分支指令跳转到这条指令（`br`/`brtrue`/`brfalse` 等）
   - 如果用新 Instruction 对象替换，跳转目标会丢失，dnlib 保存时报错：
     `Found some other method's instruction or a removed instruction`
7. 调用 `SimplifyMacros` + `OptimizeMacros` 让 dnlib 重新优化 IL（短指令合并等）
8. 写临时文件 → `module.Dispose()` 释放 → 覆盖原 DLL

### 为什么只改最后一个 return？

方法可能有多个 `ret` 指令（早返回分支），如：

```csharp
bool FnDrawMosaic(...) {
    if (someCondition) return true;   // 早返回
    DoSomething();
    return true;                       // 主返回 ← UnMosic 改的就是这条
}
```

UnMosic 只修改最后一个 `ret` 之前的取值，**保留**所有早返回分支的原逻辑。
对于"主路径"返回值的修改，这正是你想要的精确语义。

### 自动扫描关键词表

| 类型 | 关键词 | 分值 |
|------|--------|------|
| 强相关 | `mosaic` `censor` `drawglonly` `draw_gl_only` `fndrawmosaic` `drawmosaic` `getmosaicsize` `mosaicshower` `mosaicenabled` `isdrawmosaic` | 100 |
| 普通相关 | `blur` `pixelat` `obscure` `drawgl` `draw_gl` `hider` `hiding` `mask` `coverup` `shade` `steam` `blackbar` `pixe` `fog` `veil` | 50 |

## 依赖与致谢

### 第三方库

| 库 | 版本 | 协议 | 用途 |
|----|------|------|------|
| [dnlib](https://github.com/0xd4d/dnlib) | 4.4.0 | MIT | 读写 .NET 程序集 IL，dnSpy 的底层引擎 |

### 致谢

- [**dnlib**](https://github.com/0xd4d/dnlib) 作者 [@0xd4d](https://github.com/0xd4d) — 提供强大的 .NET 程序集读写库
- [**dnSpy**](https://github.com/dnSpy/dnSpy) 项目 — 启发了本工具的核心思路（dnlib 即 dnSpy 底层）
- 所有提出反馈与场景测试的用户

## 项目结构

```
UnMosic/
├── PatchReturnUI/               # WinForms GUI 版
│   ├── PatchReturnUI.csproj
│   ├── Program.cs               # 入口
│   ├── MainForm.cs              # 主窗口 + 扫描结果窗体
│   ├── Patcher.cs               # 修补引擎
│   └── Preset.cs                # 预设数据
│
├── .gitignore
├── LICENSE
└── README.md
```

## 开源协议

本项目基于 [MIT License](LICENSE) 开源，可自由使用、修改、分发、商业用途。
所引用的 dnlib 也为 MIT 协议，与本项目兼容。

## 免责声明

本工具仅供学习与研究 .NET 程序集 IL 修改技术。使用者需自行承担因不当使用产生的法律责任。
作者不鼓励、不支持任何违反当地法律法规或第三方软件许可协议的用途。
修改游戏可执行文件可能违反游戏服务条款，请先确认你拥有合法授权再使用。
