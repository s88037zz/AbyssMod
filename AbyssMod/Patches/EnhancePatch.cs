using System.Linq;
using Absf;
using HarmonyLib;
using Il2CppSystem.Threading;
using Project.Notice;
using Project.Novel;

namespace AbyssMod.Patches;

/// <summary>
/// 游戏通用增强补丁：帧率修改 + 跳过大招动画。
/// </summary>
[HarmonyPatch]
public static class EnhancePatch
{
    private static int _allowStopVoiceCount;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(NovelLive2DObject), nameof(NovelLive2DObject.Initialize))]
    public static void DisableMosaic(NovelLive2DObject __instance)
    {
        if (Config.DynamicMosaic.Value)
            return;

        __instance
            .GetDrawables()
            ?.Where(d => d.name.StartsWith("Mosaic_") || d.name.StartsWith("MosaicInsted_"))
            .ToList()
            .ForEach(d => d.gameObject.SetActive(false));
    }

    [HarmonyPrefix]
    [HarmonyPatch(
        typeof(SoundCautionPopupController),
        nameof(SoundCautionPopupController.SetupPopupEvent)
    )]
    public static bool DisableSoundCaution(SoundCautionPopupController __instance)
    {
        if (!Config.SoundCaution.Value)
        {
            __instance._onClickOk.Invoke();
            return false;
        }

        return true;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelSoundManager), nameof(NovelSoundManager.StopCategory))]
    public static bool CancelStoppingVoice(int nCategory, bool playFade)
    {
        if (Config.VoiceInterruption.Value || _allowStopVoiceCount > 0)
            return true;

        return nCategory != 2 || playFade;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(NovelSoundManager), nameof(NovelSoundManager.PlaySound))]
    public static void StopVoiceBeforePlaying(NovelSoundManager __instance, SoundCategory category)
    {
        if (!Config.VoiceInterruption.Value && category == SoundCategory.Voice)
        {
            _allowStopVoiceCount++;
            try
            {
                __instance.StopCategory(2, false);
            }
            finally
            {
                _allowStopVoiceCount--;
            }
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Project.Title.TopView), nameof(Project.Title.TopView.PlayMovie))]
    public static void DisableTitleMovie(Project.Title.TopView __instance, CancellationToken ct)
    {
        if (!Config.TitleMovie.Value)
        {
            __instance.MovieSkip(ct);
        }
    }
}
