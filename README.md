# Mayday Callouts

《Mayday Airways》的航空告警音增强插件 —— 给飞行过程补上一层真实航空风格的声音告警。

[更新记录](CHANGELOG.md) · [发布包](https://github.com/Fui-Reconk/mayday-callouts/releases/latest)

---

## 当前状态：v0.1.0 探测版

本版本**不主动发声**，只做一件事：**画出游戏告警体系的精确覆盖图**。

跑一轮飞行（最好故意触发点故障：关油门、制造破洞、忘记放起落架），日志里会记录：

- 游戏自己的驾驶舱告警呼话（`WarningPanel.LastCallout`）与声音播放状态
- 游戏弹出的每一条通知（`key` + 级别）
- 发动机火警 / 故障位 / 状态变化、燃油跨阈值、机体破洞变化
- 导演阶段切换、Chaos 随机事件的开始与结束
- 启动时转储 SoundBank 全部音效 id

拿到这份覆盖图就能确定：哪些告警游戏**只有通知没有声音**（正式版要配音的），哪些**游戏自己已经会响**（不重复做）。

## 安装

同 [mayday-career](https://github.com/Fui-Reconk/mayday-career) 的方式：BepInEx 6 BE (IL2CPP) 环境，`MaydayCallouts.dll` 放进 `BepInEx/plugins`。

可与 mayday-career 共存，互不干扰。

## 配置

首次启动后生成 `BepInEx\config\local.mayday.callouts.cfg`：

| 项 | 默认 | 说明 |
|---|---|---|
| `Probe` | `true` | 探测模式（v0.1.0 核心） |
| `DumpSoundBank` | `true` | 启动时转储游戏全部音效 id |
| `Volume` | `1` | 全局音量 |
| `TestSoundId` | 空 | 填一个 SoundBank 音效 id，进机舱按 **P** 试听（验证 Play2D 通路） |

## 已知兼容性挂载点

```
Mirror.NetworkManager.Update        每帧轮询
GameDirector.SetStage               导演阶段
ChaosEvents.TryBegin / End          随机事件
GameState.ServerBroadcastNotify     游戏通知
GameState.ServerSetAlert            驾驶舱警示
Mayday.Audio.AudioService.Play2D    音频试听（v0.2 起用于正式播放）
WarningPanel（只读观察）             游戏自己的 GPWS/呼话/失速/超速/起落架喇叭
```

## 音效文件对应表

音效文件放 `BepInEx\plugins\MaydayCallouts\Audio\`，**文件名（不含扩展名）就是触发规则**，wav/ogg 均可。缺失的自动跳过，不影响其他音效。

自制合成包（`tools\gen_sounds.py` 生成，已随安装部署）覆盖了循环告警类；**GPWS 人声呼话需自备音源**，按下面的名字改名放进目录即可：

| 文件名 | 触发条件 | 自制包已含 |
|---|---|---|
| `stall_horn` | 失速（驾驶舱失速灯亮），循环直到改出 | ✅ |
| `gear_horn` | 起落架警告（低空未放起落架），循环 | ✅ |
| `overspeed` | 超速（超速计时器走动），循环 | ✅ |
| `fire_bell` | 发动机火警（任一 fire > 0），循环 | ✅ |
| `master_caution` | 任一发动机新增故障（单次） | ✅ |
| `low_fuel` | 任一油箱油量跨过 20%（回油复位） | ✅ |
| `depress_horn` | 客舱破洞增加（单次） | ✅ |
| `callout_100` / `callout_80` / `callout_60` / `callout_40` / `callout_30` / `callout_20` / `callout_10` | 下降穿越对应离地高度（AGL，与游戏内部单位一致），一次性 | ❌ 自备 |
| `callout_minimums` | 下降穿越决断高（游戏 MinimumsHeight = 60） | ❌ 自备 |
| `gpws_sink_rate` / `gpws_pull_up` | 大下降率 / 更急（需 `ReplaceGameGpws=true`，开启后游戏自带 `gpws_pullup` 被屏蔽） | ❌ 自备 |

高度判定用游戏自己的 `PlaneFlight.GroundProbeAgl`（每帧自算的离地高度）和 `WarningPanel.calloutHeights` 配置表，所以单位/阈值与游戏内部完全一致，无需标定。

## 风险提示

修改游戏运行行为可能违反服务条款，请自行判断，风险自负。仓库内不包含任何游戏文件或由其生成的派生物。

## 许可证

[MIT](LICENSE)。BepInEx 与 HarmonyX 为 LGPL-2.1，不随发布包分发。
