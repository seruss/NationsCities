// Państwa, Miasta - JavaScript Interop
// ======================================

// NOTE: ThemeManager is now defined inline in App.razor <head> 
// so it's available immediately for onclick handlers.

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
            sessionId: sessionId,
            roomCode: roomCode,
            nickname: nickname,
            savedAt: Date.now()
        };
        localStorage.setItem(this._SESSION_KEY, JSON.stringify(data));
        console.log('[GameSession] Saved:', data);
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
                console.log('[GameSession] Session expired, clearing');
                this.clear();
                return null;
            }
            console.log('[GameSession] Loaded:', session);
            return session;
        } catch (e) {
            console.error('[GameSession] Error loading:', e);
            return null;
        }
    },

    /**
     * Clear saved game session
     */
    clear: function () {
        localStorage.removeItem(this._SESSION_KEY);
        localStorage.removeItem(this._TAB_KEY);
        console.log('[GameSession] Cleared');
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
                    console.warn('[GameSession] Could not notify Blazor of back button:', err);
                }
            }
        });

        // Push initial state to enable popstate detection
        history.pushState(null, '', window.location.href);
        console.log('[GameSession] Navigation guard setup complete');
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
                    console.warn('[GameSession] Error checking tab:', err);
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
        console.warn('[GameSession] Duplicate tab detected!');

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
                console.warn('[GameSession] Could not notify Blazor of duplicate tab:', err);
            }
        }
    }
};

/**
 * Sync game phase from Blazor for anti-cheat and navigation guard
 */
window.setGamePhase = function (phase) {
    window._gamePhase = phase;
    console.log(`[GameFlow] Phase changed to: ${phase}`);
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
        console.error('Failed to copy text: ', err);
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
        console.log(`[AntiCheat] Tracking started for room ${roomCode}, round ${roundNumber}, violations: ${existingViolationCount}`);
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
            console.log(`[AntiCheat] Tracking resumed, round ${session.roundNumber}, violations: ${session.violationCount}`);
            
            // Drain any pending violations queued while disconnected
            this._processQueue();
        } else if (roomCode) {
            // No session to resume - start fresh (fallback)
            console.log('[AntiCheat] No session to resume, starting fresh');
            this.startTracking(roomCode, roundNumber || 1);
        } else {
            console.log('[AntiCheat] No session to resume and no roomCode provided');
        }
    }

    stopTracking() {
        this._stopHeartbeat();
        this._clearSession();
        this._hideBlockOverlay();
        console.log('[AntiCheat] Tracking stopped');
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
        console.log('[AntiCheat] Tracking paused (answers submitted)');
    }

    // Clear session completely - call when returning to lobby between games
    clearSession() {
        this._stopHeartbeat();
        this._clearSession();
        this._clearPendingQueue(); // Clear pending violations too
        this._hideBlockOverlay();
        console.log('[AntiCheat] Session cleared (new game will start fresh)');
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
        console.log('[AntiCheat] Heartbeat started');
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
            console.log('[AntiCheat] Page load: Not on game page, clearing stale session');
            this._clearSession();
            return;
        }

        const gap = Date.now() - session.lastActiveAt;
        console.log(`[AntiCheat] Page load check: gap = ${(gap / 1000).toFixed(2)}s`);

        // If gap is too large (>30 min), session is stale - clear it
        if (gap > 30 * 60 * 1000) {
            console.log('[AntiCheat] Session is stale (>30 min), clearing');
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
                console.log('[AntiCheat] Page hidden, timestamp stored');
            }
        } else {
            // Page became visible - check for violation
            console.log('[AntiCheat] Page visible, checking for violations');

            if (!session || !session.isActive) {
                console.log('[AntiCheat] No active session');
                return;
            }

            // Only process violations on game round page
            if (!this._isOnGameRoundPage(session.roomCode)) {
                console.log('[AntiCheat] Not on game round page, skipping');
                return;
            }

            // Calculate gap from hiddenAt (preferred) or lastActiveAt (fallback for mobile)
            const hiddenAt = session.hiddenAt || session.lastActiveAt;
            const gap = Date.now() - hiddenAt;
            console.log(`[AntiCheat] Absence duration: ${(gap / 1000).toFixed(2)}s`);

            // Clear hiddenAt
            session.hiddenAt = null;
            session.lastActiveAt = Date.now();
            this._saveSession(session);

            if (gap > this.NOTICE_THRESHOLD_MS) {
                this._handleViolation(gap, session);
            }

            // Resume heartbeat
            this._startHeartbeat();
        }
    }

    _isOnGameRoundPage(roomCode) {
        // Check URL first (for initial page load / deep links)
        const path = window.location.pathname.toLowerCase();
        const isGamePageByUrl = path.startsWith('/game/');

        // Check SPA state (for in-app phase changes)
        const isGamePageByPhase = window._gamePhase === 'Playing';

        const isGamePage = isGamePageByUrl || isGamePageByPhase;
        console.log(`[AntiCheat] Game page check: url=${isGamePageByUrl}, phase=${isGamePageByPhase}, result=${isGamePage}`);
        return isGamePage;
    }

    _handleViolation(gapMs, session) {
        const durationSeconds = gapMs / 1000;
        session.violationCount = (session.violationCount || 0) + 1;
        session.lastActiveAt = Date.now();
        this._saveSession(session);

        // Capture round number at the time of violation!
        const roundNumber = session.roundNumber || 1;

        console.log(`[AntiCheat] Violation #${session.violationCount} detected in round ${roundNumber}: ${durationSeconds.toFixed(2)}s`);

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
        console.log(`[AntiCheat] _showBlockOverlay called: violation=${violationNumber}, duration=${durationSeconds}s`);

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
            console.log('[AntiCheat] Block overlay created');
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
        console.log(`[AntiCheat] Violation queued (id=${id}): ${violationType}, round ${roundNumber}, ${durationSeconds.toFixed(2)}s`);

        // Try to drain the queue immediately
        this._processQueue();
    }

    // Single processing loop — guarded against concurrent execution
    async _processQueue() {
        if (this._processingQueue) return; // already running
        if (!window._antiCheatDotNetRef || !window._antiCheatReady) return; // Blazor not connected

        this._processingQueue = true;
        try {
            const session = this._getSession();
            const currentRoom = session?.roomCode;

            // Work on a snapshot; queue may grow while we're awaiting
            let queue = this._getPendingQueue();
            while (queue.length > 0) {
                const violation = queue[0];

                // Drop stale violations from other rooms
                if (currentRoom && violation.roomCode !== currentRoom) {
                    console.log(`[AntiCheat] Dropping stale violation from room ${violation.roomCode}`);
                    this._removeById(violation.id);
                    queue = this._getPendingQueue();
                    continue;
                }

                // Drop very old violations (>5 min)
                if (Date.now() - violation.timestamp > 5 * 60 * 1000) {
                    console.log(`[AntiCheat] Dropping old violation (${((Date.now() - violation.timestamp) / 1000).toFixed(0)}s ago)`);
                    this._removeById(violation.id);
                    queue = this._getPendingQueue();
                    continue;
                }

                try {
                    const reported = await window._antiCheatDotNetRef.invokeMethodAsync(
                        'ReportViolationFromJS',
                        violation.violationType,
                        violation.durationSeconds,
                        violation.roundNumber || 1);

                    if (reported) {
                        console.log(`[AntiCheat] Violation ${violation.id} (round ${violation.roundNumber}) reported to server`);
                        this._removeById(violation.id);
                    } else {
                        console.warn(`[AntiCheat] Violation ${violation.id} rejected by Blazor — keeping in queue`);
                        break; // Don't spin on a rejection; wait for next trigger
                    }
                } catch (err) {
                    console.warn(`[AntiCheat] Report failed (${err.message}) — will retry later`);
                    break; // Circuit probably reconnecting; stop processing
                }

                queue = this._getPendingQueue();
            }
        } finally {
            this._processingQueue = false;
        }
    }

    // === PENDING QUEUE ===

    _getPendingQueue() {
        try {
            const data = localStorage.getItem('anticheat_pending');
            return data ? JSON.parse(data) : [];
        } catch (e) {
            console.error('[AntiCheat] Error reading pending queue:', e);
            return [];
        }
    }

    _savePendingQueue(queue) {
        try {
            localStorage.setItem('anticheat_pending', JSON.stringify(queue));
        } catch (e) {
            console.error('[AntiCheat] Error saving pending queue:', e);
        }
    }

    _addToPendingQueue(violation) {
        const queue = this._getPendingQueue();
        queue.push(violation);
        this._savePendingQueue(queue);
        console.log(`[AntiCheat] Queue size: ${queue.length}`);
    }

    _removeById(id) {
        const queue = this._getPendingQueue().filter(v => v.id !== id);
        this._savePendingQueue(queue);
    }

    _clearPendingQueue() {
        try {
            localStorage.removeItem('anticheat_pending');
        } catch (e) {
            console.error('[AntiCheat] Error clearing pending queue:', e);
        }
    }

    // === STORAGE ===

    _getSession() {
        try {
            const data = localStorage.getItem(this._sessionKey);
            return data ? JSON.parse(data) : null;
        } catch (e) {
            console.error('[AntiCheat] Error reading session:', e);
            return null;
        }
    }

    _saveSession(session) {
        try {
            localStorage.setItem(this._sessionKey, JSON.stringify(session));
        } catch (e) {
            console.error('[AntiCheat] Error saving session:', e);
        }
    }

    _clearSession() {
        try {
            localStorage.removeItem(this._sessionKey);
        } catch (e) {
            console.error('[AntiCheat] Error clearing session:', e);
        }
    }
};

// Create global instance
window.antiCheatTracker = new window.AntiCheatTracker();

// Handler registration for Blazor communication
window.registerAntiCheatHandler = function (dotNetHelper) {
    window._antiCheatDotNetRef = dotNetHelper;
    window._antiCheatReady = true;
    console.log('[AntiCheat] Blazor handler registered — draining queue');

    // Drain any violations that were queued while Blazor was disconnected
    window.antiCheatTracker._processQueue();
};

window.unregisterAntiCheatHandler = function () {
    window._antiCheatDotNetRef = null;
    window._antiCheatReady = false;
    console.log('[AntiCheat] Blazor handler unregistered');
};
