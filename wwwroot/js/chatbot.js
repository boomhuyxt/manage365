/**
 * Manage365 - Employee HR Assistant ChatBot Widget
 * Direct integration with https://manage365.click/api and local proxy fallback
 */
(() => {
  "use strict";

  // Configuration
  const DIRECT_API_BASE = "https://manage365.click/api";
  const LOCAL_API_BASE = "/api/chatbot";
  const STORAGE_KEY_HISTORY = "manage365_chatbot_history";
  const STORAGE_KEY_SOUND = "manage365_chatbot_sound";
  const STORAGE_KEY_OPEN = "manage365_chatbot_open";

  // Elements
  let launcherBtn = null;
  let chatWindow = null;
  let messagesContainer = null;
  let chatInput = null;
  let sendBtn = null;
  let statusDot = null;
  let statusText = null;
  let soundToggleBtn = null;
  let resetBtn = null;
  let templatesBtn = null;
  let templatesDrawer = null;
  let templatesList = null;

  // State
  let conversationHistory = []; // [{ role: 'user'|'model', text: '...' }]
  let isGenerating = false;
  let soundEnabled = localStorage.getItem(STORAGE_KEY_SOUND) !== "false";
  let activeAbortController = null;
  let templatesCache = null;

  // Web Audio Context for pleasant notification sound without external files
  let audioCtx = null;
  function playChime(type = "success") {
    if (!soundEnabled) return;
    try {
      if (!audioCtx) {
        audioCtx = new (window.AudioContext || window.webkitAudioContext)();
      }
      if (audioCtx.state === "suspended") {
        audioCtx.resume();
      }
      const now = audioCtx.currentTime;
      const osc = audioCtx.createOscillator();
      const gain = audioCtx.createGain();

      if (type === "pop") {
        osc.type = "sine";
        osc.frequency.setValueAtTime(420, now);
        osc.frequency.exponentialRampToValueAtTime(780, now + 0.08);
        gain.gain.setValueAtTime(0.08, now);
        gain.gain.exponentialRampToValueAtTime(0.001, now + 0.1);
        osc.connect(gain);
        gain.connect(audioCtx.destination);
        osc.start(now);
        osc.stop(now + 0.1);
      } else {
        // Soft double chime for incoming response
        const osc1 = audioCtx.createOscillator();
        const osc2 = audioCtx.createOscillator();
        const g1 = audioCtx.createGain();
        const g2 = audioCtx.createGain();

        osc1.type = "triangle";
        osc2.type = "triangle";
        osc1.frequency.setValueAtTime(523.25, now); // C5
        osc2.frequency.setValueAtTime(659.25, now + 0.08); // E5

        g1.gain.setValueAtTime(0.06, now);
        g1.gain.exponentialRampToValueAtTime(0.001, now + 0.15);
        g2.gain.setValueAtTime(0.06, now + 0.08);
        g2.gain.exponentialRampToValueAtTime(0.001, now + 0.25);

        osc1.connect(g1);
        g1.connect(audioCtx.destination);
        osc2.connect(g2);
        g2.connect(audioCtx.destination);

        osc1.start(now);
        osc1.stop(now + 0.15);
        osc2.start(now + 0.08);
        osc2.stop(now + 0.25);
      }
    } catch {
      // Audio context may be restricted by autoplay policy
    }
  }

  // Get current logged-in employee name if available
  function getCurrentUserInfo() {
    try {
      const raw = localStorage.getItem("user") || sessionStorage.getItem("user");
      if (raw) return JSON.parse(raw);
    } catch {
      // Ignore JSON error
    }
    return null;
  }

  // Basic safe markdown parser for terminal/chat rendering
  function renderMarkdown(rawText) {
    if (!rawText) return "";
    let str = rawText;

    // Convert LaTeX / formula
    str = str.replace(/\$\$([\s\S]*?)\$\$/g, (_, eq) => {
      return `<div class="cb-code-wrapper"><pre><code>${eq.trim()}</code></pre></div>`;
    });

    // Escape HTML
    str = str
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;");

    // Code blocks with copy button
    str = str.replace(/```([a-zA-Z0-9_-]*)\n([\s\S]*?)```/g, (_, lang, code) => {
      const escapedCode = code.trim();
      return `
        <div class="cb-code-wrapper">
          <div class="cb-code-header">
            <span>${lang || "văn bản / biểu mẫu"}</span>
            <button class="cb-code-copy-btn" type="button" onclick="window.manage365CopyCode(this)"><i class="fa-regular fa-copy me-1"></i>Sao chép</button>
          </div>
          <pre><code>${escapedCode}</code></pre>
        </div>
      `;
    });

    // Inline code
    str = str.replace(/`([^`]+)`/g, "<code>$1</code>");

    // Headers
    str = str.replace(/^### (.*$)/gim, "<h4>$1</h4>");
    str = str.replace(/^## (.*$)/gim, "<h3>$1</h3>");
    str = str.replace(/^# (.*$)/gim, "<h2>$1</h2>");

    // Bold & Italic
    str = str.replace(/\*\*([^*]+)\*\*/g, "<strong>$1</strong>");
    str = str.replace(/(^|[^*])\*([^*\n]+)\*([^*]|$)/g, "$1<em>$2</em>$3");

    // Horizontal Rule
    str = str.replace(/^---$/gim, "<hr/>");

    // Blockquote
    str = str.replace(/^> (.*$)/gim, "<blockquote>$1</blockquote>");

    // Bullet points
    str = str.replace(/^\s*[-*+]\s+(.*$)/gim, "<li>$1</li>");
    str = str.replace(/(<li>.*<\/li>)/s, "<ul>$1</ul>");

    // Paragraphs / line breaks
    str = str.replace(/\n\n/g, "</p><p>");
    str = str.replace(/\n/g, "<br/>");

    return `<p>${str}</p>`;
  }

  // Global helper for code copy button
  window.manage365CopyCode = function (button) {
    try {
      const wrapper = button.closest(".cb-code-wrapper");
      const code = wrapper ? wrapper.querySelector("code") : null;
      if (!code) return;
      const text = code.innerText || code.textContent;
      navigator.clipboard.writeText(text).then(() => {
        const originalHtml = button.innerHTML;
        button.innerHTML = '<i class="fa-solid fa-check me-1"></i>Đã chép!';
        button.style.background = "#2ecc71";
        setTimeout(() => {
          button.innerHTML = originalHtml;
          button.style.background = "";
        }, 2000);
      });
    } catch {
      // Fallback
    }
  };

  // Append a message bubble to the chat container
  function appendMessage(role, text, timeStr) {
    const isUser = role === "user";
    const row = document.createElement("div");
    row.className = `cb-message-row ${isUser ? "is-user" : "is-bot"}`;

    const nowTime = timeStr || new Date().toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" });

    if (isUser) {
      row.innerHTML = `
        <div class="cb-msg-bubble">
          ${text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/\n/g, "<br/>")}
          <div class="cb-msg-time"><i class="fa-regular fa-clock"></i> ${nowTime}</div>
        </div>
      `;
    } else {
      row.innerHTML = `
        <div class="cb-msg-avatar" aria-hidden="true"><i class="fa-solid fa-robot"></i></div>
        <div class="cb-msg-bubble">
          <div class="cb-msg-body">${renderMarkdown(text)}</div>
          <div class="cb-msg-time"><i class="fa-solid fa-sparkles text-warning me-1"></i> HR Assistant • ${nowTime}</div>
        </div>
      `;
    }

    messagesContainer.appendChild(row);
    scrollToBottom();
    return row;
  }

  // Show typing / waiting indicator
  function showTypingIndicator() {
    const row = document.createElement("div");
    row.id = "cb-typing-indicator-row";
    row.className = "cb-message-row is-bot";
    row.innerHTML = `
      <div class="cb-msg-avatar" aria-hidden="true"><i class="fa-solid fa-robot"></i></div>
      <div class="cb-msg-bubble">
        <div class="cb-typing-indicator">
          <div class="cb-typing-dot"></div>
          <div class="cb-typing-dot"></div>
          <div class="cb-typing-dot"></div>
        </div>
      </div>
    `;
    messagesContainer.appendChild(row);
    scrollToBottom();
    return row;
  }

  function hideTypingIndicator() {
    const indicator = document.getElementById("cb-typing-indicator-row");
    if (indicator) indicator.remove();
  }

  function scrollToBottom() {
    if (messagesContainer) {
      messagesContainer.scrollTop = messagesContainer.scrollHeight;
    }
  }

  // Save history to sessionStorage
  function saveHistory() {
    try {
      sessionStorage.setItem(STORAGE_KEY_HISTORY, JSON.stringify(conversationHistory));
    } catch {
      // Storage quota or disabled
    }
  }

  // Restore history from sessionStorage
  function restoreHistory() {
    try {
      const saved = sessionStorage.getItem(STORAGE_KEY_HISTORY);
      if (saved) {
        const items = JSON.parse(saved);
        if (Array.isArray(items) && items.length > 0) {
          conversationHistory = items;
          conversationHistory.forEach(item => {
            appendMessage(item.role, item.text);
          });
          return true;
        }
      }
    } catch {
      // Ignore
    }
    return false;
  }

  // Initial welcome message
  function showWelcomeMessage() {
    const user = getCurrentUserInfo();
    const userName = user?.displayName ? `**${user.displayName}**` : "bạn";
    const shopGreeting = user?.shopName ? ` (${user.shopName})` : "";

    const welcomeText = `Xin chào ${userName}${shopGreeting}! 👋\n\n` +
      `Tôi là **Trợ lý Nhân sự AI Manage365**, sẵn sàng giải đáp thắc mắc và hỗ trợ mọi nghiệp vụ cho nhân viên:\n\n` +
      `- 📋 **Quy chế nghỉ phép:** Nghỉ ca, ngày, dải ngày, phép năm & chế độ lương\n` +
      `- ⏰ **Phân ca & Chấm công:** Quy định ca làm, làm thêm giờ (OT), đổi ca\n` +
      `- 📑 **10 Biểu mẫu hành chính chuẩn:** Đơn nghỉ phép, tăng lương, thôi việc, tạm ứng...\n` +
      `- 🏥 **Chế độ phúc lợi:** BHXH, BHYT, thai sản, ốm đau theo Luật Lao động\n\n` +
      `Bạn có câu hỏi nào cần giải đáp ngay không? Hãy chọn gợi ý bên trên hoặc gõ câu hỏi bên dưới nhé!`;

    appendMessage("model", welcomeText);
  }

  // Check API health
  async function checkApiHealth() {
    let isOnline = false;
    try {
      // Try direct API first
      const res = await fetch(`${DIRECT_API_BASE}/health`, { signal: AbortSignal.timeout(4000) });
      if (res.ok) {
        const data = await res.json();
        isOnline = data.status === "online";
      }
    } catch {
      // Fallback to local proxy health
      try {
        const localRes = await fetch(`${LOCAL_API_BASE}/health`, { signal: AbortSignal.timeout(4000) });
        if (localRes.ok) {
          const localData = await localRes.json();
          isOnline = localData.status === "online";
        }
      } catch {
        isOnline = false;
      }
    }

    if (statusDot) {
      statusDot.classList.toggle("is-offline", !isOnline);
      const launcherDot = launcherBtn.querySelector(".launcher-status-dot");
      if (launcherDot) launcherDot.classList.toggle("is-offline", !isOnline);
    }
    if (statusText) {
      statusText.textContent = isOnline ? "Sẵn sàng hỗ trợ 24/7" : "Tạm gián đoạn";
    }
  }

  // Load and cache templates
  async function loadTemplates() {
    if (templatesCache) return templatesCache;
    try {
      let res = await fetch(`${DIRECT_API_BASE}/templates`, { signal: AbortSignal.timeout(5000) });
      if (!res.ok) {
        res = await fetch(`${LOCAL_API_BASE}/templates`, { signal: AbortSignal.timeout(5000) });
      }
      if (res.ok) {
        const data = await res.json();
        if (data.templates && Array.isArray(data.templates)) {
          templatesCache = data.templates;
          renderTemplatesList(templatesCache);
          return templatesCache;
        }
      }
    } catch (e) {
      console.warn("Could not load templates:", e);
    }
    return [];
  }

  function renderTemplatesList(templates) {
    if (!templatesList) return;
    if (!templates || templates.length === 0) {
      templatesList.innerHTML = '<div class="text-muted small p-3 text-center">Không thể tải danh sách biểu mẫu lúc này.</div>';
      return;
    }
    templatesList.innerHTML = templates.map(t => `
      <div class="cb-template-item" onclick="window.manage365SelectTemplate('${t.code}', '${t.title.replace(/'/g, "\\'")}')">
        <span class="cb-template-code">${t.code}</span>
        <span class="cb-template-name">${t.title}</span>
        <i class="fa-solid fa-chevron-right text-muted small"></i>
      </div>
    `).join("");
  }

  window.manage365SelectTemplate = function (code, title) {
    closeTemplatesDrawer();
    sendUserQuery(`Cho tôi xem chi tiết và hướng dẫn điền [${code}] ${title}`);
  };

  function openTemplatesDrawer() {
    if (templatesDrawer) {
      templatesDrawer.classList.add("is-open");
      loadTemplates();
    }
  }

  function closeTemplatesDrawer() {
    if (templatesDrawer) {
      templatesDrawer.classList.remove("is-open");
    }
  }

  // Send query via streaming with fallback to non-streaming
  async function sendUserQuery(text) {
    const cleanText = (text || "").trim();
    if (!cleanText || isGenerating) return;

    // Reset input
    chatInput.value = "";
    autoResizeTextarea(chatInput);

    // Append user message
    appendMessage("user", cleanText);
    conversationHistory.push({ role: "user", text: cleanText });
    saveHistory();

    playChime("pop");

    // UI state
    isGenerating = true;
    sendBtn.disabled = true;
    chatInput.disabled = true;
    showTypingIndicator();

    activeAbortController = new AbortController();
    const signal = activeAbortController.signal;

    // Try direct streaming API first
    let success = false;
    let fullReply = "";

    try {
      success = await callStreamingApi(DIRECT_API_BASE + "/chat/stream", cleanText, signal);
    } catch (errDirectStream) {
      console.warn("Direct stream failed, trying local proxy stream:", errDirectStream);
    }

    if (!success && !signal.aborted) {
      try {
        success = await callStreamingApi(LOCAL_API_BASE + "/stream", cleanText, signal);
      } catch (errLocalStream) {
        console.warn("Local proxy stream failed, falling back to non-streaming POST:", errLocalStream);
      }
    }

    if (!success && !signal.aborted) {
      try {
        fullReply = await callStandardPostApi(DIRECT_API_BASE + "/chat", cleanText, signal);
        success = true;
      } catch (errDirectPost) {
        try {
          fullReply = await callStandardPostApi(LOCAL_API_BASE + "/chat", cleanText, signal);
          success = true;
        } catch (errLocalPost) {
          hideTypingIndicator();
          appendMessage("model", `⚠️ **Rất tiếc!** Hiện tại không thể kết nối tới dịch vụ ChatBot nhân sự Manage365.\n\nVui lòng kiểm tra lại kết nối mạng hoặc thử lại sau.`);
        }
      }
      if (success && fullReply) {
        hideTypingIndicator();
        appendMessage("model", fullReply);
        conversationHistory.push({ role: "model", text: fullReply });
        saveHistory();
        playChime("success");
      }
    }

    // Reset UI state
    isGenerating = false;
    sendBtn.disabled = false;
    chatInput.disabled = false;
    chatInput.focus();
    activeAbortController = null;
  }

  // Handle SSE streaming from an endpoint
  async function callStreamingApi(url, message, signal) {
    const res = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        message: message,
        history: conversationHistory.slice(0, -1) // All except current
      }),
      signal: signal
    });

    if (!res.ok) {
      throw new Error(`HTTP ${res.status}`);
    }

    const reader = res.body.getReader();
    const decoder = new TextDecoder("utf-8");
    let buffer = "";
    let fullReply = "";
    let isFirstChunk = true;
    let botRow = null;
    let botBody = null;
    let renderScheduled = false;

    const scheduleRender = () => {
      if (renderScheduled) return;
      renderScheduled = true;
      requestAnimationFrame(() => {
        renderScheduled = false;
        if (botBody) {
          botBody.innerHTML = renderMarkdown(fullReply);
          scrollToBottom();
        }
      });
    };

    while (true) {
      const { done, value } = await reader.read();
      if (done) break;

      buffer += decoder.decode(value, { stream: true });
      const lines = buffer.split("\n");
      buffer = lines.pop();

      for (const line of lines) {
        const trimmed = line.trim();
        if (trimmed.startsWith("data: ")) {
          const jsonStr = trimmed.slice(6).trim();
          if (!jsonStr) continue;
          try {
            const data = JSON.parse(jsonStr);
            if (data.error) throw new Error(data.error);
            if (data.chunk) {
              if (isFirstChunk) {
                hideTypingIndicator();
                isFirstChunk = false;
                const timeNow = new Date().toLocaleTimeString("vi-VN", { hour: "2-digit", minute: "2-digit" });
                botRow = document.createElement("div");
                botRow.className = "cb-message-row is-bot";
                botRow.innerHTML = `
                  <div class="cb-msg-avatar" aria-hidden="true"><i class="fa-solid fa-robot"></i></div>
                  <div class="cb-msg-bubble">
                    <div class="cb-msg-body"></div>
                    <div class="cb-msg-time"><i class="fa-solid fa-sparkles text-warning me-1"></i> HR Assistant • ${timeNow}</div>
                  </div>
                `;
                botBody = botRow.querySelector(".cb-msg-body");
                messagesContainer.appendChild(botRow);
              }
              fullReply += data.chunk;
              scheduleRender();
            }
          } catch (e) {
            if (e.message && !e.message.includes("Unexpected end of JSON")) {
              throw e;
            }
          }
        }
      }
    }

    if (fullReply) {
      hideTypingIndicator();
      if (botBody) botBody.innerHTML = renderMarkdown(fullReply);
      conversationHistory.push({ role: "model", text: fullReply });
      saveHistory();
      playChime("success");
      scrollToBottom();
      return true;
    }
    return false;
  }

  // Handle standard JSON POST
  async function callStandardPostApi(url, message, signal) {
    const res = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        message: message,
        history: conversationHistory.slice(0, -1)
      }),
      signal: signal
    });

    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json();
    return data.reply || data.message || "";
  }

  // Toggle chat window open / close
  function toggleChat(forceOpen = null) {
    const shouldOpen = forceOpen !== null ? forceOpen : !chatWindow.classList.contains("is-open");
    const chatBubble = document.getElementById("manage365ChatBubble");
    if (shouldOpen) {
      chatWindow.classList.add("is-open");
      launcherBtn.classList.add("is-active");
      if (chatBubble) chatBubble.classList.add("is-hidden");
      sessionStorage.setItem(STORAGE_KEY_OPEN, "true");
      scrollToBottom();
      setTimeout(() => chatInput?.focus(), 200);
      playChime("pop");
    } else {
      chatWindow.classList.remove("is-open");
      launcherBtn.classList.remove("is-active");
      sessionStorage.setItem(STORAGE_KEY_OPEN, "false");
      closeTemplatesDrawer();
    }
  }

  // Auto-resize textarea
  function autoResizeTextarea(textarea) {
    if (!textarea) return;
    textarea.style.height = "auto";
    textarea.style.height = Math.min(textarea.scrollHeight, 110) + "px";
  }

  // Clear chat conversation
  function resetConversation() {
    if (isGenerating) return;
    if (conversationHistory.length > 0 && !confirm("Bạn có chắc chắn muốn làm mới toàn bộ cuộc trò chuyện với Trợ lý HR?")) {
      return;
    }
    conversationHistory = [];
    sessionStorage.removeItem(STORAGE_KEY_HISTORY);
    messagesContainer.innerHTML = "";
    showWelcomeMessage();
    playChime("pop");
  }

  // Initialize UI
  function init() {
    launcherBtn = document.getElementById("manage365ChatLauncher");
    chatWindow = document.getElementById("manage365ChatWindow");
    messagesContainer = document.getElementById("manage365ChatMessages");
    chatInput = document.getElementById("manage365ChatInput");
    sendBtn = document.getElementById("manage365ChatSendBtn");
    statusDot = document.getElementById("manage365ChatStatusDot");
    statusText = document.getElementById("manage365ChatStatusText");
    soundToggleBtn = document.getElementById("manage365ChatSoundBtn");
    resetBtn = document.getElementById("manage365ChatResetBtn");
    templatesBtn = document.getElementById("manage365ChatTemplatesBtn");
    templatesDrawer = document.getElementById("manage365ChatTemplatesDrawer");
    templatesList = document.getElementById("manage365ChatTemplatesList");

    if (!launcherBtn || !chatWindow) return;

    // Restore state
    const hasHistory = restoreHistory();
    if (!hasHistory) {
      showWelcomeMessage();
    }

    if (soundToggleBtn) {
      soundToggleBtn.innerHTML = soundEnabled
        ? '<i class="fa-solid fa-volume-high"></i>'
        : '<i class="fa-solid fa-volume-xmark"></i>';
      soundToggleBtn.title = soundEnabled ? "Tắt âm thanh" : "Bật âm thanh";
    }

    // Event listeners
    launcherBtn.addEventListener("click", () => toggleChat());

    const chatBubble = document.getElementById("manage365ChatBubble");
    const dismissBubbleBtn = document.getElementById("manage365DismissBubble");

    if (sessionStorage.getItem("manage365_bubble_dismissed") === "true") {
      chatBubble?.classList.add("is-hidden");
    }

    chatBubble?.addEventListener("click", (e) => {
      if (e.target !== dismissBubbleBtn) {
        toggleChat(true);
      }
    });

    dismissBubbleBtn?.addEventListener("click", (e) => {
      e.stopPropagation();
      chatBubble?.classList.add("is-hidden");
      sessionStorage.setItem("manage365_bubble_dismissed", "true");
    });

    document.getElementById("manage365ChatCloseBtn")?.addEventListener("click", () => toggleChat(false));
    document.getElementById("manage365ChatMinBtn")?.addEventListener("click", () => toggleChat(false));

    soundToggleBtn?.addEventListener("click", () => {
      soundEnabled = !soundEnabled;
      localStorage.setItem(STORAGE_KEY_SOUND, soundEnabled.toString());
      soundToggleBtn.innerHTML = soundEnabled
        ? '<i class="fa-solid fa-volume-high"></i>'
        : '<i class="fa-solid fa-volume-xmark"></i>';
      soundToggleBtn.title = soundEnabled ? "Tắt âm thanh" : "Bật âm thanh";
      if (soundEnabled) playChime("pop");
    });

    resetBtn?.addEventListener("click", () => resetConversation());

    templatesBtn?.addEventListener("click", () => openTemplatesDrawer());
    document.getElementById("manage365DrawerCloseBtn")?.addEventListener("click", () => closeTemplatesDrawer());

    // Suggestions chips
    document.querySelectorAll(".cb-chip-btn").forEach(chip => {
      chip.addEventListener("click", () => {
        const query = chip.dataset.query || chip.innerText.trim();
        sendUserQuery(query);
      });
    });

    // Form submit / input handlers
    document.getElementById("manage365ChatForm")?.addEventListener("submit", (e) => {
      e.preventDefault();
      sendUserQuery(chatInput.value);
    });

    chatInput?.addEventListener("input", () => {
      autoResizeTextarea(chatInput);
      sendBtn.disabled = !chatInput.value.trim() || isGenerating;
    });

    chatInput?.addEventListener("keydown", (e) => {
      if (e.key === "Enter" && !e.shiftKey) {
        e.preventDefault();
        sendUserQuery(chatInput.value);
      }
    });

    // Global keyboard shortcuts (ESC to close window)
    window.addEventListener("keydown", (e) => {
      if (e.key === "Escape" && chatWindow.classList.contains("is-open")) {
        toggleChat(false);
      }
    });

    // Initial check
    checkApiHealth();
    loadTemplates();

    // Reopen if previously open in this session
    if (sessionStorage.getItem(STORAGE_KEY_OPEN) === "true") {
      toggleChat(true);
    }
  }

  // Boot on DOM ready
  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init);
  } else {
    init();
  }
})();
