// 共享通知覆盖层：initNotifications(es) 注册 SSE notify/alarm 事件，
// 弹顶部居中 toast；alarm 卡红色闪烁 + 蜂鸣，点击关闭不自动消失。
// 全 ES5：兼容安卓 5 时代的旧 WebView（Chromium 37+）。
(function () {
  'use strict';

  function esc(s) {
    return String(s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  var css =
    '#toasts { position: fixed; top: 0; left: 50%; transform: translateX(-50%);' +
    '  z-index: 9999; display: flex; flex-direction: column; width: auto; max-width: 92vw;' +
    '  padding: 10px; box-sizing: border-box; }\n' +
    '#toasts .toast { background: #1e1e1e; color: #eee; border: 1px solid #333;' +
    '  border-radius: 10px; padding: 10px 14px; font: 14px/1.45 system-ui, sans-serif;' +
    '  box-shadow: 0 4px 18px rgba(0,0,0,.5); word-break: break-word; margin-bottom: 8px; }\n' +
    '#toasts .t { font-weight: 600; }\n' +
    '#toasts .b { color: #bbb; margin-top: 2px; }\n' +
    '#toasts .alarm { border: 2px solid #e53935; -webkit-animation: toast-blink 1s infinite; animation: toast-blink 1s infinite; cursor: pointer; }\n' +
    '@-webkit-keyframes toast-blink { 50% { border-color: #5c1210; } }\n' +
    '@keyframes toast-blink { 50% { border-color: #5c1210; } }\n';
  var style = document.createElement('style');
  style.type = 'text/css';
  if (style.styleSheet) style.styleSheet.cssText = css; // 旧 IE 路径，顺手兼容
  else style.appendChild(document.createTextNode(css));
  document.getElementsByTagName('head')[0].appendChild(style);

  var audioCtx = null;
  function beep() { // 880Hz × 150ms × 3，gain 0.1；首次调用时创建 AudioContext
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
    } catch (e) { /* 无音频设备/自动播放策略：忽略，视觉告警仍在 */ }
  }

  function show(title, body, isAlarm) {
    var host = document.getElementById('toasts');
    if (!host) {
      host = document.createElement('div');
      host.id = 'toasts';
      document.body.appendChild(host);
    }
    var card = document.createElement('div');
    card.className = 'toast' + (isAlarm ? ' alarm' : '');
    card.innerHTML = '<div class="t">' + esc(title) + '</div>' +
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
  }

  function onEvent(isAlarm) {
    return function (e) {
      try {
        var d = JSON.parse(e.data);
        show(d.title, d.body, isAlarm);
      } catch (err) { /* 忽略坏帧 */ }
    };
  }

  window.initNotifications = function (es) {
    es.addEventListener('notify', onEvent(false));
    es.addEventListener('alarm', onEvent(true));
  };
})();
