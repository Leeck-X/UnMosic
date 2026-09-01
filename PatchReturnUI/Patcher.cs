using System.Globalization;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

namespace PatchReturnUI;

/// <summary>修补结果</summary>
public class PatchResult
{
    public bool Success;
    public int ExitCode;
    public string BackupPath = "";
    public string DllPath = "";
    public string Message = "";
}

public class Patcher
{
    private readonly Action<string> _log;
    private readonly Action<string> _err;

    public Patcher(Action<string> log, Action<string> err)
    {
        _log = log;
        _err = err;
    }

    /// <summary>执行一次完整修补流程,返回成功/失败</summary>
    public PatchResult Patch(string dllPath, string funcName, string valueStr, string? typeFilter = null)
    {
        var result = new PatchResult { DllPath = dllPath };

        funcName = funcName.Trim();
        valueStr = valueStr.Trim();
        typeFilter = string.IsNullOrWhiteSpace(typeFilter) ? null : typeFilter.Trim();

        if (!File.Exists(dllPath)) { Fail(ref result, $"DLL 文件不存在: {dllPath}"); return result; }
        if (string.IsNullOrEmpty(funcName)) { Fail(ref result, "函数名不能为空"); return result; }
        if (string.IsNullOrEmpty(valueStr)) { Fail(ref result, "返回值不能为空"); return result; }

        // 1. 备份
        string backupPath = dllPath + ".bak";
        try
        {
            if (!File.Exists(backupPath))
            {
                File.Copy(dllPath, backupPath, overwrite: true);
                _log($"[+] 已备份原 DLL -> {backupPath}");
            }
            else
            {
                _log($"[!] 备份已存在,保留不动: {backupPath}");
            }
            result.BackupPath = backupPath;
        }
        catch (Exception ex)
        {
            Fail(ref result, $"备份失败: {ex.Message}");
            return result;
        }

        // 2. 从字节加载
        ModuleDefMD module;
        try
        {
            byte[] raw = File.ReadAllBytes(dllPath);
            module = ModuleDefMD.Load(raw);
        }
        catch (Exception ex)
        {
            Fail(ref result, $"加载模块失败: {ex.Message}");
            return result;
        }

        try
        {
            // 3. 查找方法
            List<MethodDef> matches = new();
            foreach (var type in module.GetTypes())
            foreach (var m in type.Methods)
            {
                if (m.Name != funcName) continue;
                if (typeFilter != null &&
                    type.FullName != typeFilter &&
                    !type.FullName.EndsWith("." + typeFilter, StringComparison.Ordinal))
                    continue;
                matches.Add(m);
            }

            if (matches.Count == 0)
            {
                Fail(ref result, $"未找到函数: '{funcName}'" + (typeFilter != null ? $" (类型过滤: {typeFilter})" : ""));
                return result;
            }
            if (matches.Count > 1)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"找到 {matches.Count} 个同名方法,请在'类型过滤'里填声明类型:");
                foreach (var m in matches)
                    sb.AppendLine($"    {m.DeclaringType?.FullName}  ::  {m.Name}  (返回 {m.ReturnType.FullName})");
                Fail(ref result, sb.ToString());
                return result;
            }

            MethodDef found = matches[0];
            _log($"[*] 命中: {found.FullName}");
            _log($"    返回类型: {found.ReturnType.FullName}  | 静态: {found.IsStatic}");

            if (!found.HasBody || found.Body is null)
            { Fail(ref result, "该方法无方法体 (P/Invoke 或抽象)"); return result; }
            if (found.Body.Instructions.Count == 0)
            { Fail(ref result, "方法体为空"); return result; }

            // 4. 定位最后一个 ret
            var instrs = found.Body.Instructions;
            int retIndex = -1;
            for (int i = instrs.Count - 1; i >= 0; i--)
                if (instrs[i].OpCode == OpCodes.Ret) { retIndex = i; break; }
            if (retIndex < 0) { Fail(ref result, "方法体中无 'ret' 指令"); return result; }
            _log($"[*] 最后一个 'ret' 位于索引 {retIndex}: {instrs[retIndex]}");

            bool isVoid = found.ReturnType.FullName == "System.Void";
            if (isVoid) { Fail(ref result, "方法返回 void,'ret' 无返回值可改"); return result; }

            // 5. 构造新取值指令
            Instruction newLoad;
            try { newLoad = BuildLoadInstruction(found.ReturnType, valueStr); }
            catch (ArgumentException ex) { Fail(ref result, ex.Message); return result; }
            _log($"[*] 新取值指令: {newLoad}");

            // 6. 修改 ret 前的取值指令
            //    必须原位修改 (改 OpCode + Operand), 不能用新 Instruction 替换对象。
            //    原因: 方法里其他分支可能跳转到这条指令, 替换对象会让跳转目标丢失,
            //    dnlib 保存时报 "Found some other method's instruction or a removed instruction"。
            if (retIndex == 0)
            {
                instrs.Insert(0, newLoad);
                _log("[+] ret 是首条指令,已插入新取值");
            }
            else
            {
                int targetIdx = retIndex - 1;
                var oldInstr = instrs[targetIdx];
                _log($"[*] 原位修改索引 {targetIdx}: {oldInstr} → {newLoad.OpCode} {newLoad.Operand}");
                oldInstr.OpCode = newLoad.OpCode;
                oldInstr.Operand = newLoad.Operand;
            }

            try { found.Body.SimplifyMacros(found.Parameters); } catch { }
            try { found.Body.OptimizeMacros(); } catch { }

            // 7. 写临时文件 -> 释放模块 -> 覆盖原文件
            string tempPath = dllPath + ".patched.tmp";
            try
            {
                var opt = new ModuleWriterOptions(module) { WritePdb = false };
                module.Write(tempPath, opt);
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
                Fail(ref result, $"保存失败: {ex.Message}");
                return result;
            }
            try { module.Dispose(); } catch { }

            try
            {
                File.Copy(tempPath, dllPath, overwrite: true);
                try { File.Delete(tempPath); } catch { }
            }
            catch (Exception ex)
            {
                Fail(ref result, $"覆盖原 DLL 失败 (可能被占用): {ex.Message}\n修补结果保留在: {tempPath}");
                return result;
            }

            result.Success = true;
            result.ExitCode = 0;
            result.Message = "修补完成";
            _log($"[+] 修补完成,已写回: {dllPath}");
            _log($"    如需还原: del \"{dllPath}\" && copy \"{backupPath}\" \"{dllPath}\"");
            return result;
        }
        finally
        {
            try { module.Dispose(); } catch { }
        }
    }

    private void Fail(ref PatchResult r, string msg)
    {
        r.Success = false;
        r.ExitCode = 1;
        r.Message = msg;
        _err(msg);
    }

    private static Instruction BuildLoadInstruction(TypeSig returnType, string valueStr)
    {
        string tn = returnType.FullName;

        if (tn == "System.Boolean")
        {
            bool? v = ParseBool(valueStr);
            if (!v.HasValue) throw new ArgumentException($"无法将 '{valueStr}' 解析为 Boolean (用 true/false)");
            return Instruction.CreateLdcI4(v.Value ? 1 : 0);
        }
        if (tn is "System.Int32" or "System.UInt32" or "System.Byte" or "System.SByte"
            or "System.Int16" or "System.UInt16" or "System.IntPtr" or "System.UIntPtr")
        {
            if (!int.TryParse(valueStr, out int v)) throw new ArgumentException($"无法将 '{valueStr}' 解析为整数");
            return Instruction.CreateLdcI4(v);
        }
        if (tn is "System.Int64" or "System.UInt64")
        {
            if (!long.TryParse(valueStr, out long v)) throw new ArgumentException($"无法将 '{valueStr}' 解析为 Int64");
            return Instruction.Create(OpCodes.Ldc_I8, v);
        }
        if (tn == "System.Single")
        {
            string s = valueStr.TrimEnd('f', 'F', 'd', 'D');
            if (!float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out float v))
                throw new ArgumentException($"无法将 '{valueStr}' 解析为 float");
            return Instruction.Create(OpCodes.Ldc_R4, v);
        }
        if (tn == "System.Double")
        {
            string s = valueStr.TrimEnd('f', 'F', 'd', 'D');
            if (!double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                throw new ArgumentException($"无法将 '{valueStr}' 解析为 double");
            return Instruction.Create(OpCodes.Ldc_R8, v);
        }
        if (tn == "System.Char")
        {
            if (valueStr.Length == 1) return Instruction.CreateLdcI4(valueStr[0]);
            if (int.TryParse(valueStr, out int cv)) return Instruction.CreateLdcI4(cv);
            throw new ArgumentException($"无法将 '{valueStr}' 解析为 Char");
        }
        if (!returnType.IsValueType)
        {
            if (valueStr.Equals("null", StringComparison.OrdinalIgnoreCase))
                return Instruction.Create(OpCodes.Ldnull);
            if (tn == "System.String") return Instruction.Create(OpCodes.Ldstr, valueStr);
            throw new ArgumentException($"对引用类型 {tn},只支持 'null' 或 string 字面量");
        }
        throw new ArgumentException($"不支持的返回类型: {tn}");
    }

    private static bool? ParseBool(string s)
    {
        if (s.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
        if (int.TryParse(s, out int i)) return i != 0;
        return null;
    }
}
