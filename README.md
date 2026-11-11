# HardwarePanel

C# 单进程：LibreHardwareMonitorLib 内嵌读传感器 + HTTP 服务（端口 8080）。
窗口里是本机 ListView，手机/浏览器访问 `http://<PC IP>:8080` 看仪表盘。

## 开发文档（改动前先读）

- `docs/REQUIREMENTS.md` — 需求列表（R1–R11）、已实现功能矩阵、非目标与已知取舍
- `docs/ARCHITECTURE.md` — 代码架构、线程模型、数据结构（SensorRow/AppConfig/SSE 帧）、内嵌 mermaid 时序图、关键设计决策（含"前端锁定 ES5"等红线）
- `docs/diagrams/architecture.drawio` — 组件与部署总览（drawio 可编辑）
- `docs/diagrams/dataflow.drawio` — 采集/配置/告警/通知四条数据流（drawio 可编辑）

## 数据来源

| 数据 | 来源 |
|---|---|
| CPU/主板温度、风扇、电压、CPU 封装功耗、内存 | LHM（MSR / SuperIO，需管理员） |
| 独立显卡（NVIDIA/AMD 温度/负载/风扇/显存/功耗）、磁盘（温度/读写速率/寿命）、网卡（上下行） | LHM（需管理员） |
| GPU 使用率 / 显存（Intel 核显） | Windows 性能计数器 GPU Engine / GPU Adapter Memory |
| 运行时间、主音量 | 伪行（TickCount64 / CoreAudio），读取失败显示 `—` |

## 运行

需要 .NET 8 SDK。程序以管理员运行（读 MSR 的 WinRing0 驱动需要）：

```
# 管理员终端里
dotnet run -c Release
```

发布单文件：`dotnet publish -c Release -r win-x64 --self-contained false`

关闭窗口后程序驻留系统托盘继续采集；双击托盘图标恢复窗口，右键托盘图标 → 退出 才会真正结束。
- 开机自启：托盘右键菜单勾选“开机自启”——内部创建“登录时、最高权限”计划任务（免 UAC 静默启动，带 `--tray` 直接进托盘）。取消勾选即删除任务。

卸载：托盘右键 → 「卸载…」——删除开机自启计划任务并退出；确认框选「是」则连同程序文件夹（含 config.json 与 img/ 图片）一并自删（约需 5 秒），选「否」仅清自启、保留文件。

## 手机访问

- USB（不占 WiFi，同 RemotePanel 的思路）：手机开 USB 调试后执行

  ```
  adb reverse tcp:8080 tcp:8080
  ```

  手机浏览器打开 `http://127.0.0.1:8080`
- WiFi：手机浏览器打开 `http://<PC 局域网 IP>:8080`（首次需放行 Windows 防火墙）

## 页面 / 接口 / 配置

- `/` 信息屏：按 `config.json` 的 `visible` 列表生成**面板式仪表盘**——按硬件分组固定 4 面板（处理器/显卡/内存·主板/存储·系统），每面板 1 个大字指标+5 分钟曲线，其余数字行（条目多自动变紧凑），`CPU Core #N` 汇成线程负载条；接近告警阈值数值变琥珀、越线变红并弹告警卡；界面中文；右上 ⚙ 进设置页；
  `/photos.html` 图片轮播页（点击/滑动切图，间隔可配）；`/settings.html` 配置页（宽版分组卡片勾选传感器、全选/全不选/还原、编辑告警规则、webhook、轮播间隔）。
- `GET/PUT /api/config` 读写整份配置（PUT 成功返回 `{"ok":true}` 并重置告警冷却）；`POST /api/notify` 推送外部通知到屏幕（若配置了 token 则需 `?token=`），示例：

  ```
  curl -X POST http://<PC IP>:8080/api/notify -H "Content-Type: application/json" \
       -d '{"title":"Agent 完成","body":"任务已结束","source":"claude"}'
  ```

- 配置文件在程序目录 `config.json`（首次启动自动生成，`visible` 键格式 `硬件|传感器|类型`）。告警阈值触发（仅支持 `>`）会在信息屏/轮播页弹红色闪烁卡片 + 蜂鸣，并转发 webhook；
  手机息屏也能收到推送的路径：webhookUrl 填 `https://ntfy.sh/<topic>`（格式 ntfy），手机装 ntfy App 订阅同名 topic 即可。

## 投图片

把 png/jpg/gif/webp 放进程序运行目录的 `img/` 文件夹，页面底部自动显示（中文文件名可用）。

## 已知限制

- 风扇/主板传感器能否读出取决于 Dell OEM 板的 SuperIO 支持——先用 LHM GUI 验证，GUI 里没有的，本程序也不会有。
- 端口 8080 写在 `MainForm.cs` 的 `Port` 常量里。
