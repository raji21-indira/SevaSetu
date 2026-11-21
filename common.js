/**
 * SevaSetu Common Script
 * Manages shared state, notifications, language toggling, authentication, and toasts.
 */

// Global State
const SevaStore = {
  getLanguage() {
    return localStorage.getItem('sevasetu_lang') || 'en';
  },
  setLanguage(lang) {
    localStorage.setItem('sevasetu_lang', lang);
  },
  getUser() {
    const raw = localStorage.getItem('sevasetu_user');
    return raw ? JSON.parse(raw) : { name: 'Priya Venkatesh', role: 'Customer / Donor', phone: '+91 98400 76543' };
  },
  setUser(user) {
    localStorage.setItem('sevasetu_user', JSON.stringify(user));
  },
  getNotifications() {
    const defaultNotes = [
      { id: 1, title: 'Donation Completed!', desc: 'Rice & Blankets delivered to Anandam Bal Ashram.', time: '10m ago', unread: true },
      { id: 2, title: 'Urgent Request Nearby', desc: 'Shanti Niketan Elders requires 50 warm blankets.', time: '2h ago', unread: true },
      { id: 3, title: 'Food Rescue Alert', desc: 'Dakshin Kitchen listed 35 sealed hot meal boxes.', time: '3h ago', unread: false }
    ];
    const raw = localStorage.getItem('sevasetu_notes');
    return raw ? JSON.parse(raw) : defaultNotes;
  },
  markNotesRead() {
    const notes = this.getNotifications().map(n => ({ ...n, unread: false }));
    localStorage.setItem('sevasetu_notes', JSON.stringify(notes));
  }
};

// Toast notification helper
function showToast(message, type = 'success') {
  let container = document.getElementById('toast-container');
  if (!container) {
    container = document.createElement('div');
    container.id = 'toast-container';
    document.body.appendChild(container);
  }

  const toast = document.createElement('div');
  const icon = type === 'success' ? 'check_circle' : type === 'error' ? 'error' : 'info';
  const bg = type === 'success' ? 'bg-primary text-on-primary' : type === 'error' ? 'bg-error text-on-error' : 'bg-on-surface text-surface';

  toast.className = `toast-msg flex items-center gap-2.5 px-4 py-3 rounded-lg shadow-lg border border-outline-variant font-body text-body-md ${bg}`;
  toast.innerHTML = `
    <span class="material-symbols-outlined text-title-lg" aria-hidden="true">${icon}</span>
    <span>${message}</span>
  `;

  container.appendChild(toast);
  requestAnimationFrame(() => toast.classList.add('show'));

  setTimeout(() => {
    toast.classList.remove('show');
    setTimeout(() => toast.remove(), 300);
  }, 3500);
}

// Language translations
const translations = {
  en: {
    heroHeadline: 'Small Acts of Kindness. Big Changes in Lives.',
    donateNow: 'Donate Now',
    rescueFood: 'Rescue Food',
    langLabel: 'EN / தமிழ்'
  },
  ta: {
    heroHeadline: 'சிறிய கருணைச் செயல்கள். வாழ்க்கையில் பெரிய மாற்றங்கள்.',
    donateNow: 'இப்போதே தானம் செய்',
    rescueFood: 'உணவை மீட்டெடு',
    langLabel: 'தமிழ் / EN'
  }
};

function toggleLang() {
  const current = SevaStore.getLanguage();
  const next = current === 'en' ? 'ta' : 'en';
  SevaStore.setLanguage(next);
  applyLanguage(next);
}

function applyLanguage(lang) {
  const t = translations[lang] || translations.en;
  const heroH = document.getElementById('heroHeadline');
  const langLabel = document.getElementById('langLabel');
  if (heroH && t.heroHeadline) heroH.textContent = t.heroHeadline;
  if (langLabel && t.langLabel) langLabel.textContent = t.langLabel;
}

// Notification Drawer Toggle
function toggleNotificationDropdown() {
  let popover = document.getElementById('notifPopover');
  if (!popover) {
    createNotifPopover();
    popover = document.getElementById('notifPopover');
  }
  popover.classList.toggle('hidden');
}

function createNotifPopover() {
  const notifBtn = document.querySelector('[aria-label="Notifications"]');
  if (!notifBtn) return;

  const notes = SevaStore.getNotifications();
  const popover = document.createElement('div');
  popover.id = 'notifPopover';
  popover.className = 'hidden absolute right-4 top-16 w-80 sm:w-96 bg-surface rounded-xl border border-outline-variant shadow-xl z-50 p-4 flex flex-col gap-3';
  
  popover.innerHTML = `
    <div class="flex items-center justify-between border-b border-outline-variant pb-2.5">
      <h3 class="font-headline text-title-md text-on-surface">Notifications</h3>
      <button type="button" onclick="clearAllNotifications()" class="font-label text-label-sm text-primary-hover hover:underline">Mark all read</button>
    </div>
    <div class="flex flex-col gap-2 max-h-72 overflow-y-auto" id="notifList">
      ${notes.map(n => `
        <div class="p-2.5 rounded-lg ${n.unread ? 'bg-primary-tint border border-primary/20' : 'bg-surface-secondary'} flex flex-col gap-1">
          <div class="flex items-center justify-between">
            <span class="font-label text-label-sm text-on-surface font-semibold">${n.title}</span>
            <span class="font-body text-body-sm text-on-surface-muted text-xs">${n.time}</span>
          </div>
          <p class="font-body text-body-sm text-on-surface-muted">${n.desc}</p>
        </div>
      `).join('')}
    </div>
  `;

  notifBtn.parentElement.classList.add('relative');
  notifBtn.parentElement.appendChild(popover);

  notifBtn.onclick = (e) => {
    e.stopPropagation();
    toggleNotificationDropdown();
  };

  document.addEventListener('click', (e) => {
    if (popover && !popover.contains(e.target) && !notifBtn.contains(e.target)) {
      popover.classList.add('hidden');
    }
  });
}

function clearAllNotifications() {
  SevaStore.markNotesRead();
  const badge = document.querySelector('[data-component-id="badge-numeric"]');
  if (badge) badge.classList.add('hidden');
  const notifList = document.getElementById('notifList');
  if (notifList) {
    notifList.innerHTML = `
      <div class="p-4 text-center font-body text-body-sm text-on-surface-muted">
        All notifications caught up!
      </div>
    `;
  }
  showToast('Notifications marked as read');
}

// Authentication Modal Handler
function handleLogin(event) {
  if (event) event.preventDefault();
  const roleSelect = document.getElementById('modalRoleSelect');
  const phoneInput = document.getElementById('modalPhoneInput');
  const role = roleSelect ? roleSelect.value : 'Customer / Donor';
  const phone = (phoneInput && phoneInput.value.trim()) ? phoneInput.value.trim() : '+91 98400 12345';

  const user = {
    name: role.includes('Restaurant') ? 'Dakshin Kitchen (Partner)' : role.includes('NGO') ? 'Anandam Bal Ashram' : 'Priya Venkatesh',
    role: role,
    phone: phone
  };

  SevaStore.setUser(user);
  const modal = document.getElementById('authModal');
  if (modal) modal.classList.add('hidden');

  updateUserUI();
  showToast(`Logged in successfully as ${user.role}!`);
}

function updateUserUI() {
  const user = SevaStore.getUser();
  const loginBtns = document.querySelectorAll('[aria-label="Login or Register"]');
  loginBtns.forEach(btn => {
    const textSpan = btn.querySelector('span:not(.material-symbols-outlined)');
    if (textSpan) {
      textSpan.textContent = user.name.split(' ')[0];
    }
  });
}

// Initialize on DOM load
document.addEventListener('DOMContentLoaded', () => {
  const lang = SevaStore.getLanguage();
  applyLanguage(lang);
  createNotifPopover();
  updateUserUI();
});
