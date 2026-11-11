// 共享语言资源:index.html / settings.html 共用一份,避免两份字典漂移。
// 全 ES5:兼容安卓 5 旧 WebView(纯 var/function)。
var TYPE_CN = {
  Temperature: '温度', Load: '负载', Power: '功耗', Data: '数据',
  Fan: '风扇', Clock: '频率', Voltage: '电压', Throughput: '速度', Level: '寿命'
};

// 中文名(键: 原始名|类型;查不到显示原始名)
var CN2 = {
  // 处理器(AMD / Intel)
  'Core (Tctl/Tdie)|Temperature': '封装温度', 'CPU Package|Temperature': '封装温度',
  'CCD1 (Tdie)|Temperature': 'CCD 温度', 'Core Average|Temperature': '平均核温',
  'CPU Total|Load': '总负载', 'CPU Core Max|Load': '最大核负载',
  'CPU Package|Power': '封装功耗', 'Package|Power': '封装功耗', 'Package Power|Power': '封装功耗',
  'Cores (Average)|Clock': '平均频率', 'Bus Speed|Clock': '总线频率',
  'Core (SVI2 TFN)|Voltage': '核心电压', 'SoC (SVI2 TFN)|Voltage': 'SoC 电压',
  // 主板(SuperIO)
  'CPU Fan|Fan': 'CPU 风扇', 'System Fan #1|Fan': '系统风扇 1', 'System Fan #2|Fan': '系统风扇 2',
  'System Fan #3|Fan': '系统风扇 3', 'Pump Fan|Fan': '水泵风扇',
  'CPU|Temperature': 'CPU 温度', 'System|Temperature': '系统温度', 'Motherboard|Temperature': '主板温度',
  'VRM MOS|Temperature': '供电 VRM', 'MOS|Temperature': '供电 MOS', 'PCH|Temperature': '芯片组',
  'Chipset|Temperature': '芯片组', 'CPU Socket|Temperature': 'CPU 插座', 'M2 #1|Temperature': 'M.2 插槽',
  'Vcore|Voltage': 'Vcore', '+12V|Voltage': '+12V', '+5V|Voltage': '+5V', '+3.3V|Voltage': '+3.3V',
  'DIMM|Voltage': '内存电压', 'CMOS Battery|Voltage': '主板电池', 'VBAT|Voltage': '主板电池',
  // 显卡
  'GPU Core|Temperature': '核心温度', 'GPU Hot Spot|Temperature': '热点',
  'GPU Memory Junction|Temperature': '显存温度', 'GPU Memory|Temperature': '显存温度',
  'GPU Core|Load': '使用率', 'GPU Memory|Load': '显存占用率',
  'GPU Memory Controller|Load': '显存控制器', 'GPU Video Engine|Load': '视频引擎',
  'D3D 3D|Load': '3D 引擎', 'GPU Core|Clock': '核心频率', 'GPU Memory|Clock': '显存频率',
  'GPU Fan 1|Fan': '风扇', 'GPU Fan 2|Fan': '风扇 2',
  'GPU Package|Power': '功耗', 'GPU Power|Power': '功耗', 'GPU Board Power|Power': '整板功耗',
  'GPU Memory Used|Data': '显存占用', 'GPU Memory Free|Data': '显存空闲', 'GPU Memory Total|Data': '显存总量',
  // 内存
  'Memory|Load': '内存使用率', 'Memory Used|Data': '已用内存', 'Memory Available|Data': '可用内存',
  // 存储
  'Composite Temperature|Temperature': '温度', 'Temperature|Temperature': '温度',
  'Temperature #1|Temperature': '温度 1', 'Temperature #2|Temperature': '温度 2',
  'Read Rate|Throughput': '读取', 'Write Rate|Throughput': '写入',
  'Used Space|Load': '已用空间', 'Free Space|Data': '剩余空间', 'Total Space|Data': '总空间',
  'Life|Level': '寿命', 'Data Read|Data': '累计读取', 'Data Written|Data': '累计写入',
  // 网卡
  'Download Speed|Throughput': '下行', 'Upload Speed|Throughput': '上行',
  'Network Utilization|Load': '网络使用率',
  // 系统(伪行)
  'Uptime|Data': '运行时间', 'Volume|Load': '主音量',
  // 核显伪行(后端已是中文)
  '总使用率|Load': '核显使用率', '专用显存|Data': '核显专用显存', '共享内存|Data': '核显共享内存'
};

function cnOf(raw, type) { return CN2[raw + '|' + type] || raw; }

// 面板归属回退(旧 exe 无 /api/hwinfo 时按硬件名猜);
// 新版以后端目录(HardwareType 判定)为准,这里只是兜底。
var PANELS_FALLBACK = ['处理器', '显卡', '内存 · 主板', '存储 · 系统'];
function panelOf(hw) {
  var s = hw.toLowerCase();
  if (/ryzen|intel core|xeon|athlon|epyc|core i[3579]|\bcpu\b/.test(s)) return 0;
  if (/nvidia|radeon|\barc\b|gpu|显卡/.test(s)) return 1;
  if (/memory|内存/.test(s)) return 2;
  if (/motherboard|msi|asus|gigabyte|asrock|biostar|dell|lenovo|\bhp\b|supermicro|nuvoton|\bite\b|fintek|nct\d|主板/.test(s)) return 2;
  return 3; // 磁盘 / 网卡 / 系统 / 未知硬件
}
