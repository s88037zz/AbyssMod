using HarmonyLib;
using Project.ItemList.Top;
using Project.Outgame.UI.Popup;

namespace AbyssMod.Patches;

/// <summary>
/// 道具说明（FlavorText / Description）翻译补丁。
/// 涵盖两处入口：
///   1. 「アイテム一覧」列表页左侧详细面板（LeftView.ViewUpdate）
///   2. 道具详情弹窗（ItemDetailFlavorTextView.UpdateView）
/// </summary>
[HarmonyPatch]
public static class ItemPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(typeof(LeftView), nameof(LeftView.ViewUpdate))]
    public static void TranslateItemListDetail(LeftViewModel model)
    {
        if (model == null)
            return;

        string desc = model.Description;
        string translated = TextTranslator.Process("items", desc);
        if (!ReferenceEquals(desc, translated))
            model.Description = translated;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(ItemDetailFlavorTextView), nameof(ItemDetailFlavorTextView.UpdateView))]
    public static void TranslateFlavorText(ref string flavorText)
    {
        flavorText = TextTranslator.Process("items", flavorText);
    }
}
