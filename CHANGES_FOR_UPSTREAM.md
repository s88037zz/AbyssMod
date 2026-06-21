# AbyssMod 改动说明（提交给原作者参考）

> 基于上游版本 **v1.0.4**（commit `6b4899c` "bump version"）
> 本文档由社区使用者整理，列出在原版基础上新增的两块功能，供作者评估是否合并。
> 所有改动**向后兼容**：新增功能默认**关闭**，不影响现有「剧情翻译」等行为。

---

## 一、改动概述

在原有「剧情翻译」之外，新增两块能力：

1. **界面文本翻译扩展（道具 / 技能 / 通用 UI）**
   除剧情外，新增对道具说明、角色技能/武器效果、以及几乎所有界面文字的翻译支持；并提供「原文收集」功能，便于建立翻译数据。

2. **机翻预处理（可选，默认关闭）**
   「平时收集 + 启动批量翻译」模式：游戏运行中把字典未命中的日文收集起来，每次启动时在后台调用本地翻译服务（如 ollama）批量翻译并缓存，下次显示即为中文。**非实时**，因此不影响游戏内流畅度。

---

## 二、新增文件（6 个）

| 文件 | 作用 |
| --- | --- |
| `AbyssMod/Patches/GeneralTextPatch.cs` | 核心补丁。Hook `TMP_Text.set_text` 翻译/收集几乎所有界面文字；并 Hook `SkillTextFormatter` 的 `CreateActionSkill/CreateChainSkill/CreateAbility`，捕获**技能原始模板**（含占位符，可适配所有等级）。接入 `TextClassifier` 将通用 UI 文本自动归类。 |
| `AbyssMod/Patches/ItemPatch.cs` | 道具说明翻译。Hook `LeftView.ViewUpdate` 与 `ItemDetailFlavorTextView.UpdateView`。 |
| `AbyssMod/Patches/TextTranslator.cs` | 共用文本处理逻辑：命中字典→替换译文；未命中且含日文假名→按类别收集。含 `HasKana` 判定。 |
| `AbyssMod/Services/TextClassifier.cs` | **新增（分类优化）** 启发式文本分类器。依关键字/结构规则把通用 UI 文本自动归入 9 个子类别：`equipment_effect/abyss_code/facility/bar/mission/materials/dialogue/system/ui_misc`。 |
| `AbyssMod/Services/TextCollector.cs` | 通用收集器，把未翻原文按类别写入 `dump/{category}_raw.json`，去重。分类优化后 dump 文件名对应细分类别。 |
| `AbyssMod/Services/MachineTranslator.cs` | 机翻预处理（收集 pending + 启动批翻 + 缓存 + 数字模板归一化 + 多引擎调用）。pending 格式升级为 `{模板: category}` 映射；机翻缓存按类别分文件存入 `translations/other/{category}/`。**仅当 `[MachineTranslation] Enabled=true` 时工作。** |

---

## 三、修改文件（7 个）

| 文件 | 改动点 |
| --- | --- |
| `Core/Config.cs` | 新增 `[Collector]`（`CollectText`、`ClassifyText`）与 `[MachineTranslation]`（`Enabled/Engine/Endpoint/Model/TimeoutSeconds`）两个配置区；`CollectText` 默认改为 `true`（游玩即持续贡献覆盖）。 |
| `Core/Plugin.cs` | `Load()` 中初始化 `MachineTranslator`；`Unload()` 中调用 `MachineTranslator.Save()` 保存缓存。 |
| `Models/Manifest.cs` | 新增 `another_name` 清单字段（对应作者最新 CDN 结构）。 |
| `Patches/PatchManager.cs` | 注册新补丁 `ItemPatch`、`GeneralTextPatch`。 |
| `Services/TranslationManager.cs` | **分类优化**：改为自动扫描 `translations/*` 本地自定义类别目录（不再硬编码 items/ui），动态加载所有子类别并合并进 `Texts`；作者类型（`another_name`/`ability_descriptions`）仍显式加载。 |
| `Services/TranslationCache.cs` | 本地自定义类别直接读本地文件（跳过远端 404 请求，减少噪音）；暴露 `CacheDir` 属性供 `TranslationManager` 扫描。 |
| `Services/TranslationPaths.cs` | **分类优化**：`BuildRemoteUrl`/`BuildCachePath` 对未知类型不再抛异常（统一按 `{type}/{lang}.json`）；新增 `ReservedTypes` 保留集合与 `EnumerateLocalCategories` 扫描辅助方法。 |

---

## 四、新增配置项

```ini
[Collector]
# 收集未翻原文到 dump 目录，便于建立翻译数据（默认 true，游玩即持续贡献）
CollectText = true
# 启用启发式分类器，将通用 UI 文本自动归入细分子类别，便于校对（默认 true）
ClassifyText = true

[MachineTranslation]
# 机翻预处理总开关（默认 false，需先安装 ollama 并 pull 模型）
Enabled = false
# 引擎：openai(兼容, 如 ollama/LM Studio) / sugoi / libre
Engine = openai
# 本地翻译服务地址（默认指向 ollama 的 OpenAI 兼容端点）
Endpoint = http://127.0.0.1:11434/v1/chat/completions
# 模型名（openai/ollama 用），如 qwen2.5:3b / qwen2.5:7b
Model = qwen2.5:3b
# 单次请求超时秒数
TimeoutSeconds = 30
```

---

## 五、运行时产生的文件（建议加入 .gitignore）

| 文件 | 说明 |
| --- | --- |
| `dump/{category}_raw.json` | 收集到的未翻原文（按细分类别），供人工/脚本翻译。常见类别：`equipment_effect/facility/bar/mission/materials/abyss_code/dialogue/system/ui_misc/items/ability_descriptions` |
| `cache/translations/{category}/zh_Hans.json` | 各类别人工翻译数据（简体）。启动时自动扫描加载，无需改代码即支持新类别 |
| `cache/translations/{category}/zh_Hant.json` | 各类别人工翻译数据（繁体） |
| `cache/translations/other/{category}/zh_Hans.json` | 机翻结果按类别缓存（本地生成） |
| `cache/translations/other/zh_Hans.pending.json` | 机翻待翻队列（格式：`{"模板": "category"}`，本地生成） |

**收集→翻译→回填闭环：**
1. 玩家游玩时，未翻译的日文自动按类别收集到 `dump/{category}_raw.json`。
2. 人工翻译或机翻（MT 开启后下次启动自动批翻）后，将译文放入 `cache/translations/{category}/zh_Hans.json`。
3. 游戏重启时自动扫描加载所有子类别，无需修改代码，新类别同样生效。

---

## 六、设计要点

- **统一查表**：界面文本走 `TextTranslator.Process` → `TranslationManager.Texts`，与剧情翻译路径解耦、互不影响。
- **启发式分类器**：`TextClassifier.Classify` 按关键字短路匹配把通用 UI 文本归入 9 个子类别，高置信类别（装备效果/深渊/设施/酒馆/素材）命中率高；`items` 与 `ability_descriptions` 有各自精确钩子，不经分类器。
- **动态类别扫描**：`TranslationManager` 启动时扫描 `translations/*` 目录，凡非保留类型的子目录均自动加载；新增类别（如 `equipment_effect`）无需改代码即生效，便于社区持续扩充。
- **技能模板翻译**：技能描述由模板 + 数值动态生成，故针对**原始模板**（含占位符）翻译，一条翻译适配所有等级。
- **机翻数字归一化**：机翻把句中数字替换为 `{0}{1}` 占位符再翻译，使「合計75%」「89.2%」等数值变体共用一条缓存；`<...>` 标签内数字保持不变；通过 system + few-shot 约束模型保留占位符。
- **机翻带类别**：pending 队列记录 `{模板: category}`；翻完后按类别写入 `other/{category}/` 子目录，便于按功能区域分别查阅和校对机翻质量。
- **机翻非实时**：翻译只在后台批量进行（启动后 + 每 30s 扫一次新增），不在 UI 线程阻塞，不卡游戏。

---

## 七、兼容性与风险

- **向后兼容**：`CollectText` 与 `MachineTranslation.Enabled` 默认均为 `false`，不开启时行为与原版一致。
- **性能**：`TMP_Text.set_text` 为全局 hook，命中字典为字典查找（O(1)）；未命中仅做一次假名判定，开销可控。机翻为后台异步，不影响主线程。
- **R18 兼容**：以日文原文为 key 进行替换，系统级文本（UI/道具/技能）在全年龄与 R18 版本基本一致，未提供翻译的内容保持原文、不会报错。

---

## 八、不建议合并的本地改动

- `AbyssMod/AbyssMod.csproj`：仅把 `<GameDir>` 改成了本地游戏路径、并调整了 `Utility.dll` 引用路径，**属于本地构建环境，请勿合并**。本补丁文件已**排除**该文件。

---

## 九、如何查看/应用

- 附带 `abyssmod_changes.patch` 为源码 diff（由 `git diff --no-index` 生成，路径为绝对路径，**仅供阅读参考**）。
- 建议直接：**拷贝上述 5 个新文件** + 按「三、修改文件」对 7 个文件做对应增改即可。
