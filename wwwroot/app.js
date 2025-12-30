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

        // Bind event handlers
        this._handleVisibilityChange = this._handleVisibilityChange.bind(this);
        this._handleBlur = this._handleBlur.bind(this);
        this._handleFocus = this._handleFocus.bind(this);
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

        // Page Visibility API (primary method for mobile)
        document.addEventListener('visibilitychange', this._handleVisibilityChange);

        // Window blur/focus (backup for tab switches)
        window.addEventListener('blur', this._handleBlur);
        window.addEventListener('focus', this._handleFocus);

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

        // Remove event listeners
        document.removeEventListener('visibilitychange', this._handleVisibilityChange);
        window.removeEventListener('blur', this._handleBlur);
        window.removeEventListener('focus', this._handleFocus);

        this._isTracking = false;
        this._roomCode = null;
        this._totalViolations = 0;

        console.log('[AntiCheat] Tracking stopped');
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
