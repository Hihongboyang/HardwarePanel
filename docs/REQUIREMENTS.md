# HardwarePanel 需求与功能清单

> 版本基线：2026-11-10（自适应面板 + 轮播 + 配置 + 告警/通知 + 卸载 + 打包 + 旧安卓兼容 全部落地）。
> 架构与数据结构见 [ARCHITECTURE.md](ARCHITECTURE.md)。

## 1. 背景

用户用一部 Android 手机跑 Fully Kiosk Browser 做常驻硬件监控屏；PC 端单进程 WinForms 程序读传感器并对局域网提供 HTTP 服务。零云依赖、家用局域网可信、目标机器为个人 PC（x64，管理员运行）。

## 2. 需求列表（按演进顺序）

| # | 需求 | 状态 |
|---|---|---|
| R1 | 本机托盘程序 1Hz 采集硬件传感器（CPU/主板/内存/核显），ListView 全量展示 | ✅ 首版 |
| R2 | 手机浏览器访问仪表盘（HTTP 8080 + SSE 实时刷新） | ✅ 首版 |
| R3 | 图片投屏（`img/` 目录平铺） | ✅ 首版（后移入轮播页） |
| R4 | 开机自启（免每次 UAC 弹窗） | ✅ 首版（计划任务实现） |
| R5 | 信息页按屏幕自适应，**一屏显示全部选中信息不滚动**；图片移到独立轮播页 | ✅ 本次 |
| R6 | 显示哪些传感器用户可配置（HTML 配置页，持久化 config.json） | ✅ 本次 |
| R7 | 阈值告警（温度过高等）推送到屏幕 + 可选 webhook（ntfy 推手机） | ✅ 本次 |
| R8 | 通用入站通知 API（AI agent / 外部系统推送到屏） | ✅ 本次 |
| R9 | 软件卸载功能（清理自启任务 + 可选删除全部文件） | ✅ 本次 |
| R10 | 可分发安装包（框架依赖 / 自包含两种 zip） | ✅ 本次 |
| R11 | 兼容旧安卓 WebView（安卓 5.0.2，前端纯 ES5） | ✅ 本次 |
| R12 | 数据源扩展：LHM 显卡（NVIDIA/AMD）/存储/网卡 + 运行时间/主音量伪行，AIDA64 配置项全覆盖 | ✅ 本次 |
| R13 | 信息屏面板化重设计：按硬件分组固定 4 面板、阈值热区（琥珀预警→红告警）、界面中文化、设置页宽版 | ✅ 本次 |

## 3. 已实现功能矩阵

### PC 端（HardwarePanel.exe，WinForms .NET 8，管理员）

| 功能 | 入口/文件 | 说明 |
|---|---|---|
| 单实例互斥 | `Program.cs` | `Global\HardwarePanel` 互斥体，重复启动弹提示 |
| 托盘驻留 | `MainForm.cs` | 关窗进托盘继续采集；双击恢复；`--tray` 启动直接进托盘 |
| 1Hz 采集 | `MainForm.cs RefreshAll()` | LHM（CPU/主板/内存/显卡/存储/网卡）+ 性能计数器（Intel 核显伪行）+ 运行时间/主音量伪行；**逐硬件/逐传感器独立容错**，读不到只丢自己那行 |
| 主音量读取 | `VolumeLevel.cs` | CoreAudio `IAudioEndpointVolume` COM 互操作；任何失败静默返回 null（该行显示 `—`） |
| 环形历史 | `SensorHub.cs Ring` | 每键 300 点（5 分钟），供 sparkline |
| HTTP 服务 | `SensorHub.cs` | HttpListener `http://+:8080`，http.sys（PID 4）代听 |
| SSE 推流 | `SensorHub.cs HandleStream` | 全量首帧（含 history）+ 每秒增量 + notify/alarm 事件帧 |
| 告警引擎 | `AlarmEngine.cs` | 仅 `>` 比较，match 子串忽略大小写，type 可空=不限，按条冷却 |
| 通知管线 | `SensorHub.cs Notify()` | SSE 事件帧逐客户端广播 + webhook（ntfy/generic）fire-and-forget |
| 配置存取 | `ConfigStore.cs` | `config.json` 缺失/损坏自动重建；原子写（tmp+Replace）；PUT 后 volatile 整体替换 |
| 首次默认配置 | `ConfigStore.SeedDefaultVisible` | 仅新建骨架时，按首轮真实行填充默认 visible |
| 开机自启 | `AutoStartHelper.cs` | schtasks 计划任务 `HardwarePanel`（ONLOGON /RL HIGHEST /F） |
| 卸载 | `MainForm.cs Uninstall()` | 删自启任务 → 退出 →（可选）cmd 延迟自删程序目录 |

### Web 端（wwwroot/，全部纯 ES5，兼容旧 WebView）

| 页面 | 功能 |
|---|---|
| `/`（index.html） | 面板式信息屏：按硬件分组固定 4 面板（处理器/显卡/内存·主板/存储·系统，未知硬件并入末面板）；每面板 1 个大字指标+5 分钟曲线，其余数字行（≤6 统计块 / 7-12 紧凑单行 / >12 紧凑双列）；`CPU Core #N` 自动汇成线程条；阈值热区（≥阈值九成变琥珀、越线变红，阈值来自 cfg.alarms）；界面中文化；键缺失显示 `—`；空 visible 引导去设置；旧机 flex 回退 |
| `/photos.html` | 全屏交叉淡入轮播（间隔可配 ≥3s，60s 重取列表，首张随机，点击/滑动切图）；同时接收通知 |
| `/settings.html` | 宽版(1080):传感器按硬件分组卡片、**分组按面板排序**(处理器→显卡→内存·主板→存储·系统);中文名+原始名悬停、全选/全不选/还原;告警行编辑器(类型中文标签)+ webhook/token/轮播间隔;PUT 保存(失效键前后端共同丢弃) |
| `/lang.js` | 共享语言资源:CN2 中文名字典 / TYPE_CN 类型标签 / panelOf 名字正则回退,index 与 settings 共用一份 |
| `/notify.js` | 共享 toast 覆盖层：notify 8s 自消（含 source 角标）；alarm 红左边线闪烁 + 蜂鸣 3 声 + 点击关闭；整体 try/catch 不影响页面 |

### HTTP API

| 方法与路径 | 用途 |
|---|---|
| `GET /api/sensors` | 全量快照 JSON（有值行带 history 数组） |
| `GET /api/hwinfo` | 设备目录 `{panels, keys:{键:{device,hwType,panel}}}`;面板归属后端权威判定 |
| `GET /api/config` | 当前配置 JSON |
| `PUT /api/config` | 整份替换（clamp 后落盘、换内存引用、重置告警冷却），坏 JSON 400 |
| `POST /api/notify` | 入站通知 `{title,body,source?}`；配置 token 后需 `?token=`，不匹配 401 |
| `GET /api/images`、`/img/{name}` | 图片列表 / 图片本体（防目录穿越） |
| `GET /notify.js` | 共享脚本 |

## 4. 明确的非目标（不要往这些方向扩）

- 不做多用户/鉴权体系（家用局域网可信；`token` 仅轻校验入站通知）
- 不内建云推送服务（外部推送统一走 webhook：ntfy 填 topic 即得手机原生推送）
- 图片页不做缩略图/目录树/上传
- 不做安装器（zip 解压即用；卸载走托盘菜单）
- 告警仅支持 `>` 一种比较
- 不监听 config.json 外部改动（只认 PUT 换引用）

## 5. 已知限制与取舍

- 风扇/主板传感器取决于 SuperIO 支持（Dell OEM 板可能读不到，LHM GUI 有才有）
- 告警冷却按**配置下标**记录：重排/删除告警条目会使对应冷却失效（自实现引擎的已知取舍）
- 端口 8080 写死在 `MainForm.cs Port` 常量
- 旧安卓 WebView（无 CSS Grid）下信息页降级为 flex 堆叠可滚动，数据完整
- 卸载「是」路径为**永久删除**（含 img/ 图片与 config.json），无回收站

## 6. 验证与分发现状

- 全部验收项（API/页面/告警/token/轮播/设置/卸载/新旧安卓实机）已通过
- 分发包：`package/HardwarePanel-win-x64.zip`（2.3MB，需 .NET 8 Desktop Runtime）与 `package/HardwarePanel-win-x64-selfcontained.zip`（64.5MB，零依赖），内含安装说明
- 本机运行实例：`publish/`（页面改动即时生效，exe 改动需重启）
