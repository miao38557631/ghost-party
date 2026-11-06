# 别在这儿蹦跶 · Ghost Party

Unity 客厅窗帘恶作剧原型。当前使用运行时生成的临时场景、演出与 BGM。

当前讨论确定的目标流程见 [基础玩法与窗帘 v1.4](Docs/基础玩法与窗帘_v1.4.md)：连续三拍点窗、NPC 靠近期间点窗帘让鬼飞入（靠近不计拍）、两拍首抽、客人转晕冒星星（也不计拍）、两组左右连抽、蓄力抽飞。下方“旧版试玩记录”仅保留历史操作说明；当前补丁以游戏中的操作提示为准，仍未宣称完整实现 v1.4 设计。

## 打开工程

1. 克隆仓库。
2. **首次打开 Unity 前**，在仓库根目录运行 `powershell -ExecutionPolicy Bypass -File .\Tools\Setup-Local.ps1`。这会生成被 Git 忽略的本地 UOS 加密密钥；已有密钥不会被覆盖。
3. 用 Unity Hub 打开仓库根目录，使用 **Unity 2022.3.62f2c1**。
4. 等待 Unity 还原 Packages 和导入资源。内置 UOS Launcher 包从 cnb.cool 获取，需要能访问该地址及 Unity 包源。
5. 推荐打开 `Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity`，点击 Play；旧原型场景 `Assets/Scenes/SampleScene.unity` 仍保留。

工程使用内置渲染管线，输入采用 Input System 1.7.0 + 旧 Input Manager（Both）。游戏键鼠和 Xbox 手柄由 `Assets/Scripts/GhostInput.cs` 统一适配，原有键鼠 UI 继续使用 StandaloneInputModule；主要玩法逻辑位于 `Assets/Scripts/GhostPartyPrototype.cs`。

## Xbox 手柄

左摇杆移动、A 附身/节拍点按、保持 RT 配合左摇杆完成方向操作与蓄力、B 退出。附身的 A 需要先松开，才能开始点按；连续左右挥击可一直按住 RT。确认命中时分档震动，暂停、失焦、退出、切场景和断连会清理马达。

力度、时长和死区在 `Assets/Resources/Input/GhostInputSettings.asset` 调整。首次改为 Both 后重启编辑器。完整规则、回归菜单和实体手柄验收见 [手柄输入与震动](Docs/手柄输入与震动.md)。

2026-11-06：Unity 原生编译及两场景虚拟手柄/键鼠 Play 回归通过（203 项，0 失败），包含完整动作流程、震动命令及 UI 回调。实体马达、真实 OS 输入和音画手感仍需人工确认。

## UI 补丁 v1（当前）

- WASD / 方向键移动，靠近窗帘按 E 进入特写；按屏幕当前提示点按左键、按住并沿箭头自由滑动。不要求描线。
- 外圈收拢提示输入拍点；滑动后保持按住等拍点。自动演出阶段不用操作；保持阶段不要松开；失误按提示重试。
- 默认集中显示当前操作、节拍圈和紧邻下一步。原长说明、流程数字和参数面板默认隐藏，F8 切换调试视图。
- L 切换中英文；特写内 R 重置，Esc 返回探索。音乐、输入判定、窗帘流程、NPC 和镜头仍使用现有逻辑。
- 当前玩家提示为运行时生成的 CurtainPlayerHUD。可在 CurtainGameplayUI 组件的 Player HUD (screen space) 参数中调整偏移、比例、字体和配色；Play 中直接修改生成对象不会永久保存。
- 本轮通过 Unity 2022.3.62f2c1 原生编译、独立语法检查及运行时 UI/阈值/进退场断言。实际 E 与 F8 已验证；完整鼠标通关、蓄力中断恢复及自然漏拍挣脱仍需手动验收，不能将运行时方法调用视为完整键鼠测试通过。

## 旧版试玩记录

以下内容对应旧原型，保留用于开发对照；当前操作以集中提示为准。

- WASD / 方向键移动，靠近窗帘按 E 进入特写。
- 吸引注意自动演出；随后按住鼠标左键向下拖动并松开，裹住客人。
- 挥打按鼓点依次左、右、左、右，可用 A/D、左右方向键或鼠标左右滑动；Space 自动选方向但仍需卡点。
- 蓄力阶段按住鼠标依次向左下、右上移动后松开，触发重抽，客人离场后返回探索。
- L 或语言按钮切换中英文，Esc 返回探索。
- 开发快捷键（特写内）：R 重置；F2 裹住；F3 挥打；F4 蓄力；F5 重抽。
- Space 可跳过部分非挥打步骤，供开发测试使用。

BGM 持续播放，挥打判定与节拍提示共用 DSP 时钟。目前严格卡点主要用于挥打段；裹住和蓄力仍为手势流程原型。

## 编辑窗帘操作 UI

一楼和 SampleScene 中保留可持久编辑的 CurtainGameplayUI 预制体与三组世界空间 Canvas。补丁 v1 在窗帘挑战中使用新的 CurtainPlayerHUD，原当前/下一步提示不再同时显示；编辑旧 Canvas 不会改变新 HUD 的布局。退出 Play 后仍可修改保留 Canvas 的 RectTransform、Image 和 Text，保存场景即可保留。菜单 **Ghost Party > UI > Select Curtain Canvases** 可快速定位。具体参数与注意事项见 [窗帘 UI 编辑说明](Docs/窗帘UI编辑说明.md)。

## 版本控制与本地配置

仓库保留 Assets（含必要 .meta）、Packages 和 ProjectSettings。Library、Temp、Logs、obj、UserSettings、构建输出以及自动生成的 IDE 工程文件不提交。

UOS Launcher 的 `EncryptKey.cs` 和 `UOSSettings.asset` 是本地配置，不上传。新克隆请先运行初始化脚本，避免缺少 EncryptKey 类型导致编译错误。新密钥不能解密另一台机器的旧 UOS 配置；如果以后使用 UOS 服务，应在当地重新配置服务信息。

## 验证范围

原型此前通过本地生成的 Assembly-CSharp.csproj 的 dotnet 编译检查。实际音画同步、HUD 和输入手感仍应在 Unity Play Mode 中验证；克隆后 .csproj 由 Unity 重新生成。

## 2026-11-06 交互更新与 UI 评审

- 已接入：[E/A 附身提示](Docs/附身提示UI.md)，与实际目标、距离和可附身状态共用判断。
- 已验证：[Unity 输入与附身提示检查记录](Docs/Validation/2026-11-06/README.md)，归档 35 项提示专项与 203 项完整流程检查，以及实际 Game 截图；实体手柄手感待人工确认。
- 待实现：[窗帘附近的小范围节拍 UI 效果预览](Docs/UIReviews/CurtainContextualUI/README.md)。保留原版场景内提示的视觉方向，本次仅归档方案与图片，未将该新版节拍 UI 接入运行时。
