# 窗帘操作 UI 编辑

## UI 补丁 v1 补充

当前窗帘挑战以运行时生成的 CurtainPlayerHUD 为玩家提示，集中显示当前操作、节拍圈和紧邻下一步。原当前/下一拍世界空间提示不再与它同时显示；长说明与参数面板默认隐藏，F8 切换调试视图。

若要调整新的玩家提示，请在场景或预制体的 CurtainGameplayUI 组件中修改 Player HUD (screen space) 下的偏移、比例、字体和配色参数。Play 时生成的 CurtainPlayerHUD 可用于观察，但停止 Play 后对生成对象的直接修改不会保留。

下文三组 Canvas 的持久编辑方法仍适用于保留对象；修改这些旧 Canvas 不会改变新 HUD 的布局。GuestEscapeGauge 仍用于客人挣脱进度。新 HUD 的所有图形关闭射线拦截，滑动进度读取玩法当前难度的真实判定阈值。

一楼 Level01_FirstFloor 和旧原型 SampleScene 中已保存 CurtainGameplayUI 预制体实例。三个提示保留 World Space Canvas，跟随窗帘或客人，不占用屏幕固定位置。

## 在 Hierarchy 修改

1. 先退出 Play 模式。若 Unity 提示场景在磁盘上更新，在保存自己需要保留的未保存修改后重新打开场景。
2. 菜单 Ghost Party > UI > Select Curtain Canvases 会选中本场景的 CurtainGameplayUI。也可以在 Hierarchy 搜索以下三个名字。
3. CurtainWorldCue：当前动作圆形提示；下有 WhiteCircle（Image）和 CueLabel（Text）。
4. CurtainNextBeatCue：下一拍提示；下有 WhiteCircle、CueLabel，CanvasGroup 控制整体透明度。
5. GuestEscapeGauge：挣脱进度环；Track 是底环，Progress 是径向填充 Image，EscapeCount 是计数 Text。
6. 修改对应 Canvas 的 RectTransform 位置、旋转、宽高、缩放，或修改子对象的 Image 颜色/图片、Text 字体/字号/颜色。保存场景即可保留。跨场景共用的调整可在预制体模式编辑 Assets/Resources/UI/CurtainGameplayUI.prefab，或将实例覆盖 Apply 到预制体。

Canvas 初始比例约 0.006（下一拍为 0.0042），因为它们使用世界坐标；例如宽度 100 乘以 0.006 就是 0.6 个世界单位。

## 自动跟随和节拍

CurtainGameplayUI 组件公开 Curtain Offset、Next Cue Offset、Escape Offset、Closeup Down Offset 和 Beat Pop Strength。整体跟随偏移与节拍弹跳强度在这里调整。

CurrentCueAnchor、NextCueAnchor、EscapeGaugeAnchor 负责自动跟随；BeatPulse 负责节拍弹跳。请改它们内部的三个命名 Canvas 来调整具体布局，避免修改由玩法控制的 Anchor 或 BeatPulse。

运行时只更新跟随锚点、显隐、动作符号和挣脱填充/计数。字体、颜色和 Canvas 本地位置/尺寸/缩放不会被逐帧重设。动作符号和计数文字仍由玩法控制。

Unity 在 Play 模式中的普通 Inspector 修改会在停止后还原；永久修改请在非 Play 模式完成并保存场景或预制体。

## 维护与检查

新场景可使用 Ghost Party > UI > Add Curtain Canvases 添加实例（不会重复添加，也不会自动保存场景）。

Ghost Party > UI > Validate Curtain Canvases 会在独立预制体预览环境检查三个 Canvas 和资源绑定，检查两个场景文件中的预制体实例，并验证跟随和节拍更新不会覆盖设计者的布局。检查不会保存预制体或改动当前场景。
