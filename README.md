# AbyssMod

> 🎮 ドットアビス 漢化 MOD（社群擴展版）

本 repo 基於原作者 [anosu/AbyssMod](https://github.com/anosu/AbyssMod) v1.0.4，在其劇情翻譯基礎上新增：

- **介面文字翻譯**（道具說明、角色技能、武器效果、通用 UI）
- **啟發式文字分類收集**（細分 9 個子類別）
- **機翻預處理**（可選，調用本地或雲端 LLM 補翻未收錄文字）
- **角色名全介面共用**（強化、編隊等介面共用 `names` 字典）

適用於 **Windows 平台 DMM Game Player 端**。

---

## 📋 目錄

- [架構說明](#-架構說明)
- [安裝（Release）](#-安裝release)
- [配置項](#-配置項)
- [機翻預處理（可選）](#-機翻預處理可選)
- [快捷鍵](#-快捷鍵)
- [翻譯資料](#-翻譯資料)
- [常見問題](#-常見問題)
- [開發者：編譯與打包](#-開發者編譯與打包)
- [社群](#-社群)

---

## 🗂 架構說明

本專案分為兩個獨立 repo：

| Repo | 內容 | 說明 |
|------|------|------|
| **[s88037zz/AbyssMod](https://github.com/s88037zz/AbyssMod)** | 插件本體 C# 原始碼 | 此 repo，含 Release 下載 |
| **[s88037zz/dotabyss-translation](https://github.com/s88037zz/dotabyss-translation)** | 劇情 / UI 翻譯 JSON | 啟動時從 CDN 自動下載 |

翻譯資料不包含在 Release 壓縮包內，插件首次啟動時會依 `AbyssMod.cfg` 的 `CDN` 設定自動下載到：

```
BepInEx/plugins/AbyssMod/cache/translations/
```

---

## 🚀 安裝（Release）

### 1. 確認遊戲已安裝

確保已透過 DMM Game Player 安裝遊戲，並知道遊戲根目錄（含 `.exe` 的資料夾）。

### 2. 下載 Release

前往 [Releases](https://github.com/s88037zz/AbyssMod/releases) 頁面，找到最新版本（綠色 `Latest` 標識），展開 `Assets` 下載 `AbyssMod-v1.0.5.7z`。

> ⚠️ 請下載 `.7z` 壓縮包，**不要**下載 `Source code`（那是原始碼，需要自行編譯）

### 3. 解壓到遊戲根目錄

將壓縮包解壓到遊戲根目錄（與 `.exe` 同層），解壓後結構如下：

```
遊戲根目錄/
├── ドットアビス.exe
├── winhttp.dll          ← 解壓後新增
├── doorstop_config.ini  ← 解壓後新增
└── BepInEx/
    ├── core/
    └── plugins/AbyssMod/
        ├── AbyssMod.dll
        ├── Utility.dll
        └── fonts/
```

### 4. 首次啟動

正常啟動遊戲。若是第一次安裝 BepInEx，啟動時會顯示一個控制台視窗並自動下載 Unity 補丁，稍等片刻即可。

> ⚠️ 若使用 ACGP 等加速器，控制台可能出現紅色報錯（無法連接 BepInEx 官網），請開啟代理後重試

### 5. 設定翻譯 CDN

首次啟動後會自動生成 `BepInEx\config\AbyssMod.cfg`。由於 Release 不含翻譯資料，需手動設定 CDN：

```ini
[Translation]
CDN      = https://raw.githubusercontent.com/s88037zz/dotabyss-translation/main/translations
Language = zh_Hant
```

存檔後重啟遊戲，插件會從 CDN 下載翻譯並套用。

> 🌐 若 GitHub 連線困難，請參考下方 [常見問題](#-常見問題) 中的 CDN 鏡像方案

---

## ⚙️ 配置項

設定檔位於 `BepInEx\config\AbyssMod.cfg`，首次啟動自動生成。

### `[General]`

| 配置項              | 預設值  | 說明                   |
| ------------------- | ------- | ---------------------- |
| `DynamicMosaic`     | `false` | 是否啟用動態馬賽克     |
| `SoundCaution`      | `false` | 是否彈出音量提醒       |
| `VoiceInterruption` | `false` | 是否啟用語音中斷       |
| `TitleMovie`        | `true`  | 是否播放標題動畫       |

### `[Translation]`

| 配置項     | 可選值                                                         | 預設值       | 說明                                         |
| ---------- | -------------------------------------------------------------- | ------------ | -------------------------------------------- |
| `Enabled`  | `true` / `false`                                               | `true`       | 是否開啟遊戲內翻譯                           |
| `CDN`      | 任意有效 URL                                                   | （作者 CDN） | 翻譯資料來源，請改為你的 translation repo    |
| `Language` | `zh_Hans`（簡體） / `zh_Hant`（繁體台灣）                     | `zh_Hans`    | 翻譯語言，機翻輸出語言也會跟著切換          |

### `[Translation.Font]`

| 配置項            | 預設值                     | 說明                                        |
| ----------------- | -------------------------- | ------------------------------------------- |
| `AssetBundlePath` | `AbyssMod/fonts/ttcuyuanj` | TMP 字體 AssetBundle 路徑（相對或絕對路徑） |

### `[Collector]`

開啟後，遊戲中出現的未翻日文原文會按類別寫入 `BepInEx\plugins\AbyssMod\dump\`，格式為 `{ "日文原文": "" }`，供後續翻譯使用。

| 配置項         | 預設值  | 說明                                                                       |
| -------------- | ------- | -------------------------------------------------------------------------- |
| `CollectText`  | `true`  | 是否收集未翻原文到 `dump/` 目錄。默認開啟，遊玩即持續為社群貢獻覆蓋         |
| `ClassifyText` | `true`  | 是否啟用啟發式分類器，將通用 UI 文字歸入細分子類別，關閉時全部歸入 `ui_misc` |

生成的 dump 檔案：

| 檔案 | 內容 |
|------|------|
| `equipment_effect_raw.json` | 裝備效果 / 被動（含 紋章 / 會心率 / 連擊率 等） |
| `facility_raw.json` | 設施 / 酒館建設 / 升級 |
| `bar_raw.json` | 酒館營業系統（員工 / 滿意度 / 服裝） |
| `mission_raw.json` | 任務目標句 |
| `materials_raw.json` | 素材 / 貨幣 / 結晶 |
| `abyss_code_raw.json` | 深淵代碼系統 |
| `dialogue_raw.json` | NPC 情感台詞 |
| `system_raw.json` | 系統短文字（按鈕 / 標籤等） |
| `ui_misc_raw.json` | 其餘通用文字 |
| `name_raw.json` | 新角色名（未在 `names` 字典中的，機翻不處理，需人工翻譯後補入） |
| `items_raw.json` | 道具說明（精確鉤子，不經分類器） |

### `[MachineTranslation]`

| 配置項           | 可選值                                            | 預設值                                            | 說明                                                         |
| ---------------- | ------------------------------------------------- | ------------------------------------------------- | ------------------------------------------------------------ |
| `Enabled`        | `true` / `false`                                  | `false`                                           | 是否啟用機翻預處理（詳見下方說明）                           |
| `Engine`         | `openai` / `claude` / `sugoi` / `libre`           | `openai`                                          | 翻譯引擎（`openai` 相容 Ollama、DeepSeek、OpenAI 等）        |
| `Endpoint`       | 任意有效 API 地址                                 | `http://127.0.0.1:11434/v1/chat/completions`      | 翻譯服務 API 地址                                            |
| `Model`          | 模型名稱                                          | `qwen2.5:3b`                                      | `openai` / `ollama` 引擎使用的模型名稱                       |
| `ApiKey`         | API 金鑰字串                                      | （空）                                            | 雲端服務需填入（OpenAI / DeepSeek / Claude API Key）         |
| `TimeoutSeconds` | 整數                                              | `30`                                              | 單次翻譯請求超時秒數，雲端 API 建議調高至 60                 |

---

## 🤖 機翻預處理（可選）

對尚未收錄到翻譯資料的文字，可啟用機翻預處理，讓插件**自動補翻**。

工作方式（**非即時**，不影響遊戲流暢度）：

1. 遊戲運行中，把字典未命中的日文按數字模板去重後加入待翻佇列。
2. 每次啟動（以及會話中每 30 秒）在**後台**批量翻譯，結果按類別寫入 `translations/other/{類別}/`。
3. 顯示時命中快取即為中文。也就是「這次玩到的新內容先收集，下次啟動後就變中文」。

機翻輸出語言跟隨 `Language` 設定：`zh_Hans` 輸出簡體，`zh_Hant` 輸出繁體台灣用語。

> ⚠️ 機翻品質不及人工校對。角色名（`name_raw.json`）不走機翻，需人工翻譯後補入 `names/` 字典。

### 方案 A：本地 Ollama（免費，需要電腦資源）

1. 安裝 [Ollama](https://ollama.com)（安裝後自動常駐於 `127.0.0.1:11434`）
2. 拉取模型：

   ```bash
   ollama pull qwen2.5:3b
   ```

3. 編輯 `AbyssMod.cfg`：

   ```ini
   [MachineTranslation]
   Enabled  = true
   Engine   = openai
   Endpoint = http://127.0.0.1:11434/v1/chat/completions
   Model    = qwen2.5:3b
   ```

4. 啟動遊戲即可。想要更好品質可改用 `qwen2.5:7b`。

### 方案 B：DeepSeek API（雲端，品質較佳，付費計量）

1. 至 [DeepSeek 官網](https://platform.deepseek.com) 取得 API Key
2. 編輯 `AbyssMod.cfg`：

   ```ini
   [MachineTranslation]
   Enabled         = true
   Engine          = openai
   Endpoint        = https://api.deepseek.com/v1/chat/completions
   Model           = deepseek-v4-flash
   ApiKey          = sk-你的DeepSeek金鑰
   TimeoutSeconds  = 60
   ```

### 方案 C：Claude API

1. 至 [Anthropic 官網](https://console.anthropic.com) 取得 API Key
2. 編輯 `AbyssMod.cfg`：

   ```ini
   [MachineTranslation]
   Enabled  = true
   Engine   = claude
   ApiKey   = sk-ant-你的Claude金鑰
   Model    = claude-haiku-4-5
   ```

---

## ⌨️ 快捷鍵

| 快捷鍵 | 功能              |
| ------ | ----------------- |
| `F8`   | 開啟 / 關閉劇情翻譯 |
| `F9`   | 開啟 / 關閉語音中斷 |
| `F10`  | 熱重載配置檔案    |

---

## 📦 翻譯資料

翻譯 JSON 存放於獨立 repo：

**[s88037zz/dotabyss-translation](https://github.com/s88037zz/dotabyss-translation)**

目錄結構：

```
translations/
├── names/          角色名（作者維護）
├── titles/         劇情標題（作者維護）
├── descriptions/   劇情概要（作者維護）
├── ability_descriptions/  技能 / 覺醒描述（作者維護）
├── novels/         劇情對話（作者維護）
└── add-on/         社群自訂分類翻譯（本 fork 新增）
    ├── items/      道具說明
    ├── equipment_effect/  裝備效果
    ├── bar/        酒館系統
    ├── facility/   設施
    ├── mission/    任務
    ├── materials/  素材
    ├── abyss_code/ 深淵代碼
    ├── dialogue/   NPC 台詞
    ├── system/     系統文字
    ├── ui/         通用 UI
    └── ui_misc/    其餘 UI
```

`add-on/` 的翻譯優先級高於機翻（`other/`）。機翻補翻的內容一旦人工校對後，可直接放進對應 `add-on/` 子目錄即生效。

---

## ❓ 常見問題

<details>
<summary><b>啟動時控制台出現紅色報錯</b></summary>
通常是 BepInEx 無法連接其官網下載 Unity 補丁，請開啟代理 / 梯子後重啟遊戲。也可能是初始化檔案因網路波動損壞，此時可嘗試刪除 Mod 資料夾後重新安裝。
</details>

<details>
<summary><b>如何隱藏控制台視窗</b></summary>
編輯 <code>BepInEx\config\BepInEx.cfg</code>，找到 <code>[Logging.Console]</code>，將 <code>Enabled</code> 設為 <code>false</code>。
</details>

<details>
<summary><b>無法連接 GitHub 下載翻譯</b></summary>
<ul>
  <li>可使用 GitHub 鏡像加速站，如 <code>https://gh-proxy.com</code>，將 CDN 改為：<br>
  <code>https://gh-proxy.com/https://raw.githubusercontent.com/s88037zz/dotabyss-translation/main/translations</code></li>
  <li>或將 CDN 改為 Gitee 等其他鏡像（需自行同步翻譯資料）</li>
</ul>
</details>

<details>
<summary><b>繁體中文與簡體中文如何切換</b></summary>
編輯 <code>BepInEx\config\AbyssMod.cfg</code>：<br>
繁體：<code>Language = zh_Hant</code><br>
簡體：<code>Language = zh_Hans</code>
</details>

<details>
<summary><b>機翻輸出語言不正確（出現簡體）</b></summary>
確認 <code>AbyssMod.cfg</code> 中 <code>Language = zh_Hant</code>（而非 <code>zh_Hans</code>）。機翻提示詞會依此自動切換，重啟遊戲即生效。
</details>

<details>
<summary><b>crash / 崩潰怎麼排查</b></summary>
查看 <code>BepInEx\ErrorLog.log</code> 與 <code>BepInEx\LogOutput.log</code>，搜尋 <code>Exception</code> 或 <code>Stack overflow</code>，然後在 <a href="https://github.com/s88037zz/AbyssMod/issues">Issues</a> 附上 log 回報。
</details>

---

## 🛠 開發者：編譯與打包

### 環境需求

- .NET 6.0 SDK
- 遊戲本體安裝完成（需要 `BepInEx/interop/*.dll` 與 `Utility.dll`）

### 編譯

1. 設定環境變數（或直接修改 `AbyssMod.csproj` 中的備選 `GameDir`，**不要提交**）：

   ```powershell
   $env:ABYSS_GAME_DIR = "D:\Games\ドットアビス"
   ```

2. 執行 build：

   ```bash
   cd AbyssMod-main
   dotnet build -c Release
   ```

   輸出 DLL 位於 `$ABYSS_GAME_DIR/BepInEx/plugins/AbyssMod/Release/net6.0/AbyssMod.dll`

### 打包 Release

打包 `AbyssMod-v1.0.5.7z`，應包含以下路徑（相對遊戲根目錄）：

```
winhttp.dll
doorstop_config.ini
BepInEx/core/
BepInEx/patchers/
BepInEx/unity-libs/
BepInEx/plugins/AbyssMod/AbyssMod.dll
BepInEx/plugins/AbyssMod/Utility.dll
BepInEx/plugins/AbyssMod/fonts/
```

**應排除**：

- `BepInEx/config/`（含 ApiKey，勿外洩）
- `BepInEx/interop/`、`BepInEx/cache/`
- `BepInEx/plugins/AbyssMod/cache/`（翻譯由 CDN 提供）
- `BepInEx/plugins/AbyssMod/dump/`、`Release/`
- `BepInEx/*.log`

發布流程：

```bash
git tag v1.0.5
git push origin v1.0.5
# 在 GitHub Releases 建立 Release，上傳 AbyssMod-v1.0.5.7z
```

---

## 💬 社群

- 海外詢問：添加 Discord 好友 `.lienchu9420`（Lienchu 恋曲）
- Issues：[GitHub Issues](https://github.com/s88037zz/AbyssMod/issues)

---

> 本 fork 基於 [anosu/AbyssMod](https://github.com/anosu/AbyssMod)，感謝原作者的劇情翻譯框架。
