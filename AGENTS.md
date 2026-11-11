# HardwarePanel 项目规则(给 AI 助手)

## 边界(必须遵守)

- **没有用户明确说「提交」,绝不执行 `git commit`。** 改动完成后停在工作区,报告改了什么,由用户决定提交时机与信息。
- 用户明确要求提交时,提交信息用中文、conventional 前缀(feat/fix/docs/chore)。

## 项目速记

- 单进程 WinForms(.NET 8,requireAdministrator)+ LibreHardwareMonitorLib + `HttpListener http://+:8080`。
- **前端锁定 ES5**(安卓 5 旧 WebView,Chromium 37):无 `=>`、反引号、`let/const`、`??`、`?.`、`fetch`、`Map/Set`、`includes`、`closest`;CSS 无变量/`inset`/flex-`gap`,grid 要 flex 回退。详见 docs/ARCHITECTURE.md 决策 #2。
- `wwwroot/` 页面改动**无需重新发布**:运行中实例按请求即时读盘;响应已带 `Cache-Control: no-cache`。
- 发布前先退托盘程序,否则 exe 被锁,`GenerateBundle` 报 IOException(决策 #9 有改名兜底方案)。
- 前端风格:EVA/NERV 皮肤(真黑底、红 #ff3030/#cc2020 警报与标识、绿 #50ff50 正常态、钢灰辅助字、全直角、六边形蜂巢签名、CRT 扫描线),分支 `feature/style-revamp`。
