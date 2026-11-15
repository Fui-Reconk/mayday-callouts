# 更新记录

## v0.3.0 — 声音层完全体（真实音源 + 现实告警逻辑）

从"探测 + 合成音"进化为完整的现实航空告警系统：

### 告警逻辑（全部按现实规则实现）

- GPWS Mode 1 包线拆分：SINK RATE 下降率超限即报（不限高度）；PULL UP 升级警告只在低空（`PullUpAgl` 可配置，默认 150），俯冲期间每 2.5s 重报
- TOO LOW GEAR（Mode 4）：真实语音每 4.5s 重报，改出复位，并被 Pull Up 优先级压制（GPWS 同时只播一条最急语音）
- MINIMUMS：决断高每次进近必响，且替代同值的 60 高度呼话（修复：此前与 callout_60 同帧被间隔保护误杀且永久闩住，从未响过）
- V1 起飞呼话（`V1Speed` 阈值可配，音源待放 `v1.wav`）
- RETARD 落地收油门提醒（放下起落架 + 下降穿越 15，每次着陆一次）

### 声音层重构（注册进游戏 SoundBank）

- 自制音效注册进游戏 SoundBank 三张表（defs/defList/clips，前缀 `mco_`），播放统一走 `AudioService.Play2D`——与游戏 UI 音同源的已验证出声管线
- 循环告警 cooldown 按音频实际时长设置，250ms 重触发自然循环
- 替换式覆盖：`SuppressSounds` 屏蔽游戏原声 + 同名 mod 音源自动接替（`ap_disconnect` 已默认强制屏蔽，接替空客 AP 断开语音）
- 真实音源接线：GPWS 人声呼话 / MINIMUMS / PULL UP / SINK RATE / TOO LOW GEAR / 空客 STALL 语音 / 波音 747 主警告 / 空客 RETARD / AP 断开

### 修复

- 音频加载弃用 UnityWebRequest（interop 缺口 MissingMethodException），改手动 RIFF/PCM 解析；ogg 暂不支持
- AGL 来源改为 FlightSim.Pos 差值（GroundProbeAgl 恒定 22.5 不可用、PlaneFlight.transform 不随飞机移动——均为实测教训）
- SoundBank / FlightSim 不是 Unity 对象，实例经 `AudioService.Instance.Bank` / `PlaneFlight.sim` 获取
- 非创建时覆盖的 cfg 屏蔽项不再丢：`ap_disconnect` 代码级强制屏蔽

## v0.1.0 — 探测版

首个版本。**不发声**，只做告警体系探测：

- 记录游戏驾驶舱告警系统（WarningPanel）的呼话、声音播放状态、失速/超速/起落架计时器
- 记录游戏弹出的每条通知（key + 级别）与驾驶舱警示
- 记录发动机火警/故障位/状态、燃油跨阈值、机体破洞数变化
- 记录导演阶段切换与 Chaos 随机事件的开始/结束
- 启动转储 SoundBank 全部音效 id；支持 Play2D 试听（按 P）

目的：跑一轮飞行后画出"游戏声音层 vs 通知层"的覆盖图，作为正式版配音的依据。
