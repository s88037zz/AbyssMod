using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbyssMod;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TMPro;
using Utility.Fonts;
using Utility.Toast;

namespace AbyssMod.Services;

/// <summary>
/// 翻译管理器：协调翻译数据的加载、缓存和查询。
/// 内部持有所有翻译数据。
/// </summary>
public class TranslationManager
{
    private readonly TranslationCache _cache;
    private readonly FontHelper _font;

    private readonly ConcurrentDictionary<string, Task> _loadingNovels = new();

    public Dictionary<string, string> Names { get; private set; } = [];
    public Dictionary<string, string> Titles { get; private set; } = [];
    public Dictionary<string, string> Descriptions { get; private set; } = [];

    /// <summary>非剧情类文本的合并字典（角色二つ名 another_name、技能描述 ability_descriptions 及所有本地类别）。</summary>
    public Dictionary<string, string> Texts { get; private set; } = [];

    /// <summary>
    /// 技能/觉醒描述专属字典（ability_descriptions）。
    /// 供 <see cref="AbyssMod.Patches.TextTranslator"/> 行内术语替换使用，
    /// 使酒馆卡片效果描述中的技能名与角色技能界面保持一致中文术语。
    /// </summary>
    public Dictionary<string, string> AbilityDescriptions { get; private set; } = [];
    public ConcurrentDictionary<string, Dictionary<string, string>> Novels { get; private set; } =
        new();
    public FontHelper Font => _font;

    public TranslationManager(TranslationCache cache, FontHelper font)
    {
        _cache = cache;
        _font = font;
    }

    public void Initialize()
    {
        Plugin.Instance.StartCoroutine(
            _font
                .LoadAsync(() =>
                {
                    Logger.Info($"Font loaded: {_font.Asset.name}");
                    TMP_Settings.fallbackFontAssets.Add(_font.Asset);
                })
                .WrapToIl2Cpp()
        );
        _ = LoadTranslationAsync();
    }

    public async Task LoadTranslationAsync()
    {
        if (!Config.Translation.Value)
            return;

        await _cache.FetchManifestAsync();

        // 作者类型：显式加载（含 CDN 哈希校验）
        var nameTask        = _cache.LoadAsync(TranslationPaths.Names);
        var titleTask       = _cache.LoadAsync(TranslationPaths.Titles);
        var descTask        = _cache.LoadAsync(TranslationPaths.Descriptions);
        var anotherNameTask = _cache.LoadAsync(TranslationPaths.AnotherName);
        var abilityTask     = _cache.LoadAsync(TranslationPaths.AbilityDescriptions);
        await Task.WhenAll(nameTask, titleTask, descTask, anotherNameTask, abilityTask);

        if (nameTask.Result != null)
        {
            Names = nameTask.Result;
            Logger.Info($"Character names translation loaded. Total: {Names.Count}");
        }
        else
        {
            Logger.Warn("Character names translation load failed");
            Toast.Warn("加载失败", "角色名称翻译加载失败");
        }

        if (titleTask.Result != null)
        {
            Titles = titleTask.Result;
            Logger.Info($"Novel titles translation loaded. Total: {Titles.Count}");
        }
        else
        {
            Logger.Warn("Novel titles translation load failed");
            Toast.Warn("加载失败", "剧情标题翻译加载失败");
        }

        if (descTask.Result != null)
        {
            Descriptions = descTask.Result;
            Logger.Info($"Novel descriptions translation loaded. Total: {Descriptions.Count}");
        }
        else
        {
            Logger.Warn("Novel descriptions translation load failed");
            Toast.Warn("加载失败", "剧情概括翻译加载失败");
        }

        // 保存 ability_descriptions 专属字典（供行内术语替换使用）
        if (abilityTask.Result != null)
            AbilityDescriptions = abilityTask.Result;

        // 合并：作者类型先合并，本地自定义类型最后合并（本地优先）
        var merged = new Dictionary<string, string>();
        MergeInto(merged, anotherNameTask.Result, "another_name");
        MergeInto(merged, abilityTask.Result, "ability_descriptions");

        // 本地自定义类别：自动扫描 translations/* 目录，支持动态新增类别
        var localCategories = TranslationPaths
            .EnumerateLocalCategories(_cache.CacheDir, Config.TranslationLanguage.Value)
            .ToList();

        if (localCategories.Count > 0)
        {
            var localTasks = localCategories
                .Select(cat => _cache.LoadAsync(cat))
                .ToList();
            await Task.WhenAll(localTasks);

            for (int i = 0; i < localCategories.Count; i++)
                MergeInto(merged, localTasks[i].Result, localCategories[i]);
        }

        Texts = merged;
        Logger.Info($"Non-story text translation merged. Total: {Texts.Count} (local categories: {localCategories.Count})");
    }

    private static void MergeInto(
        Dictionary<string, string> target,
        Dictionary<string, string> source,
        string label
    )
    {
        if (source == null)
        {
            Logger.Warn($"Text translation '{label}' load failed or empty");
            return;
        }

        foreach (var kv in source)
            target[kv.Key] = kv.Value;

        Logger.Info($"Text translation '{label}' loaded. Total: {source.Count}");
    }

    public async Task GetNovelTranslationAsync(string novelId)
    {
        if (Novels.ContainsKey(novelId))
            return;

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var existingTask = _loadingNovels.GetOrAdd(novelId, tcs.Task);

        if (existingTask != tcs.Task)
        {
            await existingTask;
            return;
        }

        try
        {
            var translations = await _cache.LoadAsync(TranslationPaths.Novels, novelId);
            if (translations != null)
            {
                Novels[novelId] = translations;
                Logger.Info($"Scenario translation loaded. Total: {translations.Count}");
            }
            else
            {
                Logger.Warn($"Translations loaded failed: {novelId}");
                Toast.Warn("加载失败", $"剧本ID: {novelId}");
            }
            tcs.SetResult();
        }
        catch (Exception ex)
        {
            tcs.SetException(ex);
            throw;
        }
        finally
        {
            _loadingNovels.TryRemove(novelId, out _);
        }
    }
}
