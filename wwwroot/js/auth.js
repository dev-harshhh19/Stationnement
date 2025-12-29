// Auth utility for JWT token management with Remember Me support
const Auth = {
    // Get the appropriate storage based on Remember Me setting
    getStorage: function () {
        // Check if we have data in localStorage (Remember Me was checked)
        if (localStorage.getItem('accessToken')) {
            return localStorage;
        }
        // Fall back to sessionStorage
        return sessionStorage;
    },

    getUser: function () {
        // Check both storages, prioritize localStorage
        const userStr = localStorage.getItem('user') || sessionStorage.getItem('user');
        return userStr ? JSON.parse(userStr) : null;
    },

    getAccessToken: function () {
        // Check both storages, prioritize localStorage
        return localStorage.getItem('accessToken') || sessionStorage.getItem('accessToken');
    },

    isAuthenticated: function () {
        return !!this.getAccessToken();
    },

    logout: async function () {
        try {
            await fetch('/api/auth/logout', { method: 'POST' });
        } catch (e) {
            console.error('Logout error:', e);
        }
        // Clear both storages
        localStorage.removeItem('user');
        localStorage.removeItem('accessToken');
        localStorage.removeItem('rememberMe');
        sessionStorage.removeItem('user');
        sessionStorage.removeItem('accessToken');
        sessionStorage.removeItem('rememberMe');
    },

    apiRequest: async function (url, options = {}) {
        const token = this.getAccessToken();
        const headers = {
            'Content-Type': 'application/json',
            ...options.headers
        };

        if (token) {
            headers['Authorization'] = `Bearer ${token}`;
        }

        const response = await fetch(url, { ...options, headers });

        // Handle 401 - try to refresh token
        if (response.status === 401) {
            const refreshed = await this.refreshToken();
            if (refreshed) {
                headers['Authorization'] = `Bearer ${this.getAccessToken()}`;
                return fetch(url, { ...options, headers });
            } else {
                this.logout();
                window.location.href = '/Auth/Login';
            }
        }

        return response;
    },

    refreshToken: async function () {
        try {
            const response = await fetch('/api/auth/refresh', { method: 'POST' });
            const data = await response.json();

            if (data.success) {
                // Store in the same storage that was used for login
                const storage = this.getStorage();
                storage.setItem('accessToken', data.data.accessToken);
                return true;
            }
        } catch (e) {
            console.error('Token refresh failed:', e);
        }
        return false;
    }
};

// Make Auth available globally
window.Auth = Auth;

// Smart redirect - Dashboard if logged in, otherwise landing page
function smartRedirect() {
    if (Auth.isAuthenticated()) {
        window.location.href = '/Dashboard';
    } else {
        window.location.href = '/';
    }
}
window.smartRedirect = smartRedirect;
