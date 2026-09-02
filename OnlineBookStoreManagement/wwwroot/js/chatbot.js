/**
 * Bookie Assistant Chatbot Frontend Controller
 */
document.addEventListener('DOMContentLoaded', function () {
    const launcherBtn = document.getElementById('chatbot-launcher');
    const chatWindow = document.getElementById('chatbot-window');
    const closeBtn = document.getElementById('chatbot-close');
    const clearBtn = document.getElementById('chatbot-clear');
    const chatBody = document.getElementById('chatbot-body');
    const chatInput = document.getElementById('chatbot-input');
    const sendBtn = document.getElementById('chatbot-send');

    if (!launcherBtn || !chatWindow || !chatBody || !chatInput || !sendBtn) {
        return;
    }

    const SESSION_KEY = 'bookie_assistant_chat_history_v1';
    let isWaitingForResponse = false;

    // Toggle Chat Window
    launcherBtn.addEventListener('click', function () {
        const isActive = chatWindow.classList.contains('active');
        if (isActive) {
            closeChatWindow();
        } else {
            openChatWindow();
        }
    });

    closeBtn.addEventListener('click', closeChatWindow);

    if (clearBtn) {
        clearBtn.addEventListener('click', function () {
            sessionStorage.removeItem(SESSION_KEY);
            chatBody.innerHTML = '';
            sendInitialGreeting();
        });
    }

    function openChatWindow() {
        chatWindow.classList.add('active');
        launcherBtn.querySelector('i').className = 'bi bi-x-lg';
        chatInput.focus();
        scrollToBottom();
    }

    function closeChatWindow() {
        chatWindow.classList.remove('active');
        launcherBtn.querySelector('i').className = 'bi bi-robot';
    }

    // Input Events
    chatInput.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            sendMessage();
        }
    });

    sendBtn.addEventListener('click', function () {
        sendMessage();
    });

    // Handle Option Chip Clicks via Event Delegation
    chatBody.addEventListener('click', function (e) {
        const chip = e.target.closest('.chat-option-chip');
        if (chip && !isWaitingForResponse) {
            const queryValue = chip.getAttribute('data-value') || chip.innerText.trim();
            if (queryValue) {
                sendUserQuery(queryValue);
            }
        }
    });

    function sendMessage() {
        const text = chatInput.value.trim();
        if (!text || isWaitingForResponse) return;
        chatInput.value = '';
        sendUserQuery(text);
    }

    function sendUserQuery(text) {
        appendMessage('user', text);
        showTypingIndicator();
        isWaitingForResponse = true;

        fetch('/api/chatbot/query', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({ message: text })
        })
        .then(response => {
            if (!response.ok) throw new Error('Network error');
            return response.json();
        })
        .then(data => {
            removeTypingIndicator();
            isWaitingForResponse = false;
            appendBotResponse(data);
        })
        .catch(err => {
            console.error('Chatbot API error:', err);
            removeTypingIndicator();
            isWaitingForResponse = false;
            appendMessage('bot', 'Sorry, I encountered an issue connecting to the server. Please try again in a moment!');
        });
    }

    function appendMessage(sender, text) {
        const msgDiv = document.createElement('div');
        msgDiv.className = `chat-msg ${sender}`;

        const timeStr = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });

        if (sender === 'bot') {
            msgDiv.innerHTML = `
                <div class="chat-msg-avatar"><i class="bi bi-robot"></i></div>
                <div class="chat-msg-content">
                    <div class="chat-bubble">${formatMarkdown(text)}</div>
                    <div class="chat-time">${timeStr}</div>
                </div>
            `;
        } else {
            msgDiv.innerHTML = `
                <div class="chat-msg-content">
                    <div class="chat-bubble">${escapeHtml(text)}</div>
                    <div class="chat-time">${timeStr}</div>
                </div>
            `;
        }

        chatBody.appendChild(msgDiv);
        scrollToBottom();
        saveHistory();
    }

    function appendBotResponse(data) {
        const msgDiv = document.createElement('div');
        msgDiv.className = 'chat-msg bot';

        const timeStr = new Date().toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
        let htmlContent = `<div class="chat-bubble">${formatMarkdown(data.reply || '')}`;

        // Render Book Recommendations if present
        if (data.books && data.books.length > 0) {
            htmlContent += `<div class="chat-book-cards">`;
            data.books.forEach(b => {
                htmlContent += `
                    <a href="/Home/Details/${b.id}" class="chat-book-item">
                        <img src="${escapeHtml(b.coverImageUrl || '/images/default-book.svg')}" alt="${escapeHtml(b.title)}" class="chat-book-thumb" onerror="this.onerror=null; this.src='/images/default-book.svg';" />
                        <div class="chat-book-info">
                            <div class="chat-book-title">${escapeHtml(b.title)}</div>
                            <div class="chat-book-author">by ${escapeHtml(b.author)}</div>
                            <div class="chat-book-meta">
                                <span class="chat-book-price">${escapeHtml(b.priceFormatted)}</span>
                                <span class="chat-book-badge">★ ${b.averageRating > 0 ? b.averageRating : 'New'}</span>
                            </div>
                        </div>
                    </a>
                `;
            });
            htmlContent += `</div>`;
        }

        // Render Action Button if present
        if (data.actionUrl && data.actionText) {
            htmlContent += `
                <a href="${escapeHtml(data.actionUrl)}" class="chat-action-link">
                    ${escapeHtml(data.actionText)} <i class="bi bi-arrow-right-short"></i>
                </a>
            `;
        }

        htmlContent += `</div>`;

        // Render Option Chips if present
        if (data.options && data.options.length > 0) {
            htmlContent += `<div class="chat-options-container">`;
            data.options.forEach(opt => {
                const iconClass = escapeHtml(opt.icon || 'bi-chat-text');
                htmlContent += `
                    <button type="button" class="chat-option-chip" data-value="${escapeHtml(opt.value)}">
                        <i class="bi ${iconClass}"></i> ${escapeHtml(opt.label)}
                    </button>
                `;
            });
            htmlContent += `</div>`;
        }

        msgDiv.innerHTML = `
            <div class="chat-msg-avatar"><i class="bi bi-robot"></i></div>
            <div class="chat-msg-content">
                ${htmlContent}
                <div class="chat-time">${timeStr}</div>
            </div>
        `;

        chatBody.appendChild(msgDiv);
        scrollToBottom();
        saveHistory();
    }

    function showTypingIndicator() {
        const typingDiv = document.createElement('div');
        typingDiv.id = 'chatbot-typing-indicator';
        typingDiv.className = 'chat-msg bot';
        typingDiv.innerHTML = `
            <div class="chat-msg-avatar"><i class="bi bi-robot"></i></div>
            <div class="chat-msg-content">
                <div class="typing-indicator">
                    <span></span><span></span><span></span>
                </div>
            </div>
        `;
        chatBody.appendChild(typingDiv);
        scrollToBottom();
    }

    function removeTypingIndicator() {
        const typingDiv = document.getElementById('chatbot-typing-indicator');
        if (typingDiv) {
            typingDiv.remove();
        }
    }

    function scrollToBottom() {
        setTimeout(() => {
            chatBody.scrollTop = chatBody.scrollHeight;
        }, 50);
    }

    function escapeHtml(str) {
        if (!str) return '';
        return str.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&#039;");
    }

    function formatMarkdown(str) {
        if (!str) return '';
        let html = escapeHtml(str);
        // Bold: **text**
        html = html.replace(/\*\*(.*?)\*\*/g, '<strong>$1</strong>');
        // Italic: *text*
        html = html.replace(/\*(.*?)\*/g, '<em>$1</em>');
        // Inline code: `code`
        html = html.replace(/`(.*?)`/g, '<code>$1</code>');
        // Line breaks
        html = html.replace(/\n/g, '<br/>');
        return html;
    }

    function saveHistory() {
        try {
            sessionStorage.setItem(SESSION_KEY, chatBody.innerHTML);
        } catch (e) {
            console.error('Failed to save chat history to sessionStorage', e);
        }
    }

    function loadHistory() {
        try {
            const saved = sessionStorage.getItem(SESSION_KEY);
            if (saved && saved.trim().length > 0) {
                chatBody.innerHTML = saved;
                scrollToBottom();
                return true;
            }
        } catch (e) {
            console.error('Failed to load chat history', e);
        }
        return false;
    }

    function sendInitialGreeting() {
        fetch('/api/chatbot/query', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ message: 'hi' })
        })
        .then(res => res.json())
        .then(data => appendBotResponse(data))
        .catch(err => {
            appendMessage('bot', 'Hi! I am your bookstore assistant. How can I help you today?');
        });
    }

    // Initialize Chat state
    const hasHistory = loadHistory();
    if (!hasHistory) {
        sendInitialGreeting();
    }
});
