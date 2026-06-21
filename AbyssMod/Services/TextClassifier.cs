using System;
using System.Text.RegularExpressions;

namespace AbyssMod.Services;

/// <summary>
/// 基于内容启发式规则的文本分类器。
/// 仅用于 TMP set_text 万能钩子捕获的文本；items 与 ability_descriptions 有各自的精确补丁，不经此处。
///
/// <para>子类别（均为本地自定义类型，作者 CDN 不覆盖）：</para>
/// <list type="bullet">
///   <item>equipment_effect — 含纹章/技能充能/会心率/连击率/UP 等战斗属性词的装备效果长句</item>
///   <item>abyss_code      — 深渊代码系统（浸食率/アビスコード/フロア/系統）</item>
///   <item>facility        — 设施/酒馆/研究所/升级/建设委托</item>
///   <item>bar             — 酒馆营业系统（员工/满意度/料理/服装）</item>
///   <item>mission         — 任务目标句（以动词形结尾，含 クリア/到達/獲得/Lv）</item>
///   <item>materials       — 素材/矿石/骨材/结晶/票券/点数/硬币</item>
///   <item>dialogue        — NPC 情感台词（含 ♪/♡/～ 等情感符号）</item>
///   <item>system          — 短系统文本（重试/取消/排序/结束等）</item>
///   <item>ui_misc         — 兜底（角色名/技能名/装备名等纯名词短语）</item>
/// </list>
/// </summary>
public static class TextClassifier
{
    // ──────────────────────────────────────────────────
    // 公开类别常量，与 TranslationPaths 保持一致命名
    // ──────────────────────────────────────────────────
    public const string EquipmentEffect = "equipment_effect";
    public const string AbyssCode       = "abyss_code";
    public const string Facility        = "facility";
    public const string Bar             = "bar";
    public const string Mission         = "mission";
    public const string Materials       = "materials";
    public const string Dialogue        = "dialogue";
    public const string System          = "system";
    public const string UiMisc          = "ui_misc";

    /// <summary>角色名类别（由字段 GameObject 名称判定，不走内容启发式）。</summary>
    public const string Name            = "name";

    // 所有本地自定义子类别（给 TranslationPaths 用）
    public static readonly string[] AllCustomCategories =
    {
        EquipmentEffect, AbyssCode, Facility, Bar,
        Mission, Materials, Dialogue, System, UiMisc,
    };

    // ──────────────────────────────────────────────────
    // 高置信关键字（包含即命中）
    // ──────────────────────────────────────────────────
    private static readonly string[] EquipmentKeywords =
    {
        "紋章：", "スキルチャージ", "会心率", "連撃率", "スキルダメージ",
        "会心ダメージ", "最大HP", "攻撃力", "防御力",
    };

    private static readonly string[] AbyssKeywords =
    {
        "アビスコード", "浸食率", "フロアキャラ", "フロント全体", "バック全体",
        "系統", "インパクトコード", "ラッシュコード", "セーフコード", "リスクコード",
        "チェックポイント", "アビスコイン",
    };

    private static readonly string[] FacilityKeywords =
    {
        "施設", "酒場の", "研究所", "溶鉱炉", "アップグレード",
        "建設依頼", "経営戦略", "鍛冶屋", "宿舎", "訓練所",
        "治療所", "噴水", "兵器工場", "鉄甲",
    };

    private static readonly string[] BarKeywords =
    {
        "スタッフ", "満足度", "の提案", "コスチューム", "おしごと",
        "ラウンジ", "個室", "VIP席", "に配置", "おにーさん",
    };

    private static readonly string[] MaterialKeywords =
    {
        "純真結晶", "チケット", "ポイント", "コイン", "素材",
        "鉱石", "骨材", "木材", "マテリアル", "シール",
        "ソウル", "フレンドポイント", "サブスク",
    };

    private static readonly string[] MissionVerbEndings =
    {
        "する", "しよう", "クリアする", "到達する", "獲得する",
        "解放する", "アップグレードする", "到達する",
    };

    private static readonly string[] MissionKeywords =
    {
        "クリアする", "到達する", "獲得する", "を実行して", "解放する",
        "にする", "Lvにする", "LvUP", "シンクロLv",
    };

    private static readonly string[] SystemKeywords =
    {
        "リトライ", "ソート", "キャンセル", "ショップ", "ゲームを終了",
        "更新まで", "通信が", "ログインに失敗", "スタミナ",
    };

    private static readonly string[] DialogueSymbols = { "♪", "♡", "～", "……", "…" };

    private static readonly Regex UpRegex  = new Regex(@"\b(UP|上昇)\b", RegexOptions.Compiled);
    private static readonly Regex LvRegex  = new Regex(@"Lv\d", RegexOptions.Compiled);

    // ──────────────────────────────────────────────────
    // 主入口
    // ──────────────────────────────────────────────────

    /// <summary>
    /// 根据文本内容返回最合适的子类别名称。
    /// 纯函数，线程安全。
    /// </summary>
    public static string Classify(string text)
    {
        if (string.IsNullOrEmpty(text))
            return UiMisc;

        // 1. 装备/被动效果（高置信）
        if (ContainsAny(text, EquipmentKeywords) && (UpRegex.IsMatch(text) || text.Contains("秒間") || text.Contains("自身")))
            return EquipmentEffect;

        // 2. 深渊代码（高置信）
        if (ContainsAny(text, AbyssKeywords))
            return AbyssCode;

        // 3. 设施/建设（高置信）
        if (ContainsAny(text, FacilityKeywords))
            return Facility;

        // 4. 酒馆营业（高置信）
        if (ContainsAny(text, BarKeywords))
            return Bar;

        // 5. 素材/货币（高置信）
        if (ContainsAny(text, MaterialKeywords))
            return Materials;

        // 6. 任务目标句（中-高置信）
        if (IsMissionText(text))
            return Mission;

        // 7. 系统短文本（中置信）
        if (IsSystemText(text))
            return System;

        // 8. NPC 情感台词（中置信）
        if (IsDialogue(text))
            return Dialogue;

        // 9. 兜底
        return UiMisc;
    }

    // ──────────────────────────────────────────────────
    // 辅助判断
    // ──────────────────────────────────────────────────

    private static bool IsMissionText(string text)
    {
        if (ContainsAny(text, MissionKeywords))
            return true;
        if (LvRegex.IsMatch(text) && (text.Contains("到達") || text.Contains("にする")))
            return true;
        foreach (var ending in MissionVerbEndings)
            if (text.EndsWith(ending, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static bool IsSystemText(string text)
    {
        if (ContainsAny(text, SystemKeywords))
            return true;
        // 短文本（≤10字）且不含假名长词 → 系统标签/按钮
        if (text.Length <= 10 && !text.Contains('\n'))
            return true;
        return false;
    }

    private static bool IsDialogue(string text)
    {
        if (!ContainsAny(text, DialogueSymbols))
            return false;
        // 需要是有一定长度的句子（避免把单个表情短词也归入）
        return text.Length >= 6;
    }

    private static bool ContainsAny(string text, string[] keywords)
    {
        foreach (var kw in keywords)
            if (text.Contains(kw, StringComparison.Ordinal))
                return true;
        return false;
    }
}
