// 共享通知覆盖层:initNotifications(es) 注册 SSE notify/alarm 事件,
// 弹顶部居中 toast;alarm 卡红色左边线闪烁 + 蜂鸣,点击关闭不自动消失。
// 全 ES5:兼容安卓 5 时代的旧 WebView(Chromium 37+)。
(function () {
  'use strict';

  function esc(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  var ICON_BELL = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M6 8a6 6 0 0 1 12 0c0 7 3 9 3 9H3s3-2 3-9"/><path d="M10.3 21a1.94 1.94 0 0 0 3.4 0"/></svg>';
  var ICON_WARN = '<svg viewBox="0 0 24 24" fill="none" stroke="#e5484d" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3"/><path d="M12 9v4"/><path d="M12 17h.01"/></svg>';

  var css =
    '#toasts { position: fixed; top: 44px; left: 50%; -webkit-transform: translateX(-50%);' +
    '  transform: translateX(-50%); z-index: 9999; display: flex; flex-direction: column;' +
    '  width: auto; max-width: 92vw; padding: 10px; box-sizing: border-box; }\n' +
    '#toasts .toast { background: #17191c; color: #e8eaed; border: 1px solid rgba(255,255,255,.09);' +
    '  border-left: 3px solid #4a5058; border-radius: 10px; padding: 10px 14px;' +
    '  font: 13px/1.5 -apple-system, "Segoe UI", system-ui, sans-serif;' +
    '  box-shadow: 0 4px 18px rgba(0,0,0,.5); word-break: break-word; margin-bottom: 8px;' +
    '  min-width: 220px; }\n' +
    '#toasts .toast .th { display: flex; align-items: center; font-weight: 600; }\n' +
    '#toasts .toast .th svg { flex: none; width: 14px; height: 14px; margin-right: 8px; }\n' +
    '#toasts .toast .b { color: #9aa0a6; margin-top: 2px; }\n' +
    '#toasts .toast .src { margin-left: auto; padding-left: 12px; font-size: 11px; color: #62666c;' +
    '  font-weight: 400; font-family: "IBM Plex Mono", Menlo, monospace; }\n' +
    '#toasts .alarm { border-left-color: #e5484d; background: #1c1214; cursor: pointer;' +
    '  -webkit-animation: toast-blink 1s infinite; animation: toast-blink 1s infinite; }\n' +
    '#toasts .alarm .th { color: #f2b8ba; }\n' +
    '@-webkit-keyframes toast-blink { 50% { border-color: rgba(229,72,77,.25); } }\n' +
    '@keyframes toast-blink { 50% { border-color: rgba(229,72,77,.25); } }\n';
  var style = document.createElement('style');
  style.type = 'text/css';
  if (style.styleSheet) style.styleSheet.cssText = css; // 旧 IE 路径,顺手兼容
  else style.appendChild(document.createTextNode(css));
  document.getElementsByTagName('head')[0].appendChild(style);

  var audioCtx = null;
  function beep() { // 880Hz × 150ms × 3,gain 0.1;首次调用时创建 AudioContext
    try {
      var AC = window.AudioContext || window.webkitAudioContext;
      if (!AC) return;
      if (!audioCtx) audioCtx = new AC();
      if (audioCtx.state === 'suspended' && audioCtx.resume) audioCtx.resume();
      for (var i = 0; i < 3; i++) {
        var t = audioCtx.currentTime + i * 0.25;
        var osc = audioCtx.createOscillator();
        var gain = audioCtx.createGain();
        osc.type = 'sine';
        osc.frequency.value = 880;
        gain.gain.setValueAtTime(0.1, t);
        gain.gain.setValueAtTime(0, t + 0.15);
        osc.connect(gain);
        gain.connect(audioCtx.destination);
        osc.start(t);
        osc.stop(t + 0.15);
      }
    } catch (e) { /* 无音频设备/自动播放策略:忽略,视觉告警仍在 */ }
  }

  function show(title, body, source, isAlarm) {
    try {
      var host = document.getElementById('toasts');
      if (!host) {
        host = document.createElement('div');
        host.id = 'toasts';
        document.body.appendChild(host);
      }
      var card = document.createElement('div');
      card.className = 'toast' + (isAlarm ? ' alarm' : '');
      card.innerHTML = '<div class="th">' + (isAlarm ? ICON_WARN : ICON_BELL) + '<span>' + esc(title) + '</span>' +
        (source ? '<span class="src">' + esc(source) + '</span>' : '') + '</div>' +
        (body ? '<div class="b">' + esc(body) + '</div>' : '');
      if (isAlarm) {
        card.title = '点击关闭';
        card.onclick = function () {
          if (card.parentNode) card.parentNode.removeChild(card);
        };
        beep();
      } else {
        setTimeout(function () {
          if (card.parentNode) card.parentNode.removeChild(card);
        }, 8000);
      }
      host.appendChild(card);
    } catch (e) { /* 覆盖层异常不影响页面 */ }
  }

  function onEvent(isAlarm) {
    return function (e) {
      try {
        var d = JSON.parse(e.data);
        show(d.title, d.body, d.source, isAlarm);
      } catch (err) { /* 忽略坏帧 */ }
    };
  }

  window.initNotifications = function (es) {
    es.addEventListener('notify', onEvent(false));
    es.addEventListener('alarm', onEvent(true));
  };
})();
