using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Drawing;
using System.Windows.Forms;

namespace SolbrandZh {
    static class SelfTests {
        static int passed;
        static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
        static byte[] Data(string text) { return Encoding.UTF8.GetBytes(text); }
        static string Md5(byte[] data) { using (var h = MD5.Create()) return PatchEngine.Hex(h.ComputeHash(data)); }
        static void Reject(Action action, string message) {
            bool failed = false;
            try { action(); } catch (IOException) { failed = true; } catch (InvalidDataException) { failed = true; }
            Assert(failed, message);
        }
        static byte[] CreatePack(Dictionary<string, byte[]> files, uint version, long dataBase) {
            using (var stream = new MemoryStream()) {
                var header = new byte[dataBase];
                using (var w = new BinaryWriter(stream, Encoding.UTF8, true)) {
                    stream.Write(header, 0, header.Length);
                    stream.Position = 0; w.Write(0x43504447U); w.Write(version); w.Write(4U); w.Write(7U); w.Write(2U); w.Write(2U); w.Write((ulong)dataBase);
                    stream.Position = dataBase;
                    var pack = new Pack { Base = dataBase };
                    foreach (var file in files) {
                        while (stream.Position % 32 != 0) stream.WriteByte(0);
                        using (var h = MD5.Create()) pack.Entries.Add(new PackEntry { Name = file.Key, Offset = stream.Position,
                            Size = file.Value.Length, Hash = h.ComputeHash(file.Value), Flags = 0 });
                        stream.Write(file.Value, 0, file.Value.Length);
                    }
                    pack.WriteIndex(stream);
                }
                return stream.ToArray();
            }
        }
        static PatchEngine Fixture(string dir, string version = "0.2.0", bool writeOriginal = true, string buildSuffix = "") {
            Directory.CreateDirectory(dir);
            var original = CreatePack(new Dictionary<string, byte[]> {
                {"project.binary", Data("original settings" + buildSuffix)}, {PatchEngine.Story, Data("original story" + buildSuffix)},
                {"Data/中文路径/unchanged.bin", Enumerable.Repeat((byte)137, 4 * 1024 * 1024 + buildSuffix.Length * 128).ToArray()}
            }, 4, 112);
            var patch = CreatePack(new Dictionary<string, byte[]> {
                {"project.binary", Data("localized settings" + buildSuffix)}, {PatchEngine.Story, Data("中文剧情" + buildSuffix)},
                {"ZhPatch/zh_CN.json", Data("{\"Hello\":\"你好\"}")},
                {"ZhPatch/ZhPatch.gd", Data("extends Node # version " + version)}
            }, 3, 128);
            if (writeOriginal) {
                File.WriteAllBytes(Path.Combine(dir, "Solbrand.pck"), original);
                File.WriteAllText(Path.Combine(dir, "Solbrand.exe"), "TEST PLACEHOLDER: this file cannot launch a game");
                File.WriteAllText(Path.Combine(dir, "save-sentinel.dat"), "untouched save");
            }
            var engine = new PatchEngine(new PatchManifest { version = version, steamBuild = "fixture",
                sourcePackSize = original.Length, sourceProjectMd5 = Md5(Data("original settings" + buildSuffix)),
                sourceStoryMd5 = Md5(Data("original story" + buildSuffix)) }, patch);
            // Only workspace fixtures bypass detection of an unrelated running real game.
            engine.CheckGameClosed = delegate { };
            return engine;
        }
        static void Test(string title, Action test) { test(); passed++; Console.WriteLine("PASS " + title); }
        static string Child(string root, string name) { return Path.Combine(root, name); }
        static IEnumerable<Control> Descendants(Control control) {
            foreach(Control child in control.Controls){ yield return child; foreach(var nested in Descendants(child))yield return nested; }
        }
        static void RenderPreview(MainForm form, string path) {
            // Render a detached control tree offscreen; never show or drive a live window.
            using(var content=form.Controls[0]){
                content.Font=form.Font;content.BackColor=form.BackColor;content.ForeColor=form.ForeColor;
                form.Controls.Remove(content);content.Dock=DockStyle.None;content.Size=form.ClientSize;content.Visible=true;
                content.CreateControl();content.PerformLayout();
                foreach(var item in Descendants(content)){item.CreateControl();item.PerformLayout();}
                using(var image=new Bitmap(content.Width,content.Height)){
                    content.DrawToBitmap(image,new Rectangle(0,0,image.Width,image.Height));image.Save(path);
                }
            }
        }
        [STAThread]
        public static int Main(string[] args) {
            try {
                // All test writes stay beneath this workspace-owned, uniquely-created directory.
                var workspace = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
                var root = Path.GetFullPath(Path.Combine(workspace, "analysis", "installer-tests", Guid.NewGuid().ToString("N")));
                Assert(root.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Test path escaped workspace.");
                Directory.CreateDirectory(root);
                Test("assistant states, game-running lock, latest-version status and learning-use notice", delegate {
                    Application.EnableVisualStyles();
                    var engine = PatchEngine.Embedded();
                    var game = Environment.GetEnvironmentVariable("SOLBRAND_GAME_DIR") ?? "";
                    using(var form = new MainForm(engine, game)){
                        var original = new StatusInfo { Kind=GameStatus.Original, Title="当前是原版", Detail="可以安装汉化。" };
                        form.ApplyInspection(original,false);
                        Assert(Descendants(form).OfType<Button>().Any(b=>b.Text=="一键安装汉化"&&b.Enabled),"Original install action missing.");
                        form.ApplyInspection(original,true);
                        Assert(!Descendants(form).OfType<Button>().Any(b=>b.Text=="一键安装汉化"&&b.Enabled),"Running game did not disable install.");
                        var latest = new StatusInfo { Kind=GameStatus.Patched, Title="已汉化", Detail="当前版本已是最新版本。" };
                        form.ApplyInspection(latest,false);
                        Assert(Descendants(form).OfType<Button>().Any(b=>b.Text=="已安装最新汉化"&&!b.Enabled),"Latest version allows duplicate installation.");
                        Assert(Descendants(form).OfType<Button>().Any(b=>b.Text=="在 Steam 中启动游戏"&&b.Enabled),"Completed installation has no launch action.");
                        form.ApplyInspection(new StatusInfo {Kind=GameStatus.UpdateAvailable,SteamUpdated=true,Title="Steam 已更新 · 需要重新汉化",Detail="旧版备份将归档，新版原版单独备份。"},false);
                        Assert(Descendants(form).OfType<Button>().Any(b=>b.Text=="更新至 "+engine.Manifest.version&&b.Enabled),"Steam update action missing.");
                        Assert(!Descendants(form).OfType<Button>().Any(b=>b.Text=="回退原版"&&b.Enabled),"Steam update offers an old-build rollback.");
                        form.ApplyInspection(engine.Inspect(game),false);
                        Assert(Descendants(form).OfType<Label>().Any(l=>l.Text.Contains("非商业性，仅供学习用途")),"Learning-use notice missing.");
                        RenderPreview(form,Child(workspace,@"analysis\installer-v1.3-preview.png"));
                    }
                    var previews = new[] {
                        new StatusInfo {Kind=GameStatus.Original,Title="当前是原版",Detail="点击“一键安装汉化”开始。工具会先备份原版游戏文件，方便随时回退。"},
                        new StatusInfo {Kind=GameStatus.UpdateAvailable,Title="发现旧版汉化 · 0.2.0",Detail="点击“更新至 0.3.0”安装新补丁。原版备份和汉化存档会保留，无需先回退。"},
                        new StatusInfo {Kind=GameStatus.Patched,Title="已汉化 · 0.3.0",Detail="从 Steam 启动即可。汉化版使用独立存档；回退后可继续原版进度。"},
                        new StatusInfo {Kind=GameStatus.Missing,Title="未找到游戏文件",Detail="请选择包含 Solbrand.exe 和 Solbrand.pck 的游戏安装文件夹。"}
                    };
                    var names = new[] { "original", "update", "running", "missing" };
                    for(int i=0;i<previews.Length;i++)using(var form=new MainForm(engine, i==3 ? "" : game)){
                        form.ApplyInspection(previews[i],i==2);
                        RenderPreview(form,Child(workspace,"analysis\\installer-v1.3-"+names[i]+".png"));
                    }
                });
                if(args.Contains("--render-only"))return 0;
                Test("install, resource checks, idempotence, byte-identical rollback, saves untouched", delegate {
                    var dir = Child(root, "正常路径 中文"); var engine = Fixture(dir);
                    var target = Child(dir, "Solbrand.pck"); var original = PatchEngine.HashFile(target);
                    Assert(engine.Inspect(dir).Kind == GameStatus.Original, "Original detection failed.");
                    engine.Install(dir);
                    Assert(engine.Inspect(dir).Kind == GameStatus.Patched, "Patched detection failed.");
                    Assert(PatchEngine.HashFile(PatchEngine.BackupFile(dir)) == original, "Backup differs from source.");
                    using (var s = File.OpenRead(target)) {
                        var p = Pack.Read(s);
                        Assert(Encoding.UTF8.GetString(Pack.Bytes(s, p.Find(PatchEngine.Story))) == "中文剧情", "Story not replaced.");
                        Assert(Pack.Bytes(s, p.Find("Data/中文路径/unchanged.bin")).All(b => b == 137), "Unmodified resource changed.");
                    }
                    var patched = PatchEngine.HashFile(target); engine.Install(dir);
                    Assert(PatchEngine.HashFile(target) == patched, "Repeated install changed the file.");
                    engine.Restore(dir);
                    Assert(PatchEngine.HashFile(target) == original, "Rollback was not byte-identical.");
                    Assert(!File.Exists(PatchEngine.BackupFile(dir)), "Consumed backup was not cleaned.");
                    Assert(File.ReadAllText(Child(dir, "save-sentinel.dat")) == "untouched save", "Save sentinel changed.");
                    Assert(engine.Inspect(dir).Kind == GameStatus.Original, "Rollback detection failed.");
                    engine.Restore(dir);
                    engine.Install(dir); engine.Restore(dir);
                    Assert(PatchEngine.HashFile(target) == original, "Reinstall/rollback cycle failed.");
                });
                Test("unsupported source rejected without modifying it", delegate {
                    var dir = Child(root, "unsupported"); var engine = Fixture(dir);
                    File.AppendAllText(Child(dir, "Solbrand.pck"), "new version");
                    var before = PatchEngine.HashFile(Child(dir, "Solbrand.pck"));
                    Reject(() => engine.Install(dir), "Unsupported install succeeded.");
                    Assert(PatchEngine.HashFile(Child(dir, "Solbrand.pck")) == before, "Unsupported source changed.");
                    Assert(!File.Exists(PatchEngine.BackupFile(dir)), "Unexpected backup for unsupported source.");
                });
                Test("modified game refused; backup preserved", delegate {
                    var dir = Child(root, "changed"); var engine = Fixture(dir); engine.Install(dir);
                    var backup = PatchEngine.HashFile(PatchEngine.BackupFile(dir));
                    File.AppendAllText(Child(dir, "Solbrand.pck"), "third-party change");
                    var changed = PatchEngine.HashFile(Child(dir, "Solbrand.pck"));
                    Reject(() => engine.Restore(dir), "Changed target was overwritten.");
                    Assert(PatchEngine.HashFile(Child(dir, "Solbrand.pck")) == changed, "Changed target damaged.");
                    Assert(PatchEngine.HashFile(PatchEngine.BackupFile(dir)) == backup, "Backup lost.");
                });
                Test("corrupt backup refused without touching current game", delegate {
                    var dir = Child(root, "corrupt"); var engine = Fixture(dir); engine.Install(dir);
                    var before = PatchEngine.HashFile(Child(dir, "Solbrand.pck"));
                    using (var s = File.Open(PatchEngine.BackupFile(dir), FileMode.Open, FileAccess.Write)) {
                        s.Position = 200; s.WriteByte(233);
                    }
                    Reject(() => engine.Restore(dir), "Corrupt backup was accepted.");
                    Assert(PatchEngine.HashFile(Child(dir, "Solbrand.pck")) == before, "Current game damaged.");
                });
                Test("interrupted final journal can restore", delegate {
                    var dir = Child(root, "pending"); var engine = Fixture(dir); var before = PatchEngine.HashFile(Child(dir, "Solbrand.pck"));
                    engine.Install(dir);
                    File.Move(Child(PatchEngine.BackupDirectory(dir), "state.json"), Child(PatchEngine.BackupDirectory(dir), "pending.json"));
                    Assert(engine.Inspect(dir).Kind == GameStatus.Patched, "Pending patch not recognized.");
                    engine.Restore(dir); Assert(PatchEngine.HashFile(Child(dir, "Solbrand.pck")) == before, "Pending restore failed.");
                });
                Test("concurrent operation refused", delegate {
                    var dir = Child(root, "locked"); var engine = Fixture(dir);
                    Directory.CreateDirectory(PatchEngine.BackupDirectory(dir));
                    var before = PatchEngine.HashFile(Child(dir, "Solbrand.pck"));
                    using (var held = new FileStream(Child(PatchEngine.BackupDirectory(dir), "operation.lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                        Reject(() => engine.Install(dir), "Operation lock ignored.");
                    Assert(PatchEngine.HashFile(Child(dir, "Solbrand.pck")) == before, "Locked source changed.");
                });
                Test("malformed pack and missing folder refused", delegate {
                    var dir = Child(root, "missing");
                    var engine = PatchEngine.Embedded();
                    engine.CheckGameClosed = delegate { };
                    Assert(engine.Inspect(dir).Kind == GameStatus.Missing, "Missing folder accepted.");
                    Reject(() => engine.Restore(dir), "Missing folder restore accepted.");
                    Reject(() => Pack.Read(new MemoryStream(new byte[128])), "Bad signature accepted.");
                });
                Test("old patch upgrades in one action, keeps original backup and saves", delegate {
                    var dir = Child(root, "upgrade"); var old = Fixture(dir);
                    var original = PatchEngine.HashFile(Child(dir, "Solbrand.pck"));
                    old.Install(dir);
                    var newer = Fixture(dir, "0.2.1", false);
                    Assert(newer.Inspect(dir).Kind == GameStatus.UpdateAvailable, "Old version not recognized.");
                    int last = 0;
                    newer.Progress = (value, text) => { Assert(value >= last, "Update progress decreased."); last = value; };
                    newer.Install(dir);
                    Assert(newer.Inspect(dir).Kind == GameStatus.Patched, "Upgrade not recognized.");
                    Assert(PatchEngine.HashFile(PatchEngine.BackupFile(dir)) == original, "Original backup replaced by patched data.");
                    Assert(File.ReadAllText(Child(dir, "save-sentinel.dat")) == "untouched save", "Update touched save.");
                    newer.Progress = delegate { };
                    newer.Restore(dir);
                    Assert(PatchEngine.HashFile(Child(dir, "Solbrand.pck")) == original, "Updated patch cannot roll back.");
                });
                Test("interruption between restore and reinstall leaves intact original", delegate {
                    var dir = Child(root, "upgrade-interrupted"); var old = Fixture(dir);
                    var original = PatchEngine.HashFile(Child(dir, "Solbrand.pck")); old.Install(dir);
                    var newer = Fixture(dir, "0.2.1", false); int checks = 0;
                    newer.CheckGameClosed = delegate { if (++checks >= 4) throw new IOException("Simulated interruption."); };
                    Reject(() => newer.Install(dir), "Simulated interruption ignored.");
                    Assert(PatchEngine.HashFile(Child(dir, "Solbrand.pck")) == original, "Interrupted update damaged original.");
                    Assert(newer.Inspect(dir).Kind == GameStatus.Original, "Interrupted update cannot resume.");
                    newer.CheckGameClosed = delegate { }; newer.Install(dir);
                    Assert(newer.Inspect(dir).Kind == GameStatus.Patched, "Interrupted update failed to resume.");
                });
                Test("running-game guard prevents all mutations", delegate {
                    var dir = Child(root, "game-running"); var engine = Fixture(dir);
                    var before = PatchEngine.HashFile(Child(dir, "Solbrand.pck"));
                    engine.CheckGameClosed = delegate { throw new IOException("Game is running."); };
                    Reject(() => engine.Install(dir), "Running-game install not rejected.");
                    Reject(() => engine.Restore(dir), "Running-game restore not rejected.");
                    Assert(PatchEngine.HashFile(Child(dir, "Solbrand.pck")) == before, "Running-game guard changed file.");
                });
                Test("Steam update archives old backup and rollback restores the NEW original", delegate {
                    var dir=Child(root,"steam-updated"); var old=Fixture(dir,"0.3.0");
                    var oldHash=PatchEngine.HashFile(Child(dir,"Solbrand.pck")); old.Install(dir);
                    var newer=Fixture(dir,"0.4.0",true,"new-build");
                    var newHash=PatchEngine.HashFile(Child(dir,"Solbrand.pck"));
                    Assert(newer.Inspect(dir).SteamUpdated,"Steam update with old journal was not recognized.");
                    newer.Install(dir);
                    Assert(newer.Inspect(dir).Kind==GameStatus.Patched,"New game was not localized.");
                    Assert(PatchEngine.HashFile(PatchEngine.BackupFile(dir))==newHash,"New original was not backed up.");
                    var archives=Directory.GetFiles(Child(PatchEngine.BackupDirectory(dir),"archive"),"Solbrand.pck.original",SearchOption.AllDirectories);
                    Assert(archives.Length==1&&PatchEngine.HashFile(archives[0])==oldHash,"Old backup was lost or changed.");
                    newer.Restore(dir);
                    Assert(PatchEngine.HashFile(Child(dir,"Solbrand.pck"))==newHash,"Rollback downgraded the game!");
                    Assert(File.ReadAllText(Child(dir,"save-sentinel.dat"))=="untouched save","Steam update touched saves.");
                });
                Test("Steam update refuses corrupt legacy backup without touching new game", delegate {
                    var dir=Child(root,"updated-corrupt");var old=Fixture(dir,"0.3.0");old.Install(dir);
                    var newer=Fixture(dir,"0.4.0",true,"new-build");var before=PatchEngine.HashFile(Child(dir,"Solbrand.pck"));
                    File.AppendAllText(PatchEngine.BackupFile(dir),"corruption");
                    Reject(()=>newer.Install(dir),"Corrupt old backup was silently replaced.");
                    Assert(PatchEngine.HashFile(Child(dir,"Solbrand.pck"))==before,"New game was damaged.");
                    Assert(File.Exists(PatchEngine.BackupFile(dir)),"Corrupt backup was deleted.");
                });
                Test("restore after Steam update preserves new original and archives old backup", delegate {
                    var dir=Child(root,"restore-updated");var old=Fixture(dir,"0.3.0");old.Install(dir);
                    var newer=Fixture(dir,"0.4.0",true,"new-build");var before=PatchEngine.HashFile(Child(dir,"Solbrand.pck"));
                    newer.Restore(dir);
                    Assert(PatchEngine.HashFile(Child(dir,"Solbrand.pck"))==before,"Restore overwrote a Steam update.");
                    Assert(newer.Inspect(dir).Kind==GameStatus.Original,"New original cannot be installed after archive.");
                });
                Test("bundled profiles select correct build for fresh, patched and updated files", delegate {
                    var dir=Child(root,"multi-profile");var old=Fixture(dir,"0.3.0");old.Install(dir);
                    var newer=Fixture(Child(root,"profile-source"),"0.4.0",true,"new-build");
                    newer.CompatibleBuilds.Add(Fixture(dir,"0.4.0",false));
                    Assert(newer.Inspect(dir).Kind==GameStatus.UpdateAvailable,"Old patched build was not selected.");
                    newer.Install(dir);Assert(newer.Inspect(dir).Kind==GameStatus.Patched,"Old build upgrade failed.");
                    newer.Restore(dir);Assert(newer.Inspect(dir).Kind==GameStatus.Original,"Old build restore failed.");
                    newer.Install(dir);
                    File.Copy(Child(Child(root,"profile-source"),"Solbrand.pck"),Child(dir,"Solbrand.pck"),true);
                    Assert(newer.Inspect(dir).SteamUpdated,"Latest original lost priority over an old state record.");
                    newer.Install(dir);newer.Restore(dir);
                });
                if (args.Length > 0 && args[0] == "--upgrade-installed") {
                    Test("actual installed patch copy: one-click upgrade and exact original rollback", delegate {
                        Assert(args.Length==2,"Usage: --upgrade-installed <game directory>");
                        var sourceDir=Path.GetFullPath(args[1]);var dir=Child(root,"real-patch-upgrade");Directory.CreateDirectory(dir);
                        var target=Child(dir,"Solbrand.pck");var backup=PatchEngine.BackupFile(dir);
                        var source=Child(sourceDir,"Solbrand.pck");var original=PatchEngine.BackupFile(sourceDir);
                        var sourceHash=PatchEngine.HashFile(source);var originalHash=PatchEngine.HashFile(original);
                        try {
                            Console.WriteLine("Copying installed patch, original backup and state into isolated test folder...");
                            File.Copy(source,target);Directory.CreateDirectory(PatchEngine.BackupDirectory(dir));File.Copy(original,backup);
                            File.Copy(Child(PatchEngine.BackupDirectory(sourceDir),"state.json"),Child(PatchEngine.BackupDirectory(dir),"state.json"));
                            File.WriteAllText(Child(dir,"Solbrand.exe"),"TEST FIXTURE: cannot launch game");
                            File.WriteAllText(Child(dir,"save-sentinel.dat"),"unchanged");
                            var engine=PatchEngine.Embedded();engine.CheckGameClosed=delegate{};
                            Assert(engine.Inspect(dir).Kind==GameStatus.UpdateAvailable,"Installed patch is not eligible for upgrade.");
                            engine.Install(dir);Assert(engine.Inspect(dir).Kind==GameStatus.Patched,"Upgrade failed.");
                            Assert(PatchEngine.HashFile(backup)==originalHash,"Upgrade changed the original backup.");
                            engine.Restore(dir);Assert(PatchEngine.HashFile(target)==originalHash,"Rollback changed the original.");
                            Assert(File.ReadAllText(Child(dir,"save-sentinel.dat"))=="unchanged","Upgrade changed saves.");
                            Assert(PatchEngine.HashFile(source)==sourceHash&&PatchEngine.HashFile(original)==originalHash,"Installed game was changed by the test!");
                            Console.WriteLine("Original/backup/restored SHA256: "+originalHash);
                        } finally {
                            foreach(var file in new[]{target,backup}){Assert(Path.GetFullPath(file).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Unsafe cleanup.");if(File.Exists(file))File.Delete(file);}
                        }
                    });
                }
                if (args.Length > 0 && args[0] == "--steam-update") {
                    Test("actual Steam update: preserve legacy archive, install new build, exact new-build rollback", delegate {
                        Assert(args.Length==4,"Usage: --steam-update <new-original.pck> <old-backup.pck> <old-state.json>");
                        var dir=Child(root,"real-steam-update");Directory.CreateDirectory(dir);
                        var target=Child(dir,"Solbrand.pck");var backup=PatchEngine.BackupFile(dir);
                        var source=Path.GetFullPath(args[1]);var oldSource=Path.GetFullPath(args[2]);
                        var before=PatchEngine.HashFile(source);var oldHash=PatchEngine.HashFile(oldSource);
                        try {
                            Console.WriteLine("Copying actual new original and legacy backup into isolated test folder...");
                            File.Copy(source,target);Directory.CreateDirectory(PatchEngine.BackupDirectory(dir));File.Copy(oldSource,backup);
                            File.Copy(args[3],Child(PatchEngine.BackupDirectory(dir),"state.json"));
                            File.WriteAllText(Child(dir,"Solbrand.exe"),"TEST FIXTURE: cannot launch game");
                            File.WriteAllText(Child(dir,"save-sentinel.dat"),"unchanged");
                            var engine=PatchEngine.Embedded();engine.CheckGameClosed=delegate{};
                            string last="";engine.Progress=(value,text)=>{if(last!=text){Console.WriteLine(value+"% "+text);last=text;}};
                            Assert(engine.Inspect(dir).SteamUpdated,"Actual updated game was not recognized.");
                            engine.Install(dir);Assert(engine.Inspect(dir).Kind==GameStatus.Patched,"Actual updated game not localized.");
                            Assert(PatchEngine.HashFile(backup)==before,"Actual new original backup changed.");
                            var archived=Directory.GetFiles(Child(PatchEngine.BackupDirectory(dir),"archive"),"Solbrand.pck.original",SearchOption.AllDirectories);
                            Assert(archived.Length==1&&PatchEngine.HashFile(archived[0])==oldHash,"Actual old backup was not preserved.");
                            engine.Restore(dir);Assert(PatchEngine.HashFile(target)==before,"Actual rollback downgraded/changed the new game.");
                            Assert(File.ReadAllText(Child(dir,"save-sentinel.dat"))=="unchanged","Save sentinel changed.");
                            Assert(PatchEngine.HashFile(source)==before&&PatchEngine.HashFile(oldSource)==oldHash,"Installed game/source backup changed!");
                            Console.WriteLine("New original/restored SHA256: "+before);
                            Console.WriteLine("Legacy original/archive SHA256: "+oldHash);
                        } finally {
                            var largeFiles=new List<string>{target,backup};
                            if(Directory.Exists(Child(PatchEngine.BackupDirectory(dir),"archive")))largeFiles.AddRange(Directory.GetFiles(Child(PatchEngine.BackupDirectory(dir),"archive"),"Solbrand.pck.original",SearchOption.AllDirectories));
                            foreach(var file in largeFiles){Assert(Path.GetFullPath(file).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase),"Unsafe cleanup.");if(File.Exists(file))File.Delete(file);}
                        }
                    });
                }
                if (args.Length > 0 && args[0] == "--real") {
                    Test("actual game copy: install and exact SHA256 rollback", delegate {
                        var source = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("SOLBRAND_ORIGINAL_PCK");
                        Assert(!String.IsNullOrWhiteSpace(source) && File.Exists(source), "Usage: SolbrandZh.Tests.exe --real <original Solbrand.pck>");
                        source = Path.GetFullPath(source);
                        var dir = Child(root, "real-game-copy"); Directory.CreateDirectory(dir);
                        var target = Child(dir, "Solbrand.pck");
                        var before = PatchEngine.HashFile(source);
                        File.WriteAllText(Child(dir, "Solbrand.exe"), "TEST PLACEHOLDER: game is never launched");
                        try {
                            Console.WriteLine("Copying original resource into test workspace...");
                            File.Copy(source, target);
                            var engine = PatchEngine.Embedded(); string last = "";
                            engine.CheckGameClosed = delegate { };
                            engine.Progress = (percent, text) => { if (text != last) { Console.WriteLine(percent + "% " + text); last = text; } };
                            Assert(engine.Inspect(dir).Kind == GameStatus.Original, "Real original not recognized.");
                            engine.Install(dir);
                            Assert(engine.Inspect(dir).Kind == GameStatus.Patched, "Real patch not recognized.");
                            Assert(PatchEngine.HashFile(PatchEngine.BackupFile(dir)) == before, "Real backup differs.");
                            engine.Restore(dir);
                            Assert(PatchEngine.HashFile(target) == before, "Real rollback differs.");
                            Assert(PatchEngine.HashFile(source) == before, "Steam original changed!");
                            Console.WriteLine("Original/backup/restored SHA256: " + before);
                        } finally {
                            // Delete only the exact large test files created above, never the source installation.
                            Assert(target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Unsafe cleanup.");
                            if (File.Exists(target)) File.Delete(target);
                            var backup = PatchEngine.BackupFile(dir);
                            if (File.Exists(backup)) File.Delete(backup);
                        }
                    });
                }
                Console.WriteLine("ALL PASSED: " + passed);
                return 0;
            } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
