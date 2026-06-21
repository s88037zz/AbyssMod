using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AbyssMod.Services;

/// <summary>
/// 翻译资源路径构建工具。
/// 负责生成远程 URL 和本地缓存路径。
/// </summary>
public static class TranslationPaths
{
    // ──────────────────────────────────────────────────
    // 类型常量（与作者 CDN 仓库一致）
    // ──────────────────────────────────────────────────
    public const string Manifest            = "manifest";
    public const string Names               = "names";
    public const string Titles              = "titles";
    public const string Descriptions        = "descriptions";
    public const string AnotherName         = "another_name";
    public const string AbilityDescriptions = "ability_descriptions";
    public const string Novels              = "novels";

    // 本仓库自定义类型（作者 CDN 无此目录，不会被覆盖，靠本地文件兜底）
    public const string Items  = "items";
    public const string Ui     = "ui";
    public const string Other  = "other";

    /// <summary>所有本地自定义类别的统一容器目录（translations/add-on/）。</summary>
    public const string AddOn  = "add-on";

    // ──────────────────────────────────────────────────
    // 保留类型集合（由 TranslationManager 扫描时跳过的目录）
    // ──────────────────────────────────────────────────

    /// <summary>
    /// 作者/框架保留的顶层目录名称。
    /// <see cref="TranslationManager"/> 自动扫描时遇到这些目录一律跳过。
    /// add-on 目录本身也列入，防止被误当成类别。
    /// </summary>
    public static readonly HashSet<string> ReservedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        Manifest, Names, Titles, Descriptions,
        AnotherName, AbilityDescriptions,
        Novels, Other, AddOn,
    };

    // ──────────────────────────────────────────────────
    // URL / 路径构建
    // ──────────────────────────────────────────────────

    /// <summary>
    /// 构建远程资源 URL。未知自定义类型统一按 {type}/{language}.json 处理（不再抛出异常）。
    /// </summary>
    public static string BuildRemoteUrl(string cdn, string type, string language, string id = null)
    {
        return type switch
        {
            Novels when id != null => $"{cdn}/{Novels}/{id}/{language}.json",
            Novels => throw new ArgumentException("Novel ID is required for novels type"),
            _ => $"{cdn}/{type}/{language}.json",
        };
    }

    /// <summary>
    /// 构建本地缓存文件路径。
    /// 本地自定义类别（不在 <see cref="ReservedTypes"/> 中）统一放在 <c>add-on/{type}/{language}.json</c>。
    /// </summary>
    public static string BuildCachePath(string cacheDir, string type, string language, string id = null)
    {
        if (type == Novels && id != null)
            return Path.Combine(cacheDir, Novels, id, $"{language}.json");
        if (type == Novels)
            throw new ArgumentException("Novel ID is required for novels type");

        // 本地自定义类别放 add-on/ 子目录
        if (!ReservedTypes.Contains(type))
            return Path.Combine(cacheDir, AddOn, type, $"{language}.json");

        return Path.Combine(cacheDir, type, $"{language}.json");
    }

    // ──────────────────────────────────────────────────
    // 本地自定义类别枚举（供 TranslationManager 扫描）
    // ──────────────────────────────────────────────────

    /// <summary>
    /// 枚举 <c>translations/add-on/</c> 下所有本地自定义类别目录名。
    /// 即：<paramref name="cacheDir"/>/add-on/ 下存在 &lt;language&gt;.json 的子目录。
    /// </summary>
    public static IEnumerable<string> EnumerateLocalCategories(string cacheDir, string language)
    {
        var addOnDir = Path.Combine(cacheDir, AddOn);
        if (!Directory.Exists(addOnDir))
            yield break;

        foreach (var dir in Directory.GetDirectories(addOnDir))
        {
            var langFile = Path.Combine(dir, $"{language}.json");
            if (File.Exists(langFile))
                yield return Path.GetFileName(dir);
        }
    }
}
