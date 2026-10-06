// Mousely - Liquid Glass Master Deck (Zero-Lag Ultra Responsive Engine)
(function() {
  'use strict';

  // State
  let ws = null;
  let serverHost = window.location.host || '';
  let isConnected = false;
  let isDraggingVolume = false;
  let isScrubbingWaveform = false;
  let currentVolume = 75;
  let isMuted = false;
  let isPlaying = false;
  let currentPosition = 97; // 1:37 default preview
  let duration = 272; // 4:32 default preview
  let lastPositionUpdate = performance.now();
  let discoveredServers = new Map();
  let volumeThrottleTimer = null;
  let volumeRafId = null;
  let isShuffle = false;
  let isRepeat = false;
  let isFavorite = false;
  let lastTrackKey = '';

  // DOM Elements
  const fluidBgCanvas = document.getElementById('fluidBgCanvas');
  const appleToast = document.getElementById('appleToast');
  const toastIcon = document.getElementById('toastIcon');
  const toastText = document.getElementById('toastText');

  const albumArtImg = document.getElementById('albumArtImg');
  const trackTitle = document.getElementById('trackTitle');
  const trackArtist = document.getElementById('trackArtist');

  const waveformCanvas = document.getElementById('waveformCanvas');
  const waveformBar = document.getElementById('waveformBar');
  const waveformNeedle = document.getElementById('waveformNeedle');
  const timeElapsed = document.getElementById('timeElapsed');
  const timeTotal = document.getElementById('timeTotal');

  const btnPlayPause = document.getElementById('btnPlayPause');
  const playIcon = document.getElementById('playIcon');
  const pauseIcon = document.getElementById('pauseIcon');
  const btnPrev = document.getElementById('btnPrev');
  const btnNext = document.getElementById('btnNext');
  const btnShuffle = document.getElementById('btnShuffle');
  const btnRepeat = document.getElementById('btnRepeat');

  const volumeTrack = document.getElementById('volumeTrack');
  const volumeFill = document.getElementById('volumeFill');
  const volumeBadge = document.getElementById('volumeBadge');
  const btnMuteToggle = document.getElementById('btnMuteToggle');
  const volIcon = document.getElementById('volIcon');
  const muteIcon = document.getElementById('muteIcon');
  const btnHeart = document.getElementById('btnHeart');

  const missionControlModal = document.getElementById('missionControlModal');
  const moreActionsModal = document.getElementById('moreActionsModal');
  const desktopLaunchOverlay = document.getElementById('desktopLaunchOverlay');
  const desktopDevicesList = document.getElementById('desktopDevicesList');
  const manualIpInput = document.getElementById('manualIpInput');

  // ================= 1. DYNAMIC CONTINUOUS FLUID BACKGROUND =================
  function initFluidBackground() {
    if (!fluidBgCanvas) return;
    const ctx = fluidBgCanvas.getContext('2d');
    let width = 0, height = 0;

    function resize() {
      width = fluidBgCanvas.width = window.innerWidth;
      height = fluidBgCanvas.height = window.innerHeight;
    }
    window.addEventListener('resize', resize);
    resize();

    let t = 0;

    function renderLoop() {
      t += 0.009; // Continuous smooth evolution
      ctx.clearRect(0, 0, width, height);

      // Deep obsidian-crimson gradient base
      const bgGrad = ctx.createRadialGradient(
        width * 0.5, height * 0.45, 40,
        width * 0.5, height * 0.5, Math.max(width, height) * 0.75
      );
      bgGrad.addColorStop(0, 'rgba(46, 4, 11, 0.98)');
      bgGrad.addColorStop(0.45, 'rgba(22, 2, 6, 0.99)');
      bgGrad.addColorStop(1, '#070103');
      ctx.fillStyle = bgGrad;
      ctx.fillRect(0, 0, width, height);

      // Additive blending for luminous silky red ribbons
      ctx.save();
      ctx.globalCompositeOperation = 'screen';

      // 4 Fluid Molten Ribbons with continuous harmonic wave equations
      const ribbons = [
        { yBase: height * 0.35, amp1: 45, amp2: 25, freq1: 0.0022, freq2: 0.0045, speed: 1.0, color: 'rgba(255, 30, 60, 0.42)', width: 68 },
        { yBase: height * 0.55, amp1: 55, amp2: 35, freq1: 0.0018, freq2: 0.0038, speed: 0.8, color: 'rgba(220, 10, 45, 0.35)', width: 90 },
        { yBase: height * 0.72, amp1: 40, amp2: 28, freq1: 0.0028, freq2: 0.0050, speed: 1.2, color: 'rgba(255, 60, 90, 0.28)', width: 55 },
        { yBase: height * 0.20, amp1: 30, amp2: 20, freq1: 0.0025, freq2: 0.0042, speed: 0.6, color: 'rgba(180, 0, 30, 0.30)', width: 80 }
      ];

      for (let r = 0; r < ribbons.length; r++) {
        const cfg = ribbons[r];
        ctx.beginPath();
        const step = 16;
        for (let x = -20; x <= width + 20; x += step) {
          const y = cfg.yBase +
            Math.sin(x * cfg.freq1 + t * cfg.speed) * cfg.amp1 +
            Math.cos(x * cfg.freq2 - t * cfg.speed * 0.7) * cfg.amp2;
          if (x === -20) ctx.moveTo(x, y);
          else ctx.lineTo(x, y);
        }

        ctx.strokeStyle = cfg.color;
        ctx.lineWidth = cfg.width;
        ctx.lineCap = 'round';
        ctx.shadowColor = '#ff1a40';
        ctx.shadowBlur = 32;
        ctx.stroke();
      }

      // Soft ambient glowing ruby orbs floating through space
      const orbs = [
        { x: width * (0.2 + 0.15 * Math.sin(t * 0.5)), y: height * (0.3 + 0.1 * Math.cos(t * 0.4)), r: 90, alpha: 0.18 },
        { x: width * (0.8 + 0.12 * Math.cos(t * 0.6)), y: height * (0.7 + 0.12 * Math.sin(t * 0.5)), r: 120, alpha: 0.15 },
        { x: width * (0.5 + 0.1 * Math.sin(t * 0.8)), y: height * (0.85 + 0.08 * Math.cos(t * 0.7)), r: 100, alpha: 0.22 }
      ];

      for (let i = 0; i < orbs.length; i++) {
        const orb = orbs[i];
        const orbGrad = ctx.createRadialGradient(orb.x, orb.y, 10, orb.x, orb.y, orb.r);
        orbGrad.addColorStop(0, `rgba(255, 42, 85, ${orb.alpha})`);
        orbGrad.addColorStop(1, 'rgba(255, 42, 85, 0)');
        ctx.fillStyle = orbGrad;
        ctx.beginPath();
        ctx.arc(orb.x, orb.y, orb.r, 0, Math.PI * 2);
        ctx.fill();
      }

      ctx.restore();
      requestAnimationFrame(renderLoop);
    }

    renderLoop();
  }

  // ================= 2. DYNAMIC AUDIO WAVEFORM SCRUBBER =================
  let waveformBars = [];
  function generateWaveformBars() {
    waveformBars = [];
    const count = 56;
    // Generate organic soundwave profile resembling the reference UI
    for (let i = 0; i < count; i++) {
      const mid = Math.abs(i - count / 2) / (count / 2);
      const envelope = Math.max(0.15, 1 - mid * 0.6);
      const raw = Math.sin(i * 0.38) * 0.35 + Math.cos(i * 0.72) * 0.3 + 0.5;
      const heightFactor = Math.max(0.12, Math.min(1.0, raw * envelope));
      waveformBars.push(heightFactor);
    }
  }

  function renderWaveform() {
    if (!waveformCanvas) return;
    const ctx = waveformCanvas.getContext('2d');
    const dpr = window.devicePixelRatio || 1;
    const rect = waveformCanvas.getBoundingClientRect();

    if (waveformCanvas.width !== Math.round(rect.width * dpr) || waveformCanvas.height !== Math.round(rect.height * dpr)) {
      waveformCanvas.width = Math.round(rect.width * dpr);
      waveformCanvas.height = Math.round(rect.height * dpr);
    }

    const w = waveformCanvas.width;
    const h = waveformCanvas.height;
    ctx.clearRect(0, 0, w, h);

    if (waveformBars.length === 0) generateWaveformBars();

    const progressRatio = duration > 0 ? Math.max(0, Math.min(1, currentPosition / duration)) : 0.36;
    const barCount = waveformBars.length;
    const gap = 2.5 * dpr;
    const totalGap = gap * (barCount - 1);
    const barWidth = Math.max(2 * dpr, (w - totalGap) / barCount);
    const centerY = h / 2;

    for (let i = 0; i < barCount; i++) {
      const x = i * (barWidth + gap);
      const barRatio = i / (barCount - 1);
      const barH = waveformBars[i] * (h * 0.85);

      const isElapsed = barRatio <= progressRatio;

      ctx.save();
      if (isElapsed) {
        ctx.fillStyle = '#ff2a55';
        ctx.shadowColor = '#ff2a55';
        ctx.shadowBlur = 6 * dpr;
      } else {
        ctx.fillStyle = 'rgba(255, 255, 255, 0.38)';
        ctx.shadowBlur = 0;
      }

      ctx.beginPath();
      // Draw rounded vertical bar
      const radius = barWidth / 2;
      const topY = centerY - barH / 2;
      ctx.roundRect ? ctx.roundRect(x, topY, barWidth, barH, radius) : ctx.rect(x, topY, barWidth, barH);
      ctx.fill();
      ctx.restore();
    }

    // Position needle
    if (waveformNeedle) {
      waveformNeedle.style.left = `${progressRatio * 100}%`;
    }

    // Format timestamps
    if (timeElapsed) timeElapsed.textContent = formatTime(currentPosition);
    if (timeTotal) timeTotal.textContent = formatTime(duration);
  }

  function handleWaveformScrub(clientX) {
    if (!waveformBar || duration <= 0) return;
    const rect = waveformBar.getBoundingClientRect();
    const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    currentPosition = Math.round(ratio * duration);
    renderWaveform();
  }

  function commitWaveformSeek() {
    if (duration > 0) {
      sendCommand('seek', { position: currentPosition });
      triggerHaptic('light');
    }
  }

  function setupWaveformEvents() {
    if (!waveformBar) return;

    waveformBar.addEventListener('pointerdown', (e) => {
      isScrubbingWaveform = true;
      try { waveformBar.setPointerCapture(e.pointerId); } catch (err) {}
      handleWaveformScrub(e.clientX);
    });

    waveformBar.addEventListener('pointermove', (e) => {
      if (isScrubbingWaveform) handleWaveformScrub(e.clientX);
    });

    const finishScrub = (e) => {
      if (isScrubbingWaveform) {
        isScrubbingWaveform = false;
        try { waveformBar.releasePointerCapture(e.pointerId); } catch (err) {}
        commitWaveformSeek();
      }
    };

    waveformBar.addEventListener('pointerup', finishScrub);
    waveformBar.addEventListener('pointercancel', finishScrub);
  }

  // Live Scrubber Progress Ticker
  setInterval(() => {
    if (isPlaying && !isScrubbingWaveform && duration > 0) {
      const now = performance.now();
      const elapsedSec = (now - lastPositionUpdate) / 1000;
      if (currentPosition < duration) {
        currentPosition = Math.min(duration, currentPosition + elapsedSec);
        lastPositionUpdate = now;
        renderWaveform();
      }
    }
  }, 250);

  // ================= 3. HAPTICS & TOAST NOTIFICATION =================
  function triggerHaptic(style = 'light') {
    try {
      if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.nativeApp) {
        window.webkit.messageHandlers.nativeApp.postMessage({ action: 'haptic', style: style });
      } else if (navigator.vibrate) {
        navigator.vibrate(style === 'medium' ? 18 : 8);
      }
    } catch (e) {}
  }

  let toastTimer = null;
  function showToast(text, icon = '✨') {
    if (!appleToast) return;
    toastIcon.textContent = icon;
    toastText.textContent = text;
    appleToast.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
      appleToast.classList.remove('show');
    }, 1800);
  }

  function formatTime(seconds) {
    if (isNaN(seconds) || seconds < 0) return '0:00';
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${mins}:${secs < 10 ? '0' : ''}${secs}`;
  }

  // ================= 4. VOLUME SLIDER ENGINE =================
  function updateVolumeUI(val, animate = true) {
    val = Math.max(0, Math.min(100, Math.round(val)));
    currentVolume = val;
    if (volumeBadge) volumeBadge.textContent = `${val}%`;
    if (volumeFill) {
      volumeFill.style.transition = animate ? 'width 0.12s ease-out' : 'none';
      volumeFill.style.width = `${val}%`;
    }
    if (volIcon && muteIcon) {
      const isZero = val === 0 || isMuted;
      volIcon.style.display = isZero ? 'none' : 'block';
      muteIcon.style.display = isZero ? 'block' : 'none';
    }
  }

  function sendVolumeUpdate(val) {
    val = Math.max(0, Math.min(100, Math.round(val)));
    currentVolume = val;

    if (!volumeRafId) {
      volumeRafId = requestAnimationFrame(() => {
        if (volumeFill) volumeFill.style.width = `${currentVolume}%`;
        if (volumeBadge) volumeBadge.textContent = `${currentVolume}%`;
        volumeRafId = null;
      });
    }

    if (!volumeThrottleTimer) {
      volumeThrottleTimer = setTimeout(() => {
        sendCommand('set_volume', { value: currentVolume });
        volumeThrottleTimer = null;
      }, 30);
    }
  }

  function handleVolumeTouch(e) {
    if (!volumeTrack) return;
    const rect = volumeTrack.getBoundingClientRect();
    const clientX = e.touches && e.touches.length ? e.touches[0].clientX : e.clientX;
    const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    sendVolumeUpdate(Math.round(ratio * 100));
  }

  function setupVolumeEvents() {
    if (!volumeTrack) return;

    volumeTrack.addEventListener('pointerdown', (e) => {
      isDraggingVolume = true;
      try { volumeTrack.setPointerCapture(e.pointerId); } catch (err) {}
      handleVolumeTouch(e);
    });

    volumeTrack.addEventListener('pointermove', (e) => {
      if (isDraggingVolume) handleVolumeTouch(e);
    });

    const finishVolume = () => {
      if (isDraggingVolume) {
        isDraggingVolume = false;
        sendCommand('set_volume', { value: currentVolume });
        triggerHaptic('light');
      }
    };

    volumeTrack.addEventListener('pointerup', finishVolume);
    volumeTrack.addEventListener('pointercancel', finishVolume);
  }

  window.toggleMute = function() {
    triggerHaptic('medium');
    isMuted = !isMuted;
    sendCommand('mute');
  };

  // ================= 5. TRANSPORT CONTROLS =================
  const bindInstantTap = (el, callback) => {
    if (!el) return;
    let touchHandled = false;
    el.addEventListener('touchstart', (e) => {
      touchHandled = true;
      callback(e);
    }, { passive: true });
    el.addEventListener('click', (e) => {
      if (touchHandled) {
        touchHandled = false;
        return;
      }
      callback(e);
    });
  };

  bindInstantTap(btnPlayPause, () => {
    triggerHaptic('medium');
    isPlaying = !isPlaying;
    playIcon.style.display = isPlaying ? 'none' : 'block';
    pauseIcon.style.display = isPlaying ? 'block' : 'none';
    sendCommand('play_pause');
  });

  bindInstantTap(btnPrev, () => {
    triggerHaptic('light');
    sendCommand('prev');
  });

  bindInstantTap(btnNext, () => {
    triggerHaptic('light');
    sendCommand('next');
  });

  window.toggleShuffle = function() {
    triggerHaptic('light');
    isShuffle = !isShuffle;
    btnShuffle.classList.toggle('active-mode', isShuffle);
    showToast(isShuffle ? 'Shuffle On' : 'Shuffle Off', '🔀');
    sendCommand('shuffle');
  };

  window.toggleRepeat = function() {
    triggerHaptic('light');
    isRepeat = !isRepeat;
    btnRepeat.classList.toggle('active-mode', isRepeat);
    showToast(isRepeat ? 'Repeat On' : 'Repeat Off', '🔁');
    sendCommand('repeat');
  };

  window.toggleFavorite = function() {
    triggerHaptic('medium');
    isFavorite = !isFavorite;
    btnHeart.classList.toggle('active-red', isFavorite);
    showToast(isFavorite ? 'Added to Favorites' : 'Removed from Favorites', '❤️');
  };

  // ================= 6. SHORTCUT PILLS (1 TO 8) =================
  window.triggerPillAction = function(type) {
    triggerHaptic('medium');
    const toastMap = {
      cut: ['Cut (Ctrl + X)', '✂️'],
      copy: ['Copied (Ctrl + C)', '📋'],
      paste: ['Pasting (Ctrl + V)', '📥'],
      undo: ['Undo (Ctrl + Z)', '↩️'],
      delete: ['Deleted (Del)', '🗑️'],
      select_all: ['Selected All (Ctrl + A)', '⬚'],
      find: ['Find (Ctrl + F)', '🔍'],
      save: ['Saved (Ctrl + S)', '💾'],
      screenshot: ['Screenshot (Win + Shift + S)', '📸'],
      history: ['Clipboard History (Win + V)', '📜']
    };

    const info = toastMap[type] || ['Action Sent', '✨'];
    showToast(info[0], info[1]);
    sendCommand('clipboard_action', { type: type });
  };

  // ================= 7. MODALS & MISSION CONTROL =================
  window.toggleMissionControl = function() {
    triggerHaptic('light');
    missionControlModal.classList.toggle('open');
  };

  window.toggleMoreMenu = function() {
    triggerHaptic('light');
    moreActionsModal.classList.toggle('open');
  };

  window.showDesktopSelector = function() {
    triggerHaptic('light');
    desktopLaunchOverlay.classList.add('open');
    probeSubnet();
  };

  window.hideDesktopSelector = function() {
    desktopLaunchOverlay.classList.remove('open');
  };

  window.closeModalsOnBackdrop = function(e) {
    if (e.target.classList.contains('deck-modal-backdrop')) {
      e.target.classList.remove('open');
    }
  };

  window.launchApp = function(slot, name) {
    triggerHaptic('medium');
    missionControlModal.classList.remove('open');
    showToast(`Launching ${name}...`, '🚀');
    sendCommand('launch_app', { slot: slot, name: name });
  };

  // ================= 8. WEBSOCKET NETWORK CORE =================
  function sendCommand(action, params = {}) {
    if (!ws || ws.readyState !== WebSocket.OPEN) {
      if (serverHost) connectWebSocket(serverHost);
      return;
    }
    const payload = Object.assign({ action: action }, params);
    try {
      ws.send(JSON.stringify(payload));
    } catch (e) {
      console.error('[Mousely] Send failed:', e);
    }
  }

  function connectWebSocket(host) {
    if (!host) return;
    if (ws && (ws.readyState === WebSocket.OPEN || ws.readyState === WebSocket.CONNECTING)) {
      if (serverHost === host) return;
      try { ws.close(); } catch (e) {}
    }

    serverHost = host;
    const wsUrl = `ws://${host}/ws`;

    try {
      ws = new WebSocket(wsUrl);
    } catch (e) {
      scheduleReconnect();
      return;
    }

    ws.onopen = function() {
      isConnected = true;
      localStorage.setItem('mousely_last_host', host);
      showToast('Connected to Windows Host', '🖥️');
      hideDesktopSelector();
      sendCommand('get_state');
    };

    ws.onmessage = function(event) {
      try {
        const msg = JSON.parse(event.data);
        handleServerMessage(msg);
      } catch (e) {}
    };

    ws.onclose = function() {
      isConnected = false;
      scheduleReconnect();
    };

    ws.onerror = function() {
      try { ws.close(); } catch (e) {}
    };
  }

  let reconnectTimer = null;
  function scheduleReconnect() {
    clearTimeout(reconnectTimer);
    reconnectTimer = setTimeout(() => {
      let saved = localStorage.getItem('mousely_last_host') || serverHost;
      if (saved) connectWebSocket(saved);
      else probeSubnet();
    }, 2500);
  }

  function handleServerMessage(msg) {
    if (!msg) return;

    if (msg.type === 'initial_state' || msg.type === 'state_update') {
      if (msg.media) updateMediaUI(msg.media);
      if (msg.volume !== undefined) updateVolumeUI(msg.volume, false);
      if (msg.isMuted !== undefined) {
        isMuted = !!msg.isMuted;
        updateVolumeUI(currentVolume, false);
      }
    } else if (msg.type === 'media_update' && msg.media) {
      updateMediaUI(msg.media);
    } else if (msg.type === 'volume_update') {
      if (msg.volume !== undefined && !isDraggingVolume) {
        updateVolumeUI(msg.volume, true);
      }
      if (msg.isMuted !== undefined) {
        isMuted = !!msg.isMuted;
        updateVolumeUI(currentVolume, false);
      }
    }
  }

  function updateMediaUI(media) {
    if (!media) return;

    const title = media.title || media.Title || 'No Media Playing';
    const artist = media.artist || media.Artist || '';
    const album = media.album || media.Album || '';
    const mediaPlaying = media.isPlaying !== undefined ? !!media.isPlaying : (media.IsPlaying !== undefined ? !!media.IsPlaying : false);
    const mediaDuration = media.duration !== undefined ? media.duration : (media.Duration !== undefined ? media.Duration : 0);
    const mediaPosition = media.position !== undefined ? media.position : (media.Position !== undefined ? media.Position : 0);
    const artwork = media.artwork || media.Artwork || null;
    const hasArtwork = media.hasArtwork !== undefined ? !!media.hasArtwork : (media.HasArtwork !== undefined ? !!media.HasArtwork : false);

    if (trackTitle) trackTitle.textContent = title;
    if (trackArtist) trackArtist.textContent = artist ? (artist + (album ? ` • ${album}` : '')) : 'Windows Media Session';

    isPlaying = mediaPlaying;
    if (playIcon) playIcon.style.display = isPlaying ? 'none' : 'block';
    if (pauseIcon) pauseIcon.style.display = isPlaying ? 'block' : 'none';

    const currentKey = `${title}|${artist}|${hasArtwork}|${artwork || ''}`;
    if (currentKey !== lastTrackKey) {
      lastTrackKey = currentKey;
      if (artwork) {
        albumArtImg.src = artwork;
      } else if (hasArtwork && serverHost) {
        albumArtImg.src = `http://${serverHost}/api/artwork?t=${Date.now()}`;
      } else {
        albumArtImg.src = 'assets/default-art.svg';
      }
    }

    if (mediaDuration > 0) duration = mediaDuration;
    if (mediaPosition >= 0) {
      currentPosition = mediaPosition;
      lastPositionUpdate = performance.now();
    }

    renderWaveform();
  }

  // ================= 9. AUTO-DISCOVERY & SUBNET PROBING =================
  function fetchWithTimeout(url, timeoutMs = 1200) {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
    return fetch(url, { signal: controller.signal }).finally(() => clearTimeout(timeoutId));
  }

  function probeSubnet() {
    let savedHost = localStorage.getItem('mousely_last_host');
    if (savedHost) {
      testAndAddServer(savedHost);
      connectWebSocket(savedHost);
    }

    if (window.location.hostname && window.location.hostname !== 'localhost' && window.location.hostname !== '127.0.0.1') {
      const parts = window.location.hostname.split('.');
      if (parts.length === 4) {
        const subnet = `${parts[0]}.${parts[1]}.${parts[2]}`;
        const currentIp = parseInt(parts[3], 10);
        for (let i = 1; i <= 254; i++) {
          if (Math.abs(i - currentIp) <= 20 || i === 1 || i === 2) {
            testAndAddServer(`${subnet}.${i}:58920`);
          }
        }
      }
    }

    ['localhost:58920', '127.0.0.1:58920'].forEach(testAndAddServer);
  }

  function testAndAddServer(host) {
    if (discoveredServers.has(host)) return;
    fetchWithTimeout(`http://${host}/api/ping`, 1200)
      .then(res => res.json())
      .then(data => {
        discoveredServers.set(host, {
          host: host,
          name: data.pcName || data.name || host.split(':')[0],
          online: true
        });
        renderDiscoveredServers();
        if (!isConnected) connectWebSocket(host);
      })
      .catch(() => {});
  }

  function renderDiscoveredServers() {
    if (!desktopDevicesList) return;
    if (discoveredServers.size === 0) {
      desktopDevicesList.innerHTML = `
        <div class="desktop-item-card" style="justify-content: center; color: rgba(255,255,255,0.6); cursor: default;">
          <span>Searching local network...</span>
        </div>`;
      return;
    }

    desktopDevicesList.innerHTML = '';
    discoveredServers.forEach(srv => {
      const card = document.createElement('div');
      card.className = 'desktop-item-card';
      card.innerHTML = `
        <div style="display: flex; align-items: center; gap: 10px;">
          <span style="font-size: 18px;">🖥️</span>
          <div>
            <div style="font-weight: 600; font-size: 14px; color: #fff;">${srv.name}</div>
            <div style="font-size: 11px; color: rgba(255,255,255,0.6);">${srv.host}</div>
          </div>
        </div>
        <span style="font-size: 11px; color: #ff2a55; font-weight: 600;">Connect →</span>
      `;
      card.onclick = () => {
        connectWebSocket(srv.host);
      };
      desktopDevicesList.appendChild(card);
    });
  }

  window.connectManual = function() {
    if (!manualIpInput) return;
    let ip = manualIpInput.value.trim();
    if (!ip) return;
    if (!ip.includes(':')) ip += ':58920';
    connectWebSocket(ip);
  };

  // ================= 10. INITIALIZATION =================
  window.addEventListener('DOMContentLoaded', () => {
    initFluidBackground();
    generateWaveformBars();
    setupWaveformEvents();
    setupVolumeEvents();
    renderWaveform();
    updateVolumeUI(currentVolume, false);

    // Initial connection attempt
    const lastHost = localStorage.getItem('mousely_last_host');
    if (lastHost) {
      connectWebSocket(lastHost);
    } else if (serverHost) {
      connectWebSocket(serverHost);
    } else {
      probeSubnet();
    }
  });

})();
