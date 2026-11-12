# HardwarePanel 项目规则(给 AI 助手)

## 边界(必须遵守)

- **没有用户明确说「提交」,绝不执行 `git commit`。** 改动完成后停在工作区,报告改了什么,由用户决定提交时机与信息。
- 用户明确要求提交时,提交信息用中文、conventional 前缀(feat/fix/docs/chore)。

## AI 助手推送约定(面板通知)

- 实例自带推送端点:`POST http://127.0.0.1:8080/api/notify`,JSON 体 `{"title","body","source":"ai"}`;若设置页「通知 token」非空,须带 `?token=<token>`。命中后:所有已连接页面(index/photos/settings)弹 toast,并按设置转发 webhook(ntfy 可达手机)。
- **每轮回复完成后**推送一条「本轮完成 + 一句话摘要」;**需要用户人工介入前**(等待表单/授权/提交决定等)先推送提醒再等待。推送失败(实例未运行)静默跳过,不影响主任务。
- 示例:`curl -s -X POST http://127.0.0.1:8080/api/notify -H "Content-Type: application/json" -d '{"title":"AI 助手","body":"本轮已完成:xxx","source":"ai"}'`(中文 UTF-8,body 里的双引号需转义)。

## 项目速记

- 单进程 WinForms(.NET 8,requireAdministrator)+ LibreHardwareMonitorLib + `HttpListener http://+:8080`。
- **前端锁定 ES5**(安卓 5 旧 WebView,Chromium 37):无 `=>`、反引号、`let/const`、`??`、`?.`、`fetch`、`Map/Set`、`includes`、`closest`;CSS 无变量/`inset`/flex-`gap`,grid 要 flex 回退。详见 docs/ARCHITECTURE.md 决策 #2。
- `wwwroot/` 页面改动**无需重新发布**:运行中实例按请求即时读盘;响应已带 `Cache-Control: no-cache`。但若运行的是 `publish/` 里的托盘 exe,实际服务目录是 `publish/wwwroot`,改完须把文件同步 `cp` 过去才生效(exe 未锁,可随时拷)。
- 发布前先退托盘程序,否则 exe 被锁,`GenerateBundle` 报 IOException(决策 #9 有改名兜底方案)。
- 前端风格:EVA/NERV 皮肤(真黑底、红 #ff3030/#cc2020 警报与标识、绿 #50ff50 正常态、钢灰辅助字、全直角、六边形蜂巢签名、CRT 扫描线),分支 `feature/style-revamp`。
