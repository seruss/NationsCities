// Państwa, Miasta - JavaScript Interop
// ======================================

/**
 * Copies text to clipboard using modern Clipboard API with fallback
 * @param {string} text - Text to copy to clipboard
 * @returns {Promise<boolean>} - True if successful, false otherwise
 */
window.copyToClipboard = async function(text) {
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
