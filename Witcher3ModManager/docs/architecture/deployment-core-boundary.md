# 部署内核迁移边界（T00-01 架构冻结说明）

> 状态：v1 — 文档化，不对应任何代码行为变化。
> 目的：明确「旧部署链已冻结」，避免继续在旧 `ModImportService` / `Witcher3Service` 里堆规则；新能力只能进入新内核（纯函数 + 样本 + 测试）。

## 一、背景

项目曾因「发现新 Mod 结构 → 打补丁」「发现新冲突 → 加白名单」反复补丁，导致旧代码相互影响。我们决定走**先搭框架、再填充内部**的路线，把结构识别与冲突证据抽成只读纯函数层，用 fixture + 回归测试保护，再逐步接入部署。

## 二、旧链（默认冻结）

- **入口**：`Views\ModListPage.xaml.cs`、`MainWindow.xaml.cs`。
- **核心服务**：`Services\ModImportService`、`Services\Witcher3Service`、`Services\ModStorageService`、`Services\ConflictService`、`Services\BackupService`、`Services\PresetService`、`Services\OrphanDetectionService`。
- **冻结定义**：
  - 只能做**最小安全修复**（P0 类：重复导入决策、失败不改状态、共享目录保护、`mods.settings` 外部条目保留、7-Zip 随附发现等）。
  - **不得**在旧链里新增「针对某个 Mod 名 / EH 前缀 / 特定目录结构」的特判。
  - **不得**用递归删除覆盖未知目录。
  - **不得**在启动时静默迁移 / 去重用户数据。
  - **不得**用空 `catch {}` 或仅返回 `false` 吞掉关键部署/保存异常（允许返回 `bool`，但必须逐层向上可感知失败，UI 要给出提示）。
- **不删除旧链**：当前阶段没有新链等价替代，直接删除会锁死正常工作流。旧链的『冻结』是一种纪律，不是删除。

## 三、新链（唯一允许新增能力的地方）

新能力必须先进入下列**纯函数 / 数据骨架**层：

| 目录 | 作用 |
| --- | --- |
| `Core\Analysis\PackageAnalysis.cs` | 纯函数包结构分析：输入解压目录 → 输出组件/证据/警告/需确认项；不移动/删除/写文件、不弹窗、不调 7-Zip、不特判 Mod 名。 |
| `Core\Analysis\ConflictAnalysis.cs` | 纯函数冲突证据：输入 `ComponentFile` 清单 → 输出 `ConflictEvidence`；按（域,归一化路径）+ 哈希 + 组件身份；同组件重复不算冲突；Mod/DLC 同路径不误报。 |
| `Core\Manifest\` | 组件 Manifest：`ComponentManifest` + `ManifestBuilder`；为组件生成文件级相对路径/哈希/长度，纯只读；另提供轻量 `ListFiles` 供导入预览。 |
| `Core\Shared\` | 共享资源引用计数：`FolderClaim` / `FolderReference` / `SharedResourceResolver`；把「共享」变成可计算的引用计数，Enable/Disable/Delete 都从数据判断，不靠 Mod 名。 |
| `Witcher3ModManager.Tests\`、`Core\Analysis\Fixtures\` | fixture 与 xUnit 回归保护；每个新结构/新 bug 先成为样本与测试，再改分析器或部署器。 |

**新链规则**：任何新目录结构、新冲突语义、新部署规则都要先落到「匿名样本 + 期望结果 + 测试」，再被分析器 / Manifest / 共享计数实现；禁止先在旧链里加 `if folder == ...`。

## 四、风险开关（什么情况下暂停并回退）

出现下列任一情况，**停止进入下一阶段**，先更新本计划并由用户确认：

1. 任意 fixture / xUnit 回归失败；
2. 出现数据丢失或无法回滚的一次性操作；
3. 旧数据（`mods.json`、`mods.settings`、游戏目录）无法被新模型解释；
4. 真实项目结构与当前清单假设差异较大；
5. 变更开始影响正式版流程而用户未授权（测试版优先，不碰正式版）。

## 五、迁移出口（未来，非当前动作）

当新内核（Manifest + 引用计数 + 冲突证据 + 部署器）成熟并覆盖旧链全部功能后，再以单独立项：

- 把 `ModImportService` / `Witcher3Service` 的关键路径改为读 Manifest / 引用计数；
- 旧链内不再被引用的方法标记 `legacy` 或删除（删除前用引用检查确认无调用）；
- 关闭旧链前，先用用户授权副本做迁移报告与回归。

在迁移出口到来前，**不要**提前冻结 / 删除旧链入口。

## 六、验收

- 文档明确列出旧链 / 新链 / 切换条件 / 禁止项；
- 不改变任何用户行为（纯文档）；
- 新功能入口不能在旧链中新增特判。