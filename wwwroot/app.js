// Państwa, Miasta - JavaScript Interop
// ======================================

// ======================================
// Theme Manager (Dark/Light Mode)
// ======================================

window.ThemeManager = class {
    constructor() {
        this._storageKey = 'theme_preference';
        this._init();
    }

    _init() {
        // Get stored preference or default to 'dark'
        const stored = localStorage.getItem(this._storageKey);
        const theme = stored || 'dark';
        this._apply(theme);
    }

    toggle() {
        const current = this.get();
        const next = current === 'dark' ? 'light' : 'dark';
        this._apply(next);
        localStorage.setItem(this._storageKey, next);
        return next;
    }

    get() {
        return document.documentElement.classList.contains('dark') ? 'dark' : 'light';
    }

    set(theme) {
        this._apply(theme);
        localStorage.setItem(this._storageKey, theme);
    }

    _apply(theme) {
        if (theme === 'dark') {
            document.documentElement.classList.add('dark');
        } else {
            document.documentElement.classList.remove('dark');
        }
        // Update meta theme-color for mobile browsers
        const metaTheme = document.querySelector('meta[name="theme-color"]');
        if (metaTheme) {
            metaTheme.setAttribute('content', theme === 'dark' ? '#101922' : '#f5f7f8');
        }
    }
};

// Create global instance
window.themeManager = new window.ThemeManager();

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

    startTracking(roomCode) {
        const existing = this._getSession();

        // If same room and we have an existing session, preserve violation count (for multi-round games)
        const preserveViolations = existing?.roomCode === roomCode;
        const existingViolationCount = preserveViolations ? (existing.violationCount || 0) : 0;

        // Clear old session
        this._clearSession();

        // Create new session - preserve violation count if same room
        this._saveSession({
            roomCode: roomCode,
            isActive: true,
            startedAt: Date.now(),
            lastActiveAt: Date.now(),
            violationCount: existingViolationCount
        });

        this._startHeartbeat();
        console.log(`[AntiCheat] Tracking started for room ${roomCode}, violation count: ${existingViolationCount}`);
    }

    // Resume tracking after pause (for round 2+)
    resumeTracking(roomCode) {
        const session = this._getSession();
        if (session) {
            session.isActive = true;
            session.lastActiveAt = Date.now();
            this._saveSession(session);
            this._startHeartbeat();
            console.log(`[AntiCheat] Tracking resumed, violations: ${session.violationCount}`);
        } else if (roomCode) {
            // No session to resume - start fresh (fallback)
            console.log('[AntiCheat] No session to resume, starting fresh');
            this.startTracking(roomCode);
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
        this._hideBlockOverlay();
        console.log('[AntiCheat] Session cleared (new game will start fresh)');
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
        const path = window.location.pathname.toLowerCase();
        // Check if we're on a game round page (/game/{roomCode})
        const isGamePage = path.startsWith('/game/');
        console.log(`[AntiCheat] URL check: path="${path}", isGamePage=${isGamePage}`);
        return isGamePage;
    }

    _handleViolation(gapMs, session) {
        const durationSeconds = gapMs / 1000;
        session.violationCount = (session.violationCount || 0) + 1;
        session.lastActiveAt = Date.now();
        this._saveSession(session);

        console.log(`[AntiCheat] Violation #${session.violationCount} detected: ${durationSeconds.toFixed(2)}s`);

        // Show block overlay IMMEDIATELY (don't wait for Blazor)
        this._showBlockOverlay(session.violationCount, durationSeconds);

        // Try to report to Blazor (may fail if circuit not ready)
        this._tryReportToBlazor(session.roomCode, 'FocusLost', durationSeconds);

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
            ? 'Pozostań w grze! Kolejne naruszenia spowodują kary czasowe.'
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
        // Match server-side Violation.CalculatePenalty - EVERY violation has penalty
        switch (violationNumber) {
            case 1: return 5;   // 1st = -5 pkt
            case 2: return 10;  // 2nd = -10 pkt
            case 3: return 20;  // 3rd = -20 pkt
            default: return 30; // 4th+ = -30 pkt
        }
    }

    // === BLAZOR COMMUNICATION ===

    _tryReportToBlazor(roomCode, violationType, durationSeconds) {
        // Dispatch event for Blazor handler (if connected)
        const event = new CustomEvent('anticheat-report', {
            detail: { roomCode, violationType, durationSeconds }
        });
        window.dispatchEvent(event);
        console.log(`[AntiCheat] Report event dispatched: ${violationType}, ${durationSeconds.toFixed(2)}s`);
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
    if (window._antiCheatHandler) {
        window.removeEventListener('anticheat-report', window._antiCheatHandler);
    }

    window._antiCheatDotNetRef = dotNetHelper;

    window._antiCheatHandler = async (e) => {
        if (!window._antiCheatDotNetRef) return;

        const report = e.detail;
        try {
            await window._antiCheatDotNetRef.invokeMethodAsync('ReportViolationFromJS',
                report.violationType,
                report.durationSeconds);
            console.log('[AntiCheat] Reported to Blazor successfully');
        } catch (err) {
            console.warn('[AntiCheat] Could not report to Blazor (circuit may be reconnecting):', err.message);
        }
    };

    window.addEventListener('anticheat-report', window._antiCheatHandler);
    console.log('[AntiCheat] Blazor handler registered');
};

window.unregisterAntiCheatHandler = function () {
    if (window._antiCheatHandler) {
        window.removeEventListener('anticheat-report', window._antiCheatHandler);
        window._antiCheatHandler = null;
    }
    window._antiCheatDotNetRef = null;
    console.log('[AntiCheat] Blazor handler unregistered');
};
