using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace SolbrandZh {
    public sealed class PatchManifest {
        public string version, steamBuild, sourceProjectMd5, sourceStoryMd5;
        public long sourcePackSize;
    }
    public sealed class PatchState {
        public string product = "SolbrandZh", version, originalHash, patchedHash;
        public long originalSize, patchedSize;
    }
    public enum GameStatus { Missing, Original, Patched, Unsupported, Recovery, UpdateAvailable }
    public sealed class StatusInfo {
        public GameStatus Kind;
        public string Title, Detail;
        public bool SteamUpdated;
    }
    public sealed class PackEntry {
        public string Name;
        public long Offset, Size;
        public byte[] Hash;
        public uint Flags;
    }
    public sealed class Pack {
        public long Base;
        public readonly List<PackEntry> Entries = new List<PackEntry>();
        public PackEntry Find(string name) {
            var e = Entries.FirstOrDefault(x => x.Name == name);
            if (e == null) throw new InvalidDataException("游戏资源缺失：" + name);
            return e;
        }
        public static Pack Read(Stream stream) {
            stream.Position = 0;
            var r = new BinaryReader(stream, Encoding.UTF8, true);
            if (r.ReadUInt32() != 0x43504447) throw new InvalidDataException("资源包格式不正确。");
            var version = r.ReadUInt32();
            if (version != 3 && version != 4) throw new InvalidDataException("不支持此资源包版本。");
            stream.Position = 20;
            if (r.ReadUInt32() != 2) throw new InvalidDataException("不支持此资源包标记。");
            var p = new Pack { Base = checked((long)r.ReadUInt64()) };
            var index = checked((long)r.ReadUInt64());
            if (index < 40 || index > stream.Length - 4 || p.Base < 0 || p.Base > stream.Length)
                throw new InvalidDataException("资源索引损坏。");
            stream.Position = index;
            var count = r.ReadUInt32();
            if (count > 100000) throw new InvalidDataException("资源索引数量异常。");
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (uint i = 0; i < count; i++) {
                var length = r.ReadInt32();
                if (length < 1 || length > 4096 || length % 4 != 0)
                    throw new InvalidDataException("资源名称损坏。");
                var name = Encoding.UTF8.GetString(ReadBytes(r, length)).TrimEnd('\0');
                var e = new PackEntry { Name = name, Offset = checked((long)r.ReadUInt64() + p.Base),
                    Size = checked((long)r.ReadUInt64()), Hash = ReadBytes(r, 16), Flags = r.ReadUInt32() };
                if (!names.Add(name) || e.Offset < p.Base || e.Size < 0 || e.Offset > stream.Length - e.Size)
                    throw new InvalidDataException("资源位置损坏：" + name);
                p.Entries.Add(e);
            }
            return p;
        }
        static byte[] ReadBytes(BinaryReader r, int n) {
            var b = r.ReadBytes(n);
            if (b.Length != n) throw new EndOfStreamException("资源包不完整。");
            return b;
        }
        public static byte[] Bytes(Stream s, PackEntry e) {
            if (e.Size > 64 * 1024 * 1024) throw new InvalidDataException("补丁资源过大。");
            s.Position = e.Offset;
            var b = ReadBytes(new BinaryReader(s, Encoding.UTF8, true), checked((int)e.Size));
            using (var md5 = MD5.Create()) {
                if (!md5.ComputeHash(b).SequenceEqual(e.Hash)) throw new InvalidDataException("资源校验失败：" + e.Name);
            }
            return b;
        }
        public void WriteIndex(Stream s) {
            var at = s.Position;
            var w = new BinaryWriter(s, Encoding.UTF8, true);
            w.Write((uint)Entries.Count);
            foreach (var e in Entries) {
                var b = Encoding.UTF8.GetBytes(e.Name);
                var padding = (4 - b.Length % 4) % 4;
                w.Write((uint)(b.Length + padding)); w.Write(b); w.Write(new byte[padding]);
                w.Write(checked((ulong)(e.Offset - Base))); w.Write((ulong)e.Size);
                w.Write(e.Hash); w.Write(e.Flags);
            }
            s.Position = 32; w.Write((ulong)at); s.Position = s.Length;
        }
    }
    public sealed class PatchEngine {
        public const string Story = ".godot/imported/Main.ink-e0f455a283e8899f6dee802ddd078da4.res";
        public readonly PatchManifest Manifest;
        readonly byte[] overlay;
        internal readonly List<PatchEngine> CompatibleBuilds = new List<PatchEngine>();
        readonly JavaScriptSerializer json = new JavaScriptSerializer();
        public Action<int, string> Progress = delegate { };
        internal Action CheckGameClosed = EnsureGameClosed;
        public PatchEngine(PatchManifest manifest, byte[] overlayBytes) {
            Manifest = manifest; overlay = overlayBytes;
            using (var s = new MemoryStream(overlay, false)) {
                var p = Pack.Read(s);
                foreach (var e in p.Entries) Pack.Bytes(s, e);
                p.Find("project.binary"); p.Find(Story);
            }
        }
        public static PatchEngine Embedded() {
            var a = Assembly.GetExecutingAssembly();
            var engine = EmbeddedProfile(a, "patch");
            foreach (var name in a.GetManifestResourceNames().Where(n => n.StartsWith("build.") && n.EndsWith(".manifest"))) {
                var other = EmbeddedProfile(a, name.Substring(0, name.Length - ".manifest".Length));
                if (other.Manifest.steamBuild != engine.Manifest.steamBuild) engine.CompatibleBuilds.Add(other);
            }
            return engine;
        }
        static PatchEngine EmbeddedProfile(Assembly a, string prefix) {
            using (var s = a.GetManifestResourceStream(prefix + ".manifest"))
            using (var r = new StreamReader(s, Encoding.UTF8))
            using (var o = a.GetManifestResourceStream(prefix + ".overlay"))
            using (var b = new MemoryStream()) {
                o.CopyTo(b);
                return new PatchEngine(new JavaScriptSerializer().Deserialize<PatchManifest>(r.ReadToEnd()), b.ToArray());
            }
        }
        PatchEngine ForDirectory(string dir) {
            if (String.IsNullOrWhiteSpace(dir) || !File.Exists(GameFile(dir))) return this;
            try {
                var all = new[] { this }.Concat(CompatibleBuilds).ToArray();
                var length = new FileInfo(GameFile(dir)).Length;
                var original = all.FirstOrDefault(e => e.Manifest.sourcePackSize == length);
                if (original != null) return original;
                var state = ReadState(dir);
                if (state != null && length == state.patchedSize)
                    return all.FirstOrDefault(e => e.Manifest.sourcePackSize == state.originalSize) ?? this;
            } catch (Exception) { /* Inspect reports damaged records without modifying anything. */ }
            return this;
        }
        static string GameFile(string dir) { return Path.Combine(Path.GetFullPath(dir), "Solbrand.pck"); }
        public static string BackupDirectory(string dir) { return Path.Combine(Path.GetFullPath(dir), ".solbrand-zh"); }
        public static string BackupFile(string dir) { return Path.Combine(BackupDirectory(dir), "Solbrand.pck.original"); }
        static string StateFile(string dir) { return Path.Combine(BackupDirectory(dir), "state.json"); }
        static string PendingFile(string dir) { return Path.Combine(BackupDirectory(dir), "pending.json"); }
        static void RejectLink(string file) {
            if ((File.Exists(file) || Directory.Exists(file)) && (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("备份或补丁路径是链接，无法安全操作：" + file);
        }
        static FileStream OpenRead(string path) {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        }
        void CheckOriginal(Stream s) {
            if (s.Length != Manifest.sourcePackSize)
                throw new InvalidDataException("游戏版本不匹配。本补丁支持 Steam Build " + Manifest.steamBuild + "。");
            var pack = Pack.Read(s);
            VerifyMd5(s, pack.Find("project.binary"), Manifest.sourceProjectMd5);
            VerifyMd5(s, pack.Find(Story), Manifest.sourceStoryMd5);
        }
        static void VerifyMd5(Stream s, PackEntry e, string expected) {
            using (var m = MD5.Create()) {
                if (Hex(m.ComputeHash(Pack.Bytes(s, e))) != expected)
                    throw new InvalidDataException("游戏资源与本补丁不匹配。请先恢复原版，或等待适配当前版本的补丁。");
            }
        }
        PatchState ReadState(string dir) {
            // A pending journal remains sufficient if interrupted just after the atomic swap.
            var file = File.Exists(PendingFile(dir)) ? PendingFile(dir) : StateFile(dir);
            if (!File.Exists(file)) return null;
            RejectLink(file);
            var state = json.Deserialize<PatchState>(File.ReadAllText(file, Encoding.UTF8));
            if (state == null || state.product != "SolbrandZh" || !IsHash(state.originalHash) || !IsHash(state.patchedHash)
                || state.originalSize < 40 || state.patchedSize < state.originalSize)
                throw new InvalidDataException("汉化记录损坏。原版备份已保留，请勿删除 .solbrand-zh 文件夹。");
            return state;
        }
        static bool IsHash(string s) { return s != null && s.Length == 64 && s.All(x => Uri.IsHexDigit(x)); }
        public StatusInfo Inspect(string dir) {
            var selected = ForDirectory(dir);
            if (selected != this) return selected.Inspect(dir);
            if (String.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)
                || !File.Exists(GameFile(dir)) || !File.Exists(Path.Combine(dir, "Solbrand.exe")))
                return new StatusInfo { Kind = GameStatus.Missing, Title = "请选择游戏文件夹", Detail = "选择包含 Solbrand.exe 的 Solbrand Demo 安装文件夹。" };
            try {
                RejectLink(BackupDirectory(dir)); RejectLink(BackupFile(dir));
                var state = ReadState(dir);
                using (var s = OpenRead(GameFile(dir))) {
                    if (state != null && s.Length == state.patchedSize && File.Exists(BackupFile(dir))) {
                        if (state.version != Manifest.version) {
                            Version installed, available;
                            if (!Version.TryParse(state.version, out installed) || !Version.TryParse(Manifest.version, out available) || installed >= available)
                                throw new InvalidDataException("汉化版本与此工具不兼容，请使用更新的工具。");
                            if (HashFile(GameFile(dir)) != state.patchedHash)
                                throw new InvalidDataException("当前游戏与汉化记录不一致，暂未更改文件。");
                            using (var original = OpenRead(BackupFile(dir))) CheckOriginal(original);
                            return new StatusInfo { Kind = GameStatus.UpdateAvailable, Title = "发现旧版汉化 · " + state.version,
                                Detail = "点击“更新至 " + Manifest.version + "”安装新补丁。原版备份和汉化存档会保留。" };
                        }
                        var current = Pack.Read(s);
                        using (var o = new MemoryStream(overlay, false)) {
                            var expected = Pack.Read(o);
                            var project = Pack.Bytes(s, current.Find("project.binary"));
                            var story = Pack.Bytes(s, current.Find(Story));
                            if (!project.SequenceEqual(Pack.Bytes(o, expected.Find("project.binary")))
                                || !story.SequenceEqual(Pack.Bytes(o, expected.Find(Story))))
                                throw new InvalidDataException("当前游戏与汉化记录不一致。备份仍在，暂未更改文件。");
                            foreach (var entry in expected.Entries)
                                if (!Pack.Bytes(s, current.Find(entry.Name)).SequenceEqual(Pack.Bytes(o, entry)))
                                    throw new InvalidDataException("汉化资源与记录不一致，请先回退原版再重新汉化。");
                        }
                        return new StatusInfo { Kind = GameStatus.Patched, Title = "已汉化 · " + state.version,
                            Detail = "从 Steam 启动即可。汉化版使用独立存档；回退后可继续原版进度。" };
                    }
                    CheckOriginal(s);
                }
                if (File.Exists(BackupFile(dir))) {
                    if (state != null) {
                        bool matches = false;
                        try { using (var old = OpenRead(BackupFile(dir))) { CheckOriginal(old); matches = true; } } catch (InvalidDataException) { }
                        if (!matches) return new StatusInfo { Kind = GameStatus.UpdateAvailable, SteamUpdated = true,
                            Title = "Steam 已更新 · 需要重新汉化",
                            Detail = "已识别 Build " + Manifest.steamBuild + "。点击更新：归档旧版备份后汉化新版，回退将恢复新版原版。存档保留。" };
                    }
                    return new StatusInfo { Kind = GameStatus.Recovery, Title = "当前是原版 · 发现旧备份",
                        Detail = "点击“回退原版”校验并清理旧记录，然后即可重新汉化。" };
                }
                return new StatusInfo { Kind = GameStatus.Original, Title = "当前是原版 · 可以汉化",
                    Detail = "点击“一键汉化”会自动备份。汉化版使用独立存档，原版进度会保留。" };
            } catch (Exception e) {
                return new StatusInfo { Kind = GameStatus.Unsupported, Title = "暂时无法操作", Detail = Friendly(e) };
            }
        }
        static void EnsureGameClosed() {
            if (Process.GetProcessesByName("Solbrand").Length > 0)
                throw new IOException("请先退出 Solbrand Demo，再进行汉化或回退。");
        }
        public static bool GameIsRunning() { return Process.GetProcessesByName("Solbrand").Length > 0; }
        FileStream AcquireLock(string dir) {
            RejectLink(BackupDirectory(dir));
            Directory.CreateDirectory(BackupDirectory(dir));
            var file = Path.Combine(BackupDirectory(dir), "operation.lock");
            RejectLink(file);
            try { return new FileStream(file, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) { throw new IOException("另一个汉化工具正在操作此游戏，请等待它完成。"); }
        }
        void SaveJournal(string dir, PatchState state) {
            var file = PendingFile(dir); RejectLink(file);
            using (var f = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.None)) {
                var b = new UTF8Encoding(false).GetBytes(json.Serialize(state));
                f.Write(b, 0, b.Length); f.Flush(true);
            }
        }
        static void FinalizeJournal(string dir) {
            var pending = PendingFile(dir); var state = StateFile(dir);
            try {
                RejectLink(state);
                if (File.Exists(state)) File.Replace(pending, state, null);
                else File.Move(pending, state);
            } catch (IOException) { /* Pending journal is already durable and can restore. */ }
              catch (UnauthorizedAccessException) { /* Same recovery path. */ }
        }
        public void Install(string dir) {
            var selected = ForDirectory(dir);
            if (selected != this) { selected.Progress = Progress; selected.CheckGameClosed = CheckGameClosed; selected.Install(dir); return; }
            CheckGameClosed();
            var info = Inspect(dir);
            if (info.SteamUpdated) {
                using (var operation = AcquireLock(dir)) ArchiveUpdatedBackup(dir);
                Install(dir);
                return;
            }
            if (info.Kind == GameStatus.UpdateAvailable) {
                // Each step is atomic. Restoring first frees the old patched copy's space,
                // so updating needs no extra full-size copy beyond the existing backup.
                var report = Progress; bool restored = false;
                try {
                    Progress = (value, text) => report(value * 35 / 100, "更新汉化：" + text);
                    Restore(dir); restored = true;
                    Progress = (value, text) => report(35 + value * 65 / 100, "更新汉化：" + text);
                    Install(dir);
                    report(100, "已更新至 " + Manifest.version + "！现有汉化存档已保留。");
                } catch (Exception e) {
                    if (restored) throw new IOException("已安全恢复原版，但更新未完成。汉化存档仍保留，请重新点击“一键汉化”。\r\n" + Friendly(e), e);
                    throw;
                } finally { Progress = report; }
                return;
            }
            if (info.Kind == GameStatus.Patched) { Progress(100, "已经是汉化版，无需重复安装。"); return; }
            if (info.Kind != GameStatus.Original) throw new IOException(info.Detail);
            var target = GameFile(dir); var temporary = Path.Combine(dir, "Solbrand.zh-" + Guid.NewGuid().ToString("N") + ".tmp");
            RejectLink(target);
            using (var operation = AcquireLock(dir)) {
                bool swapped = false;
                try {
                    if (File.Exists(BackupFile(dir))) throw new IOException("发现已有原版备份，请先回退原版。");
                    var drive = new DriveInfo(Path.GetPathRoot(target));
                    if (drive.AvailableFreeSpace < Manifest.sourcePackSize + overlay.LongLength + 32 * 1024 * 1024)
                        throw new IOException("磁盘空间不足。汉化需要约 1.7 GB 额外空间，用于保留原版备份。");
                    Progress(2, "正在校验游戏版本…");
                    string originalHash; Pack pack;
                    var lastWrite = File.GetLastWriteTimeUtc(target);
                    using (var source = OpenRead(target))
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1024 * 1024)) {
                        CheckOriginal(source); pack = Pack.Read(source); source.Position = 0;
                        using (var sha = SHA256.Create()) {
                            var buffer = new byte[1024 * 1024]; int n; long copied = 0; int last = -1;
                            while ((n = source.Read(buffer, 0, buffer.Length)) > 0) {
                                output.Write(buffer, 0, n); sha.TransformBlock(buffer, 0, n, buffer, 0); copied += n;
                                int percent = 5 + (int)(copied * 58 / source.Length);
                                if (percent != last) { Progress(percent, "正在准备汉化资源，并保留原版…"); last = percent; }
                            }
                            sha.TransformFinalBlock(new byte[0], 0, 0); originalHash = Hex(sha.Hash);
                        }
                        Progress(66, "正在写入中文剧情与界面…");
                        using (var o = new MemoryStream(overlay, false)) {
                            var patch = Pack.Read(o);
                            foreach (var e in patch.Entries) {
                                while (output.Position % 32 != 0) output.WriteByte(0);
                                var bytes = Pack.Bytes(o, e);
                                var added = new PackEntry { Name = e.Name, Offset = output.Position, Size = bytes.Length, Hash = e.Hash, Flags = e.Flags };
                                pack.Entries.RemoveAll(x => x.Name == e.Name); pack.Entries.Add(added); output.Write(bytes, 0, bytes.Length);
                            }
                            pack.WriteIndex(output); output.Flush(true);
                            var rebuilt = Pack.Read(output);
                            foreach (var e in patch.Entries)
                                if (!Pack.Bytes(output, rebuilt.Find(e.Name)).SequenceEqual(Pack.Bytes(o, e)))
                                    throw new InvalidDataException("中文资源写入校验失败。原版未更改。");
                        }
                    }
                    Progress(78, "正在校验完整文件…");
                    var state = new PatchState { version = Manifest.version, originalHash = originalHash,
                        originalSize = Manifest.sourcePackSize, patchedSize = new FileInfo(temporary).Length,
                        patchedHash = HashFile(temporary) };
                    SaveJournal(dir, state);
                    CheckGameClosed();
                    if (lastWrite != File.GetLastWriteTimeUtc(target) || new FileInfo(target).Length != Manifest.sourcePackSize)
                        throw new IOException("游戏文件在操作期间发生变化，已停止。请等待 Steam 更新完成后重试。");
                    if (File.Exists(BackupFile(dir))) throw new IOException("操作期间出现已有备份，已停止以免覆盖。");
                    Progress(95, "正在保存原版备份并完成汉化…");
                    File.Replace(temporary, target, BackupFile(dir));
                    swapped = true; FinalizeJournal(dir);
                    Progress(100, "汉化完成！现在可以照常从 Steam 启动游戏。");
                } finally {
                    if (File.Exists(temporary)) File.Delete(temporary);
                    if (!swapped && !File.Exists(BackupFile(dir)) && File.Exists(PendingFile(dir))) File.Delete(PendingFile(dir));
                }
            }
        }
        public void Restore(string dir) {
            var selected = ForDirectory(dir);
            if (selected != this) { selected.Progress = Progress; selected.CheckGameClosed = CheckGameClosed; selected.Restore(dir); return; }
            CheckGameClosed();
            if (String.IsNullOrWhiteSpace(dir) || !File.Exists(GameFile(dir))) throw new IOException("请先选择正确的游戏文件夹。");
            using (var operation = AcquireLock(dir)) {
                var state = ReadState(dir); var backup = BackupFile(dir); var target = GameFile(dir);
                RejectLink(backup); RejectLink(target);
                if (!File.Exists(backup)) {
                    using (var current = OpenRead(target)) CheckOriginal(current);
                    Progress(100, "当前已经是原版，无需回退。"); return;
                }
                if (state == null) throw new IOException("发现备份但缺少汉化记录，已保留所有文件。请勿手动删除备份。");
                Progress(10, "正在校验原版备份…");
                bool isUpdatedOriginal = false;
                try { using (var current = OpenRead(target)) CheckOriginal(current); isUpdatedOriginal = new FileInfo(backup).Length != Manifest.sourcePackSize; }
                catch (InvalidDataException) { }
                if (isUpdatedOriginal) { ArchiveUpdatedBackup(dir); Progress(100, "当前已是新版原版，旧版备份已归档，未覆盖新版游戏。"); return; }
                using (var b = OpenRead(backup)) {
                    if (b.Length != state.originalSize) throw new InvalidDataException("备份长度与记录不符，未修改文件。");
                    Pack.Read(b);
                }
                if (HashFile(backup) != state.originalHash) throw new IOException("原版备份校验失败，已停止回退。当前游戏与备份均未更改。");
                Progress(48, "正在检查游戏文件…");
                var currentHash = HashFile(target);
                if (currentHash != state.patchedHash && currentHash != state.originalHash)
                    throw new IOException("游戏文件在汉化后被 Steam 或其他程序修改，暂未覆盖。备份已保留；请使用 Steam 的“验证游戏文件完整性”恢复原版。");
                CheckGameClosed();
                Progress(90, "正在还原原版…");
                // Atomic replacement consumes the backup; no extra full-size temporary copy is needed.
                File.Replace(backup, target, null);
                foreach (var file in new[] { StateFile(dir), PendingFile(dir) }) {
                    RejectLink(file);
                    try { if (File.Exists(file)) File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                Progress(100, "已回退原版！存档未改动，可以照常从 Steam 启动。");
            }
        }
        void ArchiveUpdatedBackup(string dir) {
            CheckGameClosed();
            using (var current = OpenRead(GameFile(dir))) CheckOriginal(current);
            var state = ReadState(dir); var backup = BackupFile(dir);
            if (state == null || !File.Exists(backup) || new FileInfo(backup).Length != state.originalSize || HashFile(backup) != state.originalHash)
                throw new IOException("旧版备份无法通过记录校验，已保留文件。请勿删除 .solbrand-zh。");
            Progress(1, "Steam 已更新：正在完整保留旧版备份…");
            var archiveRoot = Path.Combine(BackupDirectory(dir), "archive"); RejectLink(archiveRoot);
            var archive = Path.Combine(archiveRoot, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(archive);
            foreach (var name in new[] { "state.json", "pending.json" }) {
                var source = Path.Combine(BackupDirectory(dir), name); RejectLink(source);
                if (File.Exists(source)) File.Copy(source, Path.Combine(archive, name));
            }
            CheckGameClosed();
            using (var current = OpenRead(GameFile(dir))) CheckOriginal(current);
            File.Move(backup, Path.Combine(archive, "Solbrand.pck.original"));
            // Preserve any journal on interruption; a validated original can be reinstalled.
            foreach (var file in new[] { StateFile(dir), PendingFile(dir) }) if (File.Exists(file)) File.Delete(file);
        }
        public static string HashFile(string file) {
            using (var s = OpenRead(file)) using (var h = SHA256.Create()) return Hex(h.ComputeHash(s));
        }
        public static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }
        public static string Friendly(Exception e) {
            if (e is UnauthorizedAccessException)
                return "没有游戏目录的写入权限。请关闭工具，右键选择“以管理员身份运行”后重试。";
            return e.Message;
        }
    }
}
