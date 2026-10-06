// Mousely - Ultra-Performance Zero-Lag Liquid Remote Engine (VisionOS & iOS 15+ Edition)
(function() {
  'use strict';

  // ================= STATE =================
  let ws = null;
  let serverHost = window.location.host || '';
  let isConnected = false;
  let currentPage = 0;

  let isDraggingVolume = false;
  let isScrubbingWaveform = false;
  let currentVolume = 75;
  let isMuted = false;
  let isPlaying = false;
  let currentPosition = 97;
  let duration = 272;
  let lastPositionUpdate = performance.now();
  let volumeThrottleTimer = null;
  let volumeRafId = null;
  let isShuffle = false;
  let isRepeat = false;
  let isFavorite = false;
  let lastTrackKey = '';
  let discoveredServers = new Map();

  // Gesture State
  let touchStartX = 0;
  let touchStartY = 0;
  let touchDiffX = 0;
  let touchDiffY = 0;
  let isSwiping = false;

  // DOM Elements
  const appleToast = document.getElementById('appleToast');
  const toastIcon = document.getElementById('toastIcon');
  const toastText = document.getElementById('toastText');

  const statusDot = document.getElementById('statusDot');
  const statusDeviceName = document.getElementById('statusDeviceName');
  const navTabs = document.getElementById('navTabs');
  const carouselTrack = document.getElementById('carouselTrack');
  const carouselViewport = document.getElementById('carouselViewport');
  const carouselDots = document.getElementById('carouselDots');

  const albumArtImg = document.getElementById('albumArtImg');
  const trackTitle = document.getElementById('trackTitle');
  const trackArtist = document.getElementById('trackArtist');

  const waveformBar = document.getElementById('waveformBar');
  const waveformBarsBase = document.getElementById('waveformBarsBase');
  const waveformBarsActive = document.getElementById('waveformBarsActive');
  const waveformProgressClip = document.getElementById('waveformProgressClip');
  const waveformNeedle = document.getElementById('waveformNeedle');
  const timeElapsed = document.getElementById('timeElapsed');
  const timeTotal = document.getElementById('timeTotal');

  const playIcon = document.getElementById('playIcon');
  const pauseIcon = document.getElementById('pauseIcon');
  const btnShuffle = document.getElementById('btnShuffle');
  const btnRepeat = document.getElementById('btnRepeat');
  const btnHeart = document.getElementById('btnHeart');

  const volumeTrack = document.getElementById('volumeTrack');
  const volumeFill = document.getElementById('volumeFill');
  const volumeBadge = document.getElementById('volumeBadge');
  const volIcon = document.getElementById('volIcon');
  const muteIcon = document.getElementById('muteIcon');

  const desktopLaunchOverlay = document.getElementById('desktopLaunchOverlay');
  const desktopDevicesList = document.getElementById('desktopDevicesList');
  const manualIpInput = document.getElementById('manualIpInput');

  // ================= HAPTIC & TOAST NOTIFICATION =================
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
    if (!appleToast || !toastIcon || !toastText) return;
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

  // ================= 1. CAROUSEL & SWIPE NAVIGATION =================
  window.switchPage = function(pageIndex) {
    if (pageIndex < 0 || pageIndex > 2) return;
    currentPage = pageIndex;

    const offsetPercent = pageIndex * 33.333333;
    if (carouselTrack) {
      carouselTrack.style.transform = `translate3d(-${offsetPercent}%, 0, 0)`;
    }

    if (navTabs) {
      const tabs = navTabs.querySelectorAll('.nav-glass-tab');
      tabs.forEach((tab, idx) => {
        tab.classList.toggle('active', idx === pageIndex);
      });
    }

    if (carouselDots) {
      const dots = carouselDots.querySelectorAll('.carousel-dot');
      dots.forEach((dot, idx) => {
        dot.classList.toggle('active', idx === pageIndex);
      });
    }

    triggerHaptic('light');
  };

  function setupSwipeNavigation() {
    if (!carouselViewport) return;

    carouselViewport.addEventListener('touchstart', (e) => {
      if (e.touches.length >= 1) {
        touchStartX = e.touches[0].clientX;
        touchStartY = e.touches[0].clientY;
        touchDiffX = 0;
        touchDiffY = 0;
        isSwiping = true;
      }
    }, { passive: true });

    carouselViewport.addEventListener('touchmove', (e) => {
      if (!isSwiping || isDraggingVolume || isScrubbingWaveform) return;
      const currentX = e.touches[0].clientX;
      const currentY = e.touches[0].clientY;
      touchDiffX = currentX - touchStartX;
      touchDiffY = currentY - touchStartY;
    }, { passive: true });

    carouselViewport.addEventListener('touchend', () => {
      if (!isSwiping) return;
      isSwiping = false;

      const swipeThreshold = 45;
      if (Math.abs(touchDiffX) > swipeThreshold && Math.abs(touchDiffX) > Math.abs(touchDiffY) * 1.2) {
        if (touchDiffX < 0) {
          window.switchPage(Math.min(2, currentPage + 1));
        } else {
          window.switchPage(Math.max(0, currentPage - 1));
        }
      }
      touchDiffX = 0;
      touchDiffY = 0;
    }, { passive: true });
  }

  // ================= 2. DESKTOP SELECTOR OVERLAY =================
  window.showDesktopSelector = function() {
    triggerHaptic('light');
    if (desktopLaunchOverlay) {
      desktopLaunchOverlay.classList.remove('hidden');
      desktopLaunchOverlay.style.display = 'flex';
    }
    probeSubnet();
    renderDiscoveredServers();
  };

  window.hideDesktopSelector = function() {
    if (desktopLaunchOverlay) {
      desktopLaunchOverlay.classList.add('hidden');
      desktopLaunchOverlay.style.display = 'none';
    }
  };

  window.closeDesktopSelector = function(e) {
    if (e && e.target === desktopLaunchOverlay) {
      window.hideDesktopSelector();
    }
  };

  window.connectManual = function() {
    if (!manualIpInput) return;
    let ip = manualIpInput.value.trim();
    if (!ip) return;
    if (!ip.includes(':')) ip += ':58920';
    connectWebSocket(ip);
  };

  // ================= 3. MEDIA CONTROLS & TRANSPORT =================
  window.togglePlayPause = function() {
    isPlaying = !isPlaying;
    if (playIcon) playIcon.style.display = isPlaying ? 'none' : 'block';
    if (pauseIcon) pauseIcon.style.display = isPlaying ? 'block' : 'none';
    triggerHaptic('medium');
    sendCommand('play_pause');
  };

  window.skipPrev = function() {
    triggerHaptic('light');
    sendCommand('prev');
  };

  window.skipNext = function() {
    triggerHaptic('light');
    sendCommand('next');
  };

  window.toggleShuffle = function() {
    isShuffle = !isShuffle;
    if (btnShuffle) btnShuffle.classList.toggle('active-mode', isShuffle);
    showToast(isShuffle ? 'Shuffle On' : 'Shuffle Off', '🔀');
    triggerHaptic('light');
    sendCommand('shuffle');
  };

  window.toggleRepeat = function() {
    isRepeat = !isRepeat;
    if (btnRepeat) btnRepeat.classList.toggle('active-mode', isRepeat);
    showToast(isRepeat ? 'Repeat On' : 'Repeat Off', '🔁');
    triggerHaptic('light');
    sendCommand('repeat');
  };

  window.toggleFavorite = function() {
    isFavorite = !isFavorite;
    if (btnHeart) btnHeart.classList.toggle('active-red', isFavorite);
    showToast(isFavorite ? 'Added to Favorites' : 'Removed from Favorites', '❤️');
    triggerHaptic('light');
  };

  window.toggleMute = function() {
    isMuted = !isMuted;
    if (volIcon && muteIcon) {
      volIcon.style.display = isMuted ? 'none' : 'block';
      muteIcon.style.display = isMuted ? 'block' : 'none';
    }
    triggerHaptic('light');
    sendCommand('mute');
  };

  // ================= 4. APP LAUNCHER & QUICK ACTIONS =================
  window.launchApp = function(slot, name) {
    triggerHaptic('medium');
    showToast(`Launching ${name}...`, '🚀');
    sendCommand('launch_app', { slot: slot, name: name });
  };

  window.triggerPillAction = function(type) {
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
    triggerHaptic('medium');
    showToast(info[0], info[1]);
    sendCommand('clipboard_action', { type: type });
  };

  // ================= 5. NATIVE SWIFT BRIDGE CALLBACKS =================
  window.onServerDiscovered = function(server) {
    if (typeof server === 'string') {
      try { server = JSON.parse(server); } catch (e) {}
    }
    if (!server || !server.ip) return;

    const host = `${server.ip}:${server.port || 58920}`;
    const name = server.name || host.split(':')[0];

    discoveredServers.set(host, {
      host: host,
      name: name,
      online: true
    });

    renderDiscoveredServers();

    // Auto-connect if currently disconnected
    if (!isConnected) {
      connectWebSocket(host);
    }
  };

  window.onNativeVolumeChanged = function(percent) {
    const val = Math.max(0, Math.min(100, Math.round(percent)));
    updateVolumeUI(val, false);
    sendCommand('set_volume', { value: val });
  };

  // ================= 6. CSS CLIP WAVEFORM SCRUBBER =================
  function initWaveformBars() {
    if (!waveformBarsBase || !waveformBarsActive) return;
    const count = 56;
    let baseHtml = '';
    let activeHtml = '';

    for (let i = 0; i < count; i++) {
      const mid = Math.abs(i - count / 2) / (count / 2);
      const envelope = Math.max(0.18, 1 - mid * 0.55);
      const raw = Math.sin(i * 0.38) * 0.35 + Math.cos(i * 0.72) * 0.3 + 0.5;
      const heightPercent = Math.max(15, Math.min(95, Math.round(raw * envelope * 95)));

      baseHtml += `<div class="waveform-bar-element" style="height: ${heightPercent}%;"></div>`;
      activeHtml += `<div class="waveform-bar-element" style="height: ${heightPercent}%;"></div>`;
    }

    waveformBarsBase.innerHTML = baseHtml;
    waveformBarsActive.innerHTML = activeHtml;
  }

  function updateWaveformProgress(ratio) {
    ratio = Math.max(0, Math.min(1, ratio));
    const percent = (ratio * 100).toFixed(2);
    if (waveformProgressClip) waveformProgressClip.style.width = `${percent}%`;
    if (waveformNeedle) waveformNeedle.style.left = `${percent}%`;
    if (timeElapsed) timeElapsed.textContent = formatTime(currentPosition);
    if (timeTotal) timeTotal.textContent = formatTime(duration);
  }

  function handleWaveformScrub(clientX) {
    if (!waveformBar || duration <= 0) return;
    const rect = waveformBar.getBoundingClientRect();
    const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    currentPosition = Math.round(ratio * duration);
    updateWaveformProgress(ratio);
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
        if (duration > 0) {
          sendCommand('seek', { position: currentPosition });
          triggerHaptic('light');
        }
      }
    };

    waveformBar.addEventListener('pointerup', finishScrub);
    waveformBar.addEventListener('pointercancel', finishScrub);
  }

  // Smooth Scrubber Progress Ticker (low-power, pauses when page hidden)
  setInterval(() => {
    if (document.hidden) return;
    if (isPlaying && !isScrubbingWaveform && duration > 0) {
      const now = performance.now();
      const elapsedSec = (now - lastPositionUpdate) / 1000;
      if (currentPosition < duration) {
        currentPosition = Math.min(duration, currentPosition + elapsedSec);
        lastPositionUpdate = now;
        updateWaveformProgress(currentPosition / duration);
      }
    }
  }, 400);

  // ================= 7. VOLUME SLIDER ENGINE =================
  function updateVolumeUI(val, animate = true) {
    val = Math.max(0, Math.min(100, Math.round(val)));
    currentVolume = val;
    if (volumeBadge) volumeBadge.textContent = `${val}%`;
    if (volumeFill) {
      volumeFill.style.transition = animate ? 'width 0.1s ease-out' : 'none';
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

  function handleVolumeTouch(clientX) {
    if (!volumeTrack) return;
    const rect = volumeTrack.getBoundingClientRect();
    const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    sendVolumeUpdate(Math.round(ratio * 100));
  }

  function setupVolumeEvents() {
    if (!volumeTrack) return;

    volumeTrack.addEventListener('pointerdown', (e) => {
      isDraggingVolume = true;
      try { volumeTrack.setPointerCapture(e.pointerId); } catch (err) {}
      handleVolumeTouch(e.clientX);
    });

    volumeTrack.addEventListener('pointermove', (e) => {
      if (isDraggingVolume) handleVolumeTouch(e.clientX);
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
      try {
        if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.nativeApp) {
          window.webkit.messageHandlers.nativeApp.postMessage({ action: 'connectionState', state: 'connected' });
        }
      } catch (e) {}

      localStorage.setItem('mousely_last_host', host);
      if (statusDot) statusDot.classList.add('connected');
      if (statusDeviceName) statusDeviceName.textContent = host.split(':')[0];
      showToast('Connected to Windows Host', '🖥️');
      window.hideDesktopSelector();
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
      try {
        if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.nativeApp) {
          window.webkit.messageHandlers.nativeApp.postMessage({ action: 'connectionState', state: 'disconnected' });
        }
      } catch (e) {}

      if (statusDot) statusDot.classList.remove('connected');
      if (statusDeviceName) statusDeviceName.textContent = 'Disconnected';
      scheduleReconnect();
    };

    ws.onerror = function() {
      try { ws.close(); } catch (e) {}
    };
  }

  let reconnectTimer = null;
  function scheduleReconnect() {
    if (isConnected) return;
    clearTimeout(reconnectTimer);
    reconnectTimer = setTimeout(() => {
      if (isConnected) return;
      let saved = localStorage.getItem('mousely_last_host') || serverHost;
      if (saved) connectWebSocket(saved);
      else probeSubnet();
    }, 3000);
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

    if (duration > 0) {
      updateWaveformProgress(currentPosition / duration);
    }
  }

  // ================= 9. AUTO-DISCOVERY & SUBNET PROBING =================
  function fetchWithTimeout(url, timeoutMs = 1200) {
    const controller = new AbortController();
    const timeoutId = setTimeout(() => controller.abort(), timeoutMs);
    return fetch(url, { signal: controller.signal }).finally(() => clearTimeout(timeoutId));
  }

  function probeSubnet() {
    if (isConnected) return;
    let savedHost = localStorage.getItem('mousely_last_host');
    if (savedHost) {
      testAndAddServer(savedHost);
      if (!isConnected) connectWebSocket(savedHost);
      return;
    }

    if (window.location.hostname && window.location.hostname !== 'localhost' && window.location.hostname !== '127.0.0.1') {
      const parts = window.location.hostname.split('.');
      if (parts.length === 4) {
        const subnet = `${parts[0]}.${parts[1]}.${parts[2]}`;
        const currentIp = parseInt(parts[3], 10);
        for (let i = 1; i <= 254; i++) {
          if (Math.abs(i - currentIp) <= 8 || i === 1 || i === 2) {
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
        triggerHaptic('light');
        connectWebSocket(srv.host);
      };
      desktopDevicesList.appendChild(card);
    });
  }

  // ================= 10. INITIALIZATION =================
  window.addEventListener('DOMContentLoaded', () => {
    setupSwipeNavigation();
    initWaveformBars();
    setupWaveformEvents();
    setupVolumeEvents();
    updateWaveformProgress(currentPosition / duration);
    updateVolumeUI(currentVolume, false);

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
