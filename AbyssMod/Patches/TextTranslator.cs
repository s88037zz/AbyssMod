using AbyssMod.Services;

namespace AbyssMod.Patches;

/// <summary>
/// 通用文本处理：翻译命中则替换，未命中且为日文则按类别收集。
/// 供各补丁共用。
/// </summary>
public static class TextTranslator
{
    /// <summary>
    /// 处理一段文本：
    ///   - 若开启翻译且字典精确命中 → 返回中文译文
    ///   - 否则若开启收集且文本含日文假名 → 记录到 dump
    /// </summary>
    public static string Process(string category, string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var trans = Plugin.Trans;
        var names = trans?.Names;
        var texts = trans?.Texts;

        if (Config.Translation.Value && trans != null)
        {
            // 角色名优先（以 names 为主）：强化、编队等任何显示角色名的界面
            // 都共用 names 翻译，保证全游戏角色名一致。
            if (names != null && names.TryGetValue(text, out string tName))
                return tName;

            if (texts != null && texts.TryGetValue(text, out string translated))
                return translated;
        }

        if (Config.CollectText.Value
            && HasKana(text)
            && (texts == null || !texts.ContainsKey(text))
            && (names == null || !names.ContainsKey(text)))
            TextCollector.Record(category, text);

        return text;
    }

    /// <summary>
    /// 是否含日文假名（平假名或片假名）。用于判断是否为待翻译的日文原文，
    /// 避免收集纯数字、英文或已翻译的中文。
    /// </summary>
    public static bool HasKana(string s)
    {
        foreach (char c in s)
        {
            if ((c >= '\u3040' && c <= '\u309F') || (c >= '\u30A0' && c <= '\u30FF'))
                return true;
        }
        return false;
    }
}
