using System.Diagnostics;

namespace PatchReturnUI;

public class MainForm : Form
{
    private readonly TextBox _dllPath;
    private readonly Button _browseBtn;
    private readonly ComboBox _presetCombo;
    private readonly TextBox _funcName;
    private readonly TextBox _valueStr;
    private readonly TextBox _typeFilter;
    private readonly Button _patchBtn;
    private readonly Button _restoreBtn;
    private readonly Button _clearLogBtn;
    private readonly Button _scanBtn;
    private readonly Button _openPresetsBtn;
    private readonly TextBox _logBox;
    private readonly ToolStripStatusLabel _statusLabel;

    private List<Preset> _presets = new();

    // 设计常量(基于 ClientSize 820x540)
    private const int FormW = 820;
    private const int FormH = 540;
    private const int Pad = 12;          // 左右边距
    private const int LblW = 80;         // 标签宽度
    private const int RowH = 30;          // 单行高度
    private const int Gap = 6;           // 控件间水平间隔
    private const int BrowseW = 90;      // 浏览按钮宽
    private const int PresetsBtnW = 130; // 打开预设按钮宽
    private const int ValLblW = 60;      // 返回值标签宽
    private const int ValBoxW = 160;     // 返回值输入框宽

    public MainForm()
    {
        Text = "PatchReturn - .NET DLL 最后 return 修补器 (dnlib/dnSpy 内核)";
        ClientSize = new Size(FormW, FormH);
        MinimumSize = new Size(720, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = SystemColors.Control;
        // 关键: 不用 Dpi 自动缩放(用户环境 DPI 非标准会导致控件错位)
        AutoScaleMode = AutoScaleMode.None;

        int leftLbl = Pad;
        int inputX = Pad + LblW + Gap;
        int rightEdge = FormW - Pad;

        // === 行 0: DLL 路径 + 浏览按钮 ===
        int y = 14;
        var lblDll = new Label
        {
            Text = "DLL 路径:",
            Location = new Point(leftLbl, y + 4),
            Size = new Size(LblW, 20),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        // 浏览按钮: 锚定 Top|Right, 设计位置距右 10
        _browseBtn = new Button
        {
            Text = "浏览...",
            Location = new Point(rightEdge - BrowseW, y - 1),
            Size = new Size(BrowseW, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        // 路径输入框: 锚定 Top|Left|Right, 右边距 = BrowseW + 10
        _dllPath = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(rightEdge - BrowseW - 10 - inputX, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _browseBtn.Click += BrowseBtn_Click;

        // === 行 1: 预设 + 打开 presets.json ===
        y += RowH;
        var lblPreset = new Label
        {
            Text = "预设:",
            Location = new Point(leftLbl, y + 4),
            Size = new Size(LblW, 20),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _openPresetsBtn = new Button
        {
            Text = "打开 presets.json",
            Location = new Point(rightEdge - PresetsBtnW, y - 1),
            Size = new Size(PresetsBtnW, 25),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        _openPresetsBtn.Click += (s, e) => OpenPresetsFile();
        _presetCombo = new ComboBox
        {
            Location = new Point(inputX, y),
            Size = new Size(rightEdge - PresetsBtnW - 10 - inputX, 23),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        LoadPresets();
        _presetCombo.SelectedIndexChanged += (s, e) => ApplyPreset();

        // === 行 2: 函数名 + 返回值 ===
        y += RowH;
        var lblFunc = new Label
        {
            Text = "函数名:",
            Location = new Point(leftLbl, y + 4),
            Size = new Size(LblW, 20),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        // 返回值标签+输入框在右侧, 锚定 Top|Right
        int valBoxRight = rightEdge;
        int valBoxLeft = valBoxRight - ValBoxW;
        int valLblLeft = valBoxLeft - ValLblW - Gap;
        _valueStr = new TextBox
        {
            Location = new Point(valBoxLeft, y),
            Size = new Size(ValBoxW, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        var lblVal = new Label
        {
            Text = "返回值:",
            Location = new Point(valLblLeft, y + 4),
            Size = new Size(ValLblW, 20),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };
        // 函数名输入框: 从 inputX 到 valLblLeft - Gap
        _funcName = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(valLblLeft - Gap - inputX, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        var valTip = new ToolTip();
        valTip.SetToolTip(_valueStr, "true / false / 整数 / 浮点(0.01f) / null / 字符串字面量");

        // === 行 3: 类型过滤 (跨右) ===
        y += RowH;
        var lblType = new Label
        {
            Text = "类型过滤:",
            Location = new Point(leftLbl, y + 4),
            Size = new Size(LblW, 20),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _typeFilter = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(rightEdge - inputX, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        var typeTip = new ToolTip();
        typeTip.SetToolTip(_typeFilter, "可选。同名方法有多个时填声明类型全名 (或 .类型名 后缀匹配)");

        // === 行 4: 留白 ===
        y += RowH + 6;

        // === 行 5: 四个按钮 ===
        int bx = inputX;
        _patchBtn = MakeBtn("🔧 修补 DLL", 140, Color.FromArgb(40, 167, 69), Color.White);
        _patchBtn.Location = new Point(bx, y); bx += 140 + 10;
        _restoreBtn = MakeBtn("↩ 还原备份", 130, Color.FromArgb(220, 53, 69), Color.White);
        _restoreBtn.Location = new Point(bx, y); bx += 130 + 10;
        _clearLogBtn = MakeBtn("🗑 清空日志", 110, SystemColors.ControlDark, SystemColors.ControlText);
        _clearLogBtn.Location = new Point(bx, y); bx += 110 + 10;
        _scanBtn = MakeBtn("🔍 扫描文件夹", 130, Color.FromArgb(0, 123, 255), Color.White);
        _scanBtn.Location = new Point(bx, y);
        _patchBtn.Click += PatchBtn_Click;
        _restoreBtn.Click += RestoreBtn_Click;
        _clearLogBtn.Click += (s, e) => _logBox.Clear();
        _scanBtn.Click += ScanBtn_Click;

        // 顶部输入区总高度
        int topH = y + 44;

        // === 日志区: 填满剩余空间 ===
        _logBox = new TextBox
        {
            Location = new Point(0, topH),
            Size = new Size(FormW, FormH - topH - 22),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };

        // === 状态栏 ===
        _statusLabel = new ToolStripStatusLabel("就绪") { Spring = true };
        var statusStrip = new StatusStrip { BackColor = SystemColors.Control };
        statusStrip.Items.Add(_statusLabel);

        // === 组装 ===
        Controls.AddRange(new Control[] {
            lblDll, _dllPath, _browseBtn,
            lblPreset, _presetCombo, _openPresetsBtn,
            lblFunc, _funcName, lblVal, _valueStr,
            lblType, _typeFilter,
            _patchBtn, _restoreBtn, _clearLogBtn, _scanBtn,
            _logBox,
            statusStrip,
        });

        // 拖放支持 (整窗口)
        AllowDrop = true;
        DragEnter += (s, e) => { if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        DragDrop += (s, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            { _dllPath.Text = files[0]; Log($"[~] 已拖入: {files[0]}"); }
        };

        AcceptButton = _patchBtn;
        Log("[*] PatchReturn 已启动 - dnlib (dnSpy 底层库) 内核");
        Log("[*] 步骤: 选 DLL → 选预设(或手填函数名/返回值) → 点修补");
        Log("[*] 预设可编辑: 点'打开 presets.json' 增删改后保存, 重启本工具生效");
        Log("[*] 批量扫描: 点'🔍 扫描文件夹' 自动识别所有马赛克候选函数");
        Log("");
    }

    private static Button MakeBtn(string text, int width, Color back, Color fore)
    {
        var b = new Button
        {
            Text = text,
            Width = width,
            Height = 34,
            FlatStyle = FlatStyle.Flat,
            BackColor = back,
            ForeColor = fore,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(back);
        return b;
    }

    // ---------- 预设 ----------
    private void LoadPresets()
    {
        string exeDir = Presets.GetExeDir();
        _presets = Presets.Load(exeDir);
        _presetCombo.Items.Clear();
        foreach (var p in _presets) _presetCombo.Items.Add(p);
        if (_presetCombo.Items.Count > 0) _presetCombo.SelectedIndex = 0;
    }

    private void ApplyPreset()
    {
        if (_presetCombo.SelectedItem is not Preset p) return;
        if (!string.IsNullOrEmpty(p.Function)) _funcName.Text = p.Function;
        if (!string.IsNullOrEmpty(p.Value))   _valueStr.Text = p.Value;
        _typeFilter.Text = p.TypeFilter ?? "";
    }

    private void OpenPresetsFile()
    {
        string exeDir = Presets.GetExeDir();
        string path = Path.Combine(exeDir, "presets.json");
        if (!File.Exists(path)) Presets.Load(exeDir);
        try
        {
            using var p = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
            Log($"[~] 打开: {path}");
        }
        catch (Exception ex) { LogErr($"打开 presets.json 失败: {ex.Message}"); }
    }

    // ---------- 浏览 DLL ----------
    private void BrowseBtn_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择 .NET DLL",
            Filter = ".NET 程序集 (*.dll)|*.dll|所有文件 (*.*)|*.*",
            InitialDirectory = string.IsNullOrEmpty(_dllPath.Text) ? "" : Path.GetDirectoryName(_dllPath.Text) ?? "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _dllPath.Text = dlg.FileName;
            Log($"[~] 已选: {dlg.FileName}");
        }
    }

    // ---------- 修补 ----------
    private async void PatchBtn_Click(object? sender, EventArgs e)
    {
        string dllPath = _dllPath.Text.Trim();
        string funcName = _funcName.Text.Trim();
        string valueStr = _valueStr.Text.Trim();
        string? typeFilter = string.IsNullOrWhiteSpace(_typeFilter.Text) ? null : _typeFilter.Text.Trim();

        if (string.IsNullOrEmpty(dllPath))  { Warn("请先选择 DLL 路径"); return; }
        if (string.IsNullOrEmpty(funcName)) { Warn("请填写函数名");       return; }
        if (string.IsNullOrEmpty(valueStr)) { Warn("请填写返回值");       return; }

        SetBusy(true);
        _statusLabel.Text = "修补中... 请等待";
        Log($"──────────────── 修补开始 {DateTime.Now:HH:mm:ss} ────────────────");

        var patcher = new Patcher(Log, LogErr);
        var result = await Task.Run(() => patcher.Patch(dllPath, funcName, valueStr, typeFilter));

        if (result.Success)
        {
            _statusLabel.Text = $"✓ 修补成功 | 备份: {Path.GetFileName(result.BackupPath)}";
            _statusLabel.BackColor = Color.FromArgb(40, 167, 69);
            _statusLabel.ForeColor = Color.White;
        }
        else
        {
            _statusLabel.Text = "✗ 修补失败 (看日志)";
            _statusLabel.BackColor = Color.FromArgb(220, 53, 69);
            _statusLabel.ForeColor = Color.White;
        }
        SetBusy(false);
    }

    // ---------- 还原 ----------
    private void RestoreBtn_Click(object? sender, EventArgs e)
    {
        string dllPath = _dllPath.Text.Trim();
        if (string.IsNullOrEmpty(dllPath)) { Warn("请先选择 DLL 路径"); return; }
        string backupPath = dllPath + ".bak";
        if (!File.Exists(backupPath)) { Warn($"备份文件不存在:\n{backupPath}"); return; }

        if (MessageBox.Show(this,
            $"确认用备份覆盖当前 DLL?\n\n备份: {backupPath}\n当前: {dllPath}",
            "确认还原", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        try
        {
            File.Copy(backupPath, dllPath, overwrite: true);
            Log($"[+] 已还原: {dllPath}");
            _statusLabel.Text = "✓ 已还原备份";
            _statusLabel.BackColor = Color.FromArgb(40, 167, 69);
            _statusLabel.ForeColor = Color.White;
        }
        catch (Exception ex)
        {
            LogErr($"还原失败: {ex.Message}");
            _statusLabel.Text = "✗ 还原失败";
            _statusLabel.BackColor = Color.FromArgb(220, 53, 69);
            _statusLabel.ForeColor = Color.White;
        }
    }

    // ---------- 自动扫描 ----------
    /// <summary>扫描结果记录</summary>
    public sealed class ScanHit
    {
        public string DllPath = "";
        public string TypeName = "";
        public string MethodName = "";
        public string ReturnType = "";
        public bool IsStatic;
        public int Score;
        public string MatchedKeyword = "";
        public override string ToString() => $"{MethodName} - {TypeName}";
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

    private async void ScanBtn_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "选择游戏目录(会扫描所有 .dll 找候选马赛克函数)",
            ShowNewFolderButton = false,
        };
        if (!string.IsNullOrEmpty(_dllPath.Text))
            dlg.InitialDirectory = Path.GetDirectoryName(_dllPath.Text) ?? "";
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string folder = dlg.SelectedPath;
        bool recurse = MessageBox.Show(this,
            "递归扫描子目录?(通常游戏 DLL 在 Managed 子目录下,选'是')",
            "扫描深度", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        Log($"═════════════════ 扫描开始 {DateTime.Now:HH:mm:ss} ═════════════════");
        Log($"[*] 文件夹: {folder}");
        Log($"[*] 递归: {recurse}");

        SetBusy(true);
        _statusLabel.Text = "扫描中... 请等待";
        _scanBtn.Enabled = false;

        var searchOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var hits = new List<ScanHit>();
        int scanned = 0, failed = 0;
        var sw = Stopwatch.StartNew();

        try
        {
            string[] dlls = await Task.Run(() => Directory.GetFiles(folder, "*.dll", searchOption));
            Log($"[*] 找到 {dlls.Length} 个 .dll 文件");
            foreach (var dll in dlls)
            {
                string shortName = Path.GetFileName(dll);
                int cur = ++scanned;
                await Task.Run(() =>
                {
                    try
                    {
                        var module = dnlib.DotNet.ModuleDefMD.Load(File.ReadAllBytes(dll));
                        var localHits = new List<ScanHit>();
                        foreach (var type in module.GetTypes())
                        foreach (var m in type.Methods)
                        {
                            if (!m.HasBody || m.Body is null) continue;
                            if (m.ReturnType.FullName == "System.Void") continue;
                            string mn = m.Name.String ?? "";
                            if (mn.Length < 3) continue;

                            string mnLower = mn.ToLowerInvariant();
                            string tnLower = (type.Name.String ?? "").ToLowerInvariant();

                            int score = 0;
                            string matched = "";
                            foreach (var k in _strongKeywords)
                            {
                                if (mnLower.Contains(k) || tnLower.Contains(k))
                                { score += 100; matched = k; break; }
                            }
                            if (score == 0)
                            {
                                foreach (var k in _mosaicKeywords)
                                {
                                    if (mnLower.Contains(k) || tnLower.Contains(k))
                                    { score += 50; matched = k; break; }
                                }
                            }
                            if (score == 0) continue;

                            localHits.Add(new ScanHit
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

                        lock (hits)
                        {
                            hits.AddRange(localHits);
                            hits.Sort((a, b) => b.Score.CompareTo(a.Score));
                        }
                        Log($"[{cur}/{dlls.Length}] {shortName,-50} 匹配 {localHits.Count} 个");
                    }
                    catch
                    {
                        Interlocked.Increment(ref failed);
                        Log($"[{cur}/{dlls.Length}] {shortName,-50} 跳过(非.NET/读取失败)");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            LogErr("扫描失败: " + ex.Message);
        }

        sw.Stop();
        Log("");
        Log($"═══════════════════════════════════════════");
        Log($"[*] 扫描完成: 耗时 {sw.Elapsed.TotalSeconds:F1}s, 扫描 {scanned} 个, 失败 {failed} 个");
        Log($"[*] 共找到 {hits.Count} 个候选函数");

        SetBusy(false);
        _scanBtn.Enabled = true;
        _statusLabel.Text = $"扫描完成: {hits.Count} 候选 | 点击结果套用";

        if (hits.Count == 0)
        {
            MessageBox.Show(this, "未找到候选函数。", "扫描结果",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 弹出结果选择窗体
        using var sf = new ScanResultForm(hits);
        sf.Owner = this;
        if (sf.ShowDialog(this) == DialogResult.OK && sf.Selected is not null)
        {
            var hit = sf.Selected;
            _dllPath.Text = hit.DllPath;
            _funcName.Text = hit.MethodName;
            _typeFilter.Text = hit.TypeName;

            // 自动推荐返回值
            if (string.IsNullOrWhiteSpace(_valueStr.Text))
            {
                if (hit.ReturnType == "System.Boolean")
                {
                    _valueStr.Text = "false";
                    Log("[*] 自动填入返回值 false (Boolean)");
                }
                else if (hit.ReturnType == "System.Single")
                {
                    _valueStr.Text = "0.01f";
                    Log("[*] 自动填入返回值 0.01f (Single)");
                }
            }
            Log($"[+] 已套用扫描结果: {hit.MethodName} - {hit.TypeName}");
            Log($"    返回 {hit.ReturnType} 静态={hit.IsStatic} 关键词={hit.MatchedKeyword} 相关度={hit.Score}");
            _statusLabel.Text = "✓ 已套用扫描结果, 点修补确认";
            _statusLabel.BackColor = Color.FromArgb(0, 123, 255);
            _statusLabel.ForeColor = Color.White;
        }
    }

    // ---------- 辅助 ----------
    private void SetBusy(bool busy)
    {
        _patchBtn.Enabled = !busy;
        _restoreBtn.Enabled = !busy;
        _browseBtn.Enabled = !busy;
        _scanBtn.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }

    private void Log(string msg)
    {
        if (InvokeRequired) { BeginInvoke(() => Log(msg)); return; }
        _logBox.AppendText(msg + Environment.NewLine);
        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.ScrollToCaret();
    }

    private void LogErr(string msg) => Log("[X] " + msg);

    private void Warn(string msg)
    {
        MessageBox.Show(this, msg, "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        _statusLabel.Text = "提示: " + msg;
        _statusLabel.BackColor = SystemColors.Control;
        _statusLabel.ForeColor = SystemColors.ControlText;
    }
}

/// <summary>扫描结果选择窗体</summary>
internal sealed class ScanResultForm : Form
{
    public MainForm.ScanHit? Selected { get; private set; }
    private readonly ListView _lv;
    private readonly Button _ok;
    private readonly Button _cancel;

    public ScanResultForm(List<MainForm.ScanHit> hits)
    {
        Text = $"扫描结果: {hits.Count} 个候选 (双击套用)";
        ClientSize = new Size(820, 460);
        MinimumSize = new Size(640, 360);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        Font = new Font("Microsoft YaHei UI", 9F);

        _lv = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            MultiSelect = false,
            Font = new Font("Consolas", 9F),
            BackColor = Color.FromArgb(248, 248, 248),
        };
        _lv.Columns.Add("★", 24);
        _lv.Columns.Add("函数名", 200);
        _lv.Columns.Add("类型", 280);
        _lv.Columns.Add("返回", 120);
        _lv.Columns.Add("静态", 50);
        _lv.Columns.Add("相关度", 60);
        _lv.Columns.Add("关键词", 120);
        _lv.Columns.Add("DLL", 220);

        foreach (var h in hits)
        {
            var item = new ListViewItem(h.Score >= 100 ? "★" : " ");
            item.SubItems.Add(h.MethodName);
            item.SubItems.Add(h.TypeName);
            item.SubItems.Add(h.ReturnType);
            item.SubItems.Add(h.IsStatic ? "是" : "否");
            item.SubItems.Add(h.Score.ToString());
            item.SubItems.Add(h.MatchedKeyword);
            item.SubItems.Add(Path.GetFileName(h.DllPath));
            item.Tag = h;
            if (h.Score >= 100)
            {
                item.BackColor = Color.FromArgb(255, 248, 220);
                item.ForeColor = Color.FromArgb(180, 90, 0);
            }
            _lv.Items.Add(item);
        }
        if (_lv.Items.Count > 0) _lv.Items[0].Selected = true;
        _lv.DoubleClick += (s, e) => { if (_lv.SelectedItems.Count > 0) { Selected = _lv.SelectedItems[0].Tag as MainForm.ScanHit; DialogResult = DialogResult.OK; Close(); } };
        _lv.SelectedIndexChanged += (s, e) => _ok.Enabled = _lv.SelectedItems.Count > 0;

        // 底部按钮
        var pnl = new Panel { Dock = DockStyle.Bottom, Height = 36, BackColor = SystemColors.Control };
        _ok = new Button
        {
            Text = "套用所选",
            Width = 130, Height = 28,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 123, 255),
            ForeColor = Color.White,
            Enabled = false,
        };
        _ok.FlatAppearance.BorderSize = 0;
        _cancel = new Button
        {
            Text = "取消",
            Width = 80, Height = 28,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
        };
        _cancel.FlatAppearance.BorderSize = 0;
        pnl.Controls.Add(_ok);
        pnl.Controls.Add(_cancel);
        pnl.Layout += (s, e) =>
        {
            _cancel.Location = new Point(pnl.ClientSize.Width - _cancel.Width - 12, 4);
            _ok.Location = new Point(_cancel.Left - _ok.Width - 10, 4);
        };
        _ok.Click += (s, e) =>
        {
            if (_lv.SelectedItems.Count > 0)
            {
                Selected = _lv.SelectedItems[0].Tag as MainForm.ScanHit;
                DialogResult = DialogResult.OK;
                Close();
            }
        };

        Controls.Add(_lv);
        Controls.Add(pnl);
        AcceptButton = _ok;
        CancelButton = _cancel;
    }
}
