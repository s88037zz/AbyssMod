using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Utility.Toast;

namespace AbyssMod.Services
{
    /// <summary>
    /// 基于清单（Manifest）的翻译缓存管理器，支持本地持久化。
    ///
    /// <para>使用示例：</para>
    /// <code>
    ///   await cache.LoadAsync("names");
    ///   await cache.LoadAsync("novels", "10005");
    /// </code>
    ///
    /// <para>加载流程：</para>
    /// <list type="number">
    ///   <item>检查清单中是否存在资源哈希</item>
    ///   <item>如果本地缓存文件存在，计算其规范化哈希值</item>
    ///   <item>哈希匹配 → 使用本地缓存</item>
    ///   <item>哈希不匹配或不存在 → 从远程获取并保存到本地缓存</item>
    /// </list>
    /// </summary>
    public class TranslationCache
    {
        private readonly string _cdn;
        private readonly string _cacheDir;
        private readonly string _language;
        private readonly string _remoteLanguage;
        private readonly bool _isFallbackMode;
        private readonly HttpClient _client;
        private Manifest _manifest;

        /// <summary>防止同一资源并发加载的锁集合。</summary>
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

        /// <summary>锁清理计数器，用于定期清理无引用的锁。</summary>
        private int _lockCleanupCounter;

        private const int LockCleanupInterval = 32;

        /// <summary>JSON 序列化选项（用于保存缓存文件）。</summary>
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false,
        };

        /// <summary>UTF-8 编码（无 BOM），用于所有文件 I/O。</summary>
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        /// <summary>
        /// 初始化翻译缓存管理器。
        /// </summary>
        /// <param name="cdn">CDN 根地址。</param>
        /// <param name="cacheDir">本地缓存根目录。</param>
        /// <param name="language">目标语言代码。</param>
        /// <param name="client">HTTP 客户端实例。</param>
        public TranslationCache(string cdn, string cacheDir, string language, HttpClient client)
        {
            _cdn = cdn.TrimEnd('/');
            _cacheDir = cacheDir;
            _language = language;
            _client = client;

            // 当请求简体 (zh_Hans) 但远程只有繁体 (zh_Hant) 时，
            // 自动下载 zh_Hant 文件并转换为简体后缓存为 zh_Hans。
            _isFallbackMode = string.Equals(
                _language, "zh_Hans",
                StringComparison.OrdinalIgnoreCase
            );
            _remoteLanguage = _isFallbackMode ? "zh_Hant" : _language;

            // 新结构按类型分目录，具体子目录在写文件时按需创建
            Directory.CreateDirectory(_cacheDir);
        }

        /// <summary>获取当前已加载的翻译清单。</summary>
        public Manifest Manifest => _manifest;

        /// <summary>本地缓存根目录（translations/）。</summary>
        public string CacheDir => _cacheDir;

        /// <summary>
        /// 获取并缓存远程翻译清单。
        /// 失败时回退到本地已缓存的清单。
        /// </summary>
        public async Task FetchManifestAsync()
        {
            if (string.IsNullOrWhiteSpace(_cdn))
            {
                TryLoadLocalManifest(
                    TranslationPaths.BuildCachePath(_cacheDir, TranslationPaths.Manifest, _language)
                );
                return;
            }

            var url = TranslationPaths.BuildRemoteUrl(
                _cdn, TranslationPaths.Manifest, _isFallbackMode ? _remoteLanguage : _language
            );
            var path = TranslationPaths.BuildCachePath(
                _cacheDir,
                TranslationPaths.Manifest,
                _language
            );

            try
            {
                var response = await _client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    _manifest = JsonSerializer.Deserialize<Manifest>(json);
                    if (_manifest == null)
                    {
                        Logger.Warn("Remote manifest parse returned null");
                    }
                    else
                    {
                        EnsureCacheDirectory(path);
                        await File.WriteAllTextAsync(path, json, Utf8);
                        Logger.Info($"Manifest loaded ({_language}). Hash: {_manifest.Hash}");
                        return;
                    }
                }
                Logger.Warn($"Manifest fetch returned {response.StatusCode}");
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to fetch manifest: {e.Message}");
            }

            // 回退：尝试加载本地缓存的清单
            TryLoadLocalManifest(path);
        }

        /// <summary>
        /// 尝试从磁盘加载之前缓存的清单文件。
        /// </summary>
        private void TryLoadLocalManifest(string path)
        {
            if (!File.Exists(path))
            {
                Logger.Warn(
                    "No local manifest cache available, will fetch without hash verification."
                );
                Toast.Warn("翻译服务", "翻译清单不可用，将直接请求翻译");
                return;
            }

            try
            {
                var json = File.ReadAllText(path, Utf8);
                _manifest = JsonSerializer.Deserialize<Manifest>(json);
                if (_manifest != null)
                {
                    Logger.Info(
                        $"Loaded cached manifest from local ({_language}). Hash: {_manifest.Hash}"
                    );
                    Toast.Warn("翻译服务", "无法连接远程，使用本地翻译清单");
                }
                else
                {
                    Logger.Warn("Cached manifest parse returned null");
                }
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to load local manifest: {e.Message}");
            }
        }

        /// <summary>
        /// 从 CDN 同步 <c>legacy/add-on/ui_misc</c> 兜底缓存（manifest.add_on.ui_misc）。
        /// </summary>
        public async Task SyncLegacyUiMiscAsync()
        {
            if (string.IsNullOrWhiteSpace(_cdn))
                return;

            const string category = TranslationPaths.LegacyUiMisc;
            string cacheKey = $"{_language}/legacy/add-on/{category}";
            string legacyCachePath = TranslationPaths.BuildLegacyAddOnCachePath(
                _cacheDir,
                category,
                _language
            );
            string addOnCachePath = TranslationPaths.BuildAddOnCachePath(
                _cacheDir,
                category,
                _language
            );
            string remoteUrl = TranslationPaths.BuildLegacyAddOnRemoteUrl(
                _cdn, category, _isFallbackMode ? _remoteLanguage : _language
            );
            // 回退模式下跳过哈希缓存验证
            string expectedHash = _isFallbackMode ? null : GetAddOnManifestHash(category);

            var semaphore = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                if (!_isFallbackMode && expectedHash != null && File.Exists(legacyCachePath))
                {
                    string localHash = HashFile(legacyCachePath);
                    if (localHash == expectedHash)
                    {
                        CopyLegacyToAddOnIfNewer(legacyCachePath, addOnCachePath);
                        Logger.Info("legacy/add-on/ui_misc cache hit");
                        return;
                    }
                }

                Dictionary<string, string> remote;
                try
                {
                    var response = await _client.GetAsync(remoteUrl);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        // 过渡期：legacy 路径不存在时回退 add-on/ui_misc
                        var fallbackUrl = TranslationPaths.BuildAddOnRemoteUrl(
                            _cdn, category, _isFallbackMode ? _remoteLanguage : _language
                        );
                        response = await _client.GetAsync(fallbackUrl);
                    }

                    if (!response.IsSuccessStatusCode)
                        return;

                    var json = await response.Content.ReadAsStringAsync();
                    remote = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (remote == null || remote.Count == 0)
                        return;
                }
                catch (Exception e)
                {
                    Logger.Warn($"legacy/add-on/{category} fetch failed: {e.Message}");
                    return;
                }

                // 回退模式：将远程获取的繁体值转换为简体
                if (_isFallbackMode)
                    ChineseConverter.ConvertDictionaryInPlace(remote);

                Dictionary<string, string> local = File.Exists(legacyCachePath)
                    ? LoadFromFile(legacyCachePath) ?? new Dictionary<string, string>()
                    : File.Exists(addOnCachePath)
                        ? LoadFromFile(addOnCachePath) ?? new Dictionary<string, string>()
                        : new Dictionary<string, string>();

                var merged = new Dictionary<string, string>(local);
                foreach (var kv in remote)
                    merged[kv.Key] = kv.Value;

                SaveToFile(legacyCachePath, merged);
                SaveToFile(addOnCachePath, merged);
                Logger.Info(
                    $"legacy/add-on/{category} synced: +{remote.Count} remote, {merged.Count} total"
                );
            }
            finally
            {
                semaphore.Release();
                CleanupLocksIfNeeded();
            }
        }

        private static void CopyLegacyToAddOnIfNewer(string legacyPath, string addOnPath)
        {
            if (!File.Exists(legacyPath))
                return;

            var legacy = LoadFromFile(legacyPath);
            if (legacy == null || legacy.Count == 0)
                return;

            Dictionary<string, string> addOn = File.Exists(addOnPath)
                ? LoadFromFile(addOnPath) ?? new Dictionary<string, string>()
                : new Dictionary<string, string>();

            var merged = new Dictionary<string, string>(addOn);
            foreach (var kv in legacy)
                merged[kv.Key] = kv.Value;
            SaveToFile(addOnPath, merged);
        }

        /// <summary>
        /// 从 CDN 同步 <c>add-on/{category}/</c> 社群精翻缓存。
        /// 远程条目覆盖本地同 key；本地独有 key 保留。
        /// </summary>
        public async Task SyncAddOnFromCdnAsync()
        {
            if (string.IsNullOrWhiteSpace(_cdn))
                return;

            var categories = CollectAddOnCategoryCandidates().ToList();
            if (categories.Count == 0)
                return;

            Logger.Info($"Syncing add-on/ from CDN ({categories.Count} categories)...");
            var tasks = categories.Select(SyncAddOnCategoryAsync);
            await Task.WhenAll(tasks);
        }

        private IEnumerable<string> CollectAddOnCategoryCandidates()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var cat in TextClassifier.AllCustomCategories)
                set.Add(cat);

            set.Add(TranslationPaths.Items);
            set.Add(TranslationPaths.Ui);
            set.Add("dictionary");

            set.Add(TranslationPaths.LegacyUiMisc);
            set.Add("ui_misc");

            foreach (var cat in TranslationPaths.EnumerateLocalCategories(_cacheDir, _language))
                set.Add(cat);

            if (_manifest?.AddOn != null)
            {
                foreach (var cat in _manifest.AddOn.Keys)
                    set.Add(cat);
            }

            return set;
        }

        private async Task SyncAddOnCategoryAsync(string category)
        {
            string cacheKey  = $"{_language}/add-on/{category}";
            string cachePath = TranslationPaths.BuildAddOnCachePath(_cacheDir, category, _language);
            string remoteUrl = TranslationPaths.BuildAddOnRemoteUrl(
                _cdn, category, _isFallbackMode ? _remoteLanguage : _language
            );
            // 回退模式下跳过哈希缓存验证
            string expectedHash = _isFallbackMode ? null : GetAddOnManifestHash(category);

            var semaphore = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                if (!_isFallbackMode && expectedHash != null && File.Exists(cachePath))
                {
                    string localHash = HashFile(cachePath);
                    if (localHash == expectedHash)
                    {
                        Logger.Info($"add-on/{category} cache hit");
                        return;
                    }
                }

                Dictionary<string, string> remote;
                try
                {
                    var response = await _client.GetAsync(remoteUrl);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        return;

                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Warn($"add-on/{category} fetch returned {response.StatusCode}");
                        return;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    remote = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (remote == null || remote.Count == 0)
                        return;
                }
                catch (Exception e)
                {
                    Logger.Warn($"add-on/{category} fetch failed: {e.Message}");
                    return;
                }

                // 回退模式：将远程获取的繁体值转换为简体
                if (_isFallbackMode)
                    ChineseConverter.ConvertDictionaryInPlace(remote);

                Dictionary<string, string> local = File.Exists(cachePath)
                    ? LoadFromFile(cachePath) ?? new Dictionary<string, string>()
                    : new Dictionary<string, string>();

                var merged = new Dictionary<string, string>(local);
                foreach (var kv in remote)
                    merged[kv.Key] = kv.Value;

                SaveToFile(cachePath, merged);
                Logger.Info($"add-on/{category} synced from CDN: +{remote.Count} remote, {merged.Count} total");
            }
            finally
            {
                semaphore.Release();
                CleanupLocksIfNeeded();
            }
        }

        private string GetAddOnManifestHash(string category)
        {
            if (_manifest?.AddOn == null)
                return null;
            return _manifest.AddOn.TryGetValue(category, out var hash) ? hash : null;
        }

        /// <summary>
        /// 从 CDN 同步 <c>other/{category}/</c> 机翻/校對缓存。
        /// 远程条目覆盖本地同 key；本地独有 key 保留。
        /// </summary>
        public async Task SyncOtherFromCdnAsync()
        {
            if (string.IsNullOrWhiteSpace(_cdn))
                return;

            var categories = CollectOtherCategoryCandidates().ToList();
            if (categories.Count == 0)
                return;

            Logger.Info($"Syncing other/ from CDN ({categories.Count} categories)...");
            var tasks = categories.Select(SyncOtherCategoryAsync);
            await Task.WhenAll(tasks);
        }

        private IEnumerable<string> CollectOtherCategoryCandidates()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var cat in TextClassifier.AllCustomCategories)
                set.Add(cat);

            foreach (var cat in TranslationPaths.EnumerateOtherCategories(_cacheDir, _language))
                set.Add(cat);

            if (_manifest?.Other != null)
            {
                foreach (var cat in _manifest.Other.Keys)
                    set.Add(cat);
            }

            return set;
        }

        private async Task SyncOtherCategoryAsync(string category)
        {
            string cacheKey  = $"{_language}/other/{category}";
            string cachePath = TranslationPaths.BuildOtherCachePath(_cacheDir, category, _language);
            string remoteUrl = TranslationPaths.BuildOtherRemoteUrl(
                _cdn, category, _isFallbackMode ? _remoteLanguage : _language
            );
            // 回退模式下跳过哈希缓存验证
            string expectedHash = _isFallbackMode ? null : GetOtherManifestHash(category);

            var semaphore = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                if (!_isFallbackMode && expectedHash != null && File.Exists(cachePath))
                {
                    string localHash = HashFile(cachePath);
                    if (localHash == expectedHash)
                    {
                        Logger.Info($"other/{category} cache hit");
                        return;
                    }
                }

                Dictionary<string, string> remote;
                try
                {
                    var response = await _client.GetAsync(remoteUrl);
                    if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                        return;

                    if (!response.IsSuccessStatusCode)
                    {
                        Logger.Warn($"other/{category} fetch returned {response.StatusCode}");
                        return;
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    remote = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                    if (remote == null || remote.Count == 0)
                        return;
                }
                catch (Exception e)
                {
                    Logger.Warn($"other/{category} fetch failed: {e.Message}");
                    return;
                }

                // 回退模式：将远程获取的繁体值转换为简体
                if (_isFallbackMode)
                    ChineseConverter.ConvertDictionaryInPlace(remote);

                Dictionary<string, string> local = File.Exists(cachePath)
                    ? LoadFromFile(cachePath) ?? new Dictionary<string, string>()
                    : new Dictionary<string, string>();

                // 远程优先，保留本地尚未发布的条目
                var merged = new Dictionary<string, string>(local);
                foreach (var kv in remote)
                    merged[kv.Key] = kv.Value;

                SaveToFile(cachePath, merged);
                Logger.Info($"other/{category} synced from CDN: +{remote.Count} remote, {merged.Count} total");
            }
            finally
            {
                semaphore.Release();
                CleanupLocksIfNeeded();
            }
        }

        private string GetOtherManifestHash(string category)
        {
            if (_manifest?.Other == null)
                return null;
            return _manifest.Other.TryGetValue(category, out var hash) ? hash : null;
        }

        /// <summary>
        /// 加载翻译数据，支持缓存感知逻辑。
        /// </summary>
        /// <param name="type">翻译类型：names、words 或 novels。</param>
        /// <param name="id">可选标识符（如 novels 的 novelId）。</param>
        /// <returns>加载的字典，失败时返回 null。</returns>
        public async Task<Dictionary<string, string>> LoadAsync(string type, string id = null)
        {
            string cacheKey = id != null ? $"{_language}/{type}/{id}" : $"{_language}/{type}";
            string cachePath = TranslationPaths.BuildCachePath(_cacheDir, type, _language, id);

            // 本地自定义类别（不在 CDN 扁平字典中）直接读本地文件
            bool isLocalOnly =
                !TranslationPaths.IsCdnFlatType(type) && type != TranslationPaths.Novels;
            if (isLocalOnly)
            {
                if (File.Exists(cachePath))
                {
                    var local = LoadFromFile(cachePath);
                    Logger.Info($"Local-only category '{type}' loaded: {local?.Count ?? 0} entries");
                    return local;
                }

                // 回退模式：尝试从 zh_Hant 本地缓存加载并转为简体
                if (_isFallbackMode)
                {
                    var hantPath = TranslationPaths.BuildCachePath(
                        _cacheDir, type, _remoteLanguage, id
                    );
                    if (File.Exists(hantPath))
                    {
                        var hantData = LoadFromFile(hantPath);
                        if (hantData != null && hantData.Count > 0)
                        {
                            var converted = ChineseConverter.ConvertDictionary(hantData);
                            SaveToFile(cachePath, converted);
                            Logger.Info(
                                $"Local-only category '{type}' converted from {_remoteLanguage}: {converted.Count} entries"
                            );
                            return converted;
                        }
                    }
                }

                // 文件不存在视为空（未有人工翻译），返回空字典而非 null，避免 Warn 噪音
                return new System.Collections.Generic.Dictionary<string, string>();
            }

            string remoteUrl    = TranslationPaths.BuildRemoteUrl(
                _cdn, type, _isFallbackMode ? _remoteLanguage : _language, id
            );
            // 回退模式下：zh_Hant 清单的哈希不匹配转换后的 zh_Hans 缓存，
            // 因此跳过哈希缓存验证，直接尝试从远程获取。
            string expectedHash = _isFallbackMode ? null : GetManifestHash(type, id);

            // 序列化同一资源的并发加载
            var semaphore = _locks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
            await semaphore.WaitAsync();
            try
            {
                // CDN 未配置：仅使用本地 cache
                if (string.IsNullOrWhiteSpace(_cdn))
                {
                    if (File.Exists(cachePath))
                    {
                        Logger.Info($"CDN disabled, loading local cache: {cacheKey}");
                        return LoadFromFile(cachePath);
                    }
                    Logger.Warn($"CDN disabled and no local cache for {cacheKey}");
                    return new Dictionary<string, string>();
                }

                // 如果清单中有预期哈希值，先检查本地缓存
                if (expectedHash != null && File.Exists(cachePath))
                {
                    string localHash = HashFile(cachePath);
                    if (localHash == expectedHash)
                    {
                        Logger.Info($"Cache hit: {cacheKey}");
                        return LoadFromFile(cachePath);
                    }
                    Logger.Info(
                        $"Cache hash mismatch for {cacheKey}, "
                            + $"expected={expectedHash}, local={localHash}"
                    );
                }

                // 从远程获取
                Logger.Info($"Fetching from remote: {remoteUrl}");
                var data = await GetAsync<Dictionary<string, string>>(remoteUrl);
                if (data != null)
                {
                    // 回退模式：将远程获取的繁体值统一转换为简体
                    if (_isFallbackMode && data.Count > 0)
                    {
                        ChineseConverter.ConvertDictionaryInPlace(data);
                        Logger.Info(
                            $"Converted {data.Count} entries from {_remoteLanguage} to {_language} for '{type}'"
                        );
                    }

                    // ability_descriptions 尚无 manifest 哈希，且社群 repo 常领先 CDN；
                    // 合并而非覆盖，保留本机已有、远端尚未发布的条目。
                    if (type == TranslationPaths.AbilityDescriptions && File.Exists(cachePath))
                    {
                        var local = LoadFromFile(cachePath) ?? new Dictionary<string, string>();
                        int remoteCount = data.Count;
                        var merged = new Dictionary<string, string>(local);
                        foreach (var kv in data)
                            merged[kv.Key] = kv.Value;
                        data = merged;
                        Logger.Info(
                            $"ability_descriptions merged: {data.Count} total "
                                + $"(remote {remoteCount}, +{data.Count - remoteCount} local-only kept)"
                        );
                    }

                    SaveToFile(cachePath, data);
                }
                else
                {
                    // 远程获取失败 → 回退到本地缓存（即使哈希不匹配）
                    Logger.Warn($"Remote fetch failed for {cacheKey}, trying local fallback.");
                    if (File.Exists(cachePath))
                    {
                        data = LoadFromFile(cachePath);
                        Logger.Info($"Loaded stale cache for {cacheKey}");
                    }
                }
                return data;
            }
            finally
            {
                semaphore.Release();
                CleanupLocksIfNeeded();
            }
        }

        /// <summary>
        /// 定期移除不再竞争的 SemaphoreSlim 条目。
        /// 防止 _locks 字典无限增长。
        /// </summary>
        private void CleanupLocksIfNeeded()
        {
            if (++_lockCleanupCounter % LockCleanupInterval != 0)
                return;

            // 仅移除无等待者的条目 - 安全删除空闲信号量
            var keysToRemove = new List<string>();
            foreach (var kvp in _locks)
            {
                if (kvp.Value.CurrentCount > 0) // 无活动等待者
                    keysToRemove.Add(kvp.Key);
            }

            foreach (var key in keysToRemove)
            {
                if (_locks.TryRemove(key, out var sem) && sem.CurrentCount > 0)
                    sem.Dispose();
            }
        }

        /// <summary>
        /// 从清单中获取指定类型/ID 组合的预期哈希值。
        /// </summary>
        /// <returns>清单不包含此资源时返回 null。</returns>
        private string GetManifestHash(string type, string id)
        {
            if (_manifest == null)
                return null;

            if (type == TranslationPaths.Novels && id != null)
            {
                return _manifest.Novels != null
                && _manifest.Novels.TryGetValue(id, out var novelHash)
                    ? novelHash
                    : null;
            }

            string dynamic = _manifest.GetFileHash(type);
            if (!string.IsNullOrEmpty(dynamic))
                return dynamic;

            return type switch
            {
                TranslationPaths.Names => _manifest.Names,
                TranslationPaths.Titles => _manifest.Titles,
                TranslationPaths.Descriptions => _manifest.Descriptions,
                TranslationPaths.AnotherName => _manifest.AnotherName,
                TranslationPaths.AbilityDescriptions => _manifest.AbilityDescriptions,
                TranslationPaths.UiTexts => _manifest.UiTexts,
                _ => null,
            };
        }

        /// <summary>
        /// 发起 HTTP GET 请求并反序列化 JSON 响应。
        /// </summary>
        private async Task<T> GetAsync<T>(string url)
            where T : class
        {
            try
            {
                var response = await _client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadFromJsonAsync<T>();
            }
            catch (Exception e)
            {
                Logger.Error($"HTTP GET error for {url}: {e.Message}");
            }
            return null;
        }

        /// <summary>
        /// 从本地文件加载翻译字典。
        /// </summary>
        private static Dictionary<string, string> LoadFromFile(string path)
        {
            try
            {
                var json = File.ReadAllText(path, Utf8);
                return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            }
            catch (Exception e)
            {
                Logger.Error($"Failed to load translation cache {path}: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 将翻译字典保存到本地文件。
        /// </summary>
        private static void SaveToFile(string path, Dictionary<string, string> data)
        {
            EnsureCacheDirectory(path);

            var json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(path, json, Utf8);
        }

        private static void EnsureCacheDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }

        /// <summary>
        /// 计算翻译 JSON 文件的规范化哈希值。
        /// </summary>
        private static string HashFile(string path)
        {
            var json = File.ReadAllText(path, Utf8);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return GetHash(dict);
        }

        /// <summary>
        /// 计算字典的规范化哈希值（与 Python 脚本兼容）。
        /// </summary>
        private static string GetHash(Dictionary<string, string> dict)
        {
            if (dict == null)
                return null;

            var sb = new StringBuilder();
            foreach (var key in dict.Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                sb.Append(key);
                sb.Append('\0');
                sb.Append(dict[key]);
                sb.Append('\0');
            }

            var hash = MD5.HashData(Utf8.GetBytes(sb.ToString()));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}
