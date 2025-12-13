# Prompt: Implementar Dark Mode no Dashboard

## Objetivo

Adicionar suporte a dark mode no dashboard web, permitindo que o usuário escolha entre tema claro, escuro ou automático (baseado no sistema operacional).

## Contexto

O dashboard atual usa apenas tema claro. Dark mode melhora a experiência de usuários que trabalham em ambientes escuros e reduz fadiga visual.

## Requisitos

### CSS Variables

```css
/* ZapJobs.AspNetCore/Dashboard/wwwroot/css/dashboard.css */

/* Light theme (default) */
:root {
    /* Background colors */
    --bg-primary: #ffffff;
    --bg-secondary: #f8f9fa;
    --bg-tertiary: #e9ecef;
    --bg-card: #ffffff;

    /* Text colors */
    --text-primary: #212529;
    --text-secondary: #6c757d;
    --text-muted: #adb5bd;

    /* Border colors */
    --border-color: #dee2e6;
    --border-light: #e9ecef;

    /* Status colors */
    --status-success: #28a745;
    --status-success-bg: #d4edda;
    --status-warning: #ffc107;
    --status-warning-bg: #fff3cd;
    --status-error: #dc3545;
    --status-error-bg: #f8d7da;
    --status-info: #17a2b8;
    --status-info-bg: #d1ecf1;
    --status-pending: #6c757d;
    --status-pending-bg: #e2e3e5;

    /* Interactive elements */
    --link-color: #007bff;
    --link-hover: #0056b3;
    --btn-primary-bg: #007bff;
    --btn-primary-text: #ffffff;

    /* Shadows */
    --shadow-sm: 0 1px 2px rgba(0,0,0,0.05);
    --shadow-md: 0 4px 6px rgba(0,0,0,0.1);
    --shadow-lg: 0 10px 15px rgba(0,0,0,0.1);

    /* Chart colors */
    --chart-line: #007bff;
    --chart-grid: #e9ecef;
}

/* Dark theme */
[data-theme="dark"] {
    /* Background colors */
    --bg-primary: #1a1a2e;
    --bg-secondary: #16213e;
    --bg-tertiary: #0f3460;
    --bg-card: #1f1f3d;

    /* Text colors */
    --text-primary: #e8e8e8;
    --text-secondary: #b8b8b8;
    --text-muted: #6c6c6c;

    /* Border colors */
    --border-color: #2d2d4a;
    --border-light: #252542;

    /* Status colors - slightly desaturated for dark mode */
    --status-success: #4ade80;
    --status-success-bg: #14532d;
    --status-warning: #facc15;
    --status-warning-bg: #713f12;
    --status-error: #f87171;
    --status-error-bg: #7f1d1d;
    --status-info: #38bdf8;
    --status-info-bg: #0c4a6e;
    --status-pending: #9ca3af;
    --status-pending-bg: #374151;

    /* Interactive elements */
    --link-color: #60a5fa;
    --link-hover: #93c5fd;
    --btn-primary-bg: #3b82f6;
    --btn-primary-text: #ffffff;

    /* Shadows - more subtle in dark mode */
    --shadow-sm: 0 1px 2px rgba(0,0,0,0.3);
    --shadow-md: 0 4px 6px rgba(0,0,0,0.4);
    --shadow-lg: 0 10px 15px rgba(0,0,0,0.5);

    /* Chart colors */
    --chart-line: #60a5fa;
    --chart-grid: #2d2d4a;
}

/* Apply variables to elements */
body {
    background-color: var(--bg-secondary);
    color: var(--text-primary);
}

.card {
    background-color: var(--bg-card);
    border-color: var(--border-color);
    box-shadow: var(--shadow-sm);
}

.table {
    color: var(--text-primary);
}

.table th {
    background-color: var(--bg-tertiary);
    border-color: var(--border-color);
}

.table td {
    border-color: var(--border-light);
}

.table tbody tr:hover {
    background-color: var(--bg-tertiary);
}

.nav-link {
    color: var(--text-secondary);
}

.nav-link:hover,
.nav-link.active {
    color: var(--text-primary);
}

.sidebar {
    background-color: var(--bg-primary);
    border-color: var(--border-color);
}

/* Status badges */
.badge-success {
    background-color: var(--status-success-bg);
    color: var(--status-success);
}
.badge-warning {
    background-color: var(--status-warning-bg);
    color: var(--status-warning);
}
.badge-error {
    background-color: var(--status-error-bg);
    color: var(--status-error);
}
.badge-info {
    background-color: var(--status-info-bg);
    color: var(--status-info);
}
.badge-pending {
    background-color: var(--status-pending-bg);
    color: var(--status-pending);
}
```

### Theme Switcher Component

```html
<!-- No header do dashboard -->
<div class="theme-switcher">
    <button id="theme-toggle" class="btn btn-icon" title="Toggle theme">
        <svg id="theme-icon-light" class="icon" viewBox="0 0 24 24">
            <path d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z"/>
        </svg>
        <svg id="theme-icon-dark" class="icon hidden" viewBox="0 0 24 24">
            <path d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z"/>
        </svg>
    </button>
    <div id="theme-dropdown" class="dropdown hidden">
        <button data-theme="light" class="dropdown-item">
            <span class="icon">☀️</span> Light
        </button>
        <button data-theme="dark" class="dropdown-item">
            <span class="icon">🌙</span> Dark
        </button>
        <button data-theme="auto" class="dropdown-item">
            <span class="icon">💻</span> System
        </button>
    </div>
</div>
```

### JavaScript para Theme Management

```javascript
// ZapJobs.AspNetCore/Dashboard/wwwroot/js/theme.js

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
```

### Atualizar Layout HTML

```html
<!-- ZapJobs.AspNetCore/Dashboard/Templates/layout.html -->
<!DOCTYPE html>
<html lang="en">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <meta name="theme-color" content="#ffffff">
    <title>ZapJobs Dashboard</title>
    <link rel="stylesheet" href="{{basePath}}/css/dashboard.css">

    <!-- Prevent flash of wrong theme -->
    <script>
        (function() {
            const saved = localStorage.getItem('zapjobs-theme') || 'auto';
            let theme;
            if (saved === 'auto') {
                theme = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
            } else {
                theme = saved;
            }
            document.documentElement.setAttribute('data-theme', theme);
            document.querySelector('meta[name="theme-color"]').content =
                theme === 'dark' ? '#1a1a2e' : '#ffffff';
        })();
    </script>
</head>
<body>
    <!-- ... rest of the layout ... -->
    <script src="{{basePath}}/js/theme.js"></script>
</body>
</html>
```

### CSS para Theme Switcher

```css
/* Theme switcher styles */
.theme-switcher {
    position: relative;
}

.btn-icon {
    background: transparent;
    border: none;
    padding: 0.5rem;
    cursor: pointer;
    color: var(--text-secondary);
    border-radius: 0.375rem;
    transition: background-color 0.15s, color 0.15s;
}

.btn-icon:hover {
    background-color: var(--bg-tertiary);
    color: var(--text-primary);
}

.btn-icon .icon {
    width: 20px;
    height: 20px;
    stroke: currentColor;
    stroke-width: 2;
    fill: none;
}

.hidden {
    display: none !important;
}

.dropdown {
    position: absolute;
    top: 100%;
    right: 0;
    margin-top: 0.5rem;
    background-color: var(--bg-card);
    border: 1px solid var(--border-color);
    border-radius: 0.5rem;
    box-shadow: var(--shadow-lg);
    min-width: 140px;
    z-index: 1000;
    overflow: hidden;
}

.dropdown-item {
    display: flex;
    align-items: center;
    gap: 0.5rem;
    width: 100%;
    padding: 0.625rem 1rem;
    background: none;
    border: none;
    color: var(--text-primary);
    cursor: pointer;
    text-align: left;
    transition: background-color 0.15s;
}

.dropdown-item:hover {
    background-color: var(--bg-tertiary);
}

.dropdown-item.active {
    background-color: var(--bg-tertiary);
    font-weight: 500;
}

/* Transition for theme changes */
body,
.card,
.sidebar,
.table,
.nav-link {
    transition: background-color 0.2s ease, color 0.2s ease, border-color 0.2s ease;
}
```

### Configuração de Opções

```csharp
// ZapJobs.AspNetCore/Dashboard/DashboardOptions.cs
public class DashboardOptions
{
    // ... existing options ...

    /// <summary>
    /// Default theme for new users (light, dark, or auto)
    /// </summary>
    public string DefaultTheme { get; set; } = "auto";

    /// <summary>
    /// Allow users to change theme
    /// </summary>
    public bool AllowThemeChange { get; set; } = true;
}
```

## Considerações de Acessibilidade

1. **Contraste adequado**: Cores escolhidas respeitam WCAG AA
2. **Sem dependência de cor**: Status usam ícones além de cores
3. **Reduced motion**: Respeitar `prefers-reduced-motion`

```css
@media (prefers-reduced-motion: reduce) {
    body,
    .card,
    .sidebar,
    .table,
    .nav-link {
        transition: none;
    }
}
```

## Arquivos a Criar/Modificar

1. `ZapJobs.AspNetCore/Dashboard/wwwroot/css/dashboard.css` - Adicionar CSS variables
2. `ZapJobs.AspNetCore/Dashboard/wwwroot/js/theme.js` - Novo arquivo
3. `ZapJobs.AspNetCore/Dashboard/Templates/layout.html` - Atualizar com theme switcher
4. `ZapJobs.AspNetCore/Dashboard/DashboardOptions.cs` - Adicionar opções de tema

## Critérios de Aceitação

1. [ ] Tema claro funciona corretamente
2. [ ] Tema escuro funciona corretamente
3. [ ] Opção "auto" segue preferência do sistema
4. [ ] Preferência é salva no localStorage
5. [ ] Não há flash de tema errado no carregamento
6. [ ] Transição suave entre temas
7. [ ] Todos os componentes suportam ambos os temas
8. [ ] Contraste de cores atende WCAG AA
9. [ ] Funciona em todos os navegadores modernos
