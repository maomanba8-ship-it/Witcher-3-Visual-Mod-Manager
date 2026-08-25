# 已知问题 → 回归样本登记规则（T00-04）

> 目的：终结「发现问题后直接补 if」的循环。任何新结构 / 新冲突 / 新 bug，必须先成为**匿名样本 + 期望结果 + 可执行测试**，再修改分析器、Manifest、共享计数或部署器。

## 一、登记要求（每个问题必须记录）

新问题录入时必须填下列字段，缺失则不允许开始改代码：

| 字段 | 说明 |
| --- | --- |
| 问题/来源 | 纯文本描述；真实 Mod 名 / 包名**不写进测试**，只写结构类型（匿名化）。 |
| 输入结构 | 解压后的目录形态（例如 `dlc\MagicSpellsRevamped\content`）。 |
| 期望 Manifest | `ManifestBuilder.Build` 期望输出的 `(域, 归一化路径, 长度)`（需含 SHA-256 的样本用 `expected.txt` 或测试内等价断言）。 |
| 期望部署 | 导入后应落在 `mods/` 还是 `dlc/`；目标目录名；是否共享。 |
| 期望冲突分类 | `ConflictKind`：`DuplicateContent` / `DefiniteOverride` / `ScriptOverride` / `BinOverrideRisk` / `UnsupportedContainer` / 无冲突。 |
| 对应测试名 | xUnit / FixtureRunner 里的用例名（例如 `ManifestBuilderTests`、`ConflictAnalyzerTests`、`PackageAnalyzerTests`）。 |
| 原始提交 | 修这个问题的 commit hash。 |

## 二、目录与文件约定

- **结构样本**：`Witcher3ModManager\Core\Analysis\Fixtures\<case-name>\`，配 `expected.txt`（用 `- Kind: Name` 行描述期望组件；`Decision:` / `Warning:` 是元信息）。
- **冲突样本**：在 `Witcher3ModManager.Tests\ConflictAnalyzerTests.cs` 以 `ComponentFile[]` 直接声明（纯内存，不需临时目录）。
- **Manifest 样本**：在 `Witcher3ModManager.Tests\ManifestBuilderTests.cs` 用临时目录 + 内存断言。
- **共享引用计数样本**：在 `Witcher3ModManager.Tests\SharedResourceResolverTests.cs` 用内存 `FolderClaim[]` 断言。
- 任何样本必须**匿名**：不得包含真实版权资产、真实包名、真实用户数据。

## 三、已登记案例（历史问题 → 回归样本）

### 1. 多 Mod / 多 DLC 组合
- **输入**：同一压缩包内多个 Mod，或 mod + dlc 配套。
- **样本**：`Fixtures\mod-with-dlc`（`modSpawn` + 其下 `dlcSpawn`）、`Fixtures\mods-container`（`mods\modX`）。
- **期望**：分别识别为 `(Mod, modSpawn)` + `(Dlc, dlcSpawn)`、`(Mod, modX)`。
- **测试**：`PackageAnalyzerTests` 的 `Analyze_matches_expected_components`。

### 2. 非标准 DLC 容器（名字不带 dlc 前缀）
- **输入**：`dlc\MagicSpellsRevamped\`（容器父目录是 `dlc`，但组件名不带 `dlc`）。
- **样本**：`Fixtures\dlc-container-nonstandard`。
- **期望**：识别为 `(Dlc, MagicSpellsRevamped)`，而非当作未知或 Mod。
- **测试**：`PackageAnalyzerTests`。

### 3. 纯 bin
- **输入**：只含 `bin\...`，无 `mod`/`dlc`。
- **样本**：`Fixtures\bin-only`。
- **期望**：识别为 `(Bin, bin)`。
- **测试**：`PackageAnalyzerTests`。

### 4. 重复导入 / 目标名补前缀
- **输入**：裸目录 `Foo`（不带 `mod` 前缀），已存在 `modFoo`。
- **样本**：`Fixtures\weak-evidence-mod`（`Foo\content`）+ 重复导入预检逻辑。
- **期望**：先按最终存储名 `modFoo` 预检，确认后写到 `modFoo`；取消不写任何数据。
- **测试**：`PackageAnalyzerTests`（weak-evidence 输出确认决策）+ 手工 A2 验收（目标名碰撞）。

### 5. 共享组件（EH_RiderUtil 类）
- **输入**：多个 Mod 在 `GameModFolders` 中引用同一文件夹；存储目录只有一份。
- **样本**：`SharedResourceResolverTests`（两个 owner 共享 => `RefCount=2`、`IsShared=true`；单 owner => 不共享）。
- **期望**：`Disable`/`Delete` 对 `RefCount>1` 的文件夹跳过（不删），`RefCount==1` 才处理。
- **测试**：`SharedResourceResolverTests`。

### 6. 冲突——Mod 与 DLC 同相对路径、不同哈希
- **样本**：`ConflictAnalyzerTests` 的 `mod_vs_dlc_same_path_diff_hash`。
- **期望**：无冲突（不同域隔离）。
- **测试**：`ConflictAnalyzerTests.Analyze_matches_expected_kinds`。

### 7. 冲突——同域同路径、相同哈希
- **样本**：`same_domain_same_hash`。
- **期望**：`DuplicateContent`（仅提示，不判真冲突）。

### 8. 冲突——同域同路径、不同哈希
- **样本**：`same_domain_diff_hash`。
- **期望**：`DefiniteOverride`。

### 9. 冲突——同域 `.ws` 不同哈希
- **样本**：`same_domain_ws_diff_hash`。
- **期望**：`ScriptOverride`。

### 10. 冲突——同一组件重复提交同一文件
- **样本**：`same_component_dup_file`。
- **期望**：无冲突。

## 四、新问题上线流程

1. 先写**匿名样本** + 期望结果 + 测试（`PackageAnalyzerTests` / `ConflictAnalyzerTests` / `ManifestBuilderTests` / `SharedResourceResolverTests` / FixtureRunner）。
2. 运行 `dotnet test`（及 `FixturesRunner` 全量）确保失败重现。
3. 再改分析器 / Manifest / 共享计数 / 部署器，直到测试转绿。
4. 独立提交，`git diff --check` 通过，不混入发布包 / `bin` / `obj` / 用户数据。
5. 在本文件登记该案例（引用测试名与 commit）。

> 记住：先 fixture，后改码。没有样本和测试，禁止直接改分析器或部署器行为。