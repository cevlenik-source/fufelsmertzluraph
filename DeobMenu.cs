using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DeobMenu
{
    public class JobRec
    {
        public string Id;
        public List<string> Log = new List<string>();
        public string State = "running";
        public string Result;
        public ListViewItem Item;
        public Process Proc;
        public string ArgsText;
    }

    public class MainForm : Form
    {
        readonly string ExeDir = AppDomain.CurrentDomain.BaseDirectory;
        readonly string DeobJs;
        readonly string SettingsPath;

        ListBox lstFiles;
        RadioButton rbFiles, rbPaste;
        TextBox txtPaste, txtCmd, txtNode, txtDefDir;
        NumericUpDown numTimeout, numBudget, numMaxRuns, numJobs, numDevirt, numV14Budget, numChunkMin, numSpin, numPipeline;
        TextBox txtExecutor, txtOutput;
        CheckBox chkNoDevirt, chkNoHooks, chkNoFold, chkStrings, chkDebug, chkKeepHarness, chkKeepPreamble, chkDetect;
        CheckBox chkV14Deep, chkSpinLate, chkDumpTrace, chkDebugPartial;
        ComboBox cmbPreset;
        RichTextBox richLog;
        ListView lstJobs;
        Label lblStatus, lblCur, lblCount;
        Button btnRun, btnCancel;
        int jobSeq = 1;
        bool syncing;
        JobRec current;
        bool running;
        readonly List<JobRec> jobs = new List<JobRec>();
        Stopwatch swJob;
        readonly Dictionary<string, string> settings = new Dictionary<string, string>();
        string lastResultPath;

        public MainForm()
        {
            DeobJs = Path.Combine(ExeDir, "deob.js");
            SettingsPath = Path.Combine(ExeDir, "DeobMenu.settings");
            Text = "Luraph Deobfuscator";
            Font = new Font("Segoe UI", 9f);
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(1180, 780);
            MinimumSize = new Size(980, 640);
            BuildUi();
            LoadSettings();
        }

        static string Quote(string a)
        {
            if (a == null) return "\"\"";
            if (a.Length == 0) return "\"\"";
            if (a.IndexOf(' ') < 0 && a.IndexOf('\t') < 0 && a.IndexOf('"') < 0) return a;
            var sb = new StringBuilder();
            sb.Append('"');
            int bs = 0;
            foreach (char c in a)
            {
                if (c == '\\') { bs++; }
                else if (c == '"') { sb.Append('\\', bs * 2 + 1); sb.Append('"'); bs = 0; }
                else { sb.Append('\\', bs); bs = 0; sb.Append(c); }
            }
            sb.Append('\\', bs * 2);
            sb.Append('"');
            return sb.ToString();
        }

        static CheckBox Chk(string text, float width)
        {
            return new CheckBox { Text = text, AutoSize = true, Width = (int)width };
        }

        void BuildUi()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(33, 34, 38) };
            var title = new Label { Text = "Luraph Deobfuscator — деобфускация скриптов", ForeColor = Color.White, Dock = DockStyle.Left, AutoSize = false, Width = 320, Padding = new Padding(12, 0, 0, 0) };
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            var lblNode = new Label { Text = "Node:", ForeColor = Color.Silver, Dock = DockStyle.Left, TextAlign = ContentAlignment.MiddleRight, AutoSize = true, Padding = new Padding(0, 0, 6, 0) };
            lblNode.Margin = new Padding(0);
            var txtNod = new TextBox { Text = "node", Dock = DockStyle.Left, Width = 220 };
            lblNode.Height = 30;
            txtNod.Height = 26;
            lblStatus = new Label { Text = "готов", ForeColor = Color.Khaki, Dock = DockStyle.Right, AutoSize = true, Padding = new Padding(0, 0, 12, 0), TextAlign = ContentAlignment.MiddleRight };
            lblStatus.Height = 30;
            var tool = new Panel { Dock = DockStyle.Fill };
            top.Controls.Add(lblStatus);
            top.Controls.Add(tool);
            top.Controls.Add(txtNod);
            top.Controls.Add(lblNode);
            top.Controls.Add(title);
            title.BringToFront(); lblNode.BringToFront(); txtNod.BringToFront();
            this.Controls.Add(top);

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 26 };
            var lblCopyright = new Label { Text = "Deobfuscator | движок deob.js (Luraph v14/v15 + generic) | пресеты: Быстрый / Глубокий v14 / Максимум", Dock = DockStyle.Right, ForeColor = Color.Gray, TextAlign = ContentAlignment.MiddleRight };
            var lblFill = new Label { Dock = DockStyle.Fill };
            bottom.Controls.Add(lblFill);
            bottom.Controls.Add(lblCopyright);
            this.Controls.Add(bottom);
            lblCopyright.Height = 24;

            var splitMain = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 340, FixedPanel = FixedPanel.Panel1, IsSplitterFixed = false, SplitterWidth = 6, BackColor = Color.FromArgb(230, 230, 230) };
            splitMain.SplitterMoved += (s, e) => { };
            this.Controls.Add(splitMain);
            bottom.BringToFront();

            BuildLeft(splitMain.Panel1);
            BuildRight(splitMain.Panel2);

            top.BringToFront();
        }

        void BuildLeft(Panel p)
        {
            p.Padding = new Padding(10);

            var gFiles = new GroupBox { Text = "Исходные файлы (для деобфускации)", Dock = DockStyle.Fill };
            p.Controls.Add(gFiles);

            var lay = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 6 };
            lay.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lay.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
            lay.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lay.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lay.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            lay.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            gFiles.Controls.Add(lay);
            lay.Dock = DockStyle.Fill;

            rbFiles = new RadioButton { Text = "Из файлов", Checked = true, AutoSize = true };
            rbPaste = new RadioButton { Text = "Из вставленного текста", AutoSize = true };
            var rbPanel = new FlowLayoutPanel { AutoSize = true };
            rbPanel.Margin = new Padding(6, 4, 6, 0);
            rbPanel.Controls.Add(rbFiles);
            rbPanel.Controls.Add(rbPaste);
            lay.Controls.Add(rbPanel, 0, 0);
            lay.SetColumnSpan(rbPanel, 2);

            lstFiles = new ListBox { Dock = DockStyle.Fill, SelectionMode = SelectionMode.MultiExtended, ScrollAlwaysVisible = true, IntegralHeight = false };
            lstFiles.HorizontalScrollbar = true;
            lstFiles.Margin = new Padding(8, 4, 8, 2);
            lay.Controls.Add(lstFiles, 0, 1);
            lay.SetColumnSpan(lstFiles, 2);

            var bFiles = new Button { Text = "Файлы…", AutoSize = true };
            var bDir = new Button { Text = "Папка…", AutoSize = true };
            var bSample = new Button { Text = "Sample", AutoSize = true };
            var bRemove = new Button { Text = "Удалить", AutoSize = true };
            var bClear = new Button { Text = "Очистить", AutoSize = true };
            bFiles.Click += (s, e) => AddFilesDialog();
            bDir.Click += (s, e) => AddFolderDialog();
            bSample.Click += (s, e) => AddSample();
            bRemove.Click += (s, e) => RemoveSelectedFiles();
            bClear.Click += (s, e) => lstFiles.Items.Clear();
            var flowB = new FlowLayoutPanel { AutoSize = true, WrapContents = true };
            flowB.Margin = new Padding(8, 2, 8, 4);
            flowB.Controls.Add(bFiles); flowB.Controls.Add(bDir); flowB.Controls.Add(bSample); flowB.Controls.Add(bRemove); flowB.Controls.Add(bClear);
            lay.Controls.Add(flowB, 0, 2);
            lay.SetColumnSpan(flowB, 2);

            var labDef = new Label { Text = "Стартовая папка:", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft };
            labDef.Margin = new Padding(8, 4, 6, 2);
            lay.Controls.Add(labDef, 0, 3);
            txtDefDir = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(6, 4, 6, 2) };
            lay.Controls.Add(txtDefDir, 1, 3);

            var labPaste = new Label { Text = "Или вставьте код:", AutoSize = true };
            labPaste.Margin = new Padding(8, 4, 6, 0);
            lay.Controls.Add(labPaste, 0, 4);
            txtPaste = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, AcceptsReturn = true, Font = new Font("Consolas", 9f), Margin = new Padding(6, 4, 8, 6) };
            lay.Controls.Add(txtPaste, 1, 4);
        }

        void BuildRight(Panel p)
        {
            p.Padding = new Padding(10);

            var gPreset = new GroupBox { Text = "Пресет", Dock = DockStyle.Top, Height = 76 };
            cmbPreset = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
            cmbPreset.Items.AddRange(new object[] { "Быстрый", "Глубокий v14", "Максимум", "Свой" });
            cmbPreset.SelectedIndex = 0;
            cmbPreset.Left = 14; cmbPreset.Top = 26;
            btnRun = new Button { Text = "Запустить", Width = 130, Height = 30, BackColor = Color.FromArgb(22, 120, 255), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnRun.Click += async (s, e) => await StartJobAsync();
            btnCancel = new Button { Text = "Остановить", Width = 110, Height = 30, Enabled = false };
            btnCancel.Click += (s, e) => CancelJob();

            gPreset.Controls.Add(cmbPreset);
            gPreset.Controls.Add(btnRun);
            gPreset.Controls.Add(btnCancel);
            btnRun.Left = 290; btnRun.Top = 26;
            btnCancel.Left = 430; btnCancel.Top = 26;
            cmbPreset.SelectedIndexChanged += (s, e) => ApplyPreset();

            var btnCopy = new Button { Text = "Копировать команду", AutoSize = true, Left = 550, Top = 26, Height = 30 };
            btnCopy.Click += (s, e) => CopyCommand();
            gPreset.Controls.Add(btnCopy);

            var btnOpen = new Button { Text = "Открыть папку результата", AutoSize = true, Left = 700, Top = 26, Height = 30 };
            btnOpen.Click += (s, e) => OpenResultFolder();
            gPreset.Controls.Add(btnOpen);

            txtCmd = new TextBox { ReadOnly = true, Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(10, 10, 14), ForeColor = Color.Orange, Font = new Font("Consolas", 9f), Margin = new Padding(6, 4, 6, 4) };
            p.Controls.Add(txtCmd);
            p.Controls.Add(gPreset);

            var splitR = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 360, FixedPanel = FixedPanel.Panel2 };
            p.Controls.Add(splitR);
            txtCmd.BringToFront();

            var settingsPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White };
            splitR.Panel1.Controls.Add(settingsPanel);
            BuildSettings(settingsPanel);

            var bottomPanel = new Panel { Dock = DockStyle.Fill };
            splitR.Panel2.Controls.Add(bottomPanel);
            BuildLog(bottomPanel);
        }

        void BuildSettings(Panel sp)
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8) };
            sp.Controls.Add(flow);

            var gProc = new GroupBox { Text = "Обработка (deob.js)", Width = 380, Height = 320, AutoSize = true };
            var t1 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 10, AutoSize = true };
            t1.Margin = new Padding(6);
            gProc.Controls.Add(t1);
            t1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            t1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

            numTimeout = MkNum(1); numBudget = MkNum(2); numMaxRuns = MkNum(3); numJobs = MkNum(4); numDevirt = MkNum(5);
            txtExecutor = new TextBox { Text = "Wave" };
            txtOutput = new TextBox { Width = 999 };

            AddNumRow(t1, 0, "timeout, сек", numTimeout, 90);
            AddNumRow(t1, 1, "budget, сек", numBudget, 30);
            AddNumRow(t1, 2, "max-runs", numMaxRuns, 12);
            AddNumRow(t1, 3, "jobs (параллельно)", numJobs, 1);
            AddNumRow(t1, 4, "devirt-rounds", numDevirt, 200);
            AddTxtRow(t1, 5, "executor", txtExecutor);
            AddTxtRow(t1, 6, "Выходной файл (-o)", txtOutput);

            chkNoDevirt = Chk("без devirt", 150); chkNoHooks = Chk("без хуков", 150);
            chkNoFold = Chk("без fold", 150); chkStrings = Chk("декодер строк", 150);
            chkKeepHarness = Chk("keep harness", 150); chkKeepPreamble = Chk("keep preamble", 150);
            chkDebug = Chk("debug (файлы рядом)", 150); chkDetect = Chk("только определить", 150);
            var fc1 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            fc1.Controls.Add(chkNoDevirt); fc1.Controls.Add(chkNoHooks); fc1.Controls.Add(chkNoFold); fc1.Controls.Add(chkStrings);
            fc1.Controls.Add(chkKeepHarness); fc1.Controls.Add(chkKeepPreamble); fc1.Controls.Add(chkDebug); fc1.Controls.Add(chkDetect);
            t1.Controls.Add(fc1, 0, 7); t1.SetColumnSpan(fc1, 2);
            flow.Controls.Add(gProc);

            var gV = new GroupBox { Text = "Глубокая машина v14 (env)", Width = 380, AutoSize = true };
            var t2 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, AutoSize = true };
            t2.Margin = new Padding(6);
            gV.Controls.Add(t2);
            t2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            t2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            numV14Budget = MkNum(1); numChunkMin = MkNum(2); numSpin = MkNum(3); numPipeline = MkNum(4);
            chkV14Deep = Chk("глубокий лифт функций", 999999); chkSpinLate = Chk("spin позже", 150); chkDumpTrace = Chk("dump trace", 150); chkDebugPartial = Chk("debug partial", 150);
            t2.Controls.Add(chkV14Deep, 0, 0); t2.SetColumnSpan(chkV14Deep, 2);
            AddNumRow(t2, 1, "DEOB_V14_DECODE_BUDGET", numV14Budget);
            AddNumRow(t2, 2, "DEOB_CHUNK_MIN (перехват)", numChunkMin);
            AddNumRow(t2, 3, "DEOB_SPIN (лимит циклов)", numSpin);
            AddNumRow(t2, 4, "DEOB_PIPELINE_TIMEOUT", numPipeline);
            var fc2 = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            fc2.Controls.Add(chkSpinLate); fc2.Controls.Add(chkDumpTrace); fc2.Controls.Add(chkDebugPartial);
            t2.Controls.Add(fc2, 0, 5); t2.SetColumnSpan(fc2, 2);
            flow.Controls.Add(gV);

            var gEnv = new GroupBox { Text = "Среда", Width = 380, AutoSize = true };
            var t3 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, AutoSize = true };
            t3.Margin = new Padding(6);
            gEnv.Controls.Add(t3);
            t3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            t3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
            txtNode = new TextBox { Text = FindNode() };
            var bNode = new Button { Text = "…", Width = 36, Height = 24 };
            bNode.Click += (s, e) =>
            {
                var ofd = new OpenFileDialog { Filter = "node.exe|node.exe", FileName = txtNode.Text };
                if (ofd.ShowDialog(this) == DialogResult.OK) txtNode.Text = ofd.FileName;
            };
            var pw = new Panel { Dock = DockStyle.Fill };
            pw.Controls.Add(bNode); pw.Controls.Add(txtNode);
            txtNode.Dock = DockStyle.Fill; txtNode.Width = pw.ClientSize.Width - 40;
            bNode.Dock = DockStyle.Right;
            t3.Controls.Add(new Label { Text = "Путь к node(.exe):", TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            t3.Controls.Add(pw, 1, 0);
            flow.Controls.Add(gEnv);

            foreach (NumericUpDown c in new NumericUpDown[] { numTimeout, numBudget, numMaxRuns, numJobs, numDevirt, numV14Budget, numChunkMin, numSpin, numPipeline })
                c.ValueChanged += (s, e) => MarkCustom();
            foreach (CheckBox c in new CheckBox[] { chkNoDevirt, chkNoHooks, chkNoFold, chkStrings, chkKeepHarness, chkKeepPreamble, chkDebug, chkDetect, chkV14Deep, chkSpinLate, chkDumpTrace, chkDebugPartial })
                c.CheckedChanged += (s, e) => MarkCustom();
            foreach (Control c in new Control[] { txtExecutor, txtOutput, txtNode })
                c.TextChanged += (s, e) => MarkCustom();
        }

        NumericUpDown MkNum(float w = 1)
        {
            return new NumericUpDown { Width = (int)(120 * w), Maximum = 999999 };
        }

        void AddNumRow(TableLayoutPanel t, int row, string label, NumericUpDown num, decimal def = 0)
        {
            num.Value = def;
            t.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            t.Controls.Add(num, 1, row);
        }

        void AddTxtRow(TableLayoutPanel t, int row, string label, Control txt)
        {
            t.Controls.Add(new Label { Text = label, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var pw = new Panel { Dock = DockStyle.Fill };
            pw.Controls.Add(txt); txt.Dock = DockStyle.Fill; txt.Width = 180;
            t.Controls.Add(pw, 1, row);
        }

        void BuildLog(Panel p)
        {
            var gLog = new GroupBox { Text = "Журнал запусков", Dock = DockStyle.Fill };
            p.Controls.Add(gLog);

            lstJobs = new ListView { Dock = DockStyle.Top, Height = 84, View = View.Details, FullRowSelect = true, Font = new Font("Segoe UI", 8.5f) };
            lstJobs.Columns.Add("#", 46);
            lstJobs.Columns.Add("Состояние", 110);
            lstJobs.Columns.Add("Команда / результат", 560);
            lstJobs.Columns.Add("Время", 120);
            gLog.Controls.Add(lstJobs);
            lstJobs.SelectedIndexChanged += (s, e) => ShowSelectedJob();

            richLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(8, 10, 14), ForeColor = Color.White, Font = new Font("Consolas", 9.5f), WordWrap = false, ScrollBars = RichTextBoxScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle, HideSelection = false, DetectUrls = false };
            gLog.Controls.Add(richLog);

            lblCur = new Label { Text = "", Dock = DockStyle.Bottom, Height = 20, ForeColor = Color.Silver, BackColor = Color.FromArgb(30, 32, 38) };
            gLog.Controls.Add(lblCur);

            lblCount = new Label { Text = "", Dock = DockStyle.Bottom, Height = 20, ForeColor = Color.Green };
            gLog.Controls.Add(lblCount);
        }

        string FindNode()
        {
            try
            {
                var ps = new ProcessStartInfo("where.exe", "node") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(ps))
                {
                    var line = p.StandardOutput.ReadLine();
                    p.WaitForExit(3000);
                    if (!string.IsNullOrEmpty(line) && File.Exists(line)) return line;
                }
            }
            catch { }
            var candidates = new string[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs", "node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "nodejs", "node.exe")
            };
            foreach (var c in candidates) if (File.Exists(c)) return c;
            return "node";
        }

        void MarkCustom()
        {
            if (syncing) return;
            if (cmbPreset != null && cmbPreset.SelectedIndex != 3) cmbPreset.SelectedIndex = 3;
        }

        void SetNum(NumericUpDown n, string v, float d)
        {
            float x;
            n.Value = float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) && x >= 0 ? (decimal)x : (decimal)d;
        }

        void LoadSettings()
        {
            syncing = true;
            try
            {
                if (!File.Exists(SettingsPath)) return;
                foreach (var line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    settings[line.Substring(0, i)] = line.Substring(i + 1);
                }
                Func<string, string> get = (k) => { string v; return settings.TryGetValue(k, out v) ? v : null; };

                var pf = Path.GetDirectoryName(Application.StartupPath) + "\\..\\sample";
                txtDefDir.Text = get("defdir") ?? (Directory.Exists(pf) ? Path.GetFullPath(pf) : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Downloads");
                txtNode.Text = get("node") ?? FindNode();
                txtOutput.Text = get("out") ?? "";
                txtExecutor.Text = get("exec") ?? "Wave";
                SetNum(numTimeout, get("timeout"), 90);
                SetNum(numBudget, get("budget"), 30);
                SetNum(numMaxRuns, get("maxruns"), 12);
                SetNum(numJobs, get("jobs"), 1);
                SetNum(numDevirt, get("devirt"), 200);
                SetNum(numV14Budget, get("v14budget"), 0);
                SetNum(numChunkMin, get("chunkmin"), 0);
                SetNum(numSpin, get("spin"), 0);
                SetNum(numPipeline, get("pipeline"), 0);
                Func<string, bool> b = (k) => get(k) == "1";
                chkNoDevirt.Checked = b("nodevirt"); chkNoHooks.Checked = b("nohooks"); chkNoFold.Checked = b("nofold");
                chkStrings.Checked = b("strings"); chkKeepHarness.Checked = b("keepharness"); chkKeepPreamble.Checked = b("keeppreamble");
                chkDebug.Checked = b("debug"); chkDetect.Checked = b("detect");
                chkV14Deep.Checked = b("v14deep"); chkSpinLate.Checked = b("spinlate"); chkDumpTrace.Checked = b("dumptrace"); chkDebugPartial.Checked = b("debugpartial");
                var selstr = get("preset");
                int idx = selstr == null ? 0 : Math.Max(0, cmbPreset.Items.IndexOf(selstr));
                cmbPreset.SelectedIndex = idx < 0 ? 0 : idx;
                if (get("preset") == null && get("set") == null) cmbPreset.SelectedIndex = 0;
            }
            catch { }
            syncing = false;
        }

        void SaveSettings()
        {
            try
            {
                var sb = new StringBuilder();
                Action<string, string> w = (k, v) => sb.Append(k).Append('=').AppendLine(v);
                w("defdir", txtDefDir.Text);
                w("node", txtNode.Text);
                w("out", txtOutput.Text);
                sb.Append("exec=").AppendLine(txtExecutor.Text);
                sb.Append("timeout=").AppendLine(numTimeout.Value.ToString());
                sb.Append("budget=").AppendLine(numBudget.Value.ToString());
                sb.Append("maxruns=").AppendLine(numMaxRuns.Value.ToString());
                sb.Append("jobs=").AppendLine(numJobs.Value.ToString());
                sb.Append("devirt=").AppendLine(numDevirt.Value.ToString());
                sb.Append("v14budget=").AppendLine(numV14Budget.Value.ToString());
                sb.Append("chunkmin=").AppendLine(numChunkMin.Value.ToString());
                sb.Append("spin=").AppendLine(numSpin.Value.ToString());
                sb.Append("pipeline=").AppendLine(numPipeline.Value.ToString());
                Action<string, bool> cb = (k, v) => sb.Append(k).Append('=').AppendLine(v ? "1" : "0");
                cb("nodevirt", chkNoDevirt.Checked); cb("nohooks", chkNoHooks.Checked); cb("nofold", chkNoFold.Checked);
                cb("strings", chkStrings.Checked); cb("keepharness", chkKeepHarness.Checked); cb("keeppreamble", chkKeepPreamble.Checked);
                cb("debug", chkDebug.Checked); cb("detect", chkDetect.Checked);
                cb("v14deep", chkV14Deep.Checked); cb("spinlate", chkSpinLate.Checked); cb("dumptrace", chkDumpTrace.Checked); cb("debugpartial", chkDebugPartial.Checked);
                /* preset+size marks */
                File.WriteAllText(SettingsPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        void ApplyPreset()
        {
            if (cmbPreset == null) return;
            syncing = true;
            try
            {
            switch (cmbPreset.SelectedIndex)
            {
                case 0:
                    numTimeout.Value = 90; numBudget.Value = 30; numMaxRuns.Value = 12; numJobs.Value = 1; numDevirt.Value = 200;
                    chkNoDevirt.Checked = false; chkNoHooks.Checked = false; chkNoFold.Checked = false; chkStrings.Checked = false;
                    chkKeepHarness.Checked = false; chkKeepPreamble.Checked = false; chkDebug.Checked = false; chkDetect.Checked = false;
                    chkV14Deep.Checked = false; chkSpinLate.Checked = false; chkDumpTrace.Checked = false; chkDebugPartial.Checked = false;
                    numV14Budget.Value = 0; numChunkMin.Value = 0; numSpin.Value = 0; numPipeline.Value = 0;
                    break;
                case 1:
                    numTimeout.Value = 600; numBudget.Value = 600; numMaxRuns.Value = 24; numJobs.Value = 2; numDevirt.Value = 400;
                    chkNoDevirt.Checked = false; chkNoHooks.Checked = false; chkNoFold.Checked = false; chkStrings.Checked = true;
                    chkKeepHarness.Checked = false; chkKeepPreamble.Checked = false; chkDebug.Checked = true; chkDetect.Checked = false;
                    chkV14Deep.Checked = true; chkSpinLate.Checked = false; chkDumpTrace.Checked = false; chkDebugPartial.Checked = false;
                    numV14Budget.Value = 1200; numChunkMin.Value = 0; numSpin.Value = 0; numPipeline.Value = 3600;
                    break;
                case 2:
                    numTimeout.Value = 1800; numBudget.Value = 1800; numMaxRuns.Value = 48; numJobs.Value = 4; numDevirt.Value = 800;
                    chkNoDevirt.Checked = false; chkNoHooks.Checked = false; chkNoFold.Checked = false; chkStrings.Checked = true;
                    chkKeepHarness.Checked = false; chkKeepPreamble.Checked = false; chkDebug.Checked = true; chkDetect.Checked = false;
                    chkV14Deep.Checked = true; chkSpinLate.Checked = false; chkDumpTrace.Checked = true; chkDebugPartial.Checked = false;
                    numV14Budget.Value = 4000; numChunkMin.Value = 512; numSpin.Value = 4096; numPipeline.Value = 7200;
                    break;
            }
            }
            catch { }
            syncing = false;
            UpdateCommandPreview();
        }

        void AddFilesDialog()
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Lua / Luau / Text (*.lua;*.luau;*.txt)|*.lua;*.luau;*.txt|Все файлы (*.*)|*.*",
                Multiselect = true,
                Title = "Выберите файлы для деобфускации",
                CheckFileExists = true
            };
            ofd.InitialDirectory = Directory.Exists(txtDefDir.Text) ? txtDefDir.Text : Application.StartupPath;
            if (ofd.ShowDialog(this) == DialogResult.OK)
            {
                foreach (var f in ofd.FileNames) AddFileItem(f);
                if (ofd.FileNames.Length > 0) txtDefDir.Text = Path.GetDirectoryName(ofd.FileNames[0]);
            }
        }

        void AddFolderDialog()
        {
            var fbd = new FolderBrowserDialog { Description = "Выберите папку с .lua / .luau / .txt файлами", SelectedPath = Directory.Exists(txtDefDir.Text) ? txtDefDir.Text : Application.StartupPath };
            if (fbd.ShowDialog(this) == DialogResult.OK)
            {
                AddFileItem(fbd.SelectedPath);
                txtDefDir.Text = fbd.SelectedPath;
            }
        }

        void AddSample()
        {
            var s = Path.Combine(ExeDir, "sample");
            if (Directory.Exists(s)) AddFileItem(s);
            else if (ExeDir.EndsWith("\\") || ExeDir.EndsWith("/")) { }
        }

        void AddFileItem(string p)
        {
            foreach (object o in lstFiles.Items) if (string.Equals((string)o, p, StringComparison.OrdinalIgnoreCase)) return;
            lstFiles.Items.Add(p);
        }

        void RemoveSelectedFiles()
        {
            var sel = new List<object>();
            foreach (object o in lstFiles.SelectedItems) sel.Add(o);
            foreach (var o in sel) lstFiles.Items.Remove(o);
        }

        List<string> CollectInputs()
        {
            var list = new List<string>();
            foreach (object o in lstFiles.Items) list.Add((string)o);
            return list;
        }

        void AppendLog(JobRec j, string line)
        {
            j.Log.Add(line);
            if (j != current || !IsHandleCreated) return;
            BeginInvoke(new Action(() =>
            {
                Color c = Color.White;
                if (line.StartsWith(">")) c = Color.Orange;
                else if (line.StartsWith("[+]")) c = Color.LawnGreen;
                else if (line.StartsWith("[!]")) c = Color.IndianRed;
                else if (line.StartsWith("[*]") || line.StartsWith("[i]")) c = Color.SkyBlue;
                richLog.SelectionStart = richLog.TextLength;
                richLog.SelectionColor = c;
                richLog.AppendText(line + "\n");
                richLog.SelectionStart = richLog.TextLength;
                richLog.ScrollToCaret();
            }));
        }

        void SetRunning(JobRec j, string state)
        {
            j.State = state;
            if (state == "done") { lblStatus.Text = "готово";
                lastResultPath = j.Result; }
            else if (state == "error") lblStatus.Text = "ошибка";
            else if (state == "cancelled") lblStatus.Text = "остановлено";
            else if (state == "no-file") lblStatus.Text = "нет файла";
            else lblStatus.Text = "работает…";
            if (j.Item != null)
            {
                var st = j.Item.SubItems[1];
                st.Text = state == "done" ? "готово" : state == "error" ? "ошибка" : state == "cancelled" ? "отменено" : state == "no-file" ? "нет файла" : "работает";
                var clr = state == "done" ? Color.DarkGreen : state == "error" ? Color.Firebrick : state == "cancelled" ? Color.DarkGoldenrod : Color.DodgerBlue;
                st.ForeColor = clr;
            }
            if (j == current) lblCount.Text = jobs.Count + " заданий · линии " + j.Log.Count;
        }

        List<string> BuildArgs()
        {
            var args = new List<string>();
            foreach (var inp in CollectInputs()) args.Add(inp);
            if (rbPaste.Checked && txtPaste.Text.Trim().Length > 0)
            {
                var tmp = Path.Combine(Path.GetTempPath(), "deob_paste_" + DateTime.Now.Ticks + ".lua");
                File.WriteAllText(tmp, txtPaste.Text, Encoding.UTF8);
                args.Add(tmp);
            }
            if (chkDetect.Checked) args.Add("--detect");
            if (chkNoDevirt.Checked) args.Add("--no-devirt");
            if (chkNoHooks.Checked) args.Add("--no-hooks");
            if (chkNoFold.Checked) args.Add("--no-fold");
            if (chkStrings.Checked) args.Add("--strings");
            if (chkKeepHarness.Checked) args.Add("--keep-harness");
            if (chkKeepPreamble.Checked) args.Add("--keep-preamble");
            if (chkDebug.Checked) args.Add("--debug");
            args.Add("--timeout"); args.Add(((int)numTimeout.Value).ToString());
            args.Add("--budget"); args.Add(((int)numBudget.Value).ToString());
            args.Add("--max-runs"); args.Add(((int)numMaxRuns.Value).ToString());
            args.Add("--jobs"); args.Add(((int)numJobs.Value).ToString());
            args.Add("--devirt-rounds"); args.Add(((int)numDevirt.Value).ToString());
            if (txtExecutor.Text.Trim().Length > 0 && txtExecutor.Text.Trim() != "Wave") { args.Add("--executor"); args.Add(txtExecutor.Text.Trim()); }
            if (txtOutput.Text.Trim().Length > 0) { args.Add("-o"); args.Add(txtOutput.Text.Trim()); }
            return args;
        }

        Dictionary<string, string> BuildEnv()
        {
            var env = new Dictionary<string, string>();
            if (chkV14Deep.Checked) env["DEOB_V14_DEEP"] = "1";
            if (numV14Budget.Value > 0) env["DEOB_V14_DECODE_BUDGET"] = ((int)numV14Budget.Value).ToString();
            if (numChunkMin.Value > 0) env["DEOB_CHUNK_MIN"] = ((int)numChunkMin.Value).ToString();
            if (numSpin.Value > 0) env["DEOB_SPIN"] = ((int)numSpin.Value).ToString();
            if (numPipeline.Value > 0) env["DEOB_PIPELINE_TIMEOUT"] = ((int)numPipeline.Value).ToString();
            if (chkSpinLate.Checked) env["DEOB_SPIN_LATE"] = "1";
            if (chkDumpTrace.Checked) env["DEOB_DUMP_TRACE"] = "1";
            if (chkDebugPartial.Checked) env["DEOB_DEBUG_PARTIAL"] = "1";
            return env;
        }

        string CommandPreview()
        {
            var sb = new StringBuilder();
            sb.Append("node ").Append(Quote(DeobJs));
            foreach (var a in BuildArgs()) sb.Append(' ').Append(Quote(a));
            var env = BuildEnv();
            if (env.Count > 0) { sb.Append("    # env: "); sb.Append(string.Join(", ", env)); }
            return sb.ToString();
        }

        void UpdateCommandPreview()
        {
            if (txtCmd != null) txtCmd.Text = CommandPreview();
        }

        void CopyCommand()
        {
            var s = CommandPreview();
            try { Clipboard.SetText(s); } catch { }
            txtCmd.Text = s;
        }

        void OpenResultFolder()
        {
            var t = lastResultPath;
            if (!string.IsNullOrEmpty(t) && (File.Exists(t) || Directory.Exists(t)))
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + t + "\"") { UseShellExecute = true }); return; }
                catch { }
            }
            var def = txtDefDir.Text;
            if (Directory.Exists(def)) try { Process.Start("explorer.exe", "\"" + def + "\""); } catch { }
        }

        void CancelJob()
        {
            if (current != null && running && current.Proc != null)
            {
                try { current.Proc.Kill(); } catch { }
            }
        }

        async Task StartJobAsync()
        {
            if (running) { CancelJob(); return; }
            if (chkDetect.Checked && CollectInputs().Count == 0 && (!rbPaste.Checked || txtPaste.Text.Trim().Length == 0))
            {
                MessageBox.Show(this, "Выберите хотя бы один файл/папку или вставьте текст.", "Deobfuscator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!chkDetect.Checked && CollectInputs().Count == 0 && (!rbPaste.Checked || txtPaste.Text.Trim().Length == 0))
            {
                MessageBox.Show(this, "Выберите хотя бы один файл/папку или вставьте текст для деобфускации.", "Deobfuscator", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!File.Exists(DeobJs))
            {
                MessageBox.Show(this, "Не найден deob.js рядом с программой:\n" + DeobJs, "Deobfuscator", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            var args = BuildArgs();
            var env = BuildEnv();
            var job = new JobRec { Id = (jobSeq++).ToString(), ArgsText = CommandPreview() };
            jobs.Add(job);
            lstJobs.BeginUpdate();
            job.Item = new ListViewItem(job.Id);
            job.Item.SubItems.Add("работа");
            job.Item.SubItems.Add(args.Count > 0 ? args[0] : "—");
            job.Item.SubItems.Add(DateTime.Now.ToString("HH:mm:ss"));
            lstJobs.Items.Insert(0, job.Item);
            lstJobs.EndUpdate();
            current = job;
            running = true;
            btnRun.Enabled = false; btnCancel.Enabled = true;
            swJob = Stopwatch.StartNew();
            lblCount.Text = jobs.Count + " заданий · линии 0";

            AppendLog(job, "> " + CommandPreview());

            var psi = new ProcessStartInfo
            {
                FileName = txtNode.Text,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = ExeDir,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            var cmd = new StringBuilder(Quote(DeobJs));
            foreach (var a in args) cmd.Append(' ').Append(Quote(a));
            psi.Arguments = cmd.ToString();
            foreach (var kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;

            try
            {
                using (var pr = new Process { StartInfo = psi })
                {
                    job.Proc = pr;
                    pr.OutputDataReceived += (s, e) => { if (e.Data != null) { OnLine(job, e.Data); foreach (Match m in Regex.Matches(e.Data, @"\[\+\]\s*result:\s*(\S.*)$")) job.Result = m.Groups[1].Value.Trim(); } };
                    pr.ErrorDataReceived += (s, e) => { if (e.Data != null) OnLine(job, e.Data); };
                    pr.Start();
                    pr.BeginOutputReadLine();
                    pr.BeginErrorReadLine();
                    await Task.Run(() => pr.WaitForExit());
                    var code = pr.ExitCode;
                    if (job.Proc != null) { job.Proc = null; }
                    string state;
                    if (code == 0) { state = "done"; }
                    else if (code == 2) { state = "no-file"; }
                    else { state = "error"; }
                    SetRunning(job, state);
                }
            }
            catch (Exception ex)
            {
                OnLine(job, "[!] не удалось запустить: " + ex.Message);
                SetRunning(job, "error");
            }
            running = false;
            btnRun.Enabled = true; btnCancel.Enabled = false;
            lblStatus.Text = lblStatus.Text;
            UpdateCommandPreview();
            SaveSettings();
        }

        void OnLine(JobRec j, string line)
        {
            if (line.EndsWith("\r")) line = line.Substring(0, line.Length - 1);
            AppendLog(j, line);
            if (line.StartsWith("[+] result:"))
            {
                j.Result = line.Substring(line.IndexOf(':') + 1).Trim();
                if (j != current) { }
            }
        }

        void ShowSelectedJob()
        {
            if (lstJobs.SelectedIndices.Count != 1) return;
            var idx = lstJobs.Items.Count - 1 - lstJobs.SelectedIndices[0];
            if (idx < 0 || idx >= jobs.Count) return;
            var j = jobs[idx];
            richLog.Clear();
            foreach (var l in j.Log)
            {
                Color c = Color.White;
                if (l.StartsWith(">")) c = Color.Orange;
                else if (l.StartsWith("[+]")) c = Color.LawnGreen;
                else if (l.StartsWith("[!]")) c = Color.IndianRed;
                else if (l.StartsWith("[*]") || l.StartsWith("[i]")) c = Color.SkyBlue;
                richLog.SelectionColor = c;
                richLog.AppendText(l + "\n");
            }
            richLog.SelectionStart = richLog.TextLength; richLog.ScrollToCaret();
            lblCount.Text = jobs.Count + " заданий · показ #" + j.Id + " (" + j.State + ")";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (running && current != null && current.Proc != null)
            {
                var r = MessageBox.Show(this, "Задание ещё выполняется. Остановить и выйти?", "Deobfuscator", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (r == DialogResult.OK) { try { current.Proc.Kill(); } catch { } }
                else { e.Cancel = true; return; }
            }
            SaveSettings();
            base.OnFormClosing(e);
        }

        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--run")
            {
                CliMode(args);
                return;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        static void CliMode(string[] args)
        {
            var node = "node";
            var deob = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "deob.js");
            var runningArgs = new List<string>();
            string outFinal = null;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "-o" && i + 1 < args.Length)
                {
                    outFinal = args[++i];
                    runningArgs.Add("-o");
                    runningArgs.Add(outFinal);
                }
                else runningArgs.Add(args[i]);
            }
            var psi = new ProcessStartInfo
            {
                FileName = node,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            var cmd = new StringBuilder(Quote(deob));
            foreach (var a in runningArgs) cmd.Append(' ').Append(Quote(a));
            psi.Arguments = cmd.ToString();
            try
            {
                using (var p = Process.Start(psi))
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };
                    p.BeginOutputReadLine(); p.BeginErrorReadLine();
                    p.WaitForExit();
                    Environment.ExitCode = p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[!] " + ex.Message);
                Environment.ExitCode = 1;
            }
        }
    }
}