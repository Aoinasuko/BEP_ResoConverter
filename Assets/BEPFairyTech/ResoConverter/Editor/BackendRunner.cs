using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace BEPFairyTech.ResoConverter
{
    internal static class BackendRunner
    {
        internal const string AssetRoot = "Assets/BEPFairyTech/ResoConverter";

        [Serializable]
        private sealed class BackendFeatures { public int facialExpressions; }
        private static string featurePayload;
        private static DateTime featureTimestamp;
        private static long featureLength;
        private static bool supportsExpressions;

        internal static bool SupportsFacialExpressions()
        {
            string payload = Path.Combine(Directory.GetParent(Application.dataPath).FullName, AssetRoot, "Editor/BackendPayload.bytes");
            var file = new FileInfo(payload);
            if (!file.Exists) return false;
            if (featurePayload == payload && featureTimestamp == file.LastWriteTimeUtc && featureLength == file.Length)
                return supportsExpressions;
            featurePayload = payload;
            featureTimestamp = file.LastWriteTimeUtc;
            featureLength = file.Length;
            supportsExpressions = false;
            try
            {
                using (var archive = ZipFile.OpenRead(payload))
                {
                    var entry = archive.GetEntry("bep-features.json");
                    if (entry == null) return false;
                    using (var reader = new StreamReader(entry.Open()))
                        supportsExpressions = JsonUtility.FromJson<BackendFeatures>(reader.ReadToEnd())?.facialExpressions >= 2;
                }
            }
            catch (InvalidDataException) { }
            catch (IOException) { }
            catch (ArgumentException) { }
            return supportsExpressions;
        }

        internal static string FindResonite()
        {
            string saved = UnityEditor.EditorPrefs.GetString("BEPFairyTech.ResoConverter.ResonitePath", "");
            if (IsResoniteFolder(saved)) return saved;
            string[] candidates = {
                @"D:\Game\SteamLibrary\steamapps\common\Resonite",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam/steamapps/common/Resonite"),
                @"D:\SteamLibrary\steamapps\common\Resonite", @"E:\SteamLibrary\steamapps\common\Resonite"
            };
            foreach (string path in candidates) if (IsResoniteFolder(path)) return path;
            return "";
        }

        internal static bool IsResoniteFolder(string path)
        {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) &&
                (File.Exists(Path.Combine(path, "FrooxEngine.dll")) ||
                 File.Exists(Path.Combine(path, "Headless/FrooxEngine.dll")) ||
                 File.Exists(Path.Combine(path, "Resonite_Data/Managed/FrooxEngine.dll")));
        }

        private static string ExtractBackend()
        {
            string project = Directory.GetParent(Application.dataPath).FullName;
            string payload = Path.Combine(project, AssetRoot, "Editor/BackendPayload.bytes");
            if (!File.Exists(payload)) throw new FileNotFoundException("変換エンジンがありません。ResoConverterを再インポートしてください。", payload);
            string hash;
            using (var stream = File.OpenRead(payload))
            using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").Substring(0, 20);
            string destination = Path.Combine(project, "Library/BEPFairyTech/ResoConverter/Backend", hash);
            string executable = Path.Combine(destination, "Launcher.exe");
            if (File.Exists(Path.Combine(destination, ".complete")) && File.Exists(executable)) return executable;
            Directory.CreateDirectory(destination);
            using (var archive = ZipFile.OpenRead(payload))
            {
                string prefix = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
                foreach (var entry in archive.Entries)
                {
                    string target = Path.GetFullPath(Path.Combine(destination, entry.FullName));
                    if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid backend archive path.");
                    if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    entry.ExtractToFile(target, true);
                }
            }
            if (!File.Exists(executable)) throw new FileNotFoundException("Backend archive does not contain Launcher.exe.");
            File.WriteAllText(Path.Combine(destination, ".complete"), hash);
            return executable;
        }

        private static string Quote(string value)
        {
            var result = new System.Text.StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes);
                result.Append(c);
                slashes = 0;
            }
            result.Append('\\', slashes * 2).Append('"');
            return result.ToString();
        }

        internal static async Task Run(string input, string output, string settings, string tempDirectory,
            string resonitePath, ConcurrentQueue<string> progress, CancellationToken cancellation)
        {
            string executable = ExtractBackend();
            var start = new ProcessStartInfo {
                FileName = executable, WorkingDirectory = Path.GetDirectoryName(executable),
                Arguments = "--input " + Quote(input) + " --output " + Quote(output) +
                    " --settings " + Quote(settings) + " --temp-directory " + Quote(tempDirectory) +
                    " --resonite-install-path " + Quote(resonitePath),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            var log = new ConcurrentQueue<string>();
            using (var process = new Process { StartInfo = start, EnableRaisingEvents = true })
            {
                var completed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.Exited += (_, __) => completed.TrySetResult(process.ExitCode);
                DataReceivedEventHandler receive = (_, e) => {
                    if (string.IsNullOrWhiteSpace(e.Data)) return;
                    log.Enqueue(e.Data);
                    if (e.Data.StartsWith("MA-RESO PROGRESS ")) progress.Enqueue(e.Data.Substring(17));
                };
                process.OutputDataReceived += receive;
                process.ErrorDataReceived += receive;
                cancellation.ThrowIfCancellationRequested();
                if (!process.Start()) throw new InvalidOperationException("Resonite変換エンジンを起動できませんでした。");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                using (cancellation.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } }))
                {
                    int exit = await completed.Task;
                    process.WaitForExit();
                    File.WriteAllLines(Path.Combine(tempDirectory, "backend.log"), log.ToArray());
                    cancellation.ThrowIfCancellationRequested();
                    if (exit != 0 || !File.Exists(output) || new FileInfo(output).Length == 0)
                        throw new InvalidOperationException("Resonite変換に失敗しました (exit " + exit + ")。ログ: " + Path.Combine(tempDirectory, "backend.log") + "\n" + string.Join("\n", log.ToArray().Reverse().Take(12).Reverse()));
                }
            }
        }
    }
}
