using System.Diagnostics;

namespace PatchReturnUINative;

internal static class Program
{
    // 当前会话状态
    private static string _dllPath = "";
    private static string _funcName = "";
    private static string _valueStr = "";
    private static string? _typeFilter = null;
    private static List<Preset> _presets = new();

    // 扫描结果缓存(供编号选择)
    private static List<ScanHit> _scanResults = new();

    private static void Main()
    {
        Console.Title = "PatchReturn - dnlib 内核";
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }

        _presets = Presets.Load(Presets.GetExeDir());

        Logo();
        Log($"[*] PatchReturn 已启动 - dnlib (dnSpy 底层库) 内核");
        Log($"[*] 预设已加载: {_presets.Count} 条 (位于 presets.json, 可编辑)");
        Log("");

        while (true)
        {
            ShowStatus();
            ShowMenu();
            Console.Write("> ");
            string? line = Console.ReadLine();
            if (line == null) break;
            line = line.Trim();
            if (line == "0" || line.Equals("exit", StringComparison.OrdinalIgnoreCase) || line.Equals("quit", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("退出");
                return;
            }
            try
            {
                // 编号形式: 直接选扫描结果
                if (line.StartsWith("s", StringComparison.OrdinalIgnoreCase) && int.TryParse(line.AsSpan(1), out int sIdx))
                { ChooseScanResult(sIdx); continue; }

                switch (line)
                {
                    case "1": ChooseDll(); break;
                    case "2": ChoosePreset(); break;
                    case "3": InputFuncName(); break;
                    case "4": InputValue(); break;
                    case "5": InputTypeFilter(); break;
                    case "6": Patch(); break;
                    case "7": Restore(); break;
                    case "8": ListFunc(); break;
                    case "9": EditPresets(); break;
                    case "a" or "A": ScanFolder(); break;
                    case "h" or "H" or "?": ShowHelp(); break;
                    default:
                        if (int.TryParse(line, out _) == false && line.Length > 0)
                            LogErr($"未知命令: {line}");
                        break;
                }
            }
            catch (Exception ex) { LogErr("操作异常: " + ex.Message); }
        }
    }

    // ---------- UI ----------
    private static void Logo()
    {
        Console.WriteLine();
        Console.WriteLine("  ____          _   _____                _                ");
        Console.WriteLine("  |  _ \\ ___  __ _| |_|  ___| __ __ _ ___| |_ ___ _ __ ___ ");
        Console.WriteLine("  | |_) / _ \\/ _` | __| |_ | '__/ _` / __| __/ _ \\ '__/ __|");
        Console.WriteLine("  |  _ <  __/ (_| | |_|  _|| | | (_| \\__ \\ ||  __/ |  \\__ \\");
        Console.WriteLine("  |_| \\_\\___|\\__,_|\\__|_|  |_|  \\__,_|___/\\__\\___|_|  |___/");
        Console.WriteLine("  最后一个 return 修补器  -  dnlib/dnSpy 内核");
        Console.WriteLine();
    }

    private static void ShowStatus()
    {
        Console.WriteLine("──────────── 当前状态 ────────────");
        Console.WriteLine($"  DLL 路径 : {(string.IsNullOrEmpty(_dllPath) ? "(未选择)" : _dllPath)}");
        Console.WriteLine($"  函数名   : {(string.IsNullOrEmpty(_funcName) ? "(未填)" : _funcName)}");
        Console.WriteLine($"  返回值   : {(string.IsNullOrEmpty(_valueStr) ? "(未填)" : _valueStr)}");
        Console.WriteLine($"  类型过滤 : {_typeFilter ?? "(无)"}");
        if (_scanResults.Count > 0)
            Console.WriteLine($"  扫描候选 : {_scanResults.Count} 条 (用 s<编号> 选, 如 s0)");
        Console.WriteLine("──────────────────────────────────");
    }

    private static void ShowMenu()
    {
        Console.WriteLine();
        Console.WriteLine("  [1] 选择 DLL 路径");
        Console.WriteLine("  [2] 选择预设");
        Console.WriteLine("  [3] 输入函数名");
        Console.WriteLine("  [4] 输入返回值");
        Console.WriteLine("  [5] 输入类型过滤 (可选, 同名消歧)");
        Console.WriteLine("  [6] 修补 DLL");
        Console.WriteLine("  [7] 还原备份 (.bak)");
        Console.WriteLine("  [8] 列出函数 (查同名)");
        Console.WriteLine("  [9] 编辑预设 (记事本打开)");
        Console.WriteLine("  [a] 自动扫描文件夹 (查所有候选函数) ★");
        Console.WriteLine("  [h] 帮助");
        Console.WriteLine("  [0] 退出");
    }

    private static void ShowHelp()
    {
        Console.WriteLine();
        Console.WriteLine("──── 使用说明 ────");
        Console.WriteLine("1. 选 DLL: 输入路径或拖文件到窗口");
        Console.WriteLine("2. 选预设: 输入数字快速套用常见马赛克补丁");
        Console.WriteLine("3-4. 函数名/返回值: 支持 true/false, 整数, 0.01f, null, 字符串");
        Console.WriteLine("5. 类型过滤: 如多个同名方法, 填声明类型全名");
        Console.WriteLine("6. 修补: 自动备份 .bak -> 替换最后一个 ret 前的取值");
        Console.WriteLine("7. 还原: 用 .bak 覆盖回原 DLL");
        Console.WriteLine("a. 扫描文件夹: 遍历所有 .dll, 用关键词匹配马赛克候选函数");
        Console.WriteLine("   扫描后用 s<编号> (如 s0) 一键套用候选到当前状态");
        Console.WriteLine("─────────────────");
    }

    // ---------- 命令实现 ----------
    private static void ChooseDll()
    {
        Console.Write("输入 DLL 路径 (或直接拖文件到窗口然后回车): ");
        string? p = Console.ReadLine()?.Trim().Trim('"');
        if (string.IsNullOrEmpty(p)) { LogErr("未输入"); return; }
        if (!File.Exists(p)) { LogErr($"文件不存在: {p}"); return; }
        _dllPath = p;
        Log($"[+] 已选: {_dllPath}");
    }

    private static void ChoosePreset()
    {
        if (_presets.Count == 0) { LogErr("无预设"); return; }
        Console.WriteLine();
        Console.WriteLine("── 预设列表 ──");
        for (int i = 0; i < _presets.Count; i++)
        {
            var p = _presets[i];
            Console.WriteLine($"  [{i}] {p.Name}");
            Console.WriteLine($"       函数={p.Function} | 值={p.Value} | 类型过滤={p.TypeFilter ?? "(无)"}");
        }
        Console.WriteLine();
        Console.Write("输入预设编号 (回车取消): ");
        string? s = Console.ReadLine()?.Trim();
        if (string.IsNullOrEmpty(s)) return;
        if (!int.TryParse(s, out int idx) || idx < 0 || idx >= _presets.Count)
        { LogErr("编号无效"); return; }

        var pre = _presets[idx];
        if (!string.IsNullOrEmpty(pre.Function)) _funcName = pre.Function;
        if (!string.IsNullOrEmpty(pre.Value)) _valueStr = pre.Value;
        _typeFilter = pre.TypeFilter;
        Log($"[+] 已套用预设 [{idx}]: {pre.Name}");
    }

    private static void InputFuncName()
    {
        Console.Write($"函数名 (当前: {_funcName}): ");
        string? s = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(s)) _funcName = s;
        Log($"[+] 函数名 = {_funcName}");
    }

    private static void InputValue()
    {
        Console.Write($"返回值 (当前: {_valueStr}) [true/false/整数/0.01f/null/字符串]: ");
        string? s = Console.ReadLine()?.Trim();
        if (!string.IsNullOrEmpty(s)) _valueStr = s;
        Log($"[+] 返回值 = {_valueStr}");
    }

    private static void InputTypeFilter()
    {
        Console.Write($"类型过滤 (当前: {_typeFilter ?? ""}, 留空清除): ");
        string? s = Console.ReadLine()?.Trim();
        _typeFilter = string.IsNullOrEmpty(s) ? null : s;
        Log($"[+] 类型过滤 = {_typeFilter ?? "(无)"}");
    }

    private static void Patch()
    {
        if (string.IsNullOrEmpty(_dllPath)) { LogErr("请先选择 DLL"); return; }
        if (string.IsNullOrEmpty(_funcName)) { LogErr("请输入函数名"); return; }
        if (string.IsNullOrEmpty(_valueStr)) { LogErr("请输入返回值"); return; }

        Console.WriteLine();
        Log($"═════════════════ 修补开始 {DateTime.Now:HH:mm:ss} ═════════════════");
        var patcher = new Patcher(Log, LogErr);
        var result = patcher.Patch(_dllPath, _funcName, _valueStr, _typeFilter);
        if (result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[✓] {result.Message}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[X] {result.Message}");
            Console.ResetColor();
        }
    }

    private static void Restore()
    {
        if (string.IsNullOrEmpty(_dllPath)) { LogErr("请先选择 DLL"); return; }
        string backup = _dllPath + ".bak";
        if (!File.Exists(backup)) { LogErr($"备份不存在: {backup}"); return; }

        Console.Write($"确认用备份覆盖当前 DLL? (y/N): ");
        if (Console.ReadLine()?.Trim().ToLower() != "y") { Log("已取消"); return; }

        try { File.Copy(backup, _dllPath, overwrite: true); Log($"[+] 已还原: {_dllPath}"); }
        catch (Exception ex) { LogErr($"还原失败: {ex.Message}"); }
    }

    private static void ListFunc()
    {
        if (string.IsNullOrEmpty(_dllPath)) { LogErr("请先选择 DLL"); return; }
        if (string.IsNullOrEmpty(_funcName)) { LogErr("请先输入函数名 (用于搜索)"); return; }

        Log($"[*] 搜索函数 '{_funcName}' 在 {_dllPath} ...");
        try
        {
            var module = dnlib.DotNet.ModuleDefMD.Load(File.ReadAllBytes(_dllPath));
            int count = 0;
            foreach (var type in module.GetTypes())
            foreach (var m in type.Methods)
            {
                if (m.Name != _funcName) continue;
                if (_typeFilter != null &&
                    type.FullName != _typeFilter &&
                    !type.FullName.EndsWith("." + _typeFilter, StringComparison.Ordinal))
                    continue;
                Log($"    [{count}] {type.FullName} :: {m.Name} (返回 {m.ReturnType.FullName}, 静态={m.IsStatic})");
                count++;
            }
            module.Dispose();
            Log(count == 0 ? "[!] 未匹配任何方法" : $"[+] 共 {count} 个同名方法");
        }
        catch (Exception ex) { LogErr("搜索失败: " + ex.Message); }
    }

    private static void EditPresets()
    {
        string path = Path.Combine(Presets.GetExeDir(), "presets.json");
        if (!File.Exists(path)) Presets.Load(Presets.GetExeDir());
        try
        {
            using var p = Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
            Log($"[~] 已用记事本打开: {path}");
            Log("[*] 编辑保存后, 重启本工具以重新加载预设");
        }
        catch (Exception ex) { LogErr("打开失败: " + ex.Message); }
    }

    // ---------- 自动扫描 ----------
    /// <summary>扫描结果记录</summary>
    private sealed class ScanHit
    {
        public string DllPath = "";
        public string TypeName = "";
        public string MethodName = "";
        public string ReturnType = "";
        public bool IsStatic;
        public int Score;       // 关键词相关度
        public string MatchedKeyword = "";
    }

    /// <summary>马赛克/审查相关关键词(小写比较)</summary>
    private static readonly string[] _mosaicKeywords =
    {
        "mosaic", "censor", "blur", "pixelat", "obscure",
        "drawgl", "draw_gl", "hider", "hiding",
        "mask", "coverup", "shade", "steam",
        "blackbar", "pixe", "fog", "veil"
    };

    /// <summary>强相关关键词(高分)</summary>
    private static readonly string[] _strongKeywords =
    {
        "mosaic", "censor", "drawglonly", "draw_gl_only",
        "fndrawmosaic", "drawmosaic", "getmosaicsize",
        "mosaicshower", "mosaicenabled", "isdrawmosaic"
    };

    private static void ScanFolder()
    {
        Console.Write("输入文件夹路径 (或拖文件夹到窗口): ");
        string? folder = Console.ReadLine()?.Trim().Trim('"');
        if (string.IsNullOrEmpty(folder)) { LogErr("未输入"); return; }
        if (!Directory.Exists(folder)) { LogErr($"文件夹不存在: {folder}"); return; }

        Console.Write("递归扫描子目录? (y/N): ");
        bool recurse = Console.ReadLine()?.Trim().ToLower() == "y";

        var searchOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        string[] dlls;
        try { dlls = Directory.GetFiles(folder, "*.dll", searchOption); }
        catch (Exception ex) { LogErr($"枚举文件失败: {ex.Message}"); return; }

        if (dlls.Length == 0) { LogErr($"未找到 .dll 文件: {folder}"); return; }

        Log($"[*] 开始扫描 {dlls.Length} 个 DLL (递归={recurse})");
        Log("");

        _scanResults.Clear();
        int scanned = 0, failed = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        foreach (var dll in dlls)
        {
            string shortName = Path.GetFileName(dll);
            Console.Write($"[扫描 {++scanned}/{dlls.Length}] {shortName,-50} ... ");

            List<ScanHit> hits = new();
            try
            {
                var module = dnlib.DotNet.ModuleDefMD.Load(File.ReadAllBytes(dll));
                foreach (var type in module.GetTypes())
                foreach (var m in type.Methods)
                {
                    if (!m.HasBody || m.Body is null) continue;
                    string mn = m.Name.String ?? "";
                    if (mn.Length == 0 || mn.StartsWith("get_", StringComparison.Ordinal) == false &&
                        mn.StartsWith("set_", StringComparison.Ordinal) == false &&
                        mn.Length < 3) continue;

                    string mnLower = mn.ToLowerInvariant();
                    string tnLower = (type.Name.String ?? "").ToLowerInvariant();

                    int score = 0;
                    string matched = "";
                    // 强关键词
                    foreach (var k in _strongKeywords)
                    {
                        if (mnLower.Contains(k) || tnLower.Contains(k))
                        {
                            score += 100;
                            matched = k;
                            break;
                        }
                    }
                    // 普通关键词
                    foreach (var k in _mosaicKeywords)
                    {
                        if (mnLower.Contains(k) || tnLower.Contains(k))
                        {
                            score += 50;
                            matched = string.IsNullOrEmpty(matched) ? k : matched + "+" + k;
                            if (score >= 100) break;
                        }
                    }
                    if (score == 0) continue;

                    // 跳过 void 返回(无法改 ret 取值)
                    if (m.ReturnType.FullName == "System.Void") continue;

                    hits.Add(new ScanHit
                    {
                        DllPath = dll,
                        TypeName = type.FullName,
                        MethodName = mn,
                        ReturnType = m.ReturnType.FullName,
                        IsStatic = m.IsStatic,
                        Score = score,
                        MatchedKeyword = matched
                    });
                }
                module.Dispose();
            }
            catch
            {
                failed++;
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("跳过 (非 .NET 或读取失败)");
                Console.ResetColor();
                continue;
            }

            if (hits.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("无匹配");
                Console.ResetColor();
                continue;
            }

            // 按相关度排序
            hits.Sort((a, b) => b.Score.CompareTo(a.Score));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"匹配 {hits.Count} 个");
            Console.ResetColor();
            foreach (var h in hits)
            {
                int idx = _scanResults.Count;
                _scanResults.Add(h);
                string star = h.Score >= 100 ? "★" : " ";
                Console.WriteLine($"    {star}[s{idx}] {h.MethodName} - {h.TypeName}");
                Console.WriteLine($"           返回 {h.ReturnType} 静态={h.IsStatic} 关键词={h.MatchedKeyword}");
            }
        }

        sw.Stop();
        Console.WriteLine();
        Console.WriteLine("═══════════════════════════════════════════");
        Console.WriteLine($"[*] 扫描完成: 耗时 {sw.Elapsed.TotalSeconds:F1}s, 扫描 {scanned} 个, 失败 {failed} 个");
        Console.WriteLine($"[*] 共找到 {_scanResults.Count} 个候选函数");
        if (_scanResults.Count > 0)
        {
            Console.WriteLine($"[*] 用 s<编号> 选候选 (如 s0), 自动填入 DLL/函数名/类型过滤");
            Console.WriteLine($"[*] 然后选预设或手动填返回值, 最后按 [6] 修补");
        }
        Console.WriteLine("═══════════════════════════════════════════");
    }

    private static void ChooseScanResult(int idx)
    {
        if (_scanResults.Count == 0) { LogErr("无扫描结果, 请先按 [a] 扫描"); return; }
        if (idx < 0 || idx >= _scanResults.Count) { LogErr($"编号超出范围 [0-{_scanResults.Count - 1}]"); return; }

        var hit = _scanResults[idx];
        _dllPath = hit.DllPath;
        _funcName = hit.MethodName;
        _typeFilter = hit.TypeName;

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[+] 已套用扫描结果 s{idx}:");
        Console.ResetColor();
        Console.WriteLine($"    DLL    : {hit.DllPath}");
        Console.WriteLine($"    函数  : {hit.MethodName}");
        Console.WriteLine($"    类型  : {hit.TypeName}");
        Console.WriteLine($"    返回  : {hit.ReturnType} 静态={hit.IsStatic}");
        Console.WriteLine($"    匹配  : {hit.MatchedKeyword} (相关度={hit.Score})");

        // 自动推荐返回值
        if (string.IsNullOrEmpty(_valueStr) && hit.ReturnType == "System.Boolean")
        {
            _valueStr = "false";
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[*] 已自动填入返回值 false (Boolean 类型, 推断为关闭马赛克)");
            Console.ResetColor();
        }
        else if (string.IsNullOrEmpty(_valueStr) && hit.ReturnType == "System.Single")
        {
            _valueStr = "0.01f";
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[*] 已自动填入返回值 0.01f (Single 类型, 推断为最小化)");
            Console.ResetColor();
        }
        Console.WriteLine("[*] 现在按 [6] 修补, 或先按 [4] 改返回值");
    }

    // ---------- 日志辅助 ----------
    private static void Log(string msg)
    {
        Console.WriteLine(msg);
    }

    private static void LogErr(string msg)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("[X] " + msg);
        Console.ResetColor();
    }
}
