// Mousely - High Performance Landscape Remote (Zero-Lag Optimized)
(function() {
  'use strict';

  // State
  let ws = null;
  let serverHost = window.location.host || '';
  let isConnected = false;
  let isDraggingVolume = false;
  let isScrubbingTimeline = false;
  let currentVolume = 50;
  let isMuted = false;
  let isPlaying = false;
  let currentPosition = 0;
  let duration = 0;
  let lastPositionUpdate = 0;
  let timelineTimer = null;
  let pingStartTime = 0;
  let latencyMs = 0;
  let reconnectAttempts = 0;
  let discoveredServers = new Map();
  let volumeThrottleTimer = null;
  let rafId = null;

  // Carousel & 2-Finger Swipe State
  let currentPage = 0;
  let isTwoFingerGesture = false;
  let twoFingerStartX = 0;
  let twoFingerLastX = 0;

  // DOM Elements
  const carouselTrack = document.getElementById('carouselTrack');
  const navTabs = document.getElementById('navTabs');
  const carouselDots = document.getElementById('carouselDots');
  
  const statusBadge = document.getElementById('statusBadge');
  const statusDot = document.getElementById('statusDot');
  const statusDeviceName = document.getElementById('statusDeviceName');
  const latencyTag = document.getElementById('latencyTag');
  
  const albumArtImg = document.getElementById('albumArtImg');
  const sourceAppPill = document.getElementById('sourceAppPill');
  const trackTitle = document.getElementById('trackTitle');
  const trackArtist = document.getElementById('trackArtist');
  
  const timelineBar = document.getElementById('timelineBar');
  const timelineProgress = document.getElementById('timelineProgress');
  const timeElapsed = document.getElementById('timeElapsed');
  const timeTotal = document.getElementById('timeTotal');
  
  const btnPrev = document.getElementById('btnPrev');
  const btnPlayPause = document.getElementById('btnPlayPause');
  const playIcon = document.getElementById('playIcon');
  const pauseIcon = document.getElementById('pauseIcon');
  const btnNext = document.getElementById('btnNext');
  
  const volumeBadge = document.getElementById('volumeBadge');
  const volumeTrack = document.getElementById('volumeTrack');
  const volumeFill = document.getElementById('volumeFill');
  const btnMuteToggle = document.getElementById('btnMuteToggle');
  const volIcon = document.getElementById('volIcon');
  const muteIcon = document.getElementById('muteIcon');
  
  const desktopLaunchOverlay = document.getElementById('desktopLaunchOverlay');
  const desktopDevicesList = document.getElementById('desktopDevicesList');
  const discoverySubtitle = document.getElementById('discoverySubtitle');
  const manualIpInput = document.getElementById('manualIpInput');

  // Instant Haptic Trigger
  function triggerHaptic(style = 'light') {
    try {
      if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.nativeApp) {
        window.webkit.messageHandlers.nativeApp.postMessage({ action: 'haptic', style: style });
      } else if (navigator.vibrate) {
        navigator.vibrate(style === 'medium' ? 18 : 8);
      }
    } catch (e) {}
  }

  // Format mm:ss
  function formatTime(seconds) {
    if (isNaN(seconds) || seconds < 0) return '0:00';
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${mins}:${secs < 10 ? '0' : ''}${secs}`;
  }

  // --- Carousel & Navigation ---
  window.switchPage = function(pageIndex) {
    if (pageIndex < 0 || pageIndex > 2) return;
    currentPage = pageIndex;

    const offsetPercent = pageIndex * 33.333333;
    carouselTrack.style.transform = `translate3d(-${offsetPercent}%, 0, 0)`;

    // Update Segmented Tabs
    const tabs = navTabs.querySelectorAll('.nav-tab');
    tabs.forEach((tab, idx) => {
      tab.classList.toggle('active', idx === pageIndex);
    });

    // Update Dots
    const dots = carouselDots.querySelectorAll('.dot');
    dots.forEach((dot, idx) => {
      dot.classList.toggle('active', idx === pageIndex);
    });

    triggerHaptic('light');
  };

  // --- Two-Finger Swipe Gesture ---
  document.addEventListener('touchstart', (e) => {
    if (e.touches.length === 2) {
      isTwoFingerGesture = true;
      twoFingerStartX = (e.touches[0].clientX + e.touches[1].clientX) / 2;
      twoFingerLastX = twoFingerStartX;
    } else {
      isTwoFingerGesture = false;
    }
  }, { passive: true });

  document.addEventListener('touchmove', (e) => {
    if (isTwoFingerGesture && e.touches.length === 2) {
      twoFingerLastX = (e.touches[0].clientX + e.touches[1].clientX) / 2;
    }
  }, { passive: true });

  document.addEventListener('touchend', (e) => {
    if (isTwoFingerGesture) {
      const deltaX = twoFingerLastX - twoFingerStartX;
      if (deltaX < -45) {
        if (currentPage < 2) window.switchPage(currentPage + 1);
      } else if (deltaX > 45) {
        if (currentPage > 0) window.switchPage(currentPage - 1);
      }
      isTwoFingerGesture = false;
    }
  }, { passive: true });

  // --- Desktop Selection & Discovery ---
  window.showDesktopSelector = function() {
    triggerHaptic('light');
    desktopLaunchOverlay.classList.remove('hidden');
    renderDiscoveredList();
  };

  window.hideDesktopSelector = function() {
    desktopLaunchOverlay.classList.add('hidden');
  };

  window.onServerDiscovered = function(serverInfo) {
    if (!serverInfo || !serverInfo.ip) return;
    const defaultPort = 58920;
    const hostKey = `${serverInfo.ip}:${serverInfo.port || defaultPort}`;
    discoveredServers.set(hostKey, serverInfo);
    renderDiscoveredList();

    // Auto-connect on launch if not connected yet
    let savedHost = localStorage.getItem('mousely_last_host');
    if (savedHost && savedHost.includes(':8089')) {
      savedHost = savedHost.replace(':8089', `:${defaultPort}`);
      localStorage.setItem('mousely_last_host', savedHost);
    }

    if (!isConnected && (!ws || ws.readyState !== WebSocket.CONNECTING)) {
      if (savedHost && savedHost === hostKey) {
        connectToHost(hostKey);
      } else if (discoveredServers.size === 1 && !savedHost) {
        connectToHost(hostKey);
      }
    }
  };

  function renderDiscoveredList() {
    if (discoveredServers.size === 0) {
      desktopDevicesList.innerHTML = `
        <div class="desktop-item-card" style="justify-content: center; color: var(--text-secondary); cursor: default;">
          <span>Searching local Wi-Fi for Windows PCs...</span>
        </div>`;
      discoverySubtitle.textContent = 'Looking for Mousely on your Wi-Fi...';
      return;
    }

    discoverySubtitle.textContent = `Found ${discoveredServers.size} desktop PC${discoveredServers.size > 1 ? 's' : ''}`;
    desktopDevicesList.innerHTML = '';

    discoveredServers.forEach((info, host) => {
      const card = document.createElement('div');
      card.className = 'desktop-item-card';
      const safeId = host.replace(/[^a-zA-Z0-9]/g, '_');
      card.innerHTML = `
        <div class="desktop-item-left">
          <div class="desktop-icon-badge">🖥️</div>
          <div class="desktop-item-meta">
            <span class="desktop-pc-name">${info.name || 'Windows Desktop'}</span>
            <span class="desktop-pc-ip">${host} • Ready</span>
          </div>
        </div>
        <button type="button" class="btn-connect-pill" id="btn-conn-${safeId}">Connect ➔</button>
      `;

      card.onclick = (e) => {
        e.preventDefault();
        connectToHost(host);
      };

      const btn = card.querySelector('.btn-connect-pill');
      if (btn) {
        btn.onclick = (e) => {
          e.stopPropagation();
          e.preventDefault();
          connectToHost(host);
        };
      }

      desktopDevicesList.appendChild(card);
    });
  }

  window.connectToHost = function(host) {
    triggerHaptic('medium');
    const safeId = host.replace(/[^a-zA-Z0-9]/g, '_');
    const btn = document.getElementById(`btn-conn-${safeId}`);
    if (btn) {
      btn.textContent = 'Connecting...';
      btn.style.opacity = '0.7';
    }
    discoverySubtitle.textContent = `Connecting to ${host}...`;
    connectWebSocket(host);
  };

  window.connectManual = function() {
    let input = manualIpInput.value.trim();
    if (!input) return;
    if (!input.includes(':')) input += ':58920';
    connectToHost(input);
  };

  // --- WebSocket Connection ---
  let reconnectTimer = null;
  let pingTimer = null;

  function connectWebSocket(host) {
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }

    serverHost = host;
    localStorage.setItem('mousely_last_host', host);

    if (ws) {
      try {
        ws.onopen = null;
        ws.onmessage = null;
        ws.onerror = null;
        ws.onclose = null;
        ws.close();
      } catch (e) {}
      ws = null;
    }

    statusDeviceName.textContent = `Connecting to ${host}...`;
    statusDot.className = 'status-dot';

    const wsUrl = `ws://${host}/ws`;
    console.log(`[Mousely] Connecting to: ${wsUrl}`);

    try {
      ws = new WebSocket(wsUrl);
    } catch (err) {
      console.warn('[Mousely] WebSocket creation error:', err);
      handleConnectionFailure(host);
      return;
    }

    ws.onopen = function() {
      console.log('[Mousely] Connected to Windows Desktop!');
      isConnected = true;
      reconnectAttempts = 0;
      statusDot.className = 'status-dot online';
      triggerHaptic('medium');
      window.hideDesktopSelector();
      notifyNativeConnectionState('connected');
      sendPing();
    };

    ws.onmessage = function(event) {
      try {
        const data = JSON.parse(event.data);
        handleServerMessage(data);
      } catch (err) {}
    };

    ws.onerror = function(err) {
      console.warn('[Mousely] WebSocket error:', err);
    };

    ws.onclose = function() {
      isConnected = false;
      statusDot.className = 'status-dot offline';
      statusDeviceName.textContent = 'Disconnected';
      latencyTag.textContent = '-- ms';
      notifyNativeConnectionState('disconnected');
      handleConnectionFailure(host);
    };
  }

  function handleConnectionFailure(host) {
    const safeId = host.replace(/[^a-zA-Z0-9]/g, '_');
    const btn = document.getElementById(`btn-conn-${safeId}`);
    if (btn) {
      btn.textContent = 'Connect ➔';
      btn.style.opacity = '1';
    }
    discoverySubtitle.textContent = `Could not connect to ${host}. Tap to retry.`;

    scheduleReconnect();
  }

  function scheduleReconnect() {
    if (reconnectTimer) {
      clearTimeout(reconnectTimer);
      reconnectTimer = null;
    }

    // Stop continuous reconnecting after 4 attempts to prevent battery drain / heating
    if (reconnectAttempts >= 4) {
      console.log('[Mousely] Max reconnect attempts reached. Idle until user tap or discovery.');
      return;
    }

    reconnectAttempts++;
    const delay = Math.min(2500 * reconnectAttempts, 8000);
    reconnectTimer = setTimeout(() => {
      if (!isConnected && serverHost) {
        connectWebSocket(serverHost);
      }
    }, delay);
  }

  function notifyNativeConnectionState(state) {
    try {
      if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.nativeApp) {
        window.webkit.messageHandlers.nativeApp.postMessage({ action: 'connectionState', state: state });
      }
    } catch (e) {}
  }

  function sendCommand(action, payload = {}) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return;
    ws.send(JSON.stringify(Object.assign({ action: action }, payload)));
  }

  function sendPing() {
    if (pingTimer) {
      clearTimeout(pingTimer);
      pingTimer = null;
    }
    if (!isConnected || !ws || ws.readyState !== WebSocket.OPEN) return;
    pingStartTime = performance.now();
    sendCommand('ping');
    pingTimer = setTimeout(sendPing, 3000);
  }

  // --- Apple Island Toast ---
  let toastTimer = null;
  function showToast(text, icon = '✨') {
    const toast = document.getElementById('appleToast');
    const toastText = document.getElementById('toastText');
    const toastIcon = document.getElementById('toastIcon');
    if (!toast) return;

    if (toastText) toastText.textContent = text;
    if (toastIcon) toastIcon.textContent = icon;

    toast.classList.add('visible');
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
      toast.classList.remove('visible');
    }, 2000);
  }
  window.showToast = showToast;

  // --- Message Dispatcher ---
  function handleServerMessage(msg) {
    switch (msg.type) {
      case 'pong':
        latencyMs = Math.round(performance.now() - pingStartTime);
        latencyTag.textContent = `${latencyMs} ms`;
        break;

      case 'action_ack':
        if (msg.action === 'launch_app') {
          showToast('App Launched', '🚀');
        } else if (msg.action === 'clipboard_action') {
          const names = { copy: 'Copied (Ctrl+C)', paste: 'Pasted (Ctrl+V)', history: 'Clipboard History (Win+V)' };
          showToast(names[msg.clipType] || 'Action Completed', '📋');
        }
        break;

      case 'init':
      case 'state':
        if (msg.deviceName) {
          statusDeviceName.textContent = msg.deviceName;
        }
        if (msg.volume !== undefined && !isDraggingVolume) {
          updateVolumeUI(msg.volume, msg.muted);
        }
        if (msg.media) {
          updateMediaUI(msg.media);
        }
        break;

      case 'volume':
        if (!isDraggingVolume) {
          updateVolumeUI(msg.value, msg.muted);
        }
        break;

      case 'media':
        updateMediaUI(msg.media);
        break;
    }
  }

  // --- Volume UI & Sync (Instant Touch) ---
  function updateVolumeUI(volPercent, muted) {
    currentVolume = Math.max(0, Math.min(100, Math.round(volPercent)));
    isMuted = !!muted;

    volumeFill.style.width = `${currentVolume}%`;
    volumeBadge.textContent = isMuted ? 'Muted' : `${currentVolume}%`;

    volIcon.style.display = isMuted || currentVolume === 0 ? 'none' : 'block';
    muteIcon.style.display = isMuted || currentVolume === 0 ? 'block' : 'none';
  }

  function sendVolumeUpdate(val) {
    val = Math.max(0, Math.min(100, Math.round(val)));
    currentVolume = val;

    if (!rafId) {
      rafId = requestAnimationFrame(() => {
        volumeFill.style.width = `${currentVolume}%`;
        volumeBadge.textContent = `${currentVolume}%`;
        rafId = null;
      });
    }

    if (!volumeThrottleTimer) {
      volumeThrottleTimer = setTimeout(() => {
        sendCommand('set_volume', { value: currentVolume });
        volumeThrottleTimer = null;
      }, 30);
    }
  }

  function commitVolume(val) {
    val = Math.max(0, Math.min(100, Math.round(val)));
    currentVolume = val;
    updateVolumeUI(currentVolume, false);
    sendCommand('set_volume', { value: currentVolume });
    triggerHaptic('light');
  }

  function handleVolumeTouch(e) {
    const rect = volumeTrack.getBoundingClientRect();
    const clientX = e.touches ? e.touches[0].clientX : e.clientX;
    const ratio = (clientX - rect.left) / rect.width;
    sendVolumeUpdate(Math.round(ratio * 100));
  }

  volumeTrack.addEventListener('pointerdown', (e) => {
    isDraggingVolume = true;
    volumeTrack.setPointerCapture(e.pointerId);
    handleVolumeTouch(e);
  });

  volumeTrack.addEventListener('pointermove', (e) => {
    if (isDraggingVolume) handleVolumeTouch(e);
  });

  const finishVolumeDrag = () => {
    if (isDraggingVolume) {
      isDraggingVolume = false;
      commitVolume(currentVolume);
    }
  };

  volumeTrack.addEventListener('pointerup', finishVolumeDrag);
  volumeTrack.addEventListener('pointercancel', finishVolumeDrag);

  window.setVolumePreset = function(val) {
    commitVolume(val);
  };

  window.toggleMute = function() {
    isMuted = !isMuted;
    sendCommand('toggle_mute');
    triggerHaptic('medium');
    updateVolumeUI(currentVolume, isMuted);
  };

  btnMuteToggle.addEventListener('click', window.toggleMute);

  window.onNativeVolumeChanged = function(newVolumePercent) {
    if (!isDraggingVolume) commitVolume(newVolumePercent);
  };

  // --- Media UI & Controls (Instant 0ms Feedback) ---
  function updateMediaUI(media) {
    if (!media) return;

    trackTitle.textContent = media.title || 'No Media Playing';
    trackArtist.textContent = media.artist ? (media.artist + (media.album ? ` • ${media.album}` : '')) : 'Windows Media Session';

    if (media.source) sourceAppPill.textContent = media.source;

    isPlaying = !!media.isPlaying;
    playIcon.style.display = isPlaying ? 'none' : 'block';
    pauseIcon.style.display = isPlaying ? 'block' : 'none';

    if (media.artwork) {
      albumArtImg.src = media.artwork;
    } else if (media.hasArtwork && serverHost) {
      albumArtImg.src = `http://${serverHost}/api/artwork?t=${Date.now()}`;
    }

    if (media.duration !== undefined) duration = media.duration;
    if (media.position !== undefined) {
      currentPosition = media.position;
      lastPositionUpdate = performance.now();
    }

    updateTimelineDisplay();
  }

  function updateTimelineDisplay() {
    if (isScrubbingTimeline) return;
    timeElapsed.textContent = formatTime(currentPosition);
    timeTotal.textContent = formatTime(duration);
    const percent = duration > 0 ? (currentPosition / duration) * 100 : 0;
    timelineProgress.style.width = `${Math.min(100, Math.max(0, percent))}%`;
  }

  clearInterval(timelineTimer);
  timelineTimer = setInterval(() => {
    if (isPlaying && duration > 0 && !isScrubbingTimeline) {
      const deltaSec = (performance.now() - lastPositionUpdate) / 1000;
      const extrapolated = Math.min(duration, currentPosition + deltaSec);
      timeElapsed.textContent = formatTime(extrapolated);
      const percent = (extrapolated / duration) * 100;
      timelineProgress.style.width = `${Math.min(100, Math.max(0, percent))}%`;
    }
  }, 500);

  function handleTimelineScrub(e) {
    if (duration <= 0) return;
    const rect = timelineBar.getBoundingClientRect();
    const clientX = e.touches ? e.touches[0].clientX : e.clientX;
    const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    currentPosition = Math.round(ratio * duration);
    timeElapsed.textContent = formatTime(currentPosition);
    timelineProgress.style.width = `${ratio * 100}%`;
  }

  timelineBar.addEventListener('pointerdown', (e) => {
    if (duration <= 0) return;
    isScrubbingTimeline = true;
    timelineBar.setPointerCapture(e.pointerId);
    handleTimelineScrub(e);
  });

  timelineBar.addEventListener('pointermove', (e) => {
    if (isScrubbingTimeline) handleTimelineScrub(e);
  });

  const finishTimelineScrub = () => {
    if (isScrubbingTimeline) {
      isScrubbingTimeline = false;
      sendCommand('seek', { position: currentPosition });
      lastPositionUpdate = performance.now();
      triggerHaptic('light');
    }
  };

  timelineBar.addEventListener('pointerup', finishTimelineScrub);
  timelineBar.addEventListener('pointercancel', finishTimelineScrub);

  // Instant Play / Pause / Skip
  btnPlayPause.addEventListener('pointerdown', () => {
    triggerHaptic('medium');
    isPlaying = !isPlaying;
    playIcon.style.display = isPlaying ? 'none' : 'block';
    pauseIcon.style.display = isPlaying ? 'block' : 'none';
    sendCommand('play_pause');
  });

  btnPrev.addEventListener('pointerdown', () => {
    triggerHaptic('light');
    sendCommand('prev');
  });

  btnNext.addEventListener('pointerdown', () => {
    triggerHaptic('light');
    sendCommand('next');
  });

  // --- Page 2: Mission Control (8 Blocks - Instant 0ms) ---
  window.launchApp = function(slot, name) {
    triggerHaptic('medium');
    showToast(`Launching ${name}...`, '🚀');
    sendCommand('launch_app', { slot: slot, name: name });
  };

  // --- Page 3: Clipboard Actions (3 Blocks - Instant 0ms) ---
  window.triggerClipboardAction = function(type) {
    triggerHaptic('medium');
    const labels = {
      copy: ['Copied (Ctrl + C)', '📋'],
      paste: ['Pasting (Ctrl + V)', '📥'],
      history: ['Clipboard History (Win + V)', '📜']
    };
    const info = labels[type] || ['Action Sent', '✨'];
    showToast(info[0], info[1]);
    sendCommand('clipboard_action', { type: type });
  };

  // --- Background Prober on Launch ---
  function probeSubnet() {
    let savedHost = localStorage.getItem('mousely_last_host');
    if (savedHost && savedHost.includes(':8089')) {
      savedHost = savedHost.replace(':8089', ':58920');
      localStorage.setItem('mousely_last_host', savedHost);
    }

    if (savedHost) {
      fetch(`http://${savedHost}/api/status`, { signal: AbortSignal.timeout(1200) })
        .then(r => r.json())
        .then(data => {
          const parts = savedHost.split(':');
          window.onServerDiscovered({
            name: data.deviceName || 'Windows PC',
            ip: parts[0],
            port: parseInt(parts[1] || '58920')
          });
        })
        .catch(() => {});
    }

    if (window.location.host && !window.location.protocol.startsWith('file')) {
      window.onServerDiscovered({
        name: 'Windows PC (Host)',
        ip: window.location.hostname,
        port: parseInt(window.location.port || '58920')
      });
    }
  }

  // --- Bootstrapping on Launch ---
  window.addEventListener('DOMContentLoaded', () => {
    // Show desktop selector overlay immediately on launch so user can see detected PCs
    window.showDesktopSelector();

    probeSubnet();

    const savedHost = localStorage.getItem('mousely_last_host');
    if (savedHost) {
      connectWebSocket(savedHost);
    }
  });

})();
