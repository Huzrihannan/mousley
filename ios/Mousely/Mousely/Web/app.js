// Mousely - Ultra-Responsive Media & Volume Controller
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

  // DOM Elements
  const statusBadge = document.getElementById('statusBadge');
  const statusDot = document.getElementById('statusDot');
  const statusDeviceName = document.getElementById('statusDeviceName');
  const latencyTag = document.getElementById('latencyTag');
  
  const playerCard = document.getElementById('playerCard');
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
  
  const deviceModal = document.getElementById('deviceModal');
  const discoveredList = document.getElementById('discoveredList');
  const manualIpInput = document.getElementById('manualIpInput');

  // Haptic feedback trigger (bridges to iOS Swift native or web vibration)
  function triggerHaptic(style = 'light') {
    try {
      if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.nativeApp) {
        window.webkit.messageHandlers.nativeApp.postMessage({ action: 'haptic', style: style });
      } else if (navigator.vibrate) {
        navigator.vibrate(style === 'medium' ? 20 : 10);
      }
    } catch (e) {}
  }

  // Format seconds to mm:ss
  function formatTime(seconds) {
    if (isNaN(seconds) || seconds < 0) return '0:00';
    const mins = Math.floor(seconds / 60);
    const secs = Math.floor(seconds % 60);
    return `${mins}:${secs < 10 ? '0' : ''}${secs}`;
  }

  // --- WebSocket Connection ---
  function connectWebSocket(host) {
    if (!host) {
      const savedHost = localStorage.getItem('mousely_last_host');
      if (savedHost) {
        host = savedHost;
      } else if (window.location.host && !window.location.protocol.startsWith('file')) {
        host = window.location.host;
      } else {
        host = '127.0.0.1:8089';
      }
    }

    serverHost = host;
    localStorage.setItem('mousely_last_host', host);

    if (ws) {
      try { ws.close(); } catch (e) {}
    }

    statusDeviceName.textContent = `Connecting to ${host}...`;
    statusDot.className = 'status-dot';

    const wsUrl = `ws://${host}/ws`;
    console.log(`Connecting to Mousely server at: ${wsUrl}`);

    try {
      ws = new WebSocket(wsUrl);
    } catch (err) {
      console.error('WebSocket creation error:', err);
      scheduleReconnect();
      return;
    }

    ws.onopen = function() {
      console.log('Connected to Mousely Server!');
      isConnected = true;
      reconnectAttempts = 0;
      statusDot.className = 'status-dot online';
      triggerHaptic('medium');
      
      // Start ping loop
      sendPing();
    };

    ws.onmessage = function(event) {
      try {
        const data = JSON.parse(event.data);
        handleServerMessage(data);
      } catch (err) {
        console.error('Error parsing incoming message:', err);
      }
    };

    ws.onerror = function(err) {
      console.warn('WebSocket error:', err);
    };

    ws.onclose = function() {
      console.log('WebSocket closed.');
      isConnected = false;
      statusDot.className = 'status-dot offline';
      statusDeviceName.textContent = 'Disconnected';
      latencyTag.textContent = '-- ms';
      scheduleReconnect();
    };
  }

  function scheduleReconnect() {
    reconnectAttempts++;
    const delay = Math.min(1000 * Math.pow(1.5, reconnectAttempts), 5000);
    setTimeout(() => {
      if (!isConnected && serverHost) {
        connectWebSocket(serverHost);
      }
    }, delay);
  }

  function sendCommand(action, payload = {}) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return;
    const msg = Object.assign({ action: action }, payload);
    ws.send(JSON.stringify(msg));
  }

  function sendPing() {
    if (!isConnected || !ws || ws.readyState !== WebSocket.OPEN) return;
    pingStartTime = performance.now();
    sendCommand('ping');
    setTimeout(sendPing, 2500);
  }

  // --- Handle Incoming Messages from Windows ---
  function handleServerMessage(msg) {
    switch (msg.type) {
      case 'pong':
        latencyMs = Math.round(performance.now() - pingStartTime);
        latencyTag.textContent = `${latencyMs} ms`;
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

  // --- Volume UI & Sync ---
  function updateVolumeUI(volPercent, muted) {
    currentVolume = Math.max(0, Math.min(100, Math.round(volPercent)));
    isMuted = !!muted;

    volumeFill.style.width = `${currentVolume}%`;
    volumeBadge.textContent = isMuted ? 'Muted' : `${currentVolume}%`;

    if (isMuted || currentVolume === 0) {
      volIcon.style.display = 'none';
      muteIcon.style.display = 'block';
    } else {
      volIcon.style.display = 'block';
      muteIcon.style.display = 'none';
    }
  }

  function sendVolumeUpdate(val) {
    val = Math.max(0, Math.min(100, Math.round(val)));
    currentVolume = val;
    volumeFill.style.width = `${currentVolume}%`;
    volumeBadge.textContent = `${currentVolume}%`;

    // Throttle messages over WebSocket to 25ms during drag
    if (!volumeThrottleTimer) {
      volumeThrottleTimer = setTimeout(() => {
        sendCommand('set_volume', { value: currentVolume });
        volumeThrottleTimer = null;
      }, 25);
    }
  }

  function commitVolume(val) {
    val = Math.max(0, Math.min(100, Math.round(val)));
    currentVolume = val;
    updateVolumeUI(currentVolume, false);
    sendCommand('set_volume', { value: currentVolume });
    triggerHaptic('light');
  }

  // Volume slider interaction
  function handleVolumeTouch(e) {
    const rect = volumeTrack.getBoundingClientRect();
    const clientX = e.touches ? e.touches[0].clientX : e.clientX;
    const ratio = (clientX - rect.left) / rect.width;
    const percent = Math.max(0, Math.min(100, Math.round(ratio * 100)));
    sendVolumeUpdate(percent);
  }

  volumeTrack.addEventListener('pointerdown', (e) => {
    isDraggingVolume = true;
    volumeTrack.setPointerCapture(e.pointerId);
    handleVolumeTouch(e);
  });

  volumeTrack.addEventListener('pointermove', (e) => {
    if (isDraggingVolume) {
      handleVolumeTouch(e);
    }
  });

  const finishVolumeDrag = (e) => {
    if (isDraggingVolume) {
      isDraggingVolume = false;
      commitVolume(currentVolume);
    }
  };

  volumeTrack.addEventListener('pointerup', finishVolumeDrag);
  volumeTrack.addEventListener('pointercancel', finishVolumeDrag);

  // Quick Presets
  window.setVolumePreset = function(val) {
    commitVolume(val);
  };

  window.toggleMute = function() {
    isMuted = !isMuted;
    sendCommand('toggle_mute');
    triggerHaptic('medium');
    updateVolumeUI(currentVolume, isMuted);
  };

  btnMuteToggle.addEventListener('click', () => {
    window.toggleMute();
  });

  document.getElementById('btnVolMax').addEventListener('click', () => {
    commitVolume(100);
  });

  // Native Volume from iOS Physical Volume Buttons
  window.onNativeVolumeChanged = function(newVolumePercent) {
    console.log('Hardware volume button pressed:', newVolumePercent);
    if (!isDraggingVolume) {
      commitVolume(newVolumePercent);
    }
  };

  // --- Media UI & Controls ---
  function updateMediaUI(media) {
    if (!media) return;

    if (media.title) {
      trackTitle.textContent = media.title;
    } else {
      trackTitle.textContent = 'No Media Playing';
    }

    if (media.artist) {
      trackArtist.textContent = media.artist + (media.album ? ` • ${media.album}` : '');
    } else {
      trackArtist.textContent = 'Windows Media Session';
    }

    if (media.source) {
      sourceAppPill.textContent = media.source;
    }

    // Playback state
    isPlaying = !!media.isPlaying;
    if (isPlaying) {
      playIcon.style.display = 'none';
      pauseIcon.style.display = 'block';
      playerCard.classList.add('playing');
    } else {
      playIcon.style.display = 'block';
      pauseIcon.style.display = 'none';
      playerCard.classList.remove('playing');
    }

    // Artwork
    if (media.artwork) {
      albumArtImg.src = media.artwork;
    } else if (media.hasArtwork && serverHost) {
      albumArtImg.src = `http://${serverHost}/api/artwork?t=${Date.now()}`;
    }

    // Timeline
    if (media.duration !== undefined) {
      duration = media.duration;
    }
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

  // Smooth local timeline ticker while playing
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

  // Timeline scrubber interaction
  function handleTimelineScrub(e) {
    if (duration <= 0) return;
    const rect = timelineBar.getBoundingClientRect();
    const clientX = e.touches ? e.touches[0].clientX : e.clientX;
    const ratio = Math.max(0, Math.min(1, (clientX - rect.left) / rect.width));
    const targetSeconds = Math.round(ratio * duration);
    currentPosition = targetSeconds;
    timeElapsed.textContent = formatTime(targetSeconds);
    timelineProgress.style.width = `${ratio * 100}%`;
  }

  timelineBar.addEventListener('pointerdown', (e) => {
    if (duration <= 0) return;
    isScrubbingTimeline = true;
    playerCard.classList.add('scrubbing');
    timelineBar.setPointerCapture(e.pointerId);
    handleTimelineScrub(e);
  });

  timelineBar.addEventListener('pointermove', (e) => {
    if (isScrubbingTimeline) {
      handleTimelineScrub(e);
    }
  });

  const finishTimelineScrub = (e) => {
    if (isScrubbingTimeline) {
      isScrubbingTimeline = false;
      playerCard.classList.remove('scrubbing');
      sendCommand('seek', { position: currentPosition });
      lastPositionUpdate = performance.now();
      triggerHaptic('light');
    }
  };

  timelineBar.addEventListener('pointerup', finishTimelineScrub);
  timelineBar.addEventListener('pointercancel', finishTimelineScrub);

  // Playback Control Buttons
  btnPlayPause.addEventListener('click', () => {
    triggerHaptic('medium');
    isPlaying = !isPlaying;
    if (isPlaying) {
      playIcon.style.display = 'none';
      pauseIcon.style.display = 'block';
      playerCard.classList.add('playing');
    } else {
      playIcon.style.display = 'block';
      pauseIcon.style.display = 'none';
      playerCard.classList.remove('playing');
    }
    sendCommand('play_pause');
  });

  btnPrev.addEventListener('click', () => {
    triggerHaptic('light');
    sendCommand('prev');
  });

  btnNext.addEventListener('click', () => {
    triggerHaptic('light');
    sendCommand('next');
  });

  // --- Device Discovery & Modal ---
  window.openDeviceModal = function() {
    triggerHaptic('light');
    deviceModal.classList.add('active');
    renderDiscoveredList();
  };

  window.closeDeviceModal = function() {
    deviceModal.classList.remove('active');
  };

  window.onServerDiscovered = function(serverInfo) {
    if (!serverInfo || !serverInfo.ip) return;
    const key = `${serverInfo.ip}:${serverInfo.port || 8089}`;
    discoveredServers.set(key, serverInfo);
    renderDiscoveredList();

    // Auto-connect if currently disconnected
    if (!isConnected) {
      connectWebSocket(key);
    }
  };

  function renderDiscoveredList() {
    if (discoveredServers.size === 0) {
      discoveredList.innerHTML = `
        <div class="device-item" style="color: var(--text-secondary); justify-content: center;">
          Searching local Wi-Fi for Windows PCs...
        </div>`;
      return;
    }

    discoveredList.innerHTML = '';
    discoveredServers.forEach((info, host) => {
      const item = document.createElement('div');
      item.className = 'device-item';
      item.onclick = () => {
        connectWebSocket(host);
        window.closeDeviceModal();
      };
      item.innerHTML = `
        <div class="device-item-info">
          <span class="device-item-name">${info.name || 'Windows PC'}</span>
          <span class="device-item-ip">${host}</span>
        </div>
        <span class="connect-badge">Connect</span>
      `;
      discoveredList.appendChild(item);
    });
  }

  window.connectManual = function() {
    const input = manualIpInput.value.trim();
    if (!input) return;
    let target = input;
    if (!target.includes(':')) {
      target += ':8089';
    }
    connectWebSocket(target);
    window.closeDeviceModal();
  };

  // --- Liquid Glass UI Setup ---
  function initLiquidGlass() {
    try {
      if (typeof Container !== 'undefined' && typeof Button !== 'undefined') {
        console.log('Initializing Liquid Glass components...');
        // Wrap interactive elements with glass shader containers if supported
        const glassConfig = {
          edgeIntensity: 0.025,
          rimIntensity: 0.08,
          blurRadius: 8.0,
          tintOpacity: 0.15
        };
        window.glassControls = glassConfig;
      }
    } catch (e) {
      console.warn('Liquid glass initialization note:', e);
    }
  }

  // Bootstrapping
  window.addEventListener('DOMContentLoaded', () => {
    initLiquidGlass();

    // Default connection
    const defaultHost = localStorage.getItem('mousely_last_host') || 
                        (window.location.host && !window.location.protocol.startsWith('file') ? window.location.host : '');
    if (defaultHost) {
      connectWebSocket(defaultHost);
    } else {
      window.openDeviceModal();
    }
  });

})();
