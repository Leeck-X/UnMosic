using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

namespace PatchReturn;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[X] 未处理异常: {ex}");
            return 99;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length < 3)
        {
            PrintUsage();
            return 1;
        }

        string dllPath = Path.GetFullPath(args[0]);
        string funcName = args[1].Trim();
        string valueStr = args[2].Trim();
        string? typeFilter = args.Length >= 4 ? args[3].Trim() : null;

        if (!File.Exists(dllPath))
        {
            Console.Error.WriteLine($"[X] DLL 文件不存在: {dllPath}");
            return 1;
        }

        // ---------- 1. 备份原 DLL ----------
        string backupPath = dllPath + ".bak";
        if (!File.Exists(backupPath))
        {
            File.Copy(dllPath, backupPath, overwrite: true);
            Console.WriteLine($"[+] 已备份原始 DLL -> {backupPath}");
        }
        else
        {
            Console.WriteLine($"[!] 备份文件已存在，保留不动: {backupPath}");
        }

        // ---------- 2. 加载模块 (从字节加载,避免长期占用原文件句柄) ----------
        ModuleDefMD module;
        try
        {
            byte[] raw = File.ReadAllBytes(dllPath);
            module = ModuleDefMD.Load(raw);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[X] 加载模块失败: {ex.Message}");
            return 2;
        }

        // ---------- 3. 按函数名查找方法 ----------
        List<MethodDef> matches = new();
        foreach (var type in module.GetTypes())
        {
            foreach (var m in type.Methods)
            {
                if (m.Name != funcName) continue;
                if (typeFilter != null &&
                    type.FullName != typeFilter &&
                    !type.FullName.EndsWith("." + typeFilter, StringComparison.Ordinal))
                {
                    continue;
                }
                matches.Add(m);
            }
        }

        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"[X] 未找到函数: '{funcName}'" +
                (typeFilter != null ? $" (在类型 {typeFilter} 中)" : ""));
            return 3;
        }

        if (matches.Count > 1)
        {
            Console.Error.WriteLine($"[X] 找到 {matches.Count} 个同名方法，请用第 4 个参数指定声明类型:");
            foreach (var m in matches)
                Console.Error.WriteLine($"    {m.DeclaringType?.FullName} :: {m.Name}  返回 {m.ReturnType.FullName}");
            return 3;
        }

        MethodDef found = matches[0];
        Console.WriteLine($"[*] 命中方法: {found.FullName}");
        Console.WriteLine($"    声明类型: {found.DeclaringType?.FullName}");
        Console.WriteLine($"    返回类型: {found.ReturnType.FullName}");
        Console.WriteLine($"    静态:     {found.IsStatic}");

        if (!found.HasBody || found.Body is null)
        {
            Console.Error.WriteLine("[X] 该方法没有方法体 (P/Invoke 或抽象方法)。");
            return 4;
        }

        var instrs = found.Body.Instructions;
        if (instrs.Count == 0)
        {
            Console.Error.WriteLine("[X] 方法体为空。");
            return 5;
        }

        // ---------- 4. 定位最后一个 ret ----------
        int retIndex = -1;
        for (int i = instrs.Count - 1; i >= 0; i--)
        {
            if (instrs[i].OpCode == OpCodes.Ret)
            {
                retIndex = i;
                break;
            }
        }
        if (retIndex < 0)
        {
            Console.Error.WriteLine("[X] 方法体中未找到 'ret' 指令。");
            return 6;
        }

        Console.WriteLine($"[*] 最后一个 'ret' 位于索引 {retIndex}: {instrs[retIndex]}");

        bool isVoid = found.ReturnType.FullName == "System.Void";
        if (isVoid)
        {
            Console.Error.WriteLine("[X] 方法返回 void，'ret' 无返回值可改。");
            return 7;
        }

        // ---------- 5. 构造新的取值指令 ----------
        Instruction newLoad;
        try
        {
            newLoad = BuildLoadInstruction(found.ReturnType, valueStr);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"[X] {ex.Message}");
            return 8;
        }
        Console.WriteLine($"[*] 新的取值指令: {newLoad}");

        // ---------- 6. 原位修改 ret 前的取值指令 ----------
        // 必须原位修改 (改 OpCode + Operand), 不能用新 Instruction 替换对象。
        // 原因: 方法里其他分支可能跳转到这条指令, 替换对象会让跳转目标丢失,
        // dnlib 保存时报 "Found some other method's instruction or a removed instruction"。
        if (retIndex == 0)
        {
            // ret 是第一条指令(罕见): 在其前面插入
            instrs.Insert(0, newLoad);
            Console.WriteLine("[+] ret 之前无指令，已插入新取值指令。");
        }
        else
        {
            int targetIdx = retIndex - 1;
            var oldInstr = instrs[targetIdx];
            Console.WriteLine($"[*] 原位修改索引 {targetIdx}: {oldInstr} → {newLoad.OpCode} {newLoad.Operand}");
            oldInstr.OpCode = newLoad.OpCode;
            oldInstr.Operand = newLoad.Operand;
        }

        // 让 dnlib 重新统计 MaxStack (writer 通常也会自动算,这里显式调一下)
        try { found.Body.SimplifyMacros(found.Parameters); } catch { /* ignore */ }
        try { found.Body.OptimizeMacros(); } catch { /* ignore */ }

        // ---------- 7. 保存(先写临时文件,释放模块后覆盖原文件) ----------
        string tempPath = dllPath + ".patched.tmp";
        try
        {
            var opt = new ModuleWriterOptions(module);
            opt.WritePdb = false;
            module.Write(tempPath, opt);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[X] 保存失败: {ex.Message}");
            if (File.Exists(tempPath)) File.Delete(tempPath);
            try { module.Dispose(); } catch { }
            Console.Error.WriteLine("[!] 原始文件未受影响。");
            return 9;
        }

        // 写完后立即释放模块,确保不持有原文件句柄
        try { module.Dispose(); } catch { /* ignore */ }

        try
        {
            File.Copy(tempPath, dllPath, overwrite: true);
            File.Delete(tempPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[X] 覆盖原 DLL 失败 (文件可能被占用): {ex.Message}");
            Console.Error.WriteLine($"[!] 修补结果保留在: {tempPath}");
            Console.Error.WriteLine($"[!] 可手动重命名以替换原 DLL,或从 {backupPath} 还原。");
            return 10;
        }

        Console.WriteLine($"[+] 修补完成,已写回: {dllPath}");
        Console.WriteLine($"    如需还原: del \"{dllPath}\" && copy \"{backupPath}\" \"{dllPath}\"");
        return 0;
    }

    private static Instruction BuildLoadInstruction(TypeSig returnType, string valueStr)
    {
        string tn = returnType.FullName;

        if (tn == "System.Boolean")
        {
            bool? v = ParseBool(valueStr);
            if (!v.HasValue)
                throw new ArgumentException($"无法将 '{valueStr}' 解析为 Boolean (用 true/false)。");
            return Instruction.CreateLdcI4(v.Value ? 1 : 0);
        }
        if (tn == "System.Int32" || tn == "System.UInt32" ||
            tn == "System.Byte"   || tn == "System.SByte" ||
            tn == "System.Int16"  || tn == "System.UInt16" ||
            tn == "System.IntPtr" || tn == "System.UIntPtr")
        {
            if (!int.TryParse(valueStr, out int v))
                throw new ArgumentException($"无法将 '{valueStr}' 解析为整数。");
            return Instruction.CreateLdcI4(v);
        }
        if (tn == "System.Int64" || tn == "System.UInt64")
        {
            if (!long.TryParse(valueStr, out long v))
                throw new ArgumentException($"无法将 '{valueStr}' 解析为 Int64。");
            return Instruction.Create(OpCodes.Ldc_I8, v);
        }
        if (tn == "System.Single")
        {
            string s = valueStr.TrimEnd('f', 'F', 'd', 'D');
            if (!float.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out float v))
                throw new ArgumentException($"无法将 '{valueStr}' 解析为 float。");
            return Instruction.Create(OpCodes.Ldc_R4, v);
        }
        if (tn == "System.Double")
        {
            string s = valueStr.TrimEnd('f', 'F', 'd', 'D');
            if (!double.TryParse(s, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double v))
                throw new ArgumentException($"无法将 '{valueStr}' 解析为 double。");
            return Instruction.Create(OpCodes.Ldc_R8, v);
        }
        if (tn == "System.Char")
        {
            if (valueStr.Length == 1)
                return Instruction.CreateLdcI4((int)valueStr[0]);
            if (int.TryParse(valueStr, out int cv))
                return Instruction.CreateLdcI4(cv);
            throw new ArgumentException($"无法将 '{valueStr}' 解析为 Char。");
        }
        // 引用类型 / 可空
        if (!returnType.IsValueType)
        {
            if (valueStr.Equals("null", StringComparison.OrdinalIgnoreCase))
                return Instruction.Create(OpCodes.Ldnull);
            if (tn == "System.String")
                return Instruction.Create(OpCodes.Ldstr, valueStr);
            throw new ArgumentException($"对引用类型 {tn},只支持 'null' 或 string 字面量。");
        }
        throw new ArgumentException($"不支持的返回类型: {tn}");
    }

    private static bool? ParseBool(string s)
    {
        if (s.Equals("true",  StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (int.TryParse(s, out int i)) return i != 0;
        return null;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("PatchReturn - 用 dnlib (dnSpy 底层库) 修改 .NET 程序集指定方法的最后一个 return 值");
        Console.WriteLine();
        Console.WriteLine("用法:");
        Console.WriteLine("  PatchReturn.exe <DLL路径> <函数名> <返回值> [类型名]");
        Console.WriteLine();
        Console.WriteLine("参数:");
        Console.WriteLine("  <DLL路径>   要修补的 .NET 程序集(如 Assembly-CSharp.dll)");
        Console.WriteLine("  <函数名>    方法名(区分大小写,需与 IL 中一致)");
        Console.WriteLine("  <返回值>    true | false | 整数 | 浮点(0.01 / 0.01f) | null | 字符串");
        Console.WriteLine("  [类型名]    可选,用于同名方法歧义时指定声明类型");
        Console.WriteLine();
        Console.WriteLine("行为:");
        Console.WriteLine("  * 自动备份原 DLL 到 <原名>.bak (若已存在则保留)");
        Console.WriteLine("  * 仅修改方法体中最后一个 'ret' 之前的取值指令");
        Console.WriteLine("  * 写入失败会自动保留备份,不会破坏原文件");
        Console.WriteLine();
        Console.WriteLine("示例:");
        Console.WriteLine("  PatchReturn.exe Assembly-CSharp.dll FnDrawMosaic false");
        Console.WriteLine("  PatchReturn.exe Assembly-CSharp.dll get_DrawGlOnly false");
        Console.WriteLine("  PatchReturn.exe Assembly-CSharp.dll GetMosaicSize 0.01f");
    }
}
