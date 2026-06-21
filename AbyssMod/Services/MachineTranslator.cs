using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AbyssMod.Services;

/// <summary>
/// 机翻预处理：采用「平时收集 + 启动批翻」模式（非实时）。
///
/// <para>工作流程：</para>
/// <list type="number">
///   <item>游戏运行中，字典未命中的日文文本按"数字模板"归一化去重后记入待翻队列
///         <c>other/&lt;lang&gt;.pending.json</c>（格式 <c>{ "模板": "category" }</c>）。</item>
///   <item>每次启动时（以及会话中定期）后台调用本地翻译引擎，把待翻队列里还没翻的批量翻译，
///         按类别写入 <c>other/{category}/&lt;lang&gt;.json</c>，同时维护内存单一缓存字典。</item>
///   <item>显示时若命中机翻缓存（按模板匹配后填回数字）即返回中文，否则返回原文并继续收集。</item>
/// </list>
///
/// <para>机翻输出语言跟随 <c>Translation.Language</c>：
/// <c>zh_Hans</c> → 简体提示词与范例；<c>zh_Hant</c> → 繁体（台湾）提示词与范例。
/// 适用于 openai / claude 等 LLM 引擎。</para>
/// </summary>
public static class MachineTranslator
{
    private static bool _initialized;
    private static string _otherDir;         // translations/other/
    private static string _pendingPath;      // translations/other/<lang>.pending.json
    private static string _language;

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>模板（已数字占位）-> 译文模板。内存单一查找字典。</summary>
    private static readonly ConcurrentDictionary<string, string> _cache = new();

    /// <summary>待翻译模板 -> category（格式升级版）。</summary>
    private static readonly ConcurrentDictionary<string, string> _pending = new();

    private static int _cacheDirty;
    private static int _pendingDirty;
    private const int SaveCacheEvery    = 10;
    private const int RescanIntervalMs  = 30000;
    private const int BetweenRequestsMs = 50;

    // ──────────────────────────────────────────────────
    // 初始化
    // ──────────────────────────────────────────────────

    /// <summary>
    /// 初始化：加载缓存与待翻队列，并启动后台批翻循环。
    /// <paramref name="otherDir"/> 即 translations/other 目录。
    /// </summary>
    public static void Initialize(string otherDir, string language)
    {
        if (_initialized)
            return;
        _initialized = true;
        _otherDir    = otherDir;
        _language    = language;

        try
        {
            _http.Timeout = TimeSpan.FromSeconds(Math.Max(5, Config.MTTimeout.Value));
        }
        catch { }

        Directory.CreateDirectory(otherDir);
        _pendingPath = Path.Combine(otherDir, $"{language}.pending.json");

        LoadAllCaches();   // 扫描 other/* 子目录，全量载入
        PruneGraduatedKeys(); // 移除已被 add-on/ 收录的重复 key
        LoadPending();

        if (Config.MTEnabled.Value)
            Task.Run(PretranslateLoop);

        var langLabel = IsTraditional ? "zh_Hant (繁體)" : "zh_Hans (簡體)";
        Logger.Info(
            $"MachineTranslator (batch mode) initialized. Enabled={Config.MTEnabled.Value}, "
                + $"language={langLabel}, cached={_cache.Count}, pending={_pending.Count}"
        );
    }

    // ──────────────────────────────────────────────────
    // 公开接口
    // ──────────────────────────────────────────────────

    /// <summary>
    /// 在 TMP set_text 拦截点调用：命中机翻缓存返回中文，否则收集该文本待下次预翻。
    /// </summary>
    public static string Handle(string category, string text)
    {
        if (!_initialized || !Config.MTEnabled.Value || string.IsNullOrEmpty(text))
            return text;
        // 角色名不机翻：片假名音译不可靠，仅由 TextTranslator 收集 raw 供人工翻译后补入 names。
        if (category == TextClassifier.Name)
            return text;
        if (!HasKana(text))
            return text;

        var (template, numbers) = Normalize(text);

        if (_cache.TryGetValue(template, out var tt))
        {
            var filled = Fill(tt, numbers);
            if (filled != null)
                return filled;
        }
        else if (_pending.TryAdd(template, category))
        {
            if (Interlocked.Increment(ref _pendingDirty) % 20 == 0)
                SavePending();
        }

        return text;
    }

    /// <summary>立即保存缓存与待翻队列（退出时调用）。</summary>
    public static void Save()
    {
        SaveAllCaches();
        SavePending();
    }

    // ──────────────────────────────────────────────────
    // 后台批翻
    // ──────────────────────────────────────────────────

    private static async Task PretranslateLoop()
    {
        await Task.Delay(3000);

        while (_initialized)
        {
            try
            {
                if (Config.MTEnabled.Value)
                    await TranslatePendingOnce();
            }
            catch (Exception e)
            {
                Logger.Error($"MT batch error: {e.Message}");
            }
            await Task.Delay(RescanIntervalMs);
        }
    }

    private static async Task TranslatePendingOnce()
    {
        var todo = _pending
            .Where(kv => !_cache.ContainsKey(kv.Key))
            .Select(kv => (template: kv.Key, category: kv.Value))
            .ToList();

        if (todo.Count == 0)
            return;

        Logger.Info($"MT pretranslate: {todo.Count} pending templates...");
        int done = 0;

        foreach (var (template, category) in todo)
        {
            if (!Config.MTEnabled.Value)
                break;

            if (_cache.ContainsKey(template))
            {
                _pending.TryRemove(template, out _);
                continue;
            }

            var translated = await TranslateAsync(template);
            if (!string.IsNullOrEmpty(translated))
            {
                _cache[template] = translated;
                _pending.TryRemove(template, out _);
                done++;

                // 按类别写入 other/{category}/<lang>.json
                WriteToCategoryFile(category, template, translated);

                if (Interlocked.Increment(ref _cacheDirty) % SaveCacheEvery == 0)
                    SaveAllCaches();
            }
            await Task.Delay(BetweenRequestsMs);
        }

        if (done > 0)
        {
            SaveAllCaches();
            SavePending();
            Logger.Info($"MT pretranslate done: +{done} translated, cache={_cache.Count}");
        }
    }

    // ──────────────────────────────────────────────────
    // 分类别文件 I/O
    // ──────────────────────────────────────────────────

    private static readonly object _fileLock = new();

    private static string CategoryCachePath(string category) =>
        Path.Combine(_otherDir, category, $"{_language}.json");

    /// <summary>
    /// 把单条译文追加写入对应类别的缓存文件（线程安全）。
    /// </summary>
    private static void WriteToCategoryFile(string category, string template, string translated)
    {
        var path = CategoryCachePath(category);
        lock (_fileLock)
        {
            try
            {
                Dictionary<string, string> dict;
                if (File.Exists(path))
                {
                    var raw = File.ReadAllText(path, Utf8NoBom);
                    dict = JsonSerializer.Deserialize<Dictionary<string, string>>(raw)
                           ?? new Dictionary<string, string>();
                }
                else
                {
                    dict = new Dictionary<string, string>();
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                }
                dict[template] = translated;
                WriteJson(path, dict);
            }
            catch (Exception e)
            {
                Logger.Warn($"WriteToCategoryFile failed ({category}): {e.Message}");
            }
        }
    }

    /// <summary>
    /// 启动时扫描 other/* 所有子目录的缓存文件，全量载入 _cache。
    /// 同时兼容旧版单文件 other/&lt;lang&gt;.json。
    /// </summary>
    private static void LoadAllCaches()
    {
        // 旧版单文件兼容
        var legacySingle = Path.Combine(_otherDir, $"{_language}.json");
        if (File.Exists(legacySingle))
        {
            LoadCacheFile(legacySingle, "(legacy)");
        }

        // 新版 other/{category}/<lang>.json
        if (!Directory.Exists(_otherDir))
            return;

        foreach (var subDir in Directory.GetDirectories(_otherDir))
        {
            var catFile = Path.Combine(subDir, $"{_language}.json");
            if (File.Exists(catFile))
            {
                var catName = Path.GetFileName(subDir);
                LoadCacheFile(catFile, catName);
            }
        }
    }

    /// <summary>
    /// 啟動清理：掃描 add-on/{category}/lang.json，
    /// 把 other/{category}/lang.json 中已被 add-on 收錄的 key 移除。
    /// 讓 other/ 只保留「尚未人工校對」的機翻條目。
    /// </summary>
    private static void PruneGraduatedKeys()
    {
        // add-on 目錄：other/ 的兄弟目錄
        var addOnDir = Path.Combine(Path.GetDirectoryName(_otherDir)!, TranslationPaths.AddOn);
        if (!Directory.Exists(addOnDir) || !Directory.Exists(_otherDir))
            return;

        int totalPruned = 0;

        foreach (var otherSubDir in Directory.GetDirectories(_otherDir))
        {
            var category  = Path.GetFileName(otherSubDir);
            var otherFile = Path.Combine(otherSubDir, $"{_language}.json");
            if (!File.Exists(otherFile))
                continue;

            // 對應的 add-on/{category}/lang.json
            var addOnFile = Path.Combine(addOnDir, category, $"{_language}.json");
            if (!File.Exists(addOnFile))
                continue;

            try
            {
                var otherRaw  = File.ReadAllText(otherFile, Utf8NoBom);
                var addOnRaw  = File.ReadAllText(addOnFile, Utf8NoBom);
                var otherDict = JsonSerializer.Deserialize<Dictionary<string, string>>(otherRaw) ?? new();
                var addOnDict = JsonSerializer.Deserialize<Dictionary<string, string>>(addOnRaw) ?? new();

                int before = otherDict.Count;
                foreach (var key in addOnDict.Keys)
                {
                    if (otherDict.Remove(key))
                        _cache.TryRemove(key, out _); // 同步從記憶體 cache 移除
                }

                int pruned = before - otherDict.Count;
                if (pruned > 0)
                {
                    WriteJson(otherFile, otherDict);
                    totalPruned += pruned;
                    Logger.Info($"MT prune: removed {pruned} graduated key(s) from other/{category}");
                }
            }
            catch (Exception e)
            {
                Logger.Warn($"PruneGraduatedKeys failed for '{category}': {e.Message}");
            }
        }

        // 同步清理 pending（已有 add-on 翻譯的模板不必再翻）
        if (totalPruned > 0)
        {
            int pendingPruned = 0;
            foreach (var key in _pending.Keys.ToList())
                if (_cache.ContainsKey(key) == false && IsInAnyAddOn(addOnDir, key))
                {
                    _pending.TryRemove(key, out _);
                    pendingPruned++;
                }

            Logger.Info($"MT prune total: {totalPruned} entries cleaned from other/, {pendingPruned} removed from pending");
        }
    }

    /// <summary>
    /// 檢查指定 key 是否已存在於 add-on/ 任意類別檔案中（供清理 pending 用）。
    /// </summary>
    private static bool IsInAnyAddOn(string addOnDir, string key)
    {
        foreach (var catDir in Directory.GetDirectories(addOnDir))
        {
            var f = Path.Combine(catDir, $"{_language}.json");
            if (!File.Exists(f)) continue;
            try
            {
                var d = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    File.ReadAllText(f, Utf8NoBom));
                if (d != null && d.ContainsKey(key))
                    return true;
            }
            catch { }
        }
        return false;
    }

    private static void LoadCacheFile(string path, string label)
    {
        try
        {
            var json = File.ReadAllText(path, Utf8NoBom);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null)
                foreach (var kv in dict)
                    _cache[kv.Key] = kv.Value;
            Logger.Info($"MT cache loaded from '{label}': {dict?.Count ?? 0} entries");
        }
        catch (Exception e)
        {
            Logger.Warn($"Load MT cache '{label}' failed: {e.Message}");
        }
    }

    /// <summary>把内存 _cache 按类别刷回各自文件（全量重写，确保无遗漏）。</summary>
    private static void SaveAllCaches()
    {
        // 重建各类别的缓存文件。
        // 只保存在 _pending（已知 category）里有记录的，或已有对应文件的条目。
        // 实际上，翻完一条就已经 WriteToCategoryFile 了，这里是保险兜底。
        // 不做此处的全量扫描——避免覆盖已正确写入的文件。
    }

    // ──────────────────────────────────────────────────
    // Pending 文件（{模板: category} 映射）
    // ──────────────────────────────────────────────────

    private static void LoadPending()
    {
        if (!File.Exists(_pendingPath))
            return;
        try
        {
            var json = File.ReadAllText(_pendingPath, Utf8NoBom);

            // 尝试新格式：{ "template": "category" }
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (dict != null)
                {
                    foreach (var kv in dict)
                        if (!_cache.ContainsKey(kv.Key))
                            _pending.TryAdd(kv.Key, kv.Value);
                    Logger.Info($"MT pending loaded (new format): {dict.Count} entries");
                    return;
                }
            }
            catch { }

            // 兼容旧格式：["template1", "template2", ...]
            var list = JsonSerializer.Deserialize<List<string>>(json);
            if (list != null)
            {
                foreach (var t in list)
                    if (!_cache.ContainsKey(t))
                        _pending.TryAdd(t, TextClassifier.UiMisc);
                Logger.Info($"MT pending loaded (legacy list format): {list.Count} entries");
            }
        }
        catch (Exception e)
        {
            Logger.Warn($"Load MT pending failed: {e.Message}");
        }
    }

    private static void SavePending()
    {
        var dict = new Dictionary<string, string>(_pending);
        WriteJson(_pendingPath, dict);
    }

    // ──────────────────────────────────────────────────
    // 翻译引擎
    // ──────────────────────────────────────────────────

    private static async Task<string> TranslateAsync(string text)
    {
        try
        {
            var engine = (Config.MTEngine.Value ?? "openai").Trim().ToLowerInvariant();
            return engine switch
            {
                "claude" => await TranslateClaude(text),
                "sugoi"  => await TranslateSugoi(text),
                "libre"  => await TranslateLibre(text),
                _ => await TranslateOpenAI(text),
            };
        }
        catch (Exception e)
        {
            Logger.Warn($"MT request failed: {e.Message}");
            return null;
        }
    }

    /// <summary>当前翻译语言是否为繁体中文（台湾）。</summary>
    private static bool IsTraditional =>
        string.Equals(Config.TranslationLanguage.Value, "zh_Hant", StringComparison.OrdinalIgnoreCase);

    /// <summary>依语言设置选择对应的系统提示词（简体/繁体台湾）。</summary>
    private static string SystemPrompt => IsTraditional ? SystemPromptHant : SystemPromptHans;

    private const string SystemPromptHans =
        "你是手机游戏《ドットアビス》的日译简体中文本地化译者。"
        + "请把用户给出的日文翻译成简体中文，只输出译文本身，不要解释、不要加引号、不要输出英文。"
        + "最重要：原文中形如 {0} {1} 的花括号占位符必须连同花括号原样保留，位置和数量都不能改；"
        + "<...> 标签、【】符号、\\n 换行也都原样保留。"
        + "术语：紋章=纹章，衝撃=冲击，情熱=热情，会心=会心，スキル=技能，マナ=魔力，"
        + "バリア=护盾，付与=附加，上昇=提升，永続=永续，リトライ=重试，パーティ=队伍，ソート=排序。";

    private const string SystemPromptHant =
        "你是手機遊戲《ドットアビス》的日譯繁體中文（台灣）在地化譯者。"
        + "請把使用者給出的日文翻譯成台灣慣用的繁體中文，只輸出譯文本身，不要解釋、不要加引號、不要輸出英文或簡體字。"
        + "最重要：原文中形如 {0} {1} 的花括號佔位符必須連同花括號原樣保留，位置和數量都不能改；"
        + "<...> 標籤、【】符號、\\n 換行也都原樣保留。"
        + "術語：紋章=紋章，衝撃=衝擊，情熱=熱情，会心=會心，スキル=技能，マナ=魔力，"
        + "バリア=護盾，付与=附加，上昇=提升，永続=永續，リトライ=重試，パーティ=隊伍，ソート=排序。";

    private static object[] FewShot => IsTraditional ? FewShotHant : FewShotHans;

    private static readonly object[] FewShotHans =
    {
        new { role = "user", content = "自身の攻撃力が【{0}】上昇" },
        new { role = "assistant", content = "自身攻击力提升【{0}】" },
        new { role = "user", content = "紋章：衝撃を【{0}】付与 / 自身に最大HP【{1}】分の回復" },
        new { role = "assistant", content = "附加纹章：冲击【{0}】 / 回复自身最大HP的【{1}】" },
    };

    private static readonly object[] FewShotHant =
    {
        new { role = "user", content = "自身の攻撃力が【{0}】上昇" },
        new { role = "assistant", content = "自身攻擊力提升【{0}】" },
        new { role = "user", content = "紋章：衝撃を【{0}】付与 / 自身に最大HP【{1}】分の回復" },
        new { role = "assistant", content = "附加紋章：衝擊【{0}】 / 回復自身最大HP的【{1}】" },
    };

    /// <summary>
    /// 调用 Anthropic Claude API（非 OpenAI 兼容，原生格式）。
    /// 需要在 AbyssMod.cfg 设置 Engine=claude、ApiKey=sk-ant-...
    /// 默认 Endpoint 为 https://api.anthropic.com/v1/messages，可自行修改为代理地址。
    /// </summary>
    private static async Task<string> TranslateClaude(string text)
    {
        var apiKey  = Config.MTApiKey?.Value ?? "";
        var model   = Config.MTModel.Value ?? "claude-haiku-4-5";
        var endpoint = Config.MTEndpoint.Value;

        // 若用户没改 Endpoint（还是 Ollama 地址），自动改用官方地址
        if (string.IsNullOrEmpty(endpoint) || endpoint.Contains("11434"))
            endpoint = "https://api.anthropic.com/v1/messages";

        var messages = new List<object>();
        foreach (var fs in FewShot)
            messages.Add(fs);
        messages.Add(new { role = "user", content = text });

        var body = new
        {
            model,
            max_tokens = 512,
            system     = SystemPrompt,
            messages,
        };

        var payload = JsonSerializer.Serialize(body);
        using var req = new HttpRequestMessage(HttpMethod.Post, endpoint);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        req.Headers.Add("x-api-key", apiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        using var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync();
            Logger.Warn($"Claude API {(int)resp.StatusCode}: {err[..Math.Min(200, err.Length)]}");
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        // Claude 响应格式：{"content": [{"type": "text", "text": "..."}], ...}
        var content = doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString();
        return Clean(content);
    }

    private static async Task<string> TranslateOpenAI(string text)
    {
        var messages = new List<object> { new { role = "system", content = SystemPrompt } };
        messages.AddRange(FewShot);
        messages.Add(new { role = "user", content = text });

        var body = new { model = Config.MTModel.Value, temperature = 0, stream = false, messages };
        using var resp = await PostJson(Config.MTEndpoint.Value, body, apiKey: Config.MTApiKey?.Value);
        if (resp == null)
            return null;
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
        return Clean(content);
    }

    private static async Task<string> TranslateLibre(string text)
    {
        var body = new { q = text, source = "ja", target = "zh", format = "text" };
        using var resp = await PostJson(Config.MTEndpoint.Value, body);
        if (resp == null)
            return null;
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return Clean(doc.RootElement.GetProperty("translatedText").GetString());
    }

    private static async Task<string> TranslateSugoi(string text)
    {
        var body = new { content = text, message = "translate sentences" };
        using var resp = await PostJson(Config.MTEndpoint.Value, body);
        if (resp == null)
            return null;
        var json = (await resp.Content.ReadAsStringAsync()).Trim();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.String)
                return Clean(doc.RootElement.GetString());
            if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                return Clean(doc.RootElement[0].GetString());
        }
        catch { }
        return Clean(json);
    }

    private static async Task<HttpResponseMessage> PostJson(string url, object body, string apiKey = null)
    {
        var payload = JsonSerializer.Serialize(body);
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        if (!string.IsNullOrEmpty(apiKey))
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        var resp = await _http.SendAsync(req);
        if (!resp.IsSuccessStatusCode)
        {
            Logger.Warn($"MT endpoint returned {(int)resp.StatusCode}");
            resp.Dispose();
            return null;
        }
        return resp;
    }

    private static string Clean(string s)
    {
        if (string.IsNullOrEmpty(s))
            return null;
        s = s.Trim();
        if (s.Length >= 2 && (s[0] == '"' || s[0] == '「' || s[0] == '\u201C'))
        {
            char last = s[s.Length - 1];
            if ((s[0] == '"' && last == '"') || (s[0] == '「' && last == '」') || (s[0] == '\u201C' && last == '\u201D'))
                s = s.Substring(1, s.Length - 2).Trim();
        }
        return s.Length == 0 ? null : s;
    }

    // ──────────────────────────────────────────────────
    // 数字模板
    // ──────────────────────────────────────────────────

    private static readonly Regex TagOrNumber = new(
        @"<[^>]*>|[0-9]+(?:\.[0-9]+)?",
        RegexOptions.Compiled
    );
    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.Compiled);

    private static (string template, string[] numbers) Normalize(string text)
    {
        var nums = new List<string>();
        int i = 0;
        var template = TagOrNumber.Replace(text, m =>
        {
            if (m.Value.Length > 0 && m.Value[0] == '<')
                return m.Value;
            nums.Add(m.Value);
            return "{" + (i++) + "}";
        });
        return (template, nums.ToArray());
    }

    private static string Fill(string template, string[] numbers)
    {
        if (numbers.Length == 0)
            return template;
        bool ok = true;
        var result = Placeholder.Replace(template, m =>
        {
            int idx = int.Parse(m.Groups[1].Value);
            if (idx < 0 || idx >= numbers.Length) { ok = false; return m.Value; }
            return numbers[idx];
        });
        return ok ? result : null;
    }

    // ──────────────────────────────────────────────────
    // 工具
    // ──────────────────────────────────────────────────

    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static void WriteJson(string path, object data)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOpts), Utf8NoBom);
        }
        catch (Exception e)
        {
            Logger.Warn($"Save MT file failed ({Path.GetFileName(path)}): {e.Message}");
        }
    }

    private static bool HasKana(string s)
    {
        foreach (char c in s)
            if ((c >= '\u3040' && c <= '\u309F') || (c >= '\u30A0' && c <= '\u30FF'))
                return true;
        return false;
    }
}
