using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WslUsbManager
{
    /// <summary>
    /// 应用更新检查与下载（体验对齐 DeveloperTools/AdbForge）：
    /// 1. 优先读取 feed 分支的 latest.json（version / url / sha256 / notes，由 CI 在发版时维护）；
    /// 2. feed 不可达时回退 GitHub Releases API（api.github.com/repos/{repo}/releases/latest）；
    /// 3. 发现新版本可下载便携包 zip 到「下载\USBridge」，feed 来源附带 SHA256 自动校验。
    /// </summary>
    public static class UpdateService
    {
        public const string Repo = "ideastudios/USBridge";
        public const string FeedUrl = "https://raw.githubusercontent.com/" + Repo + "/feed/latest.json";
        private const string ReleasesApi = "https://api.github.com/repos/" + Repo + "/releases/latest";

        /// <summary>检查结果（Success = 网络与解析成功；HasNewVersion = 需要升级）</summary>
        public class CheckResult
        {
            public bool Success;
            public bool HasNewVersion;
            public string LatestVersion;   // 展示用版本号（原样保留，如 1.3.0）
            public string DownloadUrl;     // 便携包 zip 直链
            public string Sha256;          // zip 校验值（仅 feed 来源提供）
            public string NotesUrl;        // Release 页面（更新说明 / 手动下载入口）
            public string Notes;           // Release 说明文本（仅 API 来源才有）
            public string AssetName;
            public long AssetSize;
            public string Message;
        }

        // ---- feed latest.json 结构（CI 生成；JavaScriptSerializer 只填充属性，不填充字段）----
        private class FeedJson
        {
            public string version { get; set; }
            public string url { get; set; }
            public string sha256 { get; set; }
            public string notes { get; set; }
        }

        // ---- GitHub Releases API 结构（只取用到的字段）----
        private class ReleaseJson
        {
            public string tag_name { get; set; }
            public string html_url { get; set; }
            public string body { get; set; }
            public ReleaseAsset[] assets { get; set; }
        }

        private class ReleaseAsset
        {
            public string name { get; set; }
            public long size { get; set; }
            public string browser_download_url { get; set; }
        }

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 1 << 26 };

        static UpdateService()
        {
            // Win7 SP1 等老系统上 .NET 4.8 默认协议可能不含 TLS 1.2（GitHub 强制要求）
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
        }

        // ================= 检查更新 =================

        /// <summary>
        /// 检查更新：feed 优先，异常或解析失败时回退 GitHub Releases API。
        /// </summary>
        public static Task<CheckResult> CheckAsync()
        {
            return Task.Run(async delegate
            {
                CheckResult r = await CheckFeedAsync();
                if (r != null) return r;   // feed 成功（含「已是最新」）即不再请求 API
                return await CheckGitHubAsync();
            });
        }

        private static async Task<CheckResult> CheckFeedAsync()
        {
            try
            {
                string json = await HttpGetAsync(FeedUrl);
                FeedJson f = Json.Deserialize<FeedJson>(json);
                if (f == null || string.IsNullOrEmpty(f.version) || string.IsNullOrEmpty(f.url))
                    return null;   // feed 内容异常 → 交给 API 兜底

                Version latest;
                if (!TryParseVersion(f.version, out latest))
                    return null;

                Version current = new Version(App.AppVersion);
                if (latest <= current)
                    return new CheckResult { Success = true, Message = "当前已是最新版本（v" + App.AppVersion + "）。" };

                return new CheckResult
                {
                    Success = true,
                    HasNewVersion = true,
                    LatestVersion = f.version.Trim(),
                    DownloadUrl = f.url.Trim(),
                    Sha256 = (f.sha256 == null ? "" : f.sha256.Trim()),
                    NotesUrl = string.IsNullOrEmpty(f.notes) ? null : f.notes.Trim(),
                };
            }
            catch { return null; }   // feed 不可达 → 回退 GitHub API
        }

        private static async Task<CheckResult> CheckGitHubAsync()
        {
            try
            {
                string json = await HttpGetAsync(ReleasesApi);
                ReleaseJson rel = Json.Deserialize<ReleaseJson>(json);
                if (rel == null || string.IsNullOrEmpty(rel.tag_name))
                    return new CheckResult { Success = false, Message = "无法解析 GitHub Releases 数据。" };

                Version latest;
                if (!TryParseVersion(rel.tag_name, out latest))
                    return new CheckResult { Success = false, Message = "GitHub 返回了无法识别的版本号: " + rel.tag_name };

                Version current = new Version(App.AppVersion);
                if (latest <= current)
                    return new CheckResult { Success = true, Message = "当前已是最新版本（v" + App.AppVersion + "）。" };

                // 安装包：优先 USBridge-*.zip，其次任意 zip
                ReleaseAsset asset = null;
                if (rel.assets != null)
                {
                    foreach (ReleaseAsset a in rel.assets)
                    {
                        if (a.name != null && a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                            a.name.StartsWith("USBridge", StringComparison.OrdinalIgnoreCase)) { asset = a; break; }
                    }
                    if (asset == null)
                    {
                        foreach (ReleaseAsset a in rel.assets)
                        {
                            if (a.name != null && a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { asset = a; break; }
                        }
                    }
                }

                return new CheckResult
                {
                    Success = true,
                    HasNewVersion = true,
                    LatestVersion = rel.tag_name.Trim(),
                    DownloadUrl = asset == null ? null : asset.browser_download_url,
                    AssetName = asset == null ? null : asset.name,
                    AssetSize = asset == null ? 0 : asset.size,
                    NotesUrl = rel.html_url,
                    Notes = Truncate(rel.body),
                    Message = asset == null ? "Release 未附带便携包 zip。" : null,
                };
            }
            catch (Exception ex)
            {
                return new CheckResult { Success = false, Message = "无法连接更新源（" + ex.Message + "）。" };
            }
        }

        // ================= 下载 =================

        /// <summary>
        /// 下载文件到 dest。progress(readBytes, totalBytes) 在调用方的同步上下文上回调
        /// （UI 线程调用本方法时可直接更新控件）。
        /// </summary>
        public static async Task DownloadAsync(string url, string dest, Action<long, long> progress)
        {
            HttpWebRequest req = CreateRequest(url, 300000);
            using (HttpWebResponse resp = (HttpWebResponse)await GetResponseAsync(req))
            {
                if ((int)resp.StatusCode >= 400) throw new Exception("HTTP " + (int)resp.StatusCode);
                long total = resp.ContentLength;

                using (Stream src = resp.GetResponseStream())
                using (FileStream fs = new FileStream(dest, FileMode.Create, FileAccess.Write))
                {
                    byte[] buf = new byte[81920];
                    long read = 0;
                    while (true)
                    {
                        int n = await src.ReadAsync(buf, 0, buf.Length);
                        if (n <= 0) break;
                        fs.Write(buf, 0, n);
                        read += n;
                        if (total > 0 && progress != null) progress(read, total);
                    }
                }
            }
        }

        /// <summary>计算文件 SHA256（小写十六进制，与 CI 生成的校验值一致）</summary>
        public static string Sha256OfFile(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
            }
        }

        // ================= 内部 =================

        private static HttpWebRequest CreateRequest(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "USBridge/" + App.AppVersion;
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            return req;
        }

        private static Task<WebResponse> GetResponseAsync(HttpWebRequest req)
        {
            return Task.Factory.FromAsync<WebResponse>(req.BeginGetResponse, req.EndGetResponse, null);
        }

        private static async Task<string> HttpGetAsync(string url)
        {
            using (HttpWebResponse resp = (HttpWebResponse)await GetResponseAsync(CreateRequest(url, 10000)))
            {
                if ((int)resp.StatusCode >= 400) throw new Exception("HTTP " + (int)resp.StatusCode);
                using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    return await sr.ReadToEndAsync();
                }
            }
        }

        /// <summary>解析版本号，容忍前缀 v/V 与预发布后缀（如 v1.3.0-beta → 1.3.0）</summary>
        private static bool TryParseVersion(string text, out Version ver)
        {
            ver = null;
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim().TrimStart('v', 'V');
            int dash = t.IndexOf('-');
            if (dash > 0) t = t.Substring(0, dash);
            return Version.TryParse(t, out ver);
        }

        private static string Truncate(string text, int max = 400)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }
}
