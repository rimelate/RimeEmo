using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace RimeEmo.Editor
{
    [InitializeOnLoad]
    internal static class RimeEmoUpdates
    {
        internal const string PackageName = "io.github.rimelate.rimeemo";
        internal const string RepositoryUrl = "https://github.com/rimelate/RimeEmo";
        internal const string ListingUrl = "https://raw.githubusercontent.com/rimelate/RimeEmo/vpm/index.json";
        private const string LatestUrl = "https://raw.githubusercontent.com/rimelate/RimeEmo/vpm/latest.json";
        private const string Prefix = "RimeEmo.Updates.";
        private const double IntervalSeconds = 12 * 60 * 60;
        private static readonly Regex SemVer = new Regex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?\z", RegexOptions.CultureInvariant);

        [Serializable]
        internal sealed class Release
        {
            public string name;
            public string version;
            public string releaseUrl;
        }

        internal enum CheckState { Idle, Checking, Current, Available, Failed, UnknownVersion }
        internal static event Action Changed;
        internal static CheckState State { get; private set; }
        internal static Release Latest { get; private set; }
        private static UnityWebRequest request;
        private static string currentVersion;

        static RimeEmoUpdates()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Cancel;
            EditorApplication.quitting += Cancel;
            if (TryReadRelease(EditorPrefs.GetString(Prefix + "Latest", ""), out var cached)) Latest = cached;
        }

        internal static string CurrentVersion
        {
            get
            {
                if (currentVersion != null) return currentVersion;
                try
                {
                    var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(RimeEmoUpdates).Assembly);
                    string path = package != null && package.name == PackageName
                        ? Path.Combine(package.resolvedPath, "package.json")
                        : Path.Combine(Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath("fa02794b94cfd9443b94bd95991691fd")) ?? "", "../package.json");
                    var manifest = JsonUtility.FromJson<Release>(File.ReadAllText(path));
                    currentVersion = manifest != null && manifest.name == PackageName && IsVersion(manifest.version) ? manifest.version : "";
                }
                catch { currentVersion = ""; }
                return currentVersion;
            }
        }

        internal static bool Automatic
        {
            get => EditorPrefs.GetBool(Prefix + "Automatic", true);
            set
            {
                EditorPrefs.SetBool(Prefix + "Automatic", value);
                if (!value) Cancel();
                Changed?.Invoke();
                if (value) Check();
            }
        }

        internal static bool HasUpdate => Latest != null && IsNewerStable(Latest.version, CurrentVersion);
        internal static bool ShowBanner => HasUpdate && EditorPrefs.GetString(Prefix + "Ignored", "") != Latest.version;
        internal static void Dismiss()
        {
            if (Latest != null) EditorPrefs.SetString(Prefix + "Ignored", Latest.version);
            Changed?.Invoke();
        }

        internal static void OpenVcc() => Application.OpenURL("vcc://vpm/addRepo?url=" + Uri.EscapeDataString(ListingUrl));
        internal static void OpenRelease() => Application.OpenURL(HasUpdate ? Latest.releaseUrl : RepositoryUrl + "/releases");

        internal static void Check(bool force = false)
        {
            if (Application.isBatchMode || request != null || (!force && !Automatic)) return;
            if (string.IsNullOrEmpty(CurrentVersion))
            {
                State = CheckState.UnknownVersion;
                Changed?.Invoke();
                return;
            }
            double now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            double.TryParse(EditorPrefs.GetString(Prefix + "LastAttempt", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out double last);
            if (!force && now >= last && now - last < IntervalSeconds) return;
            EditorPrefs.SetString(Prefix + "LastAttempt", now.ToString(CultureInfo.InvariantCulture));
            if (force) EditorPrefs.DeleteKey(Prefix + "Ignored");
            State = CheckState.Checking;
            try
            {
                request = UnityWebRequest.Get(LatestUrl);
                request.timeout = 15;
                request.SendWebRequest();
                EditorApplication.update += Poll;
            }
            catch
            {
                Cancel();
                State = CheckState.Failed;
            }
            Changed?.Invoke();
        }

        private static void Poll()
        {
            if (request == null || !request.isDone) return;
            try
            {
                if (request.result != UnityWebRequest.Result.Success || !TryReadRelease(request.downloadHandler.text, out var release))
                {
                    State = CheckState.Failed;
                    return;
                }
                Latest = release;
                EditorPrefs.SetString(Prefix + "Latest", JsonUtility.ToJson(release));
                State = HasUpdate ? CheckState.Available : CheckState.Current;
            }
            catch { State = CheckState.Failed; }
            finally
            {
                Cancel();
                Changed?.Invoke();
            }
        }

        private static void Cancel()
        {
            EditorApplication.update -= Poll;
            if (request != null) { request.Abort(); request.Dispose(); request = null; }
            if (State == CheckState.Checking) State = CheckState.Idle;
        }

        internal static bool TryReadRelease(string json, out Release release)
        {
            release = null;
            if (string.IsNullOrEmpty(json) || json.Length > 16384) return false;
            try
            {
                var candidate = JsonUtility.FromJson<Release>(json);
                if (candidate == null || candidate.name != PackageName || !IsVersion(candidate.version)
                    || SemVer.Match(candidate.version).Groups[4].Success
                    || candidate.releaseUrl != RepositoryUrl + "/releases/tag/v" + candidate.version) return false;
                release = candidate;
                return true;
            }
            catch { return false; }
        }

        private static bool IsVersion(string version)
        {
            if (string.IsNullOrEmpty(version) || version.Length > 128) return false;
            var match = SemVer.Match(version);
            if (!match.Success) return false;
            foreach (string part in match.Groups[4].Value.Split('.'))
                if (part.Length > 1 && part[0] == '0' && Regex.IsMatch(part, @"^[0-9]+$")) return false;
            return true;
        }

        internal static bool IsNewerStable(string available, string installed)
        {
            if (!IsVersion(available) || !IsVersion(installed)) return false;
            var next = SemVer.Match(available);
            var current = SemVer.Match(installed);
            if (next.Groups[4].Success) return false;
            for (int i = 1; i <= 3; i++)
            {
                string a = next.Groups[i].Value, b = current.Groups[i].Value;
                int comparison = a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
                if (comparison != 0) return comparison > 0;
            }
            return current.Groups[4].Success;
        }
    }
}
