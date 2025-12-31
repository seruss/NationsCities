// Państwa, Miasta - JavaScript Interop
// ======================================

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
// Anti-Cheat System
// ======================================

window.AntiCheatTracker = class {
    constructor() {
        this._isTracking = false;
        this._roomCode = null;
        this._violationStartTime = null;
        this._isCurrentlyViolating = false;
        this._lastVisibilityState = document.visibilityState;
        this._totalViolations = 0;

        this.NOTICE_THRESHOLD = 2000;
        this.WARNING_THRESHOLD = 10000;
        this.PENALTY_THRESHOLD = 30000;

        this._storageKey = 'anticheat_violation_start';

        this._handleVisibilityChange = this._handleVisibilityChange.bind(this);
        this._handleBlur = this._handleBlur.bind(this);
        this._handleFocus = this._handleFocus.bind(this);
        this._handlePageHide = this._handlePageHide.bind(this);
        this._handlePageShow = this._handlePageShow.bind(this);
    }

    startTracking(roomCode) {
        if (this._isTracking) {
            console.warn('[AntiCheat] Already tracking');
            return;
        }

        this._roomCode = roomCode;
        this._isTracking = true;

        this._checkSuspendedViolation();

        document.addEventListener('visibilitychange', this._handleVisibilityChange);
        window.addEventListener('blur', this._handleBlur);
        window.addEventListener('focus', this._handleFocus);
        window.addEventListener('pagehide', this._handlePageHide);
        window.addEventListener('pageshow', this._handlePageShow);

        console.log(`[AntiCheat] Tracking started for room ${roomCode}`);
    }

    stopTracking() {
        if (!this._isTracking) {
            return;
        }

        if (this._isCurrentlyViolating) {
            this._endViolation('FocusLost');
        }

        this._clearStoredViolation();

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

    _checkSuspendedViolation() {
        try {
            const stored = localStorage.getItem(this._storageKey);
            if (stored) {
                const data = JSON.parse(stored);
                const suspendTime = data.timestamp;
                const now = Date.now();
                const durationMs = now - suspendTime;

                console.log(`[AntiCheat] Found suspended violation, duration: ${(durationMs / 1000).toFixed(2)}s`);

                this._clearStoredViolation();

                if (durationMs > this.NOTICE_THRESHOLD) {
                    this._reportViolation('FocusLost', durationMs / 1000);
                }
            }
        } catch (e) {
            console.error('[AntiCheat] Error checking suspended violation:', e);
        }
    }

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

    _clearStoredViolation() {
        try {
            localStorage.removeItem(this._storageKey);
        } catch (e) {
            console.error('[AntiCheat] Error clearing stored violation:', e);
        }
    }

    _handlePageHide(event) {
        if (!this._isTracking) return;

        console.log('[AntiCheat] Page hide event');

        if (!this._isCurrentlyViolating) {
            this._startViolation();
        }

        this._storeViolationStart();
    }

    _handlePageShow(event) {
        if (!this._isTracking) return;

        console.log(`[AntiCheat] Page show event, persisted: ${event.persisted}`);

        if (event.persisted) {
            this._checkSuspendedViolation();
        }

        if (this._isCurrentlyViolating) {
            this._endViolation('FocusLost');
        }
    }

    _handleVisibilityChange() {
        if (!this._isTracking) return;

        const isHidden = document.hidden;

        if (isHidden && !this._isCurrentlyViolating) {
            this._startViolation();
            this._lastVisibilityState = 'hidden';
        } else if (!isHidden && this._isCurrentlyViolating) {
            this._endViolation('FocusLost');
            this._lastVisibilityState = 'visible';
        }
    }

    _handleBlur() {
        if (!this._isTracking || this._isCurrentlyViolating) return;

        if (!document.hidden) {
            this._startViolation();
        }
    }

    _handleFocus() {
        if (!this._isTracking || !this._isCurrentlyViolating) return;

        this._endViolation('TabSwitch');
    }

    _startViolation() {
        this._violationStartTime = performance.now();
        this._isCurrentlyViolating = true;

        this._storeViolationStart();

        console.log('[AntiCheat] Violation started');
    }

    _endViolation(violationType) {
        if (!this._violationStartTime) return;

        const durationMs = performance.now() - this._violationStartTime;
        const durationSeconds = durationMs / 1000;

        this._isCurrentlyViolating = false;
        this._violationStartTime = null;
        this._totalViolations++;

        this._clearStoredViolation();

        console.log(`[AntiCheat] Violation ended: ${violationType}, duration: ${durationSeconds.toFixed(2)}s`);

        this._reportViolation(violationType, durationSeconds);

        if (durationMs >= this.NOTICE_THRESHOLD) {
            this._showViolationFeedback(violationType, durationSeconds);
        }
    }

    _reportViolation(violationType, durationSeconds) {
        // Safety guard: don't report if tracking has been stopped
        if (!this._isTracking) {
            console.warn('[AntiCheat] Cannot report - tracking stopped');
            return;
        }

        if (!this._roomCode) {
            console.error('[AntiCheat] Cannot report - no room code');
            return;
        }

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

    _showViolationFeedback(violationType, durationSeconds) {
        let severity = 'notice';
        let message = '⚠️ Utrata fokusu wykryta';
        let penalty = 0;

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

    isTracking() {
        return this._isTracking;
    }

    getTotalViolations() {
        return this._totalViolations;
    }
};

// Create global instance
window.antiCheatTracker = new window.AntiCheatTracker();

window.registerAntiCheatHandler = function (dotNetHelper) {
    if (window._antiCheatHandler) {
        window.removeEventListener('anticheat-report', window._antiCheatHandler);
    }

    // Store reference for cleanup
    window._antiCheatDotNetRef = dotNetHelper;

    window._antiCheatHandler = async (e) => {
        // Safety guard: verify tracking is still active
        if (!window.antiCheatTracker || !window.antiCheatTracker.isTracking()) {
            console.warn('[AntiCheat] Handler called but tracking is stopped - ignoring');
            return;
        }

        // Safety guard: verify we still have a valid .NET reference
        if (!window._antiCheatDotNetRef) {
            console.warn('[AntiCheat] Handler called but .NET reference is null - ignoring');
            return;
        }

        const report = e.detail;
        try {
            await window._antiCheatDotNetRef.invokeMethodAsync('ReportViolationFromJS',
                report.violationType,
                report.durationSeconds);
        } catch (err) {
            // Likely the Blazor circuit is disposed - stop tracking to prevent further errors
            console.error('[AntiCheat] Callback failed, stopping tracking:', err.message);
            window.antiCheatTracker?.stopTracking();
        }
    };

    window.addEventListener('anticheat-report', window._antiCheatHandler);
    console.log('[AntiCheat] Handler registered');
};

window.unregisterAntiCheatHandler = function () {
    if (window._antiCheatHandler) {
        window.removeEventListener('anticheat-report', window._antiCheatHandler);
        window._antiCheatHandler = null;
    }
    // Clear the .NET reference to prevent stale calls
    window._antiCheatDotNetRef = null;
    console.log('[AntiCheat] Handler unregistered');
};
