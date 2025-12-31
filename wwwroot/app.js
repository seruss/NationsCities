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

window.unregisterAntiCheatHandler = function () {
    if (window._antiCheatHandler) {
        window.removeEventListener('anticheat-report', window._antiCheatHandler);
        window._antiCheatHandler = null;
        console.log('[AntiCheat] Handler unregistered');
    }
};

// ======================================
// ckeyboard Virtual Keyboard (iOS-style)
// ======================================

window._kbDotNetRef = null;
window._kbInitialized = false;
window._currentInputField = null;
window._longPressTimer = null;
window._longPressActive = false;

// Polish character mappings for long-press
window._polishChars = {
    'a': ['ą'],
    'c': ['ć'],
    'e': ['ę'],
    'l': ['ł'],
    'n': ['ń'],
    'o': ['ó'],
    's': ['ś'],
    'z': ['ź', 'ż']
};

window.initVirtualKeyboard = function (dotNetRef) {
    if (window._kbInitialized) {
        console.log('[Keyboard] Already initialized');
        return;
    }

    window._kbDotNetRef = dotNetRef;

    // Configure ckeyboard - STANDARD LAYOUT WITHOUT Polish row
    cKeyboard_config.input_target = '.kb-input';
    cKeyboard_config.interation_mode = 'click';
    cKeyboard_config.target = '#keyboard';
    cKeyboard_config.target_numeric = '#keyboard_numeric';

    // Standard QWERTY layout (no Polish chars row)
    cKeyboard_config.layout = [
        {
            'q': { name: 'q', text: 'q', class: 'cKKey' },
            'w': { name: 'w', text: 'w', class: 'cKKey' },
            'e': { name: 'e', text: 'e', class: 'cKKey' },
            'r': { name: 'r', text: 'r', class: 'cKKey' },
            't': { name: 't', text: 't', class: 'cKKey' },
            'y': { name: 'y', text: 'y', class: 'cKKey' },
            'u': { name: 'u', text: 'u', class: 'cKKey' },
            'i': { name: 'i', text: 'i', class: 'cKKey' },
            'o': { name: 'o', text: 'o', class: 'cKKey' },
            'p': { name: 'p', text: 'p', class: 'cKKey' }
        },
        {
            'a': { name: 'a', text: 'a', class: 'cKKey' },
            's': { name: 's', text: 's', class: 'cKKey' },
            'd': { name: 'd', text: 'd', class: 'cKKey' },
            'f': { name: 'f', text: 'f', class: 'cKKey' },
            'g': { name: 'g', text: 'g', class: 'cKKey' },
            'h': { name: 'h', text: 'h', class: 'cKKey' },
            'j': { name: 'j', text: 'j', class: 'cKKey' },
            'k': { name: 'k', text: 'k', class: 'cKKey' },
            'l': { name: 'l', text: 'l', class: 'cKKey' }
        },
        {
            'shift': { name: 'shift', text: '', class: 'cKFunction' },
            'z': { name: 'z', text: 'z', class: 'cKKey' },
            'x': { name: 'x', text: 'x', class: 'cKKey' },
            'c': { name: 'c', text: 'c', class: 'cKKey' },
            'v': { name: 'v', text: 'v', class: 'cKKey' },
            'b': { name: 'b', text: 'b', class: 'cKKey' },
            'n': { name: 'n', text: 'n', class: 'cKKey' },
            'm': { name: 'm', text: 'm', class: 'cKKey' },
            'backspace': { name: 'backspace', text: '', class: 'cKFunction' }
        },
        {
            'space': { name: 'space', text: 'spacja', class: 'cKKey' }
        }
    ];

    // Initialize ckeyboard
    cKeyboard();

    // Hide keyboard initially
    $('#keyboard').hide();
    $('#keyboard_numeric').hide();

    // Override ALL event handlers from ckeyboard
    $('body').off('touchstart click', '.cK.cKKey');
    $('body').off('click', '.cK.cKFunction.cKey-backspace');

    // === FIXED: Key press handler ===
    $('body').on('click', '.cK.cKKey', function () {
        if (!window._currentInputField || window._longPressActive) return;

        var $input = $(window._currentInputField);
        var maxLength = $input.attr('maxlength');

        if (maxLength !== undefined) {
            if ($input.val().length >= parseInt(maxLength)) {
                return;
            }
        }

        var char = $(this).html();
        if (cKeyboard_config.capslock_state) {
            char = char.toUpperCase();
        }

        var newValue = $input.val() + char;
        $input.val(newValue);

        // Trigger input event for Blazor binding
        $input[0].dispatchEvent(new Event('input', { bubbles: true }));
    });

    // === FIXED: Backspace handler - uses CURRENT input only ===
    $('body').on('click', '.cK.cKFunction.cKey-backspace', function () {
        if (!window._currentInputField) return;

        var $input = $(window._currentInputField);
        $input.val($input.val().slice(0, -1));

        // Trigger input event for Blazor binding
        $input[0].dispatchEvent(new Event('input', { bubbles: true }));
    });

    // === NEW: Long-press for Polish characters ===
    $('body').on('mousedown touchstart', '.cK.cKKey', function (e) {
        var $key = $(this);
        var char = $key.html().toLowerCase();

        // Check if this key has Polish alternatives
        if (!window._polishChars[char]) return;

        window._longPressTimer = setTimeout(function () {
            window._longPressActive = true;
            showPolishCharPicker($key, char);
        }, 500); // 500ms for long-press
    });

    $('body').on('mouseup touchend mouseleave', '.cK.cKKey', function () {
        if (window._longPressTimer) {
            clearTimeout(window._longPressTimer);
            window._longPressTimer = null;
        }
        setTimeout(function () {
            window._longPressActive = false;
        }, 100);
    });

    // Attach focus handlers to all kb-input fields
    $(document).on('focus click', '.kb-input', function (e) {
        e.preventDefault();
        e.stopPropagation(); // Prevent triggering document click

        // Remove focus from all inputs first
        $('.kb-input').removeClass('ring-2 ring-primary');
        $('.kb-input').addClass('ring-1 ring-slate-700');

        // Add focus styling to this input
        $(this).removeClass('ring-1 ring-slate-700');
        $(this).addClass('ring-2 ring-primary');

        // Fully block native keyboard
        this.blur();
        setTimeout(() => this.blur(), 10); // Extra safety

        window._currentInputField = this;
        window._keyboardJustOpened = true; // Flag to prevent immediate closing

        // Show keyboard first to get its height
        $('#keyboard').fadeIn(200);

        var $input = $(this);

        // Move footer (STOP button) and adjust main area
        setTimeout(function () {
            var keyboardHeight = $('#keyboard').outerHeight() || 190;
            var footerHeight = $('footer').outerHeight() || 70;
            var totalHeightToReserve = keyboardHeight + footerHeight + 10; // Small buffer

            // Move footer above keyboard
            $('footer').css('bottom', keyboardHeight + 'px');
            $('footer').css('transition', 'bottom 0.2s ease');

            // Add padding to main so content is scrollable above keyboard+footer
            $('main').css('padding-bottom', totalHeightToReserve + 'px');

            // Scroll input into view
            setTimeout(function () {
                $input[0].scrollIntoView({ behavior: 'smooth', block: 'center' });

                // Clear the flag after scroll completes
                setTimeout(function () {
                    window._keyboardJustOpened = false;
                }, 500);
            }, 250);
        }, 50);

        console.log('[Keyboard] Showing keyboard for input:', this.id);
    });

    // Hide keyboard when clicking outside input or keyboard
    $(document).on('click', function (e) {
        // Prevent hiding if keyboard just opened (race condition fix)
        if (window._keyboardJustOpened) return;

        // Check if keyboard is visible
        if (!$('#keyboard').is(':visible')) return;

        // Don't hide if clicking on input, keyboard, or Polish char picker
        if ($(e.target).closest('.kb-input, #keyboard, #keyboard_numeric, .polish-char-picker').length > 0) {
            return;
        }

        // Hide keyboard
        window.hideVirtualKeyboard();
        console.log('[Keyboard] Hidden by clicking outside');
    });

    // Prevent keyboard from appearing on input events
    $(document).on('keydown keypress', '.kb-input', function (e) {
        e.preventDefault();
        return false;
    });

    window._kbInitialized = true;
    console.log('[Keyboard] ckeyboard initialized with long-press Polish support');
};

// Show Polish character picker on long-press
function showPolishCharPicker($key, baseChar) {
    var polishOptions = window._polishChars[baseChar];
    if (!polishOptions) return;

    // Create picker popup
    var $picker = $('<div class="polish-char-picker"></div>');

    polishOptions.forEach(function (polishChar) {
        var $option = $('<div class="polish-char-option">' + polishChar + '</div>');

        // Use mousedown/touchstart for immediate response
        $option.on('mousedown touchstart', function (e) {
            e.preventDefault();
            e.stopPropagation();
            insertPolishChar(polishChar);
            $('.polish-char-picker').remove();
            window._longPressActive = false;
            return false;
        });

        $picker.append($option);
    });

    // Position above the key
    var keyOffset = $key.offset();
    $picker.css({
        position: 'absolute',
        left: keyOffset.left + 'px',
        bottom: ($(window).height() - keyOffset.top + 10) + 'px'
    });

    $('body').append($picker);

    // Remove picker if clicking outside (with delay to avoid immediate removal)
    setTimeout(function () {
        $(document).one('mousedown touchstart', function (e) {
            if (!$(e.target).closest('.polish-char-picker').length) {
                $('.polish-char-picker').remove();
                window._longPressActive = false;
            }
        });
    }, 100);
}

function insertPolishChar(char) {
    if (!window._currentInputField) return;

    var $input = $(window._currentInputField);
    var maxLength = $input.attr('maxlength');

    if (maxLength !== undefined) {
        if ($input.val().length >= parseInt(maxLength)) {
            return;
        }
    }

    if (cKeyboard_config.capslock_state) {
        char = char.toUpperCase();
    }

    var newValue = $input.val() + char;
    $input.val(newValue);

    // Trigger input event for Blazor binding
    $input[0].dispatchEvent(new Event('input', { bubbles: true }));
}

window.hideVirtualKeyboard = function () {
    $('#keyboard').fadeOut(200);
    $('#keyboard_numeric').fadeOut(200);

    // Reset footer position
    $('footer').css('bottom', '0');

    // Reset main padding
    $('main').css('padding-bottom', '0');

    // Remove focus styling
    $('.kb-input').removeClass('ring-2 ring-primary');
    $('.kb-input').addClass('ring-1 ring-slate-700');

    window._currentInputField = null;

    // Clear any Polish char pickers
    $('.polish-char-picker').remove();
};

window.destroyVirtualKeyboard = function () {
    window._kbDotNetRef = null;
    window._kbInitialized = false;
    window._currentInputField = null;

    if (window._longPressTimer) {
        clearTimeout(window._longPressTimer);
        window._longPressTimer = null;
    }
};
