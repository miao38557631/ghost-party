# NPC 随机生成与贴地

NPC 的生成、随机分布和"踩地"从 `GhostPartyPrototype` 里拆出来了，各自是独立组件，挂在空物体上就能调参数，不会再往大文件里堆逻辑。

## 怎么用

1. 菜单 **Ghost Party > Level > Create NPC Spawner**（或自己建一个空物体，加 `NpcSpawner` 组件）。
2. 在 Inspector 里调 `Npc Count`（默认 6）和 `Ground Offset`。
3. 进 Play。

场景里手摆的 `PartyGuest_01..04` 会在 Play 时被隐藏（`Hide Scene Guests`）；`CurtainGuest` 保留，因为窗帘遭遇战要用它，但它也会被自动加上贴地组件，不再飘着。

## NpcSpawner 参数

| 字段 | 默认 | 作用 |
| --- | --- | --- |
| Npc Count | 6 | 生成数量 |
| Random Seed | 0 | 0 = 每次 Play 都不同；填一个数字可复现同一布局 |
| Spawn Area | 空 | 限定生成范围；空 = 所有地板根节点（`GhostFloorSurface` 解析出来的） |
| Min Spacing | 2.5 | NPC 之间的最小间距（米） |
| Edge Margin | 1.5 | 离地板边缘的最小距离，防止卡在墙边 |
| Player Clearance | 3 | 避开鬼的出生点；0 = 不避让 |
| Ground Offset | 0 | 贴地微调：0 = 动画最低点正好踩地；正数抬高、负数沉下去 |
| Ground Scene Actors | 开 | 给场景里已有的 NPC（含 CurtainGuest）补贴地组件 |
| Hide Scene Guests | 开 | Play 时隐藏手摆的 `PartyGuest_*` |
| Match Reference Scale | 关 | 打开时改用场景里已有 NPC（默认 `CurtainGuest`）的世界缩放，而不是 `Npc Scale` |
| Size Reference | 空 | 手动的参照 NPC；空则自动找 `CurtainGuest`，再退回任意 `PrankNpcActor` |
| Npc Scale | 1.5 | 生成 NPC 的统一缩放（Level01 里 CurtainGuest 也设成了 1.5，所以整体统一） |
| Enable Roaming | 开 | 有烘焙 NavMesh 时让 NPC 随机漫游 |
| Roam Radius | 18 | 漫游半径 |

右键组件菜单里还有 **Spawn NPCs Now** / **Clear Spawned NPCs**（只在 Play 模式生效，不会改场景资产）。

## 贴地是怎么算的

`NpcGroundContact` 每帧取 Spine 美术的 `Renderer.bounds`，用 `bounds.min.y - artwork.position.y` 得到"当前帧最低点相对枢轴的距离"，并记录整个动画周期里的**最低**值。然后把美术的 Y 对准：

```
artwork.y = 地板高度 + Ground Offset - 动画最低点相对枢轴的距离
```

所以动画本身的上下起伏会保留，但整个周期里最低的那个姿态正好落在地板面上——也就是"最低点贴地"，而不是把每帧都焊死在地板上（那样会没有起伏）。

地板高度用 `GhostFloorProbe` 在 NPC 脚下向下探测（只认地板根节点、法线朝上的面），所以 NPC 走进舞池凹陷时也会跟着降下去。

## 不再用占位球

`PrankNpcActor` 多了一个 `CreatePlaceholderBody` 开关（默认开，保持场景里手摆 NPC 的旧行为）。生成出来的 NPC 把它设成关：不再 `CreatePrimitive(Sphere)` 造身体/头，只有 Spine 美术本体。

## 大小对齐

生成的 NPC 默认沿用场景里已有 NPC 的世界缩放（`CurtainGuest` 是 1.24，而新建物体默认是 1，所以之前看起来小一圈）。`ResolveSpawnScale()` 在生成前读一次参照 NPC 的 `lossyScale.y`，写到每个生成根节点的 `localScale`（已折算 spawner 自身的父级缩放）。实测 6 个生成 NPC 都是 1.24，美术世界高度 1.24~1.31，和窗帘 NPC 一致。想统一改大小就调 `Npc Scale` 并关掉 `Match Reference Scale`。

## 移动

漫游依赖烘焙好的 NavMesh（**Ghost Party > Level > Setup NPC Navigation** 会烘焙 `1_Floor`）。生成时会用 `NavMesh.SamplePosition` 采样，采样不到就退化成站桩，不会报错。NavMeshAgent 的 `baseOffset` 设为 0——高度由贴地组件负责，不再靠 agent 抬高。

诊断：菜单 **Ghost Party > Level > Report NPC Ground Contact** 会列出每个 NPC 是否有贴地组件、当前偏移与位置。
