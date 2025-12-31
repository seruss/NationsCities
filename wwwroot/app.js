// Państwa, Miasta - JavaScript Interop
// ======================================

/**
 * Copies text to clipboard using modern Clipboard API with fallback
 * @param {string} text - Text to copy to clipboard
 * @returns {Promise<boolean>} - True if successful, false otherwise
 */
window.copyToClipboard = async function (text) {
    try {
        // Try modern Clipboard API first
        if (navigator.clipboard && window.isSecureContext) {
            await navigator.clipboard.writeText(text);
            return true;
        } else {
            // Fallback for older browsers or non-secure contexts
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
// Anti-Cheat System
// ======================================

/**
 * Anti-cheat tracker for detecting focus loss, tab switches, and suspicious behavior
 * Designed for mobile browsers with adaptive thresholds
 */
window.AntiCheatTracker = class {
    constructor() {
        this._isTracking = false;
        this._roomCode = null;
        this._violationStartTime = null;
        this._isCurrentlyViolating = false;
        this._lastVisibilityState = document.visibilityState;
        this._totalViolations = 0;

        // Thresholds (in milliseconds)
        this.NOTICE_THRESHOLD = 2000;      // < 2s = notice only
        this.WARNING_THRESHOLD = 10000;    // 2-10s = warning
        this.PENALTY_THRESHOLD = 30000;    // > 30s = severe penalty

        // Storage key for persisting violation start time across browser suspend
        this._storageKey = 'anticheat_violation_start';

        // Bind event handlers
        this._handleVisibilityChange = this._handleVisibilityChange.bind(this);
        this._handleBlur = this._handleBlur.bind(this);
        this._handleFocus = this._handleFocus.bind(this);
        this._handlePageHide = this._handlePageHide.bind(this);
        this._handlePageShow = this._handlePageShow.bind(this);
    }

    /**
     * Start tracking violations for a room
     * @param {string} roomCode - Room code
     */
    startTracking(roomCode) {
        if (this._isTracking) {
            console.warn('[AntiCheat] Already tracking');
            return;
        }

        this._roomCode = roomCode;
        this._isTracking = true;

        // Check if we were in a violation before page suspend (mobile)
        this._checkSuspendedViolation();

        // Page Visibility API (primary method for mobile)
        document.addEventListener('visibilitychange', this._handleVisibilityChange);

        // Window blur/focus (backup for tab switches)
        window.addEventListener('blur', this._handleBlur);
        window.addEventListener('focus', this._handleFocus);

        // Page lifecycle events (mobile browser suspend/resume)
        window.addEventListener('pagehide', this._handlePageHide);
        window.addEventListener('pageshow', this._handlePageShow);

        console.log(`[AntiCheat] Tracking started for room ${roomCode}`);
    }

    /**
     * Stop tracking violations
     */
    stopTracking() {
        if (!this._isTracking) {
            return;
        }

        // Finalize any ongoing violation
        if (this._isCurrentlyViolating) {
            this._endViolation('FocusLost');
        }

        // Clear stored violation time
        this._clearStoredViolation();

        // Remove event listeners
        document.removeEventListener('visibilitychange', this._handleVisibilityChange);
        window.removeEventListener('blur', this._handleBlur);
        window.removeEventListener('focus', this._handleFocus);
        window.removeEventListener('pagehide', this._handlePageHide);
        window.removeEventListener('pageshow', this._handlePageShow);

        this._isTracking = false;
        this._roomCode = null;
        this._totalViolations = 0;

        console.log('[AntiCheat] Tracking stopped');
    }

    /**
     * Check if there was a violation in progress when the page was suspended
     */
    _checkSuspendedViolation() {
        try {
            const stored = localStorage.getItem(this._storageKey);
            if (stored) {
                const data = JSON.parse(stored);
                const suspendTime = data.timestamp;
                const now = Date.now();
                const durationMs = now - suspendTime;

                console.log(`[AntiCheat] Found suspended violation, duration: ${(durationMs / 1000).toFixed(2)}s`);

                // Clear stored data
                this._clearStoredViolation();

                // Report the violation if significant
                if (durationMs > this.NOTICE_THRESHOLD) {
                    this._reportViolation('FocusLost', durationMs / 1000);
                }
            }
        } catch (e) {
            console.error('[AntiCheat] Error checking suspended violation:', e);
        }
    }

    /**
     * Store violation start time in localStorage (survives browser suspend)
     */
    _storeViolationStart() {
        try {
            localStorage.setItem(this._storageKey, JSON.stringify({
                timestamp: Date.now(),
                roomCode: this._roomCode
            }));
        } catch (e) {
            console.error('[AntiCheat] Error storing violation start:', e);
        }
    }

    /**
     * Clear stored violation data
     */
    _clearStoredViolation() {
        try {
            localStorage.removeItem(this._storageKey);
        } catch (e) {
            console.error('[AntiCheat] Error clearing stored violation:', e);
        }
    }

    /**
     * Handle page hide (more reliable than visibilitychange on some mobile browsers)
     */
    _handlePageHide(event) {
        if (!this._isTracking) return;

        console.log('[AntiCheat] Page hide event');

        if (!this._isCurrentlyViolating) {
            this._startViolation();
        }

        // Store in localStorage so we can detect duration after resume
        this._storeViolationStart();
    }

    /**
     * Handle page show (fires when returning from bfcache or suspend)
     */
    _handlePageShow(event) {
        if (!this._isTracking) return;

        console.log(`[AntiCheat] Page show event, persisted: ${event.persisted}`);

        // If coming from bfcache (frozen state), check for suspended violation
        if (event.persisted) {
            this._checkSuspendedViolation();
        }

        // End any ongoing violation
        if (this._isCurrentlyViolating) {
            this._endViolation('FocusLost');
        }
    }

    /**
     * Handle page visibility change (mobile-friendly)
     */
    _handleVisibilityChange() {
        if (!this._isTracking) return;

        const isHidden = document.hidden;

        if (isHidden && !this._isCurrentlyViolating) {
            // Page became hidden - start tracking violation
            this._startViolation();
            this._lastVisibilityState = 'hidden';
        } else if (!isHidden && this._isCurrentlyViolating) {
            // Page became visible again - end violation
            this._endViolation('FocusLost');
            this._lastVisibilityState = 'visible';
        }
    }

    /**
     * Handle window blur (tab switch or minimize)
     */
    _handleBlur() {
        if (!this._isTracking || this._isCurrentlyViolating) return;

        // Only start violation if page is still "visible" according to Page Visibility API
        // This prevents double-counting when both events fire
        if (!document.hidden) {
            this._startViolation();
        }
    }

    /**
     * Handle window focus (tab restored)
     */
    _handleFocus() {
        if (!this._isTracking || !this._isCurrentlyViolating) return;

        this._endViolation('TabSwitch');
    }

    /**
     * Start tracking a violation
     */
    _startViolation() {
        this._violationStartTime = performance.now();
        this._isCurrentlyViolating = true;

        // Also store in localStorage for mobile suspend detection
        this._storeViolationStart();

        console.log('[AntiCheat] Violation started');
    }

    /**
     * End tracking a violation and report to server
     * @param {string} violationType - Type of violation (FocusLost, TabSwitch)
     */
    _endViolation(violationType) {
        if (!this._violationStartTime) return;

        const durationMs = performance.now() - this._violationStartTime;
        const durationSeconds = durationMs / 1000;

        // Reset state
        this._isCurrentlyViolating = false;
        this._violationStartTime = null;
        this._totalViolations++;

        // Clear stored violation data
        this._clearStoredViolation();

        console.log(`[AntiCheat] Violation ended: ${violationType}, duration: ${durationSeconds.toFixed(2)}s`);

        // Report to server (even short violations, server decides penalty)
        this._reportViolation(violationType, durationSeconds);

        // Show immediate UI feedback if above notice threshold
        if (durationMs >= this.NOTICE_THRESHOLD) {
            this._showViolationFeedback(violationType, durationSeconds);
        }
    }

    /**
     * Report violation via custom event (Blazor will handle hub call)
     */
    _reportViolation(violationType, durationSeconds) {
        if (!this._roomCode) {
            console.error('[AntiCheat] Cannot report - no room code');
            return;
        }

        // Dispatch event for Blazor to handle
        const event = new CustomEvent('anticheat-report', {
            detail: {
                roomCode: this._roomCode,
                violationType: violationType,
                durationSeconds: durationSeconds
            }
        });
        window.dispatchEvent(event);

        console.log(`[AntiCheat] Violation event dispatched: ${violationType}, ${durationSeconds.toFixed(2)}s`);
    }

    /**
     * Show visual feedback to player
     */
    _showViolationFeedback(violationType, durationSeconds) {
        let severity = 'notice';
        let message = '⚠️ Utrata fokusu wykryta';
        let penalty = 0;

        // Determine severity based on duration
        if (durationSeconds >= this.PENALTY_THRESHOLD / 1000) {
            severity = 'severe';
            penalty = -15;
            message = `🚨 Długa nieobecność (-${Math.abs(penalty)} pkt)`;
        } else if (durationSeconds >= this.WARNING_THRESHOLD / 1000) {
            severity = 'warning';
            penalty = -10;
            message = `⚠️ Nieobecność wykryta (-${Math.abs(penalty)} pkt)`;
        } else if (durationSeconds >= this.NOTICE_THRESHOLD / 1000) {
            severity = 'warning';
            penalty = -5;
            message = `⚠️ Utrata fokusu (-${Math.abs(penalty)} pkt)`;
        }

        // Dispatch custom event that Blazor components can listen to
        const event = new CustomEvent('anticheat-violation', {
            detail: {
                type: violationType,
                duration: durationSeconds,
                severity: severity,
                message: message,
                penalty: penalty,
                timestamp: new Date().toISOString()
            }
        });
        window.dispatchEvent(event);
    }

    /**
     * Get current tracking status
     */
    isTracking() {
        return this._isTracking;
    }

    /**
     * Get total violations in current session
     */
    getTotalViolations() {
        return this._totalViolations;
    }
};

// Create global instance
window.antiCheatTracker = new window.AntiCheatTracker();

/**
 * Register the anti-cheat event handler with a DotNetObjectReference
 * This must be called from Blazor to properly pass the reference
 * @param {object} dotNetHelper - DotNetObjectReference from Blazor
 */
window.registerAntiCheatHandler = function (dotNetHelper) {
    // Remove any existing handler first
    if (window._antiCheatHandler) {
        window.removeEventListener('anticheat-report', window._antiCheatHandler);
    }

    // Create new handler with the dotNetHelper
    window._antiCheatHandler = async (e) => {
        const report = e.detail;
        try {
            await dotNetHelper.invokeMethodAsync('ReportViolationFromJS',
                report.violationType,
                report.durationSeconds);
        } catch (err) {
            console.error('[AntiCheat] Callback failed:', err);
        }
    };

    window.addEventListener('anticheat-report', window._antiCheatHandler);
    console.log('[AntiCheat] Handler registered');
};

/**
 * Unregister the anti-cheat event handler
 */
window.unregisterAntiCheatHandler = function () {
    if (window._antiCheatHandler) {
        window.removeEventListener('anticheat-report', window._antiCheatHandler);
        window._antiCheatHandler = null;
        console.log('[AntiCheat] Handler unregistered');
    }
};
