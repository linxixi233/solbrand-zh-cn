using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SolbrandZh {
    static class Program {
        [STAThread]
        static void Main(string[] args) {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try {
                var engine = PatchEngine.Embedded();
                string folder = args.Length == 2 && args[0] == "--folder" ? args[1] : FindGame();
                Application.Run(new MainForm(engine, folder));
            } catch (Exception e) {
                MessageBox.Show("工具未能启动：" + PatchEngine.Friendly(e), "Solbrand 汉化助手",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        static string FindGame() {
            var choices = new List<string>();
            try {
                var config = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Solbrand-ZH.path.txt");
                if (File.Exists(config)) choices.Add(File.ReadAllText(config, Encoding.UTF8).Trim());
            } catch (IOException) { }
            choices.Add(AppDomain.CurrentDomain.BaseDirectory);
            choices.Add(@"D:\SteamLibrary\steamapps\common\Solbrand Demo");
            var steam = new List<string>();
            foreach (var key in new[] { @"HKEY_CURRENT_USER\Software\Valve\Steam", @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam" }) {
                var value = Registry.GetValue(key, key.Contains("CURRENT_USER") ? "SteamPath" : "InstallPath", null) as string;
                if (!String.IsNullOrEmpty(value)) steam.Add(value);
            }
            foreach (var root in steam.ToArray()) {
                try {
                    var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
                    if (File.Exists(vdf)) foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s*\"([^\"]+)\""))
                        steam.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
                } catch (IOException) { }
            }
            choices.AddRange(steam.Select(x => Path.Combine(x, "steamapps", "common", "Solbrand Demo")));
            return choices.FirstOrDefault(x => !String.IsNullOrWhiteSpace(x) && File.Exists(Path.Combine(x, "Solbrand.exe"))
                && File.Exists(Path.Combine(x, "Solbrand.pck"))) ?? "";
        }
    }
    sealed class MainForm : Form {
        readonly PatchEngine engine;
        readonly TextBox folder = new TextBox(), log = new TextBox();
        readonly PatchButton browse = new PatchButton(), install = new PatchButton(), restore = new PatchButton(), launch = new PatchButton(), refresh = new PatchButton();
        readonly Label statusTitle = new Label(), statusDetail = new Label(), step = new Label();
        readonly Label installHint = new Label();
        readonly Label statusBadge = new Label();
        readonly SurfacePanel card = new SurfacePanel();
        readonly ThinProgress progress = new ThinProgress();
        readonly Timer debounce = new Timer { Interval = 450 };
        readonly Timer gameMonitor = new Timer { Interval = 2500 };
        readonly Color ink = Palette.Ink, muted = Palette.Muted;
        bool busy, scanning;
        int scanTicket;
        StatusInfo status;
        string lastError = "";
        bool gameRunning;
        public MainForm(PatchEngine patch, string initialFolder) {
            engine = patch;
            Text = "Solbrand 汉化助手 1.4.1 · 非商业学习版";
            Font = new Font("Microsoft YaHei UI", 9.5F);
            BackColor = Palette.Paper; ForeColor = ink;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(900, 700); MinimumSize = new Size(916, 739);
            StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false;
            using (var stream = typeof(MainForm).Assembly.GetManifestResourceStream("patch.icon")) {
                using (var icon = new Icon(stream)) Icon = (Icon)icon.Clone();
            }
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = new Padding(0), ColumnCount = 1, RowCount = 2 };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(root);

            var header = new BrandPanel { Dock = DockStyle.Fill, Margin = new Padding(0) };
            var edition = LabelAt("S O L B R A N D   /   简体中文 · DEMO", 28, 19, 630, 22, 8.5F, FontStyle.Regular);
            edition.ForeColor = Color.FromArgb(148, 193, 195);
            var title = LabelAt("Solbrand 汉化助手", 25, 43, 650, 43, 24F, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(245, 250, 250);
            var tagline = LabelAt("让故事，以熟悉的语言继续。", 28, 91, 630, 23, 9F, FontStyle.Regular);
            tagline.ForeColor = Color.FromArgb(175, 198, 207);
            header.Controls.Add(edition); header.Controls.Add(title); header.Controls.Add(tagline);
            root.Controls.Add(header, 0, 0);

            var body = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26, 12, 26, 14), ColumnCount = 1, RowCount = 8, Margin = new Padding(0) };
            foreach (var h in new[] { 40F, 100F, 110F, 84F, 42F, 48F }) body.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); body.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            root.Controls.Add(body, 0, 1);
            body.Controls.Add(new StepStrip { Dock = DockStyle.Fill, Font = new Font(Font.FontFamily, 9F), Margin = new Padding(0) }, 0, 0);

            var location = new SurfacePanel { Dock = DockStyle.Fill, Padding = new Padding(15, 9, 15, 12), Margin = new Padding(0, 0, 0, 12) };
            var locationGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.Transparent, Margin = new Padding(0) };
            locationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            locationGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            locationGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); locationGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var folderHeading = new Label { Text = "游戏安装位置", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, BackColor = Color.Transparent, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0) };
            var autoHint = new Label { Text = "支持自动识别", Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopRight, ForeColor = muted, BackColor = Color.Transparent, Font = new Font(Font.FontFamily, 8.5F), Margin = new Padding(0) };
            locationGrid.Controls.Add(folderHeading, 0, 0); locationGrid.Controls.Add(autoHint, 1, 0);
            var pathFrame = new SurfacePanel { Dock = DockStyle.Fill, FillColor = Palette.Paper, BackColor = Color.White, Radius = 7, Padding = new Padding(10, 7, 10, 3), Margin = new Padding(0, 0, 10, 0) };
            folder.Dock = DockStyle.Fill; folder.BorderStyle = BorderStyle.None; folder.BackColor = Palette.Paper; folder.Font = new Font("Microsoft YaHei UI", 9F);
            folder.Margin = new Padding(0); folder.Text = initialFolder; folder.AccessibleName = "游戏安装位置"; folder.TabIndex = 0;
            pathFrame.Controls.Add(folder);
            browse.Text = "选择文件夹"; StyleButton(browse, false); browse.Dock = DockStyle.Fill; browse.Margin = new Padding(0); browse.BackColor = Color.White; browse.TabIndex = 1;
            locationGrid.Controls.Add(pathFrame, 0, 1); locationGrid.Controls.Add(browse, 1, 1); location.Controls.Add(locationGrid);
            body.Controls.Add(location, 0, 1);

            card.Dock = DockStyle.Fill; card.FillColor = Color.FromArgb(234, 244, 244); card.LineColor = Color.FromArgb(209, 229, 227); card.Padding = new Padding(17, 12, 17, 12); card.Margin = new Padding(0, 0, 0, 12);
            var statusGrid = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2, BackColor = Color.Transparent, Margin = new Padding(0) };
            statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); statusGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            statusGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 29)); statusGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            statusTitle.Text = "正在检查游戏…"; statusTitle.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold); statusTitle.Dock = DockStyle.Fill;
            statusTitle.Margin = new Padding(0); statusTitle.BackColor = Color.Transparent;
            statusBadge.Text = "检测中"; statusBadge.Dock = DockStyle.Fill; statusBadge.TextAlign = ContentAlignment.MiddleRight; statusBadge.ForeColor = Palette.Teal;
            statusBadge.BackColor = Color.Transparent; statusBadge.Font = new Font(Font.FontFamily, 9F, FontStyle.Bold); statusBadge.Margin = new Padding(0, 0, 0, 3);
            statusDetail.Text = "正在读取游戏与补丁版本，请稍候。"; statusDetail.Dock = DockStyle.Fill; statusDetail.ForeColor = muted; statusDetail.BackColor = Color.Transparent; statusDetail.Margin = new Padding(0, 3, 0, 0);
            statusGrid.Controls.Add(statusTitle, 0, 0); statusGrid.Controls.Add(statusBadge, 1, 0); statusGrid.Controls.Add(statusDetail, 0, 1); statusGrid.SetColumnSpan(statusDetail, 2); card.Controls.Add(statusGrid);
            body.Controls.Add(card, 0, 2);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0) };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            actions.RowStyles.Add(new RowStyle(SizeType.Absolute, 50)); actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            install.Text = "一键汉化"; restore.Text = "回退原版";
            StyleButton(install, true); StyleButton(restore, false);
            install.Dock = DockStyle.Fill; restore.Dock = DockStyle.Fill;
            install.Margin = new Padding(0, 0, 7, 0); restore.Margin = new Padding(7, 0, 0, 0); install.TabIndex = 2; restore.TabIndex = 3;
            actions.Controls.Add(install, 0, 0); actions.Controls.Add(restore, 1, 0);
            installHint.Text = "自动备份原版 · 安装后即可启动游戏"; installHint.Dock = DockStyle.Fill; installHint.ForeColor = muted;
            installHint.Font = new Font(Font.FontFamily, 8.5F); installHint.Padding = new Padding(1, 7, 0, 0); installHint.Margin = new Padding(0);
            actions.Controls.Add(installHint, 0, 1);
            actions.Controls.Add(new Label { Text = "恢复原版文件，保留存档", Dock = DockStyle.Fill, ForeColor = muted, Font = new Font(Font.FontFamily, 8.5F), Padding = new Padding(8, 7, 0, 0), Margin = new Padding(0) }, 1, 1);
            body.Controls.Add(actions, 0, 3);

            var progressGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(0, 9, 0, 0), Margin = new Padding(0) };
            progressGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 5)); progressGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            progress.Dock = DockStyle.Fill; progress.Margin = new Padding(0);
            step.Text = "准备就绪"; step.Dock = DockStyle.Fill; step.ForeColor = muted; step.Padding = new Padding(0, 7, 0, 0); step.Font = new Font(Font.FontFamily, 8.5F); step.Margin = new Padding(0);
            progressGrid.Controls.Add(progress, 0, 0); progressGrid.Controls.Add(step, 0, 1); body.Controls.Add(progressGrid, 0, 5);

            var history = new SurfacePanel { Dock = DockStyle.Fill, FillColor = Color.FromArgb(237, 242, 245), LineColor = Color.FromArgb(237, 242, 245), Padding = new Padding(12, 10, 12, 8), Margin = new Padding(0) };
            var historyGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Margin = new Padding(0) };
            historyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72)); historyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            historyGrid.Controls.Add(new Label { Text = "操作记录", Dock = DockStyle.Fill, BackColor = Color.Transparent, ForeColor = muted, Font = new Font(Font.FontFamily, 8.5F, FontStyle.Bold), Margin = new Padding(0) }, 0, 0);
            log.Dock = DockStyle.Fill; log.ReadOnly = true; log.Multiline = true; log.ScrollBars = ScrollBars.Vertical;
            log.BackColor = history.FillColor; log.ForeColor = muted; log.BorderStyle = BorderStyle.None; log.TabStop = false; log.Margin = new Padding(0);
            log.Font = new Font(Font.FontFamily, 9F);
            log.Text = "确认目录后，点击上方主按钮安装或更新汉化。\r\n需要恢复时，退出游戏后点击“回退原版”。";
            historyGrid.Controls.Add(log, 1, 0); history.Controls.Add(historyGrid); body.Controls.Add(history, 0, 6);
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0), Padding = new Padding(0, 10, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220));
            footer.Controls.Add(new Label { Text = "非官方汉化 · 非商业性，仅供学习用途", Dock = DockStyle.Fill, ForeColor = muted, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Font.FontFamily, 9F), Margin = new Padding(0) }, 0, 0);
            footer.Controls.Add(new Label { Text = "助手 1.4.1  /  补丁 " + engine.Manifest.version, Dock = DockStyle.Fill, ForeColor = muted, TextAlign = ContentAlignment.MiddleRight, Font = new Font(Font.FontFamily, 8.5F), Margin = new Padding(0) }, 1, 0);
            body.Controls.Add(footer, 0, 7);

            var nextActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
            launch.Text = "在 Steam 中启动游戏"; StyleButton(launch, false); launch.Size = new Size(238, 34); launch.Margin = new Padding(0, 0, 10, 0); launch.TabIndex = 4;
            refresh.Text = "重新检测"; StyleButton(refresh, false); refresh.Size = new Size(108, 34); refresh.Quiet = true; refresh.Margin = new Padding(0); refresh.TabIndex = 5;
            nextActions.Controls.Add(launch); nextActions.Controls.Add(refresh); body.Controls.Add(nextActions, 0, 4);
            launch.Click += delegate {
                try { Process.Start(new ProcessStartInfo("steam://rungameid/4826310") { UseShellExecute = true }); }
                catch (Exception e) { lastError = "未能打开 Steam，请从 Steam 客户端启动游戏。\r\n" + e.Message; Scan(); }
            };
            refresh.Click += delegate { lastError = ""; Scan(); };

            folder.TextChanged += delegate { if (!busy) { scanTicket++; lastError = ""; status = null; SetButtons(); debounce.Stop(); debounce.Start(); } };
            debounce.Tick += delegate { debounce.Stop(); Scan(); };
            browse.Click += delegate {
                using (var dialog = new FolderBrowserDialog { Description = "选择包含 Solbrand.exe 的游戏安装文件夹", ShowNewFolderButton = false }) {
                    if (Directory.Exists(folder.Text)) dialog.SelectedPath = folder.Text;
                    if (dialog.ShowDialog(this) == DialogResult.OK) { folder.Text = dialog.SelectedPath; debounce.Stop(); Scan(); }
                }
            };
            install.Click += async delegate { await Run(false); };
            restore.Click += async delegate { await Run(true); };
            Shown += delegate { Scan(); gameMonitor.Start(); };
            gameMonitor.Tick += delegate { if (!busy && gameRunning != PatchEngine.GameIsRunning()) Scan(); };
            FormClosed += delegate { debounce.Dispose(); gameMonitor.Dispose(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) {
                if (busy) { e.Cancel = true; step.Text = "正在处理文件，请等操作完成后再关闭。"; }
            };
        }
        Label LabelAt(string text, int x, int y, int w, int h, float size, FontStyle style) {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(w, h),
                Font = new Font(Font.FontFamily, size, style), ForeColor = style == FontStyle.Bold ? ink : muted, BackColor = Color.Transparent };
        }
        void StyleButton(PatchButton b, bool primary) {
            b.Primary = primary; b.ForeColor = primary ? Color.White : ink;
            b.Font = new Font(Font.FontFamily, primary ? 12F : 10F, FontStyle.Bold);
            b.Cursor = Cursors.Hand; b.UseVisualStyleBackColor = false;
        }
        string SelectedFolder() { return folder.Text.Trim().Trim('"'); }
        async void Scan() {
            if (busy) return;
            var ticket = ++scanTicket; var path = SelectedFolder(); scanning = true; SetButtons();
            statusTitle.Text = "正在检查游戏…"; statusBadge.Text = "● 检测中";
            statusDetail.Text = "正在核对游戏文件与汉化补丁版本，请稍候。";
            StatusInfo result;
            try { result = await Task.Run(() => engine.Inspect(path)); }
            catch (Exception e) { result = new StatusInfo { Kind = GameStatus.Unsupported, Title = "请检查游戏路径", Detail = PatchEngine.Friendly(e) }; }
            if (IsDisposed || ticket != scanTicket) return;
            scanning = false;
            ApplyInspection(result, PatchEngine.GameIsRunning());
        }
        internal void ApplyInspection(StatusInfo result, bool running) {
            status = result;
            gameRunning = running;
            statusTitle.Text = result.Title;
            statusDetail.Text = gameRunning ? "游戏正在运行。请退出游戏，工具会自动重新检测并启用安装 / 更新 / 回退。" : result.Detail;
            if (!String.IsNullOrEmpty(lastError)) { statusTitle.Text = "上次操作未完成"; statusDetail.Text = lastError; }
            bool warning = gameRunning || !String.IsNullOrEmpty(lastError) || result.Kind == GameStatus.Unsupported || result.Kind == GameStatus.Recovery || result.Kind == GameStatus.Missing;
            card.FillColor = warning ? Color.FromArgb(252, 245, 232) : Color.FromArgb(234, 244, 241);
            card.LineColor = warning ? Color.FromArgb(239, 223, 191) : Color.FromArgb(208, 229, 221);
            statusBadge.ForeColor = warning ? Color.FromArgb(149, 102, 34) : Palette.Teal;
            statusBadge.Text = gameRunning ? "● 游戏运行中" : !String.IsNullOrEmpty(lastError) ? "● 需检查" : result.Kind == GameStatus.Patched ? "● 已完成" : result.Kind == GameStatus.UpdateAvailable ? "● 可更新" : result.Kind == GameStatus.Original ? "● 可安装" : "● 需检查";
            if (!busy) {
                progress.Value = result.Kind == GameStatus.Patched ? 100 : 0;
                step.Text = gameRunning ? "请先退出游戏，操作按钮会自动恢复。" : !String.IsNullOrEmpty(lastError) ? "操作未完成，请查看上方提示和操作记录。"
                    : result.Kind == GameStatus.Patched ? "汉化已就绪，可以从 Steam 启动游戏。"
                    : result.SteamUpdated ? "准备就绪 · 旧版备份将归档，新版原版将单独备份。"
                    : result.Kind == GameStatus.UpdateAvailable ? "准备就绪 · 更新会沿用现有的原版备份。"
                    : result.Kind == GameStatus.Original ? "准备就绪 · 安装前将自动备份原版。" : "请根据上方提示检查游戏目录及版本。";
            }
            SetButtons();
        }
        void SetButtons() {
            var available = !busy && !scanning && !gameRunning && status != null;
            install.Text = busy ? "正在处理，请稍候…" : status != null && status.Kind == GameStatus.UpdateAvailable ? "更新至 " + engine.Manifest.version : status != null && status.Kind == GameStatus.Patched ? "已安装最新汉化" : "一键安装汉化";
            installHint.Text = busy ? "正在校验游戏文件，请等待操作完成。" : gameRunning ? "游戏运行中 · 请先退出游戏" : status != null && status.Kind == GameStatus.Patched ? "汉化已就绪 · 点击下方 Steam 按钮启动" : "自动备份原版 · 安装后即可启动游戏";
            install.Enabled = available && (status.Kind == GameStatus.Original || status.Kind == GameStatus.UpdateAvailable);
            bool backup = false;
            try { backup = File.Exists(PatchEngine.BackupFile(SelectedFolder())); } catch (Exception) { }
            restore.Enabled = available && backup && !status.SteamUpdated;
            browse.Enabled = !busy; folder.Enabled = !busy;
            launch.Enabled = available && (status.Kind == GameStatus.Patched || status.Kind == GameStatus.Original);
            launch.Text = status != null && status.Kind == GameStatus.UpdateAvailable ? "更新完成后可启动游戏" : "在 Steam 中启动游戏";
            refresh.Enabled = !busy && !scanning;
        }
        async Task Run(bool undo) {
            if (busy) return;
            var selected = SelectedFolder();
            lastError = "";
            bool updating = status != null && status.Kind == GameStatus.UpdateAvailable;
            busy = true; scanTicket++; debounce.Stop(); SetButtons(); progress.Value = 0;
            statusTitle.Text = undo ? "正在回退原版…" : updating ? "正在更新汉化…" : "正在汉化…";
            statusBadge.Text = "● 处理中";
            statusDetail.Text = "正在校验并处理游戏文件，请稍候。";
            var resultText = "";
            engine.Progress = (value, text) => {
                if (IsDisposed) return;
                BeginInvoke((Action)delegate { progress.Value = Math.Max(0, Math.Min(100, value)); step.Text = text; });
                resultText = text;
            };
            bool success = false;
            try {
                await Task.Run(() => { if (undo) engine.Restore(selected); else engine.Install(selected); });
                success = true;
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Solbrand-ZH.path.txt"), selected, new UTF8Encoding(false)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                log.AppendText("\r\n" + DateTime.Now.ToString("HH:mm:ss") + "  " + resultText);
            } catch (Exception e) {
                resultText = PatchEngine.Friendly(e); step.Text = "操作未完成，请查看下方说明。";
                log.AppendText("\r\n" + DateTime.Now.ToString("HH:mm:ss") + "  " + resultText);
                log.SelectionStart = log.TextLength; log.ScrollToCaret();
                lastError = resultText;
            } finally {
                busy = false; scanning = false; Scan();
                if (success) step.Text = resultText;
            }
        }
    }
}
