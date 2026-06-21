using HarmonyLib;
using Il2CppSystem;
using Il2CppSystem.Collections.Generic;
using Il2CppSystem.Threading;
using Project.Library;
using Project.MainStory;
using Project.Novel;
using Project.Outgame;

namespace AbyssMod.Patches;

/// <summary>
/// 剧情翻译补丁：标题、人名、对话文本的翻译注入。
/// </summary>
[HarmonyPatch]
public static class TranslationPatch
{
    private static NovelController _novelController;
    private static string NovelId
    {
        get => _novelController?._common?.ScriptId ?? string.Empty;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelController), nameof(NovelController.InitNovel))]
    public static void InitNovelController(NovelController __instance)
    {
        _novelController = __instance;
    }

    public static bool TryGetCurrentNovel(
        out System.Collections.Generic.Dictionary<string, string> translation
    )
    {
        translation = null;
        return Config.Translation.Value
            && Plugin.Trans.Novels.TryGetValue(NovelId, out translation);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NovelPathUtility), nameof(NovelPathUtility.GetNovelScenarioDirectory))]
    public static void SetupTranslation(string novelId)
    {
        if (!Config.Translation.Value)
            return;

        Plugin.Log.LogInfo($"NovelId: {novelId}");

        Plugin.Trans.GetNovelTranslationAsync(novelId).Wait();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NovelScriptInfoUtility), nameof(NovelScriptInfoUtility.GetScriptInfo))]
    public static void SetTitleAndDescription(ValueTuple<string, string> __result)
    {
        if (TryGetCurrentNovel(out var _))
        {
            string title = __result.Item1;
            if (
                !string.IsNullOrEmpty(title)
                && Plugin.Trans.Titles.TryGetValue(title, out string tTitle)
            )
                __result.Item1 = tTitle;

            string description = __result.Item2;
            if (
                !string.IsNullOrEmpty(description)
                && Plugin.Trans.Descriptions.TryGetValue(description, out string tDescription)
            )
                __result.Item2 = tDescription;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelTitle), nameof(NovelTitle.SetTitle))]
    public static void SetTitle(ref string title)
    {
        if (TryGetCurrentNovel(out var _))
        {
            if (
                !string.IsNullOrEmpty(title)
                && Plugin.Trans.Titles.TryGetValue(title, out string tTitle)
            )
                title = tTitle;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelViewMessageWindow), nameof(NovelViewMessageWindow.SetName))]
    public static void SetName(ref string name)
    {
        if (TryGetCurrentNovel(out var _))
        {
            if (
                !string.IsNullOrEmpty(name)
                && Plugin.Trans.Names.TryGetValue(name, out string tName)
            )
                name = tName;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelText), nameof(NovelText.Parse))]
    public static void SetText(List<Letter> letters, ref string message)
    {
        if (TryGetCurrentNovel(out var translation))
        {
            if (
                !string.IsNullOrEmpty(message)
                && translation.TryGetValue(message, out string tMessage)
            )
                message = tMessage;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelModelMessageLog), nameof(NovelModelMessageLog.Add))]
    public static void SetLogAdd(
        string scriptId,
        string assetId,
        ref string charaName,
        ref string message,
        string logId,
        NovelSound voice,
        CancellationToken ct
    )
    {
        charaName = charaName?.Replace("<user>", "%user%");
        message = message?.Replace("<user>", "%user%");
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelLogPopup), nameof(NovelLogPopup.SetData))]
    public static void SetLog(ref List<NovelLogData> dataList)
    {
        List<NovelLogData> list = new();
        foreach (var data in dataList)
        {
            string name = data.Name?.Replace("%user%", "<user>");
            string message = data.Message?.Replace("%user%", "<user>");

            if (TryGetCurrentNovel(out var translation))
            {
                if (
                    !string.IsNullOrEmpty(name)
                    && Plugin.Trans.Names.TryGetValue(name, out string tName)
                )
                    name = tName;

                if (
                    !string.IsNullOrEmpty(message)
                    && translation.TryGetValue(message, out string tMessage)
                )
                    message = tMessage;
            }

            list.Add(
                new NovelLogData(
                    data.ScriptId,
                    data.AssetId,
                    name,
                    message,
                    data.LogId,
                    data.Voice,
                    data.Ct
                )
            );
        }
        dataList = list;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelModelDotBalloon), nameof(NovelModelDotBalloon.StartBalloonMessage))]
    public static void SetBalloon(CommandDotMessageData messageData)
    {
        if (TryGetCurrentNovel(out var translation))
        {
            string message = messageData.Message;
            if (
                !string.IsNullOrEmpty(message)
                && translation.TryGetValue(message, out string tMessage)
            )
                messageData.Message = tMessage;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(
        typeof(NovelMessageTextComponent),
        nameof(NovelMessageTextComponent.SetMessageText)
    )]
    public static void SetTextCenter(NovelModelCommon common, CommandMessageTextData data)
    {
        if (TryGetCurrentNovel(out var translation))
        {
            string message = data.Message;
            if (
                !string.IsNullOrEmpty(message)
                && translation.TryGetValue(message, out string tMessage)
            )
                data.Message = tMessage;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(
        typeof(LibraryNovelPlayPopupController),
        nameof(LibraryNovelPlayPopupController.InitializePopup)
    )]
    public static void SetLibraryPopup(LibraryNovelPlayPopupController __instance, TextPopup popup)
    {
        if (Config.Translation.Value)
        {
            string title = __instance._model.Title;
            if (
                !string.IsNullOrEmpty(title)
                && Plugin.Trans.Titles.TryGetValue(title, out string tTitle)
            )
                popup._titleText.text = tTitle;

            string description = __instance._model.Description;
            if (
                !string.IsNullOrEmpty(description)
                && Plugin.Trans.Descriptions.TryGetValue(description, out string tDescription)
            )
                popup._contentText.text = tDescription;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(StoryQuestDetailPopup), nameof(StoryQuestDetailPopup.Setup))]
    public static void SetStoryQuestDetail(StoryQuestDetailPopup __instance)
    {
        if (Config.Translation.Value)
        {
            string description = __instance._storyDescription.text;
            if (
                !string.IsNullOrEmpty(description)
                && Plugin.Trans.Descriptions.TryGetValue(description, out string tDescription)
            )
                __instance._storyDescription.text = tDescription;
        }
    }
}
