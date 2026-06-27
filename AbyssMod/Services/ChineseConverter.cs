using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AbyssMod.Services;

/// <summary>
/// 繁体中文字 → 简体中文字转换器。
/// <para>内嵌 OpenCC (Apache-2.0) 的 TSCharacters.txt / TSPhrases.txt 映射数据，
/// 零外部依赖，仅需 AbyssMod.dll 自身。</para>
/// <para>使用示例：</para>
/// <code>
///   string s = ChineseConverter.ToSimplified("繁體字");
///   var dict = ChineseConverter.ConvertDictionary(traditionalDict);
/// </code>
/// </summary>
public static class ChineseConverter
{
    /// <summary>单字映射：繁 → 简。</summary>
    private static readonly Dictionary<char, char> CharMap;

    /// <summary>
    /// 词汇映射：繁 → 简。
    /// 按 key 长度从长到短排序，确保最长匹配优先。
    /// </summary>
    private static readonly List<KeyValuePair<string, string>> PhraseMap;

    /// <summary>用于记录解析统计，仅在首次加载时生成。</summary>
    private static readonly string LoadSummary;

    static ChineseConverter()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var charMap = new Dictionary<char, char>();
        LoadCharMap(assembly, charMap);
        CharMap = charMap;

        var phraseList = new List<KeyValuePair<string, string>>();
        LoadPhraseMap(assembly, phraseList);
        // 按 key 长度降序排序，确保长词优先匹配
        PhraseMap = phraseList
            .OrderByDescending(kv => kv.Key.Length)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToList();

        LoadSummary =
            $"ChineseConverter loaded: {charMap.Count} chars, {phraseList.Count} phrases";
        Logger.Info(LoadSummary);
    }

    /// <summary>
    /// 将繁体字符串转换为简体。
    /// 先尝试最长词汇匹配，再逐字查表。
    /// </summary>
    public static string ToSimplified(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        // 预分配 StringBuilder 避免频繁扩容
        var sb = new StringBuilder(text.Length);
        int i = 0;

        while (i < text.Length)
        {
            bool matched = false;

            // 尝试词汇匹配（从当前位置开始）
            int remaining = text.Length - i;
            foreach (var pair in PhraseMap)
            {
                if (pair.Key.Length > remaining)
                    continue;

                if (string.Compare(text, i, pair.Key, 0, pair.Key.Length, StringComparison.Ordinal) == 0)
                {
                    sb.Append(pair.Value);
                    i += pair.Key.Length;
                    matched = true;
                    break;
                }
            }

            if (matched)
                continue;

            // 逐字处理
            char c = text[i];
            if (CharMap.TryGetValue(c, out char simplified))
                sb.Append(simplified);
            else
                sb.Append(c);

            i++;
        }

        return sb.ToString();
    }

    /// <summary>
    /// 批量转换字典中的所有值（保留 key 不变）。
    /// </summary>
    public static Dictionary<string, string> ConvertDictionary(Dictionary<string, string> source)
    {
        if (source == null)
            return null;

        var result = new Dictionary<string, string>(source.Count, source.Comparer);
        foreach (var kv in source)
            result[kv.Key] = ToSimplified(kv.Value ?? string.Empty);
        return result;
    }

    /// <summary>
    /// 批量转换字典中的所有值（原地替换，避免分配新字典）。
    /// </summary>
    public static void ConvertDictionaryInPlace(Dictionary<string, string> dict)
    {
        if (dict == null || dict.Count == 0)
            return;

        foreach (var key in dict.Keys.ToList())
        {
            var value = dict[key];
            if (!string.IsNullOrEmpty(value))
                dict[key] = ToSimplified(value);
        }
    }

    /// <summary>调试用：返回加载摘要。</summary>
    public static string GetLoadSummary() => LoadSummary;

    /// <summary>
    /// 加载单字映射表。
    /// 格式（TSCharacters.txt）：繁\t简1 简2 ...
    /// 取第一个候选字作为默认转换。
    /// </summary>
    private static void LoadCharMap(Assembly assembly, Dictionary<char, char> map)
    {
        using var stream = assembly.GetManifestResourceStream(
            "AbyssMod.Resources.TSCharacters.txt"
        );
        if (stream == null)
        {
            Logger.Warn("TSCharacters.txt not found in embedded resources");
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            // 跳过注释和空行
            if (string.IsNullOrEmpty(line) || line[0] == '#')
                continue;

            int tab = line.IndexOf('\t');
            if (tab < 0)
                continue;

            string traditional = line[..tab];
            string simplifiedPart = line[(tab + 1)..];

            // 取第一个候选字（空格分隔）
            int space = simplifiedPart.IndexOf(' ');
            string simplified = space >= 0 ? simplifiedPart[..space] : simplifiedPart;

            if (traditional.Length == 1 && simplified.Length == 1)
            {
                char t = traditional[0];
                char s = simplified[0];
                // 繁简相同时跳过（无需转换）
                if (t != s)
                    map[t] = s;
            }
        }
    }

    /// <summary>
    /// 加载词汇映射表。
    /// 格式（TSPhrases.txt）：繁短语\t简短语1 简短语2 ...
    /// 取第一个候选短语作为默认转换。
    /// </summary>
    private static void LoadPhraseMap(Assembly assembly, List<KeyValuePair<string, string>> list)
    {
        using var stream = assembly.GetManifestResourceStream(
            "AbyssMod.Resources.TSPhrases.txt"
        );
        if (stream == null)
        {
            Logger.Warn("TSPhrases.txt not found in embedded resources");
            return;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrEmpty(line) || line[0] == '#')
                continue;

            int tab = line.IndexOf('\t');
            if (tab < 0)
                continue;

            string traditional = line[..tab];
            string simplifiedPart = line[(tab + 1)..];

            // 取第一个候选短语
            int space = simplifiedPart.IndexOf(' ');
            string simplified = space >= 0 ? simplifiedPart[..space] : simplifiedPart;

            if (traditional.Length > 0 && simplified.Length > 0 && traditional != simplified)
                list.Add(new KeyValuePair<string, string>(traditional, simplified));
        }
    }
}
