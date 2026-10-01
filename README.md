# 别在这儿蹦跶 · Ghost Party

Unity 客厅窗帘恶作剧原型。当前使用运行时生成的临时场景、演出与 BGM。

## 打开工程

1. 克隆仓库。
2. **首次打开 Unity 前**，在仓库根目录运行 `powershell -ExecutionPolicy Bypass -File .\Tools\Setup-Local.ps1`。这会生成被 Git 忽略的本地 UOS 加密密钥；已有密钥不会被覆盖。
3. 用 Unity Hub 打开仓库根目录，使用 **Unity 2022.3.62f2c1**。
4. 等待 Unity 还原 Packages 和导入资源。内置 UOS Launcher 包从 cnb.cool 获取，需要能访问该地址及 Unity 包源。
5. 打开 `Assets/Scenes/SampleScene.unity`，点击 Play。

工程使用内置渲染管线、旧版 Input Manager。主要逻辑位于 `Assets/Scripts/GhostPartyPrototype.cs`，通过运行时入口自动创建原型。

## 试玩

- WASD / 方向键移动，靠近窗帘按 E 进入特写。
- 吸引注意自动演出；随后按住鼠标左键向下拖动并松开，裹住客人。
- 挥打按鼓点依次左、右、左、右，可用 A/D、左右方向键或鼠标左右滑动；Space 自动选方向但仍需卡点。
- 蓄力阶段按住鼠标依次向左下、右上移动后松开，触发重抽，客人离场后返回探索。
- L 或语言按钮切换中英文，Esc 返回探索。
- 开发快捷键（特写内）：R 重置；F2 裹住；F3 挥打；F4 蓄力；F5 重抽。
- Space 可跳过部分非挥打步骤，供开发测试使用。

BGM 持续播放，挥打判定与节拍提示共用 DSP 时钟。目前严格卡点主要用于挥打段；裹住和蓄力仍为手势流程原型。

## 版本控制与本地配置

仓库保留 Assets（含必要 .meta）、Packages 和 ProjectSettings。Library、Temp、Logs、obj、UserSettings、构建输出以及自动生成的 IDE 工程文件不提交。

UOS Launcher 的 `EncryptKey.cs` 和 `UOSSettings.asset` 是本地配置，不上传。新克隆请先运行初始化脚本，避免缺少 EncryptKey 类型导致编译错误。新密钥不能解密另一台机器的旧 UOS 配置；如果以后使用 UOS 服务，应在当地重新配置服务信息。

## 验证范围

原型此前通过本地生成的 Assembly-CSharp.csproj 的 dotnet 编译检查。实际音画同步、HUD 和输入手感仍应在 Unity Play Mode 中验证；克隆后 .csproj 由 Unity 重新生成。
