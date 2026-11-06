# 窗帘附身提示

一楼 `Assets/LevelWhitebox/Scenes/Level01_FirstFloor.unity` 已保存新提示配置。黄色窗户右侧的棕红色 `Curtain` 是实际附身目标；`Curtain/PromptAnchor` 是气泡尖角锚点。原独立红色 `Press E` TextMesh 已移除。

## 素材与布局

- 使用素材包原始 `Possess_E.png` / `Possess_A.png`，保存在现有 `Assets/Resources/UI` 目录。文件内容未改绘。
- 导入为 Sprite (2D and UI)、Single、Full Rect；保留输入 Alpha，开启 Alpha Is Transparency；无 Mipmap，无有损纹理压缩。
- 键鼠显示方形 E，手柄显示绿色圆形 A。直接复用 `GhostInput.UsingGamepad`，不增加另一套设备检测或按键入口。
- `CurtainPossessionPrompt` 的 Sprite 引用、216×64 显示尺寸、尖角 Pivot `(0.019, 0.5)` 和屏幕偏移保存在场景中。
- `PromptAnchor` 默认局部位置为 `(-0.5, 0.22, 0.5)`；该方块在场景中绕 Y 轴旋转了约 90°，这个位置对应当前探索镜头下的右边缘中上部。可直接在 Inspector 调整锚点 Transform。
- 气泡 Image 在 Play 时挂到已有 `PrototypeHUD` Canvas 下，不新增 Canvas；使用已有 1920×1080 CanvasScaler，关闭 Raycast Target。每帧将场景锚点投影到 Canvas，气泡保持正对屏幕。
- 原附身 HUD 放入 `PrankHudContent`，探索时隐藏该组，保留同一个 Canvas 来显示附身气泡。

## 交互一致性

`WhiteboxPlayable.CurrentInteractionTarget` 同时供 E/A 输入及气泡查询，继续使用工程原有 1.35 米水平交互距离。楼梯与窗帘同时可用时，保留原先的楼梯优先顺序；此时不显示窗帘气泡。

`LevelCurtainEncounter.CanStart` 与 `GhostPartyPrototype.CanStartEmbeddedPrank` 共同检查目标启用状态、初始化完成及探索模式。离开范围、目标禁用、引用失效、锚点无效、失焦/暂停或进入附身时隐藏；退出附身后按当前目标重新判断。进入附身的 A 继续使用现有释放门控。

## 验证方式

退出 Play 后执行 `Ghost Party > UI > Run Possession Prompt Play Checks`。测试用虚拟键鼠和 Gamepad 驱动当前一楼真实交互入口，检查距离、有效状态、楼梯重叠优先级、图标切换、漂移、E/A 进场、释放门控及退出恢复，并保存实际 Game 画面。

结果：`Logs/InputSystem/possess-ui-checks.json`；截图：`possess-E.png` / `possess-A.png`（同目录）。测试期间临时隔离 Unity 内的实体输入设备，结束时恢复；不保存 Play 中移动物件、锚点或玩家造成的变化。

2026-11-06 验证结果：Unity 原生编译通过；提示专项 35 项通过、0 失败、0 运行时错误；一楼与 SampleScene 原有手柄/键鼠完整流程复测 203 项通过、0 失败。已逐张检查实际 Game 截图，气泡尖角贴合方块右边缘中上部。程序化输入与画面检查不替代实体手柄操作确认。

实体手柄确认：Play 后靠近方块，轻推左摇杆确认绿色 A；按住 A 附身，确认气泡立即隐藏且未自动敲窗；松开后点按，按 B 退出，确认气泡按距离恢复。用键盘移动切回 E 后静置摇杆，图标不应因漂移跳回 A。
