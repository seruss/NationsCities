// Państwa, Miasta - JavaScript Interop
// ======================================

// ======================================
// Configurable Logger
// ======================================
// Control via localStorage: localStorage.setItem('debug_log_level', 'debug')
// Levels: 'off' | 'error' | 'warn' | 'info' | 'debug' | 'trace'
// Default: 'info' (shows info + warn + error)
window.GameLog = (function () {
    const LEVELS = { off: 0, error: 1, warn: 2, info: 3, debug: 4, trace: 5 };

    function getLevel() {
        try {
            const stored = localStorage.getItem('debug_log_level');
            if (stored && LEVELS[stored] !== undefined) return LEVELS[stored];
        } catch (e) { /* localStorage unavailable */ }
        return LEVELS.info; // default
    }

    function ts() {
        const d = new Date();
        return `${d.getHours().toString().padStart(2, '0')}:${d.getMinutes().toString().padStart(2, '0')}:${d.getSeconds().toString().padStart(2, '0')}.${d.getMilliseconds().toString().padStart(3, '0')}`;
    }

    function log(level, tag, msg, ...args) {
        if (LEVELS[level] > getLevel()) return;
        const prefix = `[${ts()}][${tag}]`;
        switch (level) {
            case 'error': console.error(prefix, msg, ...args); break;
            case 'warn': console.warn(prefix, msg, ...args); break;
            case 'info': console.info(prefix, msg, ...args); break;
            case 'debug': console.log(prefix, msg, ...args); break;
            case 'trace': console.log(prefix, `[TRACE]`, msg, ...args); break;
        }
    }

    return {
        error: (tag, msg, ...args) => log('error', tag, msg, ...args),
        warn: (tag, msg, ...args) => log('warn', tag, msg, ...args),
        info: (tag, msg, ...args) => log('info', tag, msg, ...args),
        debug: (tag, msg, ...args) => log('debug', tag, msg, ...args),
        trace: (tag, msg, ...args) => log('trace', tag, msg, ...args),
        setLevel: (lvl) => { try { localStorage.setItem('debug_log_level', lvl); } catch (e) { } },
        getLevel: () => { try { return localStorage.getItem('debug_log_level') || 'info'; } catch (e) { return 'info'; } }
    };
})();

// NOTE: ThemeManager is now defined inline in App.razor <head> 
// so it's available immediately for onclick handlers.

// ======================================
// Countdown Bar (requestAnimationFrame driver)
// ======================================
// Drives countdown bar width smoothly using real clock time.
// Immune to Blazor re-renders and +1s time additions — just call start() with
// the new endTime and it seamlessly continues from the correct position.
window.countdownBar = (function () {
    let _raf = null;

    return {
        /**
         * Start (or restart) the countdown bar animation.
         * Width is driven by JS rAF; color is handled by CSS class (with transition).
         * @param {number} endTimeMs  - Unix ms timestamp when countdown ends
         * @param {number} totalMs    - Total countdown duration in ms (for % calculation)
         * @param {string[]} ids      - Array of element IDs to update
         */
        start: function (endTimeMs, totalMs, ids) {
            if (_raf !== null) {
                cancelAnimationFrame(_raf);
                _raf = null;
            }

            function tick() {
                const remaining = Math.max(0, endTimeMs - Date.now());
                const pct = Math.min(100, (remaining / totalMs) * 100);

                for (const id of ids) {
                    const el = document.getElementById(id);
                    if (el) el.style.width = pct + '%';
                }

                if (remaining > 0) {
                    _raf = requestAnimationFrame(tick);
                } else {
                    _raf = null;
                }
            }

            tick();
        },

        stop: function () {
            if (_raf !== null) {
                cancelAnimationFrame(_raf);
                _raf = null;
            }
        }
    };
})();


// ======================================
// Disable Pull-to-Refresh on iOS Safari
// ======================================
// CSS overscroll-behavior is not reliable on iOS Safari.
// This JS approach is the most robust cross-platform solution.
(function () {
    let lastTouchY = 0;
    document.addEventListener('touchstart', function (e) {
        if (e.touches.length === 1) {
            lastTouchY = e.touches[0].clientY;
        }
    }, { passive: true });

    document.addEventListener('touchmove', function (e) {
        const touchY = e.touches[0].clientY;
        const touchYDelta = touchY - lastTouchY;
        lastTouchY = touchY;

        // Block pull-down when at the top of the page (native pull-to-refresh)
        // Only block if: not inside a scrollable element that is itself scrollable upward
        const target = e.target;
        const scrollableParent = target.closest('[data-scrollable]') ||
            (target.scrollHeight > target.clientHeight ? target : null);

        if (!scrollableParent && touchYDelta > 0 && window.scrollY === 0) {
            e.preventDefault();
        }
    }, { passive: false });
})();

// ======================================
// Game Session Management (SPA Support)
// ======================================

window.gameSession = {
    _SESSION_KEY: 'game_session',
    _TAB_KEY: 'game_active_tab',
    _tabId: null,
    _dotNetRef: null,

    /**
     * Get or create a unique session ID for this player
     */
    getOrCreateSessionId: function () {
        let sessionId = localStorage.getItem('game_session_id');
        if (!sessionId) {
            sessionId = 'sess_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
            localStorage.setItem('game_session_id', sessionId);
        }
        return sessionId;
    },

    /**
     * Get unique tab ID for multi-tab detection
     */
    getTabId: function () {
        if (!this._tabId) {
            this._tabId = 'tab_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
        }
        return this._tabId;
    },

    /**
     * Save current game session to localStorage
     */
    save: function (sessionId, roomCode, nickname) {
        const data = {
            roomCode: roomCode,
            nickname: nickname,
            savedAt: Date.now()
        };
        localStorage.setItem(this._SESSION_KEY, JSON.stringify(data));
        GameLog.debug('Session', 'Saved:', data);
    },

    /**
     * Load saved game session from localStorage
     */
    load: function () {
        try {
            const data = localStorage.getItem(this._SESSION_KEY);
            if (!data) return null;

            const session = JSON.parse(data);
            // Check if session is stale (>30 min)
            if (Date.now() - session.savedAt > 30 * 60 * 1000) {
                GameLog.info('Session', 'Session expired, clearing');
                this.clear();
                return null;
            }
            GameLog.debug('Session', 'Loaded:', session);
            return session;
        } catch (e) {
            GameLog.error('Session', 'Error loading:', e);
            return null;
        }
    },

    /**
     * Clear saved game session
     */
    clear: function () {
        localStorage.removeItem(this._SESSION_KEY);
        localStorage.removeItem(this._TAB_KEY);
        GameLog.debug('Session', 'Cleared');
    },

    /**
     * Save last used nickname (persists even after session is cleared)
     */
    saveLastNickname: function (nickname) {
        if (nickname) {
            localStorage.setItem('game_last_nickname', nickname);
        }
    },

    /**
     * Get last used nickname (for pre-filling the input)
     */
    getLastNickname: function () {
        return localStorage.getItem('game_last_nickname') || '';
    },

    /**
     * Setup navigation guard for browser back button and refresh/close
     */
    setupNavigationGuard: function (dotNetRef) {
        this._dotNetRef = dotNetRef;

        // Handle beforeunload (refresh/close) - shows native ugly browser popup
        window.addEventListener('beforeunload', (e) => {
            if (window._gamePhase && window._gamePhase !== 'Home') {
                e.preventDefault();
                e.returnValue = 'Czy na pewno chcesz opuścić grę?';
            }
        });

        // Handle popstate (back/forward button) - shows custom Blazor modal
        // CRITICAL: stopImmediatePropagation prevents Blazor's own popstate handler
        // (in blazor.web.js) from processing this event, which caused the old
        // "flash and disappear" bug. Since app.js loads before blazor.web.js,
        // our handler is registered first and runs first.
        window.addEventListener('popstate', async (e) => {
            if (window._gamePhase && window._gamePhase !== 'Home' && this._dotNetRef) {
                // Stop Blazor from seeing this event
                e.stopImmediatePropagation();
                // Prevent navigation by pushing state back
                history.pushState(null, '', window.location.href);
                // Notify Blazor to show the pretty modal
                try {
                    await this._dotNetRef.invokeMethodAsync('OnBackButtonPressed');
                } catch (err) {
                    GameLog.warn('Session', 'Could not notify Blazor of back button:', err);
                }
            }
        });

        // Push initial state to enable popstate detection
        history.pushState(null, '', window.location.href);
        GameLog.debug('Session', 'Navigation guard setup complete');
    },

    /**
     * Setup multi-tab detection
     */
    setupTabProtection: function (roomCode) {
        // Listen for storage changes from other tabs
        window.addEventListener('storage', (e) => {
            if (e.key === this._TAB_KEY && e.newValue) {
                try {
                    const otherTab = JSON.parse(e.newValue);
                    if (otherTab.tabId !== this._tabId && otherTab.roomCode === roomCode) {
                        this._handleDuplicateTab();
                    }
                } catch (err) {
                    GameLog.warn('Session', 'Error checking tab:', err);
                }
            }
        });

        // Claim this tab as active
        this.claimTab(roomCode);
    },

    /**
     * Claim this tab as the active game tab
     */
    claimTab: function (roomCode) {
        const data = {
            tabId: this._tabId,
            roomCode: roomCode,
            timestamp: Date.now()
        };
        localStorage.setItem(this._TAB_KEY, JSON.stringify(data));
    },

    /**
     * Handle duplicate tab detected
     */
    _handleDuplicateTab: function () {
        GameLog.warn('Session', 'Duplicate tab detected!');

        // Create blocking overlay
        const overlay = document.createElement('div');
        overlay.className = 'fixed inset-0 z-[9999] flex flex-col items-center justify-center bg-slate-900/95 backdrop-blur-sm';
        overlay.innerHTML = `
            <div class="flex flex-col items-center gap-6 p-8 text-center">
                <span class="material-symbols-outlined text-red-500" style="font-size: 80px;">tab_close</span>
                <h2 class="text-2xl font-bold text-white">Gra jest otwarta w innej karcie</h2>
                <p class="text-slate-400 max-w-xs">Zamknij tę kartę lub wróć do poprzedniej karty z grą.</p>
                <button onclick="window.location.href='/'" class="px-6 py-3 bg-primary text-white rounded-full font-semibold hover:bg-primary/90 transition-colors">
                    Wróć do strony głównej
                </button>
            </div>
        `;
        document.body.appendChild(overlay);

        // Notify Blazor if reference exists
        if (this._dotNetRef) {
            try {
                this._dotNetRef.invokeMethodAsync('OnDuplicateTabDetected');
            } catch (err) {
                GameLog.warn('Session', 'Could not notify Blazor of duplicate tab:', err);
            }
        }
    }
};

/**
 * Sync game phase from Blazor for anti-cheat and navigation guard
 */
window.setGamePhase = function (phase) {
    window._gamePhase = phase;
    GameLog.info('Flow', `Phase changed to: ${phase}`);
};

/**
 * Copies text to clipboard using modern Clipboard API with fallback
 */
window.copyToClipboard = async function (text) {
    try {
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
            return true;
        } else {
            const textArea = document.createElement("textarea");
            textArea.value = text;
            textArea.style.position = "fixed";
            textArea.style.left = "-999999px";
            textArea.style.top = "-999999px";
            document.body.appendChild(textArea);
            textArea.focus();
            textArea.select();
            const successful = document.execCommand('copy');
            textArea.remove();
            return successful;
        }
    } catch (err) {
        GameLog.error('Clipboard', 'Failed to copy text:', err);
        return false;
    }
};

/**
 * Sets up the theme toggle button click handler and initial icon state.
 * Called from Blazor's OnAfterRenderAsync to ensure handler is attached after render.
 */
window.setupThemeToggle = function (buttonId, iconId) {
    var icon = document.getElementById(iconId);
    if (icon) {
        icon.textContent = document.documentElement.classList.contains('dark') ? 'light_mode' : 'dark_mode';
    }

    var btn = document.getElementById(buttonId);
    if (btn) {
        btn.onclick = function (e) {
            e.preventDefault();
            e.stopPropagation();
            var newTheme = window.themeManager.toggle();
            var iconEl = document.getElementById(iconId);
            if (iconEl) {
                iconEl.textContent = newTheme === 'dark' ? 'light_mode' : 'dark_mode';
            }
        };
    }
};

// ======================================
// Anti-Cheat System (Heartbeat-based)
// ======================================

window.AntiCheatTracker = class {
    constructor() {
        this._sessionKey = 'anticheat_session';
        this._heartbeatInterval = null;
        this._blockOverlay = null;

        this.HEARTBEAT_MS = 500;
        this.NOTICE_THRESHOLD_MS = 2000;
        this.WARNING_THRESHOLD_MS = 10000;
        this.PENALTY_THRESHOLD_MS = 30000;

        // Bind event handlers
        this._handleVisibilityChange = this._handleVisibilityChange.bind(this);

        // Always listen for visibility changes to detect violations on resume
        document.addEventListener('visibilitychange', this._handleVisibilityChange);

        // Check for violations on page load (handles page refresh scenario)
        this._checkForViolationOnLoad();
    }

    // === PUBLIC API ===

    startTracking(roomCode, roundNumber = 1) {
        const existing = this._getSession();

        // If same room and we have an existing session, preserve violation count (for multi-round games)
        const preserveViolations = existing?.roomCode === roomCode;
        const existingViolationCount = preserveViolations ? (existing.violationCount || 0) : 0;

        // Clear old session
        this._clearSession();

        // Create new session - preserve violation count if same room
        this._saveSession({
            roomCode: roomCode,
            roundNumber: roundNumber,
            isActive: true,
            startedAt: Date.now(),
            lastActiveAt: Date.now(),
            violationCount: existingViolationCount
        });

        this._startHeartbeat();
        GameLog.info('AntiCheat', `Tracking started: room=${roomCode}, round=${roundNumber}, violations=${existingViolationCount}`);
    }

    // Resume tracking after pause (for round 2+)
    resumeTracking(roomCode, roundNumber) {
        const session = this._getSession();
        if (session) {
            session.isActive = true;
            session.lastActiveAt = Date.now();
            // Update round number for new round
            if (roundNumber) {
                session.roundNumber = roundNumber;
            }
            this._saveSession(session);
            this._startHeartbeat();
            GameLog.info('AntiCheat', `Tracking resumed: round=${session.roundNumber}, violations=${session.violationCount}`);

            // Drain any pending violations queued while disconnected.
            // NOTE: do NOT reset _processingQueue here — if registerAntiCheatHandler was just
            // called (reconnect scenario), it already reset the flag and started _processQueue.
            // Resetting it again here would break that guard and cause duplicate reports.
            this._processQueue();
        } else if (roomCode) {
            // No session to resume - start fresh (fallback)
            GameLog.info('AntiCheat', 'No session to resume, starting fresh');
            this.startTracking(roomCode, roundNumber || 1);
        } else {
            GameLog.warn('AntiCheat', 'No session to resume and no roomCode provided');
        }
    }

    stopTracking() {
        this._stopHeartbeat();
        this._clearSession();
        this._hideBlockOverlay();
        GameLog.info('AntiCheat', 'Tracking stopped');
    }

    // Pause tracking - stops detecting new violations but keeps handler for reporting
    pauseTracking() {
        this._stopHeartbeat();
        const session = this._getSession();
        if (session) {
            session.isActive = false;
            this._saveSession(session);
        }
        this._hideBlockOverlay();
        GameLog.info('AntiCheat', 'Tracking paused (answers submitted)');
    }

    // Clear session completely - call when returning to lobby between games
    clearSession() {
        this._stopHeartbeat();
        this._stopQueueRetry();
        this._clearSession();
        this._clearPendingQueue(); // Clear pending violations too
        this._hideBlockOverlay();
        GameLog.info('AntiCheat', 'Session cleared (new game will start fresh)');
    }

    // Flush all pending violations NOW before navigating away
    // This ensures violations are reported before showing scoreboard
    async flushPendingViolations(dotNetRef) {
        // Temporarily set the dotNetRef so _processQueue can use it
        const prevRef = window._antiCheatDotNetRef;
        const prevReady = window._antiCheatReady;
        window._antiCheatDotNetRef = dotNetRef;
        window._antiCheatReady = true;
        try {
            await this._processQueue();
        } finally {
            // Restore previous state
            window._antiCheatDotNetRef = prevRef;
            window._antiCheatReady = prevReady;
        }
    }

    isTracking() {
        const session = this._getSession();
        return session?.isActive === true;
    }

    getRoomCode() {
        const session = this._getSession();
        return session?.roomCode || null;
    }

    // === HEARTBEAT ===

    _startHeartbeat() {
        this._stopHeartbeat();
        this._heartbeatInterval = setInterval(() => {
            this._updateHeartbeat();
        }, this.HEARTBEAT_MS);
        GameLog.trace('AntiCheat', 'Heartbeat started');
    }

    _stopHeartbeat() {
        if (this._heartbeatInterval) {
            clearInterval(this._heartbeatInterval);
            this._heartbeatInterval = null;
        }
    }

    _updateHeartbeat() {
        const session = this._getSession();
        if (session && session.isActive) {
            session.lastActiveAt = Date.now();
            this._saveSession(session);
        }
    }

    // === VIOLATION DETECTION ===

    _checkForViolationOnLoad() {
        // Check if there was an active session when page loads
        const session = this._getSession();
        if (!session || !session.isActive) return;

        // Only process on game page
        if (!this._isOnGameRoundPage(session.roomCode)) {
            GameLog.debug('AntiCheat', 'Page load: Not on game page, clearing stale session');
            this._clearSession();
            return;
        }

        const gap = Date.now() - session.lastActiveAt;
        GameLog.debug('AntiCheat', `Page load check: gap=${(gap / 1000).toFixed(2)}s`);

        // If gap is too large (>30 min), session is stale - clear it
        if (gap > 30 * 60 * 1000) {
            GameLog.info('AntiCheat', 'Session is stale (>30 min), clearing');
            this._clearSession();
            return;
        }

        if (gap > this.NOTICE_THRESHOLD_MS) {
            this._handleViolation(gap, session);
        } else {
            // Resume heartbeat if gap is small
            this._startHeartbeat();
        }
    }

    _handleVisibilityChange() {
        const session = this._getSession();

        if (document.hidden) {
            // Page is being hidden - store the timestamp
            if (session && session.isActive) {
                session.hiddenAt = Date.now();
                this._saveSession(session);
                GameLog.debug('AntiCheat', `Page hidden: hiddenAt=${session.hiddenAt}, round=${session.roundNumber}`);
            } else {
                GameLog.debug('AntiCheat', `Page hidden but no active session (session=${!!session}, active=${session?.isActive})`);
            }
        } else {
            // Page became visible - check for violation
            GameLog.info('AntiCheat', `Page VISIBLE: session=${!!session}, active=${session?.isActive}, dotNetRef=${!!window._antiCheatDotNetRef}, ready=${!!window._antiCheatReady}, phase=${window._gamePhase}`);

            if (!session || !session.isActive) {
                GameLog.debug('AntiCheat', 'No active session, nothing to check');
                return;
            }

            // Only process violations when actively playing a round in the same room
            // Use strict mode: require Playing phase to prevent false positives
            // (e.g. tab left overnight, then new room created next day)
            if (!this._isOnGameRoundPage(session.roomCode, { requirePlayingPhase: true })) {
                GameLog.debug('AntiCheat', 'Not actively playing a round, skipping violation check');
                return;
            }

            // Calculate gap from hiddenAt (preferred) or lastActiveAt (fallback for mobile)
            const hiddenAt = session.hiddenAt || session.lastActiveAt;
            const gap = Date.now() - hiddenAt;
            GameLog.info('AntiCheat', `Absence: gap=${(gap / 1000).toFixed(2)}s, hiddenAt=${session.hiddenAt ? new Date(session.hiddenAt).toISOString() : 'null'}, lastActiveAt=${new Date(session.lastActiveAt).toISOString()}`);

            // Staleness guard: if absent > 30 min, session is stale (e.g. tab left overnight)
            // Clear instead of reporting — the game has certainly ended by now.
            if (gap > 30 * 60 * 1000) {
                GameLog.info('AntiCheat', `Session is stale on visibility change (gap=${(gap / 1000).toFixed(0)}s > 30 min), clearing`);
                this._clearSession();
                this._clearPendingQueue();
                this._hideBlockOverlay();
                return;
            }

            // Clear hiddenAt
            session.hiddenAt = null;
            session.lastActiveAt = Date.now();
            this._saveSession(session);

            if (gap > this.NOTICE_THRESHOLD_MS) {
                this._handleViolation(gap, session);
            } else {
                // No new violation, but there may be pending violations from
                // a previous visibility change whose report failed because
                // the Blazor circuit was still reconnecting. Retry now.
                const pendingQueue = this._getPendingQueue();
                if (pendingQueue.length > 0) {
                    GameLog.info('AntiCheat', `Page visible, retrying ${pendingQueue.length} pending violations`);
                    // Reset processing flag in case a previous call hung
                    this._processingQueue = false;
                    setTimeout(() => this._processQueue(), 500);
                    this._startQueueRetry();
                }
            }

            // Resume heartbeat
            this._startHeartbeat();
        }
    }

    _isOnGameRoundPage(roomCode, { requirePlayingPhase = false } = {}) {
        // Check SPA state — only trigger violations when actively playing a round
        const isPlayingPhase = window._gamePhase === 'Playing';

        // Check URL (for page load / deep links when Blazor hasn't set phase yet)
        const path = window.location.pathname.toLowerCase();
        const isGamePageByUrl = path.startsWith('/game/');

        // Verify the room code in the URL matches the session's room code
        // (prevents cross-room false positives after server restart / new room)
        const urlRoomCode = isGamePageByUrl ? path.split('/game/')[1]?.split('/')[0]?.toUpperCase() : null;
        const roomMatch = !roomCode || !urlRoomCode || urlRoomCode === roomCode.toUpperCase();

        let isGamePage;
        if (requirePlayingPhase) {
            // Strict mode: require Playing phase AND matching room (for visibility changes)
            isGamePage = isPlayingPhase && roomMatch;
        } else {
            // Relaxed mode: URL or phase check (for page load, before Blazor sets phase)
            isGamePage = (isGamePageByUrl || isPlayingPhase) && roomMatch;
        }

        GameLog.trace('AntiCheat', `Game page check: phase=${isPlayingPhase}, url=${isGamePageByUrl}, roomMatch=${roomMatch} (url=${urlRoomCode}, session=${roomCode}), strict=${requirePlayingPhase}, result=${isGamePage}`);
        return isGamePage;
    }

    _handleViolation(gapMs, session) {
        const durationSeconds = gapMs / 1000;
        session.violationCount = (session.violationCount || 0) + 1;
        session.lastActiveAt = Date.now();
        this._saveSession(session);

        // Capture round number at the time of violation!
        const roundNumber = session.roundNumber || 1;

        GameLog.warn('AntiCheat', `VIOLATION #${session.violationCount} detected: round=${roundNumber}, duration=${durationSeconds.toFixed(2)}s`);

        // Show block overlay IMMEDIATELY (don't wait for Blazor)
        this._showBlockOverlay(session.violationCount, durationSeconds);

        // Try to report to Blazor (may fail if circuit not ready)
        // Pass round number captured at detection time!
        this._tryReportToBlazor(session.roomCode, 'FocusLost', durationSeconds, roundNumber);

        // Resume heartbeat
        this._startHeartbeat();
    }

    // === BLOCK OVERLAY (shown directly in JS) ===

    _showBlockOverlay(violationNumber, durationSeconds) {
        GameLog.debug('AntiCheat', `showBlockOverlay: violation=${violationNumber}, duration=${durationSeconds}s`);

        // Calculate block duration based on violation number
        const blockSeconds = this._getBlockDuration(violationNumber);
        const isWarning = violationNumber === 1;
        const penalty = this._getPenalty(violationNumber);

        // Create overlay if doesn't exist or was removed from DOM
        if (!this._blockOverlay || !document.body.contains(this._blockOverlay)) {
            // Remove old reference if exists
            if (this._blockOverlay) {
                this._blockOverlay.remove();
            }
            this._blockOverlay = document.createElement('div');
            this._blockOverlay.id = 'anticheat-block-overlay';
            document.body.appendChild(this._blockOverlay);
            GameLog.debug('AntiCheat', 'Block overlay created');
        }

        const bgClass = isWarning ? 'bg-warning' : 'bg-block';
        const textColor = isWarning ? '#78350f' : 'white';
        const title = isWarning ? 'Ostrzeżenie' : 'Kara czasowa';
        const message = isWarning
            ? 'Pozostań w grze! Kolejne naruszenia spowodują kary punktowe.'
            : `Opuściłeś grę podczas rundy. Kara: ${penalty} pkt i blokada na ${blockSeconds}s.`;
        const icon = isWarning ? 'warning' : 'block';
        const circleColor = isWarning ? '#eab308' : 'url(#blockGradient)';
        const bgCircleColor = isWarning ? '#78350f' : '#1e293b';

        this._blockOverlay.className = `anticheat-overlay ${bgClass}`;
        this._blockOverlay.innerHTML = `
            <div class="anticheat-overlay-content">
                <div class="anticheat-circle-wrapper">
                    <svg class="anticheat-circle-svg" viewBox="0 0 100 100">
                        <circle cx="50" cy="50" r="45" fill="none" stroke="${bgCircleColor}" stroke-width="8"/>
                        <circle id="anticheat-progress-circle" cx="50" cy="50" r="45" fill="none" 
                                stroke="${circleColor}" 
                                stroke-width="8" 
                                stroke-linecap="round"
                                stroke-dasharray="283 283"
                                class="anticheat-progress"/>
                        <defs>
                            <linearGradient id="blockGradient" x1="0%" y1="0%" x2="100%" y2="0%">
                                <stop offset="0%" stop-color="#ef4444"/>
                                <stop offset="100%" stop-color="#f97316"/>
                            </linearGradient>
                        </defs>
                    </svg>
                    <div class="anticheat-circle-number" id="anticheat-countdown">${blockSeconds}</div>
                </div>
                <div class="anticheat-icon">
                    <span class="material-symbols-outlined" style="font-size: 48px; color: ${isWarning ? '#854d0e' : '#ef4444'}">${icon}</span>
                </div>
                <h2 style="color: ${textColor}; font-size: 24px; font-weight: 900; margin: 0;">${title}</h2>
                <p style="color: ${textColor}; opacity: 0.9; font-size: 14px; margin: 8px 0 0 0; text-align: center; max-width: 280px;">${message}</p>
                <div class="anticheat-badge ${isWarning ? 'badge-warning' : 'badge-block'}">
                    <span class="material-symbols-outlined" style="font-size: 14px;">${isWarning ? 'info' : 'warning'}</span>
                    <span>Naruszenie nr ${violationNumber}</span>
                </div>
            </div>
        `;

        this._blockOverlay.style.display = 'flex';

        // Countdown timer with progress animation
        let remaining = blockSeconds;
        const countdownEl = document.getElementById('anticheat-countdown');
        const progressCircle = document.getElementById('anticheat-progress-circle');
        const circumference = 2 * Math.PI * 45; // 283

        const countdownInterval = setInterval(() => {
            remaining--;
            if (countdownEl) countdownEl.textContent = remaining;

            // Update circle progress
            if (progressCircle) {
                const progress = remaining / blockSeconds;
                const dashLength = progress * circumference;
                progressCircle.style.strokeDasharray = `${dashLength} ${circumference}`;
            }

            if (remaining <= 0) {
                clearInterval(countdownInterval);
                // Wait for animation to complete before hiding
                setTimeout(() => this._hideBlockOverlay(), 900);
            }
        }, 1000);
    }

    _hideBlockOverlay() {
        if (this._blockOverlay) {
            this._blockOverlay.style.display = 'none';
        }
    }

    _getBlockDuration(violationNumber) {
        // Match GameRound.razor StartBlockPenalty values
        switch (violationNumber) {
            case 1: return 2;
            case 2: return 3;
            case 3: return 5;
            case 4: return 8;
            default: return 13;
        }
    }

    _getPenalty(violationNumber) {
        // Match server-side Violation.CalculatePenalty
        // Server uses previousViolations.Count (0-based, before adding current violation)
        // JS violationNumber is 1-based (already incremented), so offset by 1
        switch (violationNumber) {
            case 1: return 0;   // 1st = warning only (server: previousCount=0 → 0)
            case 2: return 10;  // 2nd = -10 pkt (server: previousCount=1 → 10)
            case 3: return 20;  // 3rd = -20 pkt (server: previousCount=2 → 20)
            default: return 30; // 4th+ = -30 pkt (server: previousCount=3+ → 30)
        }
    }

    // === BLAZOR COMMUNICATION (single queue-based path) ===

    _tryReportToBlazor(roomCode, violationType, durationSeconds, roundNumber) {
        const id = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`;
        this._addToPendingQueue({ id, roomCode, violationType, durationSeconds, roundNumber, timestamp: Date.now() });
        GameLog.info('AntiCheat', `Violation QUEUED: id=${id}, type=${violationType}, round=${roundNumber}, duration=${durationSeconds.toFixed(2)}s`);

        // Delay processing slightly to allow Blazor circuit/WebSocket to stabilize
        // after mobile resume — visibilitychange fires before WebSocket fully wakes up
        setTimeout(() => this._processQueue(), 500);
        this._startQueueRetry();
    }

    /**
     * Start a self-stopping interval that retries _processQueue every 2s.
     * Stops automatically when the queue is empty or after 30s.
     */
    _startQueueRetry() {
        if (this._queueRetryInterval) return; // already running
        const startedAt = Date.now();
        this._queueRetryInterval = setInterval(() => {
            const queue = this._getPendingQueue();
            if (queue.length === 0 || Date.now() - startedAt > 30000) {
                clearInterval(this._queueRetryInterval);
                this._queueRetryInterval = null;
                if (queue.length > 0) {
                    GameLog.warn('AntiCheat', 'Queue retry timed out, items remain in queue');
                }
                return;
            }
            GameLog.debug('AntiCheat', `Retry: draining queue (${queue.length} pending)`);
            this._processQueue();
        }, 2000);
    }

    _stopQueueRetry() {
        if (this._queueRetryInterval) {
            clearInterval(this._queueRetryInterval);
            this._queueRetryInterval = null;
        }
    }

    // Promise timeout helper — prevents hanging invokeMethodAsync from blocking the queue
    _withTimeout(promise, ms) {
        return Promise.race([
            promise,
            new Promise((_, reject) => setTimeout(() => reject(new Error('Timeout')), ms))
        ]);
    }

    // Single processing loop — guarded against concurrent execution
    async _processQueue() {
        if (this._processingQueue) {
            GameLog.trace('AntiCheat', '_processQueue: already running, skipping');
            return;
        }
        if (!window._antiCheatDotNetRef || !window._antiCheatReady) {
            GameLog.debug('AntiCheat', `_processQueue: Blazor not ready (dotNetRef=${!!window._antiCheatDotNetRef}, ready=${!!window._antiCheatReady})`);
            return;
        }

        this._processingQueue = true;
        GameLog.debug('AntiCheat', '_processQueue: START');
        try {
            const session = this._getSession();
            const currentRoom = session?.roomCode;

            // Work on a snapshot; queue may grow while we're awaiting
            let queue = this._getPendingQueue();
            while (queue.length > 0) {
                const violation = queue[0];

                // Drop stale violations from other rooms
                if (currentRoom && violation.roomCode !== currentRoom) {
                    GameLog.info('AntiCheat', `Dropping stale violation from room ${violation.roomCode}`);
                    this._removeById(violation.id);
                    queue = this._getPendingQueue();
                    continue;
                }

                // Drop very old violations (>5 min)
                if (Date.now() - violation.timestamp > 5 * 60 * 1000) {
                    GameLog.info('AntiCheat', `Dropping old violation (${((Date.now() - violation.timestamp) / 1000).toFixed(0)}s ago)`);
                    this._removeById(violation.id);
                    queue = this._getPendingQueue();
                    continue;
                }

                try {
                    GameLog.debug('AntiCheat', `_processQueue: invoking ReportViolationFromJS for id=${violation.id}, type=${violation.violationType}, round=${violation.roundNumber}`);
                    // 5s timeout prevents hanging promise from blocking the queue forever
                    // (mobile WebSocket may be in a frozen state after resume)
                    const reported = await this._withTimeout(
                        window._antiCheatDotNetRef.invokeMethodAsync(
                            'ReportViolationFromJS',
                            violation.violationType,
                            violation.durationSeconds,
                            violation.roundNumber || 1),
                        5000);

                    if (reported) {
                        GameLog.info('AntiCheat', `Violation REPORTED to server: id=${violation.id}, round=${violation.roundNumber}`);
                        this._removeById(violation.id);
                    } else {
                        GameLog.warn('AntiCheat', `Violation REJECTED by Blazor: id=${violation.id} — keeping in queue`);
                        break;
                    }
                } catch (err) {
                    GameLog.warn('AntiCheat', `Report FAILED: id=${violation.id}, error=${err.message}, name=${err.name} — will retry later`);
                    break;
                }

                queue = this._getPendingQueue();
            }
        } finally {
            this._processingQueue = false;
            GameLog.debug('AntiCheat', `_processQueue: END, remaining=${this._getPendingQueue().length}`);
        }
    }

    // === PENDING QUEUE ===

    _getPendingQueue() {
        try {
            const data = localStorage.getItem('anticheat_pending');
            return data ? JSON.parse(data) : [];
        } catch (e) {
            GameLog.error('AntiCheat', 'Error reading pending queue:', e);
            return [];
        }
    }

    _savePendingQueue(queue) {
        try {
            localStorage.setItem('anticheat_pending', JSON.stringify(queue));
        } catch (e) {
            GameLog.error('AntiCheat', 'Error saving pending queue:', e);
        }
    }

    _addToPendingQueue(violation) {
        const queue = this._getPendingQueue();
        queue.push(violation);
        this._savePendingQueue(queue);
        GameLog.trace('AntiCheat', `Queue size: ${queue.length}`);
    }

    _removeById(id) {
        const queue = this._getPendingQueue().filter(v => v.id !== id);
        this._savePendingQueue(queue);
    }

    _clearPendingQueue() {
        try {
            localStorage.removeItem('anticheat_pending');
        } catch (e) {
            GameLog.error('AntiCheat', 'Error clearing pending queue:', e);
        }
    }

    // === STORAGE ===

    _getSession() {
        try {
            const data = localStorage.getItem(this._sessionKey);
            return data ? JSON.parse(data) : null;
        } catch (e) {
            GameLog.error('AntiCheat', 'Error reading session:', e);
            return null;
        }
    }

    _saveSession(session) {
        try {
            localStorage.setItem(this._sessionKey, JSON.stringify(session));
        } catch (e) {
            GameLog.error('AntiCheat', 'Error saving session:', e);
        }
    }

    _clearSession() {
        try {
            localStorage.removeItem(this._sessionKey);
        } catch (e) {
            GameLog.error('AntiCheat', 'Error clearing session:', e);
        }
    }
};

// Create global instance
window.antiCheatTracker = new window.AntiCheatTracker();

// Handler registration for Blazor communication
window.registerAntiCheatHandler = function (dotNetHelper) {
    const hadPrevious = !!window._antiCheatDotNetRef;
    window._antiCheatDotNetRef = dotNetHelper;
    window._antiCheatReady = true;
    // NOTE: do NOT reset _processingQueue here. _processQueue uses try/finally and
    // _withTimeout(5s) to guarantee the flag is always cleared — resetting it here
    // breaks the guard if registerAntiCheatHandler is called while _processQueue is
    // already running (e.g. ReconnectAsync + RoundView.OnAfterRenderAsync racing),
    // which causes duplicate violation reports.
    const pending = window.antiCheatTracker._getPendingQueue();
    GameLog.info('AntiCheat', `Blazor handler registered: hadPrevious=${hadPrevious}, pendingViolations=${pending.length}, phase=${window._gamePhase}`);

    // Drain any violations that were queued while Blazor was disconnected
    window.antiCheatTracker._processQueue();
    // Also start retry loop in case _processQueue partially fails
    if (pending.length > 0) {
        GameLog.info('AntiCheat', `Starting queue retry for ${pending.length} pending violations`);
        window.antiCheatTracker._startQueueRetry();
    }
};

window.unregisterAntiCheatHandler = function () {
    window._antiCheatDotNetRef = null;
    window._antiCheatReady = false;
    GameLog.info('AntiCheat', 'Blazor handler unregistered');
};

// ======================================
// Countdown Vignette Pulse (Gaussian bell)
// ======================================

/**
 * Fires a single Gaussian-bell pulse on the #countdown-vignette element.
 * Call once per timer tick while the countdown is active.
 */
window.beatVignette = function () {
    const maxAlpha = 0.45;
    const maxSize = 20;
    const maxSpread = 10;
    const dur = 720;   // ms — must be < 1000 (one tick)
    const sigma = 0.25;  // sharper peak = tighter edge glow

    const el = document.getElementById('countdown-vignette');
    if (!el) return;

    const start = performance.now();

    function frame(now) {
        const x = (now - start) / dur; // 0 → 1
        if (x >= 1) { el.style.boxShadow = 'none'; return; }
        const g = Math.exp(-Math.pow(x - 0.5, 2) / (2 * sigma * sigma));
        const a = (g * maxAlpha).toFixed(3);
        const sz = Math.round(g * maxSize);
        const sp = Math.round(g * maxSpread);
        el.style.boxShadow = `inset 0 0 ${sz}px ${sp}px rgba(180,30,30,${a})`;
        requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
};

/**
 * Clears the vignette effect immediately.
 */
window.clearVignette = function () {
    const el = document.getElementById('countdown-vignette');
    if (el) el.style.boxShadow = 'none';
};

// ======================================
// Swipe Card Voting
// ======================================
// Handles pointer-driven swipe gestures on the voting card.
// Blazor calls swipeCard.init(dotNetRef) after each new card is rendered.
// When the swipe threshold is crossed (or flyOut is called from a button),
// it animates the card off-screen and invokes OnSwipeDecided on the .NET side.
window.swipeCard = (function () {
    let _el = null;
    let _dotNet = null;
    let _startX = 0;
    let _dragX = 0;
    let _active = false;
    let _exiting = false;

    const DRAG_THRESHOLD = 90;   // px to trigger a decision
    const TINT_START = 30;       // px where tint labels start fading in
    const FLY_DISTANCE = 900;    // px to fly off screen
    const EXIT_MS = 220;         // animation duration in ms

    function updateVisuals(x) {
        if (!_el) return;
        _el.style.transform = `translateX(${x}px) rotate(${x * 0.04}deg)`;
        const labelGood = document.getElementById('swipe-label-good');
        const labelBad = document.getElementById('swipe-label-bad');
        if (x > TINT_START) {
            _el.style.setProperty('--tw-border-opacity', '1');
            _el.style.borderColor = 'oklch(72.3% 0.219 149.579)'; // green-500
            if (labelGood) labelGood.style.opacity = Math.min(1, (x - TINT_START) / 60);
            if (labelBad) labelBad.style.opacity = 0;
        } else if (x < -TINT_START) {
            _el.style.borderColor = 'oklch(63.7% 0.237 25.331)'; // red-500
            if (labelBad) labelBad.style.opacity = Math.min(1, (-x - TINT_START) / 60);
            if (labelGood) labelGood.style.opacity = 0;
        } else {
            _el.style.borderColor = '';
            if (labelGood) labelGood.style.opacity = 0;
            if (labelBad) labelBad.style.opacity = 0;
        }
    }

    function flyOut(voteType) {
        if (!_el || _exiting) return;
        _exiting = true;
        const offset = voteType === 'valid' ? FLY_DISTANCE : -FLY_DISTANCE;
        _el.style.transition = `transform ${EXIT_MS}ms ease`;
        _el.style.transform = `translateX(${offset}px) rotate(${offset * 0.04}deg)`;
        // Fully reveal the tint label before flying off
        const labelGood = document.getElementById('swipe-label-good');
        const labelBad = document.getElementById('swipe-label-bad');
        if (voteType === 'valid' && labelGood) labelGood.style.opacity = 1;
        if (voteType === 'invalid' && labelBad) labelBad.style.opacity = 1;
        setTimeout(() => {
            if (_dotNet) {
                _dotNet.invokeMethodAsync('OnSwipeDecided', voteType).catch(() => {});
            }
        }, EXIT_MS);
    }

    function onDown(e) {
        if (_exiting) return;
        _startX = e.clientX;
        _dragX = 0;
        _active = true;
        _el.setPointerCapture(e.pointerId);
        _el.style.transition = 'none';
    }

    function onMove(e) {
        if (!_active || _exiting) return;
        _dragX = e.clientX - _startX;
        updateVisuals(_dragX);
    }

    function onUp() {
        if (!_active) return;
        _active = false;
        if (_dragX > DRAG_THRESHOLD) {
            flyOut('valid');
        } else if (_dragX < -DRAG_THRESHOLD) {
            flyOut('invalid');
        } else {
            // Snap back
            _el.style.transition = `transform ${EXIT_MS}ms ease`;
            updateVisuals(0);
        }
    }

    function cleanup() {
        if (_el) {
            _el.removeEventListener('pointerdown', onDown);
            _el.removeEventListener('pointermove', onMove);
            _el.removeEventListener('pointerup', onUp);
            _el.removeEventListener('pointercancel', onUp);
            _el = null;
        }
    }

    return {
        init: function (dotNetRef) {
            cleanup();
            _dotNet = dotNetRef;
            _exiting = false;
            _active = false;
            _dragX = 0;
            _el = document.getElementById('swipe-card');
            if (!_el) return;
            _el.style.transform = '';
            _el.style.borderColor = '';
            _el.style.transition = 'none';
            _el.addEventListener('pointerdown', onDown);
            _el.addEventListener('pointermove', onMove);
            _el.addEventListener('pointerup', onUp);
            _el.addEventListener('pointercancel', onUp);
        },
        flyOut: function (voteType) {
            flyOut(voteType);
        },
        dispose: function () {
            cleanup();
            _dotNet = null;
        }
    };
})();
