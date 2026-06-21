using BepInEx.Configuration;
using Utility.Toast;

namespace AbyssMod
{
    /// <summary>
    /// 全局配置管理器。
    /// 负责初始化所有配置项并绑定事件监听。
    /// </summary>
    public static class Config
    {
#if DEBUG
        #region Debug
        public static ConfigEntry<bool> Offline;
        public static ConfigEntry<string> OfflineAPI;
        public static bool OfflineStartup;
        #endregion
#endif

        #region General
        public static ConfigEntry<bool> DynamicMosaic;
        public static ConfigEntry<bool> SoundCaution;
        public static ConfigEntry<bool> VoiceInterruption;
        public static ConfigEntry<bool> TitleMovie;
        #endregion

        #region Translation
        public static ConfigEntry<bool> Translation;
        public static ConfigEntry<string> TranslationCDN;
        public static ConfigEntry<string> TranslationLanguage;
        public static ConfigEntry<string> TranslationCryptoTag;
        public static ConfigEntry<string> TranslationCryptoKey;
        #endregion

        #region Font
        public static ConfigEntry<string> FontBundlePath;
        #endregion

        #region Collector
        public static ConfigEntry<string> MTApiKey;
        public static ConfigEntry<bool> CollectText;
        public static ConfigEntry<bool> ClassifyText;
        #endregion

        #region MachineTranslation
        public static ConfigEntry<bool> MTEnabled;
        public static ConfigEntry<string> MTEngine;
        public static ConfigEntry<string> MTEndpoint;
        public static ConfigEntry<string> MTModel;
        public static ConfigEntry<int> MTTimeout;
        #endregion

        /// <summary>
        /// 初始化配置系统。
        /// </summary>
        public static void Initialize()
        {
            BindAllEntries();
            BindSettingChangedLog();
        }

        private static void BindAllEntries()
        {
#if DEBUG
            #region Debug
            Offline = Plugin.ConfigFile.Bind(
                "Debug.Offline",
                "Enabled",
                false,
                "API localization for debug"
            );
            OfflineAPI = Plugin.ConfigFile.Bind(
                "Debug.Offline",
                "CDN",
                "http://localhost:33333/abyss/",
                "CDN for debug"
            );
            #endregion
#endif

            #region General
            DynamicMosaic = Plugin.ConfigFile.Bind(
                "General",
                "DynamicMosaic",
                false,
                "是否启用游戏内动态马赛克"
            );
            SoundCaution = Plugin.ConfigFile.Bind(
                "General",
                "SoundCaution",
                false,
                "是否启用进入游戏时的音量提醒弹窗"
            );
            VoiceInterruption = Plugin.ConfigFile.Bind(
                "General",
                "VoiceInterruption",
                false,
                "剧情中播放下一段无声文本时是否中断当前角色语音"
            );
            TitleMovie = Plugin.ConfigFile.Bind(
                "General",
                "TitleMovie",
                true,
                "是否开启进入游戏时的标题动画"
            );
            #endregion

            #region Translation
            Translation = Plugin.ConfigFile.Bind(
                "Translation",
                "Enabled",
                true,
                "是否开启游戏内剧情翻译"
            );
            TranslationCDN = Plugin.ConfigFile.Bind(
                "Translation",
                "CDN",
                "https://raw.githubusercontent.com/anosu/dotabyss-translation/refs/heads/main/translations",
                "翻译加载的CDN"
            );
            TranslationLanguage = Plugin.ConfigFile.Bind(
                "Translation",
                "Language",
                "zh_Hans",
                "翻译语言，取值范围：[zh_Hans, zh_Hant]。机翻也会依此输出简体或繁体"
            );
            TranslationCryptoTag = Plugin.ConfigFile.Bind(
                "Translation.Crypto",
                "Tag",
                "ENC:",
                "翻译文本加密标签（可选）"
            );
            TranslationCryptoKey = Plugin.ConfigFile.Bind(
                "Translation.Crypto",
                "Key",
                "woshitonghuadawang",
                "翻译文本解密密钥（可选）"
            );
            #endregion

            #region Font
            FontBundlePath = Plugin.ConfigFile.Bind(
                "Translation.Font",
                "AssetBundlePath",
                $"{MyPluginInfo.PLUGIN_GUID}/fonts/ttcuyuanj",
                "TMP字体AssetBundle的路径，默认相对于插件目录，也可使用绝对路径"
            );
            #endregion

            #region Collector
            MTApiKey = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "ApiKey",
                "",
                "API 密钥（Engine=claude 时填入 Anthropic API Key；Engine=openai 且使用云端 OpenAI 时填入 OpenAI API Key）。Ollama 等本地服务留空即可"
            );
            CollectText = Plugin.ConfigFile.Bind(
                "Collector",
                "CollectText",
                true,
                "是否收集游戏内出现的原文（道具说明等）到 dump 目录，用于建立翻译数据。默认开启，写盘开销极小，可持续为社区贡献覆盖"
            );
            ClassifyText = Plugin.ConfigFile.Bind(
                "Collector",
                "ClassifyText",
                true,
                "是否启用启发式文本分类器，将通用 UI 文本自动归入 equipment_effect/facility/bar/mission/materials/abyss_code/dialogue/system/ui_misc 子类别，便于分类校对。关闭时全部归入 ui_misc"
            );
            #endregion

            #region MachineTranslation
            MTEnabled = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "Enabled",
                false,
                "是否启用机翻预处理：平时收集字典未命中的日文，启动时后台批量调用本地翻译引擎翻译并缓存（非实时，需自行运行翻译服务，如 ollama）"
            );
            MTEngine = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "Engine",
                "openai",
                "翻译引擎类型，可选：openai（OpenAI兼容，如 LM Studio）、ollama、sugoi、libre"
            );
            MTEndpoint = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "Endpoint",
                "http://127.0.0.1:11434/v1/chat/completions",
                "本地翻译服务的完整 API 地址。ollama(OpenAI兼容)默认 http://127.0.0.1:11434/v1/chat/completions；sugoi 通常 http://127.0.0.1:14366/；libre 通常 http://127.0.0.1:5000/translate"
            );
            MTModel = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "Model",
                "qwen2.5:3b",
                "模型名称（openai/ollama 引擎使用），如 qwen2.5:3b（质量更好可换 qwen2.5:7b）。sugoi/libre 可留空"
            );
            MTTimeout = Plugin.ConfigFile.Bind(
                "MachineTranslation",
                "TimeoutSeconds",
                30,
                "单次翻译请求超时秒数"
            );
            #endregion
        }

        /// <summary>
        /// 绑定配置变更日志输出。
        /// </summary>
        private static void BindSettingChangedLog()
        {
            Plugin.ConfigFile.SettingChanged += (_, e) =>
            {
                var c = e.ChangedSetting;
                Plugin.Log.LogInfo(
                    $"[{c.Definition.Section}] {c.Definition.Key} => {c.BoxedValue}"
                );
                Toast.Info($"[{c.Definition.Section}]", $"{c.Definition.Key} => {c.BoxedValue}");
            };
        }
    }
}
