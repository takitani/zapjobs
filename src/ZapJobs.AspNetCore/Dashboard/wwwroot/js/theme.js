// ZapJobs Dashboard Theme Manager
const ThemeManager = {
    STORAGE_KEY: 'zapjobs-theme',

    init() {
        // Load saved preference or default to auto
        const saved = localStorage.getItem(this.STORAGE_KEY) || 'auto';
        this.setTheme(saved);

        // Listen for system preference changes
        window.matchMedia('(prefers-color-scheme: dark)')
            .addEventListener('change', e => {
                if (this.getPreference() === 'auto') {
                    this.applyTheme(e.matches ? 'dark' : 'light');
                }
            });

        // Setup UI
        this.setupToggle();
        this.updateUI(saved);
    },

    getPreference() {
        return localStorage.getItem(this.STORAGE_KEY) || 'auto';
    },

    setTheme(preference) {
        localStorage.setItem(this.STORAGE_KEY, preference);

        if (preference === 'auto') {
            const systemDark = window.matchMedia('(prefers-color-scheme: dark)').matches;
            this.applyTheme(systemDark ? 'dark' : 'light');
        } else {
            this.applyTheme(preference);
        }

        this.updateUI(preference);
    },

    applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);

        // Update meta theme-color for mobile browsers
        const metaTheme = document.querySelector('meta[name="theme-color"]');
        if (metaTheme) {
            metaTheme.content = theme === 'dark' ? '#1a1a2e' : '#ffffff';
        }
    },

    updateUI(preference) {
        const lightIcon = document.getElementById('theme-icon-light');
        const darkIcon = document.getElementById('theme-icon-dark');

        if (lightIcon && darkIcon) {
            const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
            lightIcon.classList.toggle('hidden', isDark);
            darkIcon.classList.toggle('hidden', !isDark);
        }

        // Update dropdown selection
        document.querySelectorAll('[data-theme]').forEach(btn => {
            btn.classList.toggle('active', btn.dataset.theme === preference);
        });
    },

    setupToggle() {
        const toggle = document.getElementById('theme-toggle');
        const dropdown = document.getElementById('theme-dropdown');

        if (!toggle || !dropdown) return;

        toggle.addEventListener('click', (e) => {
            e.stopPropagation();
            dropdown.classList.toggle('hidden');
        });

        dropdown.querySelectorAll('[data-theme]').forEach(btn => {
            btn.addEventListener('click', () => {
                this.setTheme(btn.dataset.theme);
                dropdown.classList.add('hidden');
            });
        });

        // Close dropdown on outside click
        document.addEventListener('click', () => {
            dropdown.classList.add('hidden');
        });
    }
};

// Initialize on DOM ready
document.addEventListener('DOMContentLoaded', () => ThemeManager.init());
