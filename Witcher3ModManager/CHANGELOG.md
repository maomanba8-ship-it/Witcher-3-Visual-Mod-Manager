# Changelog

## v0.8.1 (2026-06-19)
- 修复 .w3strings 虚假冲突（与忽略规则同条件）
- 修复 DLC-only Mod 导入 Access Denied 报错
- 设置页版本号同步为 v0.8.1
- 设置页「更多功能」列表同步当前开发进度
- 部分 UI 中文文本修正

## v0.8.0 (2026-06-17)
- 多 Mod/DLC 文件夹支持（GameModFolders / GameDlcFolders 列表）
- 冲突检测文件忽略规则系统
- 潜在覆盖检测与状态栏可点击
- 导入对话框级联弹窗修复（事件处理器累积）
- OpenFileDialog COM 泄漏修复
- 测试/正式双模式数据库分离

## v0.7.0
- Mod 去重系统
- 预设保存/加载/删除
- 自定义封面图片系统
- 编辑显示名（不影响原始文件名）
- 冲突检测缓存化

## v0.6.0
- 冲突检测初版
- Bundle 补丁 Mod 拒绝安装提示
- 状态栏冲突/覆盖计数

## v0.5.0
- BAML DeferredBinaryDeserializerExtension 崩溃修复
- 所有 StaticResource 替换为硬编码颜色
- Mod 启用/禁用使用 GameModFolderName
- FindModRoot 智能识别