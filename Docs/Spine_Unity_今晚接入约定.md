# Spine → Unity：今晚的最小接入约定

本页只规定素材交接与播放边界。窗帘玩法时序以 [基础玩法与窗帘 v1.4](基础玩法与窗帘_v1.4.md) 为准；当前 Unity 原型仍是旧流程。

## 先确认版本

- Unity 工程：2022.3.62f2c1，内置渲染管线。当前动画端是 **Spine 3.8.75 Professional**。
- 已按讨论把官网 `spine-unity 3.8`（2021-11-10）的 **Runtime 与 Editor 文件**导入 `Assets/Spine/`，保留原 `.meta`；未导入示例场景，也未改现有场景或玩法代码。官网标示的 Unity 支持范围到 **2020.3**。随后 Unity 已生成 `spine-unity.dll` 与 `spine-unity-editor.dll`，用户侧 Console 仅见 `TextureImporter.spritesheet` 的 CS0618 弃用警告；**脚本编译已通过，3.8 骨骼资产的导入与实际播放仍待验证**。
- 支持 Unity 2022.3 的最低 Runtime 系列是 `spine-unity 4.1`，但它不能读取 3.8 导出的骨骼数据。若当前 3.8 Runtime 在编辑器内不通过，再处理升级或逐帧图备选。
- 若现有 Spine Professional 授权可使用官方启动器，优先切到 **4.2.x 或 4.3.x**，然后给 Unity 安装相同系列的 Runtime；官方说明 Professional 后续版本更新不另收费。切换前复制一份 `.spine` 源文件；新版能打开旧版工程，保存为新版后旧版不能再打开。
- 当前先按 3.8 继续制作并验证骨骼导入。若实际资产导入或播放失败，再考虑改用 PNG 帧序列或升级版本；仅有 CS0618 黄色警告不构成失败。
- Unity 工程的 Color Space 当前是 **Gamma**。首轮先用官方推荐的该版本导出默认值；如果换色彩空间，再一起检查 Atlas 的 Alpha 设置。

## 今晚做的素材

| 骨骼文件 | 动画名 | 做到什么程度 |
|---|---|---|
| `npc_party` | `idle`、`walk`、`run` | 一个角色、一套骨骼；走和跑原地循环，Unity 控制实际位移 |
| `curtain_livingroom` | `idle` | 一套可抓住尾部、可向左右弯折的骨骼；今晚先验证变形范围 |

如果还有时间，再给 `curtain_livingroom` 加 `pull_left`、`slap_right` 两个粗动画，以及给 NPC 加 `first_hit`。这些名称先作为交接接口，后续双方一起改名时同步修改 Unity 引用。

### 窗帘拖尾端与自动回弹（路径约束方案）

先前建议把整片窗帘做成 `HingeJoint2D` 链，**没有解决尾端拖拽时整片布可控地跟随**，不作为当前方案。IK 单条只解 1–2 根骨骼，也不是主方案。

在 Spine 3.8 Professional 里，做一条从窗帘顶部到尾端的开放 Path。把 Path 顶部的结点和手柄权重绑到固定的 `top_ctrl`，尾部绑到 `tail_ctrl`，中段按需要混合或加 `mid_ctrl`。沿 Path 排若干布面骨骼，用 **Path Constraint** 让它们跟随路径（可先试 `Chain Scale`），再把窗帘网格权重绑到这些跟随骨骼。这样在 Spine 里只拖 `tail_ctrl`，路径、整列骨骼和布面就会一起弯；并不受 IK 两骨限制。参考：[Path Constraint](https://esotericsoftware.com/spine-path-constraints)、[Stretchyman 实例](https://esotericsoftware.com/spine-examples-stretchyman)。

路径约束负责**形状跟随**，本身不计算松手后的惯性。导入 Unity 后，程序在拖动时驱动 `tail_ctrl` 的位置；松手后用阻尼弹簧让这个控制骨骼自动回弹，Path Constraint 每帧把变形传给整片窗帘。Spine 3.8 编辑器没有 4.2 才加入的内置 Physics Constraint；不要把路径约束误称为自动物理。先在 Spine 内验证“拖一个控制骨骼整片布会弯”，再在 Unity 验证“松手自动回弹”。

## 导出与导入

1. 保留 `.spine` 工程源文件；当前用 **Spine 3.8.75** 导出 `npc_party.json` / `curtain_livingroom.json`、同名 `.atlas.txt` 和 PNG 图集，交给已安装的 3.8 Runtime。初版用 JSON，方便看版本和动画名。
2. 在 Spine 导出时勾选 Texture Atlas / Pack；Atlas 后缀设为 `.atlas.txt`。工程当前是 Gamma 色彩空间，初版用 Spine 3.8 默认的 PMA 图集设置；若看到黑边、白边或颜色异常，再检查 Spine 导出与 Unity 材质/纹理导入的 Alpha 设置是否匹配。
3. 素材分别放进 Unity 的 `Assets/Art/Spine/NPCParty/` 与 `Assets/Art/Spine/CurtainLivingroom/`，不要把两个图集和 JSON 混在同一文件夹。Unity 导入后应自动生成 `_SkeletonData`、`_Atlas` 和材质资源。
4. 场景角色使用 `SkeletonAnimation`（世界中的角色），先能播放 `idle → walk → run`，窗帘先能显示并播放 `idle`；无需今晚接入完整恶作剧。

## 与程序同学的边界

- 程序拥有角色移动、节拍判定、窗帘阶段、镜头与特写、NPC 退场。动画拥有骨骼、图集、动作和受击姿态。
- `walk` / `run` 做原地循环，角色在地图上的位移由程序控制，避免双重位移。窗帘尾端的抓取点和路径是 Unity 交互对象，动画负责布料表现。
- 只有判定成功才触发 slap / hit 动画；NPC 靠近和首次受击后的转晕是非计拍过门，等演出完成再进入下一输入阶段。BGM 始终继续。
- 双方不要同时改同一个 Unity Prefab 或场景：动画先交导出资源，程序在自己的场景 / Prefab 中挂组件。更新骨骼时覆盖导出资源并保留 Unity `.meta`。

## 明天的最小验收

1. 用单独的 `SpineSmokeTest` 场景导入一个 NPC 骨骼，拖 `_SkeletonData` 到场景，选 `SkeletonAnimation`。先在 Inspector 直接播放 `idle`，再用独立小脚本按 `1/2/3` 切 `idle/walk/run`；重复按键也不要每帧重置动画。窗帘先只验证骨骼可见和 `idle` 可播。
2. NPC 靠近窗户时由程序移动，Spine 播 `walk`；到位切 `idle`。
3. 鼠标抓窗帘尾部时，路径提示和布料动作方向一致；第一拍左拉不播放受击，第二拍右抽成功才播放 NPC 受击。

官方文档：[安装](https://esotericsoftware.com/spine-unity-installation) · [导出与导入](https://esotericsoftware.com/spine-unity-assets) · [版本下载](https://esotericsoftware.com/spine-unity-download)
