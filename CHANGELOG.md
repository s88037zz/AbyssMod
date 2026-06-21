# Changelog

All notable changes to this project will be documented in this file.

## [1.0.6] - 2026-06-21

### 新增 / Added

- **`other/` CDN 自動同步**：啟動時從 CDN 拉取 `other/{category}/` 譯文，合併至本機 cache（遠端同 key 優先，本機獨有 key 保留）
- **`MachineTranslator.ReloadFromDisk()`**：CDN 同步完成後重新載入 other/ 記憶體快取
- **`manifest.other` 可選雜湊欄位**：支援對 other/ 各子類別做 cache 驗證（可選）

### 修復 / Fixed

- **other/ 校對譯文需開啟機翻才顯示**：命中 other/ 快取時，即使 `[MachineTranslation] Enabled = false` 也會顯示已校對譯文

---

## [1.0.5] - 2026-06-21

### 新增 / Added

- **介面文字翻譯擴展**：新增 `TextTranslator`、`TextCollector`、`TextClassifier` 三個服務，涵蓋道具說明、裝備效果、技能、酒館系統、設施、任務等介面文字
- **啟發式分類器**（`TextClassifier`）：將未翻日文依語意歸入 9 個細分子類別，便於人工校對與社群分工
- **add-on 資料夾**：`cache/translations/add-on/` 作為社群自訂翻譯的獨立命名空間，與作者 CDN 管理類別明確隔離
- **機翻繁簡語言切換**：機翻提示詞（System Prompt + Few-shot 範例）依 `Language` 設定自動切換繁體（台灣用語）或簡體
- **機翻支援雲端 API**：新增 `Engine = openai`（相容 DeepSeek、OpenAI）及 `Engine = claude` 配置，`ApiKey` / `Endpoint` / `Model` 全部可配置
- **角色名全介面共用**：`TextTranslator.Process` 優先查詢 `names` 字典，讓強化、編隊等介面角色名與劇情介面保持一致
- **角色名自動收集**：透過 GameObject 名稱啟發（`CharaName`、`TextName`）識別名字欄位，新角色名寫入 `name_raw.json`，不走機翻，待人工補入 `names/`
- **other/ 清洗機制**（`PruneGraduatedKeys`）：每次啟動自動移除 `other/` 中已被 `add-on/` 收錄的 key，保持機翻暫存區乾淨
- **`GameDir` 改為環境變數配置**：`ABYSS_GAME_DIR` 環境變數優先，方便多人協作不需硬編路徑
- 新增 `CHANGELOG.md` 與更新 `README.md`（社群發布說明）

### 修復 / Fixed

- **`SetText` 堆疊溢出崩潰**：移除對 `TMP_Text.SetText(string)` 及 `SetText(string, bool)` 的 Harmony Hook（IL2CPP 環境下兩者互相觸發形成遞迴），改為僅 hook `set_text` 屬性 setter
- **IL2CPP 參數名稱錯誤**：`HarmonyPrefix` 的 `ref string text` 改為 `ref string __0`（IL2CPP 反混淆後的參數名）
- **機翻輸出語言不一致**：修正繁體中文模式下機翻仍輸出簡體的問題；並對既有 `other/` 快取執行 OpenCC（s2twp）轉換

### 變更 / Changed

- 翻譯資料路徑從 `cache/zh_Hans/` 重構為 `cache/translations/`，子目錄對齊原作者 CDN 結構
- `MachineTranslator._pending` 由單純字串集合改為 `{template: category}` 映射，使批翻後能正確分類寫入
- `TranslationManager` 現在動態掃描 `add-on/` 子目錄並以最高優先級合併，不再需要逐一硬編路徑

---

## [1.0.4] 以前

基於 [anosu/AbyssMod](https://github.com/anosu/AbyssMod) 原版，包含劇情翻譯（`novels/`）、角色名（`names/`）、標題（`titles/`）、技能（`ability_descriptions/`）等 CDN 管理翻譯資料。
