(() => {
  const app = document.querySelector('[data-mobile-app]');
  if (!app) return;

  const initial = JSON.parse(document.querySelector('#mobile-initial-data').textContent);
  const state = { snapshot: initial, latestMessageId: Math.max(0, ...initial.messages.map(message => message.id)), activeView: 'patients' };
  const formatTime = value => new Intl.DateTimeFormat('fr-FR', { hour: '2-digit', minute: '2-digit' }).format(new Date(value));
  const escape = value => String(value ?? '').replace(/[&<>'"]/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#39;', '"': '&quot;' })[character]);

  function renderPatients() {
    const list = app.querySelector('[data-patient-list]');
    const patients = state.snapshot.patients;
    app.querySelector('[data-patient-count]').textContent = patients.length;
    app.querySelector('[data-patient-badge]').textContent = patients.length;
    list.innerHTML = patients.length ? patients.map(patient => `
      <article class="mobile-card patient-card ${patient.isTaken ? 'is-complete' : ''}">
        <span class="patient-accent" style="background:${safeColor(patient.colors)}"></span>
        <div class="patient-main">
          <div class="patient-heading"><strong>${escape([patient.title, patient.firstName, patient.lastName].filter(Boolean).join(' '))}</strong><time>${formatTime(patient.holdTime)}</time></div>
          <p>${escape(patient.exams || 'Examen non renseigné')} ${patient.eye ? `<b>• ${escape(patient.eye)}</b>` : ''}</p>
          <small>${escape(patient.position || 'Salle non renseignée')}${patient.annotation ? ` — ${escape(patient.annotation)}` : ''}</small>
        </div>
        <span class="patient-state">${patient.isTaken ? 'Terminé' : 'En attente'}</span>
      </article>`).join('') : emptyState('Aucun patient aujourd’hui', 'La file des patients du jour est vide.');
  }

  function renderMessages() {
    const list = app.querySelector('[data-message-list]');
    const messages = state.snapshot.messages;
    app.querySelector('[data-message-count]').textContent = messages.length;
    app.querySelector('[data-message-badge]').textContent = messages.length;
    list.innerHTML = messages.length ? messages.map(message => `
      <article class="mobile-card message-card">
        <span class="message-avatar">${escape((message.sender || '?').slice(0, 1).toUpperCase())}</span>
        <div><div class="message-heading"><strong>${escape(message.sender || 'Inconnu')}</strong><time>${formatTime(message.timestamp)}</time></div>
        <small>${escape(message.destinataire || message.room || 'Tous')}</small><p>${escape(message.content)}</p></div>
      </article>`).join('') : emptyState('Aucun message', 'Les messages du jour apparaîtront ici.');
  }

  function safeColor(value) {
    if (/^#[0-9a-f]{8}$/i.test(value || '')) return `#${value.slice(3)}`;
    return /^(#[0-9a-f]{6}|[a-z]+)$/i.test(value || '') ? value : '#246bfd';
  }

  function emptyState(title, detail) {
    return `<div class="mobile-empty"><span>✓</span><strong>${title}</strong><p>${detail}</p></div>`;
  }

  async function refresh({ quiet = false } = {}) {
    const status = app.querySelector('[data-status]');
    if (!quiet) status.textContent = 'Actualisation…';
    try {
      const response = await fetch(app.dataset.snapshotUrl, { credentials: 'same-origin', cache: 'no-store' });
      if (response.status === 401 || response.redirected) {
        window.location.reload();
        return;
      }
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const snapshot = await response.json();
      const newMessages = snapshot.messages.filter(message => message.id > state.latestMessageId);
      state.snapshot = snapshot;
      state.latestMessageId = Math.max(state.latestMessageId, ...snapshot.messages.map(message => message.id));
      renderPatients();
      renderMessages();
      if (newMessages.length && state.activeView !== 'messages') app.querySelector('[data-unread]').hidden = false;
      notifyNewMessages(newMessages);
      status.textContent = `À jour à ${formatTime(new Date())}`;
    } catch {
      status.textContent = 'Connexion interrompue — nouvel essai automatique';
    }
  }

  function notifyNewMessages(messages) {
    if (!('Notification' in window) || Notification.permission !== 'granted' || document.visibilityState === 'visible') return;
    messages.slice(0, 3).forEach(message => new Notification(`Message de ${message.sender}`, { body: message.content, tag: `message-${message.id}` }));
  }

  app.querySelectorAll('[data-tab]').forEach(button => button.addEventListener('click', () => {
    state.activeView = button.dataset.tab;
    app.querySelectorAll('[data-tab]').forEach(tab => tab.classList.toggle('is-active', tab === button));
    app.querySelectorAll('[data-view]').forEach(view => {
      const active = view.dataset.view === state.activeView;
      view.hidden = !active;
      view.classList.toggle('is-active', active);
    });
    if (state.activeView === 'messages') app.querySelector('[data-unread]').hidden = true;
  }));
  app.querySelector('[data-refresh]').addEventListener('click', () => refresh());
  app.querySelector('[data-notifications]').addEventListener('click', async event => {
    if (!('Notification' in window)) return;
    const permission = await Notification.requestPermission();
    event.currentTarget.classList.toggle('is-enabled', permission === 'granted');
  });
  document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'visible') refresh({ quiet: true }); });
  if ('serviceWorker' in navigator) navigator.serviceWorker.register('/mobile-sw.js');

  renderPatients();
  renderMessages();
  setInterval(() => refresh({ quiet: true }), 10000);
})();
