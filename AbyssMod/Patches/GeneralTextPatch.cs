using System;
using AbyssMod.Services;
using HarmonyLib;
using Project;
using TMPro;
using AbyssMod;

namespace AbyssMod.Patches;

/// <summary>
/// 通用文本补丁：
///   1. 拦截 TMP_Text 文本设置（属性 setter 与纯字符串 SetText 重载），
///      涵盖技能名、装备效果、UI、酒馆卡片标签等几乎所有界面文字。
///   2. 拦截技能描述格式化器构造，翻译技能原始模板（含占位符）。
/// </summary>
[HarmonyPatch]
public static class GeneralTextPatch
{
    // ──────────────────────────────────────────────────
    // text = "..." 属性 setter（参数名为 value）
    // ──────────────────────────────────────────────────

    [HarmonyPrefix]
    [HarmonyPatch(typeof(TMP_Text), "set_text")]
    public static void OnSetText(ref string value, TMP_Text __instance)
    {
        ApplyTranslation(ref value, __instance);
    }

    // ──────────────────────────────────────────────────
    // 注意：不可 patch TMP_Text.SetText(string) / SetText(string, bool) 多载！
    // 在 Harmony + IL2CPP 下，patch 这些方法时调用「原方法」会再次 dispatch
    // 回被 patch 的入口，形成无限递归 → Stack Overflow 崩溃。
    // [ThreadStatic] 防护无效（递归发生在调用原方法阶段，Prefix 早已返回）。
    // 游戏绝大多数界面文字都走 set_text 属性 setter，已由上方补丁覆盖。
    // ──────────────────────────────────────────────────

    // ──────────────────────────────────────────────────
    // 共用处理逻辑
    // [ThreadStatic] 防止 TMP_Text 内部重入导致 Stack Overflow
    // ──────────────────────────────────────────────────

    [ThreadStatic]
    private static bool _inTranslation;

    private static void ApplyTranslation(ref string s, TMP_Text instance = null)
    {
        if (_inTranslation) return;
        _inTranslation = true;
        try
        {
            // 角色名字段（由 GameObject 名称精确判定）强制归入 name 类别，
            // 使所有界面的新角色名统一收集到 name，便于补入 names 字典。
            string cat;
            if (IsNameField(instance))
                cat = TextClassifier.Name;
            else
                cat = Config.ClassifyText.Value ? TextClassifier.Classify(s) : "ui_misc";

            s = TextTranslator.Process(cat, s);
            s = MachineTranslator.Handle(cat, s);
        }
        finally
        {
            _inTranslation = false;
        }
    }

    /// <summary>
    /// 依 TMP 元件的 GameObject 名称判定是否为「角色名字段」。
    /// 精确规则（避免误判技能名 Set_NameLv/TextSkill、二つ名 Chara1 等）：
    ///   1. 自身名含 "CharaName"（如 TextCharaName）
    ///   2. 自身名等于 "TextName"
    ///   3. 父层名精确等于 "Name"
    /// </summary>
    private static bool IsNameField(TMP_Text tmp)
    {
        if (tmp == null) return false;
        try
        {
            var go = tmp.gameObject;
            if (go == null) return false;

            var self = go.name ?? string.Empty;
            if (self.IndexOf("CharaName", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (string.Equals(self, "TextName", StringComparison.OrdinalIgnoreCase))
                return true;

            var parent = go.transform?.parent;
            if (parent != null && string.Equals(parent.name, "Name", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // 访问 GameObject 失败时按非角色名处理
        }
        return false;
    }

    // ──────────────────────────────────────────────────
    // 技能描述格式化器（战斗技能，不走分类器）
    // ──────────────────────────────────────────────────

    [HarmonyPrefix]
    [HarmonyPatch(
        typeof(SkillTextFormatter),
        nameof(SkillTextFormatter.CreateActionSkill),
        new Type[] { typeof(string), typeof(long), typeof(int) }
    )]
    public static void OnCreateActionSkill(ref string description)
    {
        description = TextTranslator.Process("ability_descriptions", description);
    }

    [HarmonyPrefix]
    [HarmonyPatch(
        typeof(SkillTextFormatter),
        nameof(SkillTextFormatter.CreateChainSkill),
        new Type[] { typeof(string), typeof(long), typeof(int) }
    )]
    public static void OnCreateChainSkill(ref string description)
    {
        description = TextTranslator.Process("ability_descriptions", description);
    }

    [HarmonyPrefix]
    [HarmonyPatch(
        typeof(SkillTextFormatter),
        nameof(SkillTextFormatter.CreateAbility),
        new Type[] { typeof(string), typeof(string) }
    )]
    public static void OnCreateAbility(ref string skillText, ref string awakeText)
    {
        skillText = TextTranslator.Process("ability_descriptions", skillText);
        awakeText = TextTranslator.Process("ability_descriptions", awakeText);
    }
}
