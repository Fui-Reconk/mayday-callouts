# Mayday Callouts

《Mayday Airways》的航空告警音增强插件 —— 按现实民航（波音 737 / 空客 A320）的告警逻辑，给飞行过程补上一层完整的驾驶舱声音告警。

[更新记录](CHANGELOG.md) · [发布包](https://github.com/Fui-Reconk/mayday-callouts/releases/latest)

---

## 功能

游戏的告警体系是半成品：状态机、指示灯、文字通知都在，但**驾驶舱声音大面积缺失**（失速只有灯、起落架喇叭无声、呼话链从未生效）。本插件按现实航空规则把声音层补齐：

### 告警语音（GPWS / 机组呼话）

| 告警 | 现实规则 | 触发判据 |
|---|---|---|
| 高度呼话 100/80/60/40/30/20/10 | 下降逐次报出 | 离地高度 AGL 穿越对应值（一次性门闩，爬升复位） |
| **MINIMUMS** | 决断高每次进近必响；与高度呼话同值时**替代**该呼话 | AGL ≤ 60（游戏 MinimumsHeight） |
| **SINK RATE** | 下降率超限，**不限高度** | 垂直速度 vy < -5，拉平复位 |
| **PULL UP** | 升级警告，只在低空成立 | vy < -9 且 AGL < `PullUpAgl`（默认 150），俯冲期间每 2.5s 重报 |
| **TOO LOW - GEAR** | 低空未放起落架（GPWS Mode 4） | 游戏起落架警告计时器，每 4.5s 重报，改出复位 |
| **STALL** | 空客 FWS 风格语音循环 | 游戏失速灯状态（stallLoop > 0） |
| 发动机火警铃 | 任一发动机 fire > 0，持续 | 循环直到解除 |
| 主警告 | 任一发动机新增故障 | 单次 |
| 低油量 | 任一油箱跨过 20% | 单次，回油复位 |
| 客舱释压 | 破洞数增加 | 单次 |
| **V1** | 起飞决断速度（需配置 `V1Speed`） | 滑跑速度达到阈值，每次起飞一次 |
| **RETARD** | 落地收油门提醒（A320） | 放下起落架 + 下降穿越 15 高度，每次着陆一次 |

**优先级仲裁**：PULL UP 播后 4 秒内压制 TOO LOW GEAR 等低优先级语音（真机 GPWS 同时只播一条最急语音）。

### 替换式覆盖

游戏自己的声音（如 `gpws_pullup`、`ap_disconnect`）可通过配置屏蔽，由 mod 的真实音源接管：

- `ReplaceGameGpws = true`：屏蔽游戏自带 GPWS Pull Up → mod 的 "PULL UP" 语音
- `SuppressSounds` 清单：任意游戏音效 id 精确静音；**若 Audio 目录有同名音源则自动接替播放**（如 `ap_disconnect` → 空客 AP 断开语音，已内置）

### 探测模式（保留）

启动日志记录：游戏通知/驾驶舱警示全量、发动机 fire/faults/状态、燃油跨阈值、破洞变化、阶段切换、Chaos 事件、全部声音播放 id（`声音: id` 行）——调试与后续扩展的依据。

## 安装

同 [mayday-career](https://github.com/Fui-Reconk/mayday-career) 的方式：BepInEx 6 BE (IL2CPP) 环境，`MaydayCallouts.dll` 放进 `BepInEx/plugins`。

可与 mayday-career 共存，互不干扰。**音效包**：把 wav 文件放进 `BepInEx\plugins\MaydayCallouts\Audio\`（见下方对应表；仓库不含音频内容）。

## 按键（进机舱后生效）

| 键 | 功能 |
|---|---|
| **P** | 播放 mod 音源自测音（master_caution）—— 验证播放链路 |
| **O** | 游戏 Play2D 试听（需配置 `TestSoundId`） |

## 配置

首次启动后生成 `BepInEx\config\local.mayday.callouts.cfg`：

### `[General]`

| 项 | 默认 | 说明 |
|---|---|---|
| `Probe` | `true` | 探测模式（日志观察，不影响游戏） |
| `DumpSoundBank` | `true` | 启动转储 SoundBank 音效 id |
| `Volume` | `1` | 全局音量（0-1） |
| `TestSoundId` | 空 | 按 **O** 试听的游戏音效 id |
| `SuppressSounds` | `ap_disconnect` | 屏蔽的游戏原声 id（逗号分隔，精确匹配）；同名 mod 音源自动接替 |

### `[Alarms]`

| 项 | 默认 | 说明 |
|---|---|---|
| `Enabled` | `true` | 告警音总开关 |
| `Stall` / `Gear` / `Overspeed` / `EngineFire` / `MasterCaution` / `LowFuel` / `Depressurize` | `true` | 各告警独立开关 |

### `[Callouts]`

| 项 | 默认 | 说明 |
|---|---|---|
| `Enabled` | `true` | 高度呼话引擎总开关 |
| `ReplaceGameGpws` | `false` | 屏蔽游戏自带 `gpws_pullup`，由 mod 的真实 GPWS 语音接管 |
| `V1Speed` | `0` | 起飞 V1 呼话速度阈值（0=关闭；先看滑跑日志 SPD= 再定） |
| `PullUpAgl` | `150` | PULL UP 的离地高度上限（按巡航高度调整） |

### `[Debug]`

| 项 | 默认 | 说明 |
|---|---|---|
| `TestAural` | `false` | 失速时直调游戏自己的 Aural 语音路径（诊断用） |

## 音效文件对应表

音效文件放 `BepInEx\plugins\MaydayCallouts\Audio\`，**文件名（不含扩展名）就是触发规则**，wav（16bit PCM）即可。缺失的自动跳过。

**音效文件不入 git 仓库**（音源自备/本地生成）：合成告警包可用 `tools\gen_sounds.py` 一键生成（零版权风险）；GPWS 人声等真实音源自备（授权自查），**同名文件 = 直接替换**。

| 文件名 | 触发条件 | 当前安装的音源 |
|---|---|---|
| `callout_100` / `callout_80` / `callout_60` / `callout_40` / `callout_30` / `callout_20` / `callout_10` | 下降穿越对应 AGL，一次性 | 真实 GPWS 人声 |
| `callout_minimums` | 下降穿越决断高 60 | 真实 "MINIMUMS" 语音 |
| `gpws_sink_rate` / `gpws_pull_up` | Mode 1 包线（见上表），需 `ReplaceGameGpws=true` | 真实语音 |
| `gpws_too_low_gear` | 起落架警告（Mode 4），每 4.5s 重报 | 真实 "TOO LOW GEAR" 语音 |
| `stall_horn` | 失速灯亮，按音频长度循环 | 空客 "STALL STALL STALL" 语音 |
| `overspeed` | 超速计时器走动，循环 | 合成抖杆音 |
| `fire_bell` | 发动机火警，循环 | 合成火警铃 |
| `master_caution` | 任一发动机新增故障，单次 | 波音 747 真实 MSTR CAUTION |
| `low_fuel` / `depress_horn` | 低油量 / 释压，单次 | 合成提示音 |
| `ap_disconnect` | 游戏放 AP 断开音时接替（需在 `SuppressSounds`） | 空客 AP 断开语音 |
| `callout_retard` | 着陆收油门提醒（见上表） | 空客 "RETARD" 语音 |
| `v1` | 起飞 V1（需 `V1Speed` > 0） | **缺失** —— 放入 v1.wav 即启用 |
| `gpws_bank_angle` / `gpws_dont_sink` / `gpws_glideslope` / `gpws_terrain` / `gpws_too_low_flaps` / `gpws_too_low_terrain` / `gpws_windshear` | 预留（规则在后续版本实现） | 已部署真实语音 |
| `callout_5` / `callout_70` / `callout_200` / `callout_300` / `callout_400` / `callout_500` / `callout_1000` / `callout_2000` / `callout_2500` | 预留（呼话表扩展后启用） | 已部署真实语音 |

### 技术要点（踩坑实录）

- **播放走游戏自己的管线**：自制音效注册进游戏 SoundBank 三张表（`defs`/`defList` 静态表 + 实例 `clips` 字典，前缀 `mco_`），用 `AudioService.Play2D` 播放——`AudioService.Instance` 是静态单例，SoundBank **不是 Unity 对象**（FindObjectOfType 拿不到，必须从单例的 `Bank` 属性取）
- **加载不走 UnityWebRequest**：interop 的 `GetAudioClip` 引用了不存在的 `DownloadHandlerAudioClip` 构造（MissingMethodException），改为手动解析 RIFF/PCM（16bit）+ `AudioClip.Create` + `SetData`
- **AGL 来源**：`FlightSim.Pos.y − 地面标定`（地面阶段自动捕获）。实测教训：`PlaneFlight.GroundProbeAgl` 恒定 22.5 不更新、`PlaneFlight.transform` 不随飞机移动——都不能用
- **循环告警**：def.cooldown 按音频实际时长设置，250ms 重触发被冷却吞掉，自然形成按音频长度的循环，条件解除后最后一声自然收尾

## 已知兼容性挂载点

```
Mirror.NetworkManager.Update         每帧轮询
GameDirector.SetStage                导演阶段
ChaosEvents.TryBegin / End           随机事件
GameState.ServerBroadcastNotify      游戏通知
GameState.ServerSetAlert             驾驶舱警示
Mayday.Audio.SoundBank.Get           音频解析观察（仅音乐走此路）
Mayday.Audio.AudioService.Play2D/Play  播放拦截 + 替换（原声屏蔽）
Mayday.Flight.WarningPanel（只读观察） 失速/超速/起落架/呼话状态
Mayday.Flight.PlaneFlight.sim.Pos    飞机位置（AGL）
Mayday.Systems.EngineUnit            fire/faults/state
Mayday.Systems.FuelSystem / HullSystem  燃油/破洞
```

## 风险提示

修改游戏运行行为可能违反服务条款，请自行判断，风险自负。仓库内不包含任何游戏文件或由其生成的派生物，也**不包含任何音频内容**（音源自备，授权自查）。

## 许可证

[MIT](LICENSE)。BepInEx 与 HarmonyX 为 LGPL-2.1，不随发布包分发。
