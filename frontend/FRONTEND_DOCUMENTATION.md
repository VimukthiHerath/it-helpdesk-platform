# Frontend Documentation

Snapshot of the `frontend` folder as of 2026-09-06.

## Contents

- [Overview](#overview)
- [Project tree](#project-tree)
- [Application behavior](#application-behavior)
- [Complete source and configuration](#complete-source-and-configuration)
- [Binary assets](#binary-assets)
- [Run and test commands](#run-and-test-commands)

## Overview

This frontend is a Create React App application for an IT helpdesk platform.

- Framework: React 19
- Routing: `react-router-dom`
- Notifications: `react-toastify`
- API services used by the UI:
  - Auth API: `http://localhost:5121`
  - Ticket API: `http://localhost:5164`
- Authentication storage: browser `localStorage` key `token`

## Project tree

```text
frontend/
├── .gitignore
├── FRONTEND_DOCUMENTATION.md
├── README.md
├── package-lock.json
├── package.json
├── public/
│   ├── favicon.ico
│   ├── index.html
│   ├── logo192.png
│   ├── logo512.png
│   ├── manifest.json
│   └── robots.txt
└── src/
    ├── App.css
    ├── App.js
    ├── App.test.js
    ├── index.css
    ├── index.js
    ├── logo.svg
    ├── reportWebVitals.js
    ├── setupTests.js
    ├── features/
    │   ├── auth/
    │   │   ├── components/
    │   │   │   ├── LoginForm.css
    │   │   │   ├── LoginForm.jsx
    │   │   │   ├── RegisterForm.css
    │   │   │   └── RegisterForm.jsx
    │   │   └── pages/
    │   │       ├── LoginPage.jsx
    │   │       └── RegisterPage.jsx
    │   └── ticket/
    │       └── components/
    │           ├── ticketCreateForm.css
    │           └── ticketCreateForm.jsx
    └── pages/
        ├── dashboard.css
        ├── dashboard.jsx
        ├── myTickets.css
        └── myTickets.jsx
```

## Application behavior

### Routes

| Route | Access | Component | Behavior |
|---|---|---|---|
| `/login` | Public | `LoginPage` | Shows the login form. Authenticated users are redirected to `/`. |
| `/` | Protected | `Dashboard` | Shows ticket creation and dashboard actions. |
| `/my-tickets` | Protected | `MyTickets` | Loads and displays the current user's tickets. |
| Any other route | Protected | Redirect | Redirects to `/`. |

### Authentication

`App.js` reads the JWT from `localStorage`, checks that it has three sections, decodes the payload, checks the `exp` claim, and then verifies the token with `GET http://localhost:5121/api/auth/me`. Invalid, expired, or rejected tokens are removed and the user is sent to `/login`.

The login form sends credentials to `POST http://localhost:5121/api/auth/login`. On success it stores the returned `token` and navigates to the dashboard.

### Ticket creation

The dashboard sends a new ticket to `POST http://localhost:5164/api/ticket` with the bearer token and this JSON body:

```json
{
  "issueType": "string",
  "urgency": 0,
  "description": "string"
}
```

The description is limited to 255 characters. Urgency values map to one, six, twelve, or twenty-four hours.

### Ticket history

`MyTickets` sends `GET http://localhost:5164/api/ticket/mine` with the bearer token. It presents a selectable list and a detail panel. A 401 response clears the token and redirects to login.

## Complete source and configuration

### `package.json`

```json
{
  "name": "frontend",
  "version": "0.1.0",
  "private": true,
  "dependencies": {
    "@testing-library/dom": "^10.4.1",
    "@testing-library/jest-dom": "^6.9.1",
    "@testing-library/react": "^16.3.2",
    "@testing-library/user-event": "^13.5.0",
    "react": "^19.2.8",
    "react-dom": "^19.2.8",
    "react-router-dom": "^7.18.2",
    "react-scripts": "5.0.1",
    "react-toastify": "^11.1.0",
    "web-vitals": "^2.1.4"
  },
  "scripts": {
    "start": "react-scripts start",
    "build": "react-scripts build",
    "test": "react-scripts test",
    "eject": "react-scripts eject"
  },
  "eslintConfig": {
    "extends": ["react-app", "react-app/jest"]
  },
  "browserslist": {
    "production": [">0.2%", "not dead", "not op_mini all"],
    "development": ["last 1 chrome version", "last 1 firefox version", "last 1 safari version"]
  }
}
```

### `.gitignore`

```gitignore
# See https://help.github.com/articles/ignoring-files/ for more about ignoring files.

# dependencies
/node_modules
/.pnp
.pnp.js

# testing
/coverage

# production
/build

# misc
.DS_Store
.env.local
.env.development.local
.env.test.local
.env.production.local

npm-debug.log*
yarn-debug.log*
yarn-error.log*
```

### `src/index.js`

```javascript
import React from 'react';
import ReactDOM from 'react-dom/client';
import './index.css';
import App from './App';
import reportWebVitals from './reportWebVitals';

const root = ReactDOM.createRoot(document.getElementById('root'));
root.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);

// If you want to start measuring performance in your app, pass a function
// to log results (for example: reportWebVitals(console.log))
// or send to an analytics endpoint. Learn more: https://bit.ly/CRA-vitals
reportWebVitals();
```

### `src/App.js`

```javascript
import React from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import LoginPage from './features/auth/pages/LoginPage';
import Dashboard from './pages/dashboard';
import MyTickets from './pages/myTickets';

import './App.css';

const AUTH_API_URL = 'http://localhost:5121/api/auth/me';

const isAuthenticated = async () => {
  const token = localStorage.getItem('token');

  if (!token) {
    return false;
  }

  try {
    const tokenParts = token.split('.');
    if (tokenParts.length !== 3) {
      throw new Error('Invalid token format');
    }

    const payload = JSON.parse(atob(tokenParts[1].replace(/-/g, '+').replace(/_/g, '/')));
    if (typeof payload.exp !== 'number' || payload.exp <= Date.now() / 1000) {
      throw new Error('Token expired');
    }

    const response = await fetch(AUTH_API_URL, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });

    if (!response.ok) {
      throw new Error('Token rejected by API');
    }

    return true;
  } catch {
    localStorage.removeItem('token');
    return false;
  }
};

const ProtectedRoute = ({ children }) => {
  const [authenticated, setAuthenticated] = React.useState(null);

  React.useEffect(() => {
    isAuthenticated().then(setAuthenticated);
  }, []);

  if (authenticated === null) {
    return null;
  }

  return authenticated ? children : <Navigate to="/login" replace />;
};

const PublicRoute = ({ children }) => {
  const [authenticated, setAuthenticated] = React.useState(null);

  React.useEffect(() => {
    isAuthenticated().then(setAuthenticated);
  }, []);

  if (authenticated === null) {
    return null;
  }

  return authenticated ? <Navigate to="/" replace /> : children;
};

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route
          path="/login"
          element={<PublicRoute><LoginPage /></PublicRoute>}
        />
        <Route
          path="/"
          element={<ProtectedRoute><Dashboard /></ProtectedRoute>}
        />
        <Route
          path="/my-tickets"
          element={<ProtectedRoute><MyTickets /></ProtectedRoute>}
        />
        <Route
          path="*"
          element={<ProtectedRoute><Navigate to="/" replace /></ProtectedRoute>}
        />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
```

### `src/index.css`

```css
body {
  margin: 0;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'Roboto', 'Oxygen',
    'Ubuntu', 'Cantarell', 'Fira Sans', 'Droid Sans', 'Helvetica Neue',
    sans-serif;
  -webkit-font-smoothing: antialiased;
  -moz-osx-font-smoothing: grayscale;
}

code {
  font-family: source-code-pro, Menlo, Monaco, Consolas, 'Courier New',
    monospace;
}
```

### `src/App.css`

This stylesheet is currently empty.

```css
```

### `src/features/auth/pages/LoginPage.jsx`

```jsx
import LoginForm from '../components/LoginForm';

const LoginPage = () => {
    return (
        <div>
            <LoginForm/>
        </div>
    );
}

export default LoginPage;
```

### `src/features/auth/pages/RegisterPage.jsx`

```jsx
import React from 'react';

const RegisterPage = () => {
    return (
        <div>
            Register Page
        </div>
    );
}

export default RegisterPage;
```

### `src/features/auth/components/LoginForm.jsx`

```jsx
import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast, ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';
import './LoginForm.css';

const LoginForm = () => {
    const [formData, setFormData] = useState({ email: '', password: '' });
    const [errors, setErrors] = useState({ email: '', password: '' });
    const [isLoading, setIsLoading] = useState(false);
    const navigate = useNavigate();

    const handleChange = (e) => {
        const { name, value } = e.target;

        setFormData((prev) => ({
            ...prev,
            [name]: value,
        }));

        if (errors[name]) {
            setErrors((prev) => ({
                ...prev,
                [name]: '',
            }));
        }
    };

    const validateForm = () => {
        const newErrors = {};

        if (!formData.email.trim()) {
            newErrors.email = 'Email is required';
        } else if (!/\S+@\S+\.\S+/.test(formData.email)) {
            newErrors.email = 'Please enter a valid email address';
        }

        if (!formData.password) {
            newErrors.password = 'Password is required';
        } else if (formData.password.length < 6) {
            newErrors.password = 'Password must be at least 6 characters';
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    };

    const handleSubmit = async (e) => {
        e.preventDefault();

        if(!validateForm()) return;

        setIsLoading(true);

        try {
            const response = await fetch('http://localhost:5121/api/auth/login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify(formData),
            });

            const data = await response.json().catch(() => ({}));

            if (!response.ok) {
                const message = data?.message || 'Login failed. Please try again.';
                throw new Error(message);
            }

            localStorage.setItem('token', data.token || '');
            toast.success('Login successful! Redirecting...', {
                position: 'top-right',
                autoClose: 1800,
            });

            navigate('/', { replace: true });

            console.log('Login successful:', data);
        } catch (error) {
            console.error('Error logging in:', error);
            toast.error(error.message || 'Something went wrong during login.', {
                position: 'top-right',
            });
        } finally {
            setIsLoading(false);
        }
    };

    return (
        <div className="auth-shell">
            <ToastContainer />
            <div className="auth-card">
                <div className="auth-header">
                    <p className="eyebrow">Welcome back</p>
                    <h2>Sign in</h2>
                </div>

                <form onSubmit={handleSubmit} className="auth-form" noValidate>
                    <div className="field-group">
                        <label htmlFor="email">Email</label>
                        <input
                            id="email"
                            type="email"
                            name="email"
                            value={formData.email}
                            onChange={handleChange}
                            className={errors.email ? 'input-error' : ''}
                            placeholder="you@example.com"
                            autoComplete="email"
                        />
                        {errors.email && <span className="error-text">{errors.email}</span>}
                    </div>

                    <div className="field-group">
                        <label htmlFor="password">Password</label>
                        <input
                            id="password"
                            type="password"
                            name="password"
                            value={formData.password}
                            onChange={handleChange}
                            className={errors.password ? 'input-error' : ''}
                            placeholder="Enter your password"
                            autoComplete="current-password"
                        />
                        {errors.password && <span className="error-text">{errors.password}</span>}
                    </div>

                    <button type="submit" className="primary-btn" disabled={isLoading}>
                        {isLoading ? 'Logging in...' : 'Login'}
                    </button>
                </form>
            </div>
        </div>
    );
};

export default LoginForm;
```

### `src/features/auth/components/LoginForm.css`

```css
.auth-shell {
  min-height: 100vh;
  display: grid;
  place-items: center;
  background: #0f172a;
  padding: 24px;
  font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
}

.auth-card {
  width: min(100%, 430px);
  background: rgba(15, 23, 42, 0.78);
  border: 1px solid rgba(148, 163, 184, 0.18);
  box-shadow: 0 20px 50px rgba(15, 23, 42, 0.35);
  border-radius: 20px;
  padding: 32px 28px;
  backdrop-filter: blur(12px);
}

.auth-header {
  margin-bottom: 24px;
}

.eyebrow {
  margin: 0 0 8px;
  text-transform: uppercase;
  letter-spacing: 0.12em;
  font-size: 11px;
  color: #7dd3fc;
  font-weight: 700;
}

.auth-header h2 {
  margin: 0;
  color: #f8fafc;
  font-size: 2rem;
  font-weight: 700;
}

.auth-form {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.field-group label {
  color: #e2e8f0;
  font-size: 0.95rem;
  font-weight: 600;
}

.field-group input {
  border: 1px solid rgba(148, 163, 184, 0.32);
  background: rgba(15, 23, 42, 0.6);
  color: #f8fafc;
  border-radius: 12px;
  padding: 14px 14px;
  font-size: 1rem;
  transition: border-color 0.25s ease, box-shadow 0.25s ease, transform 0.15s ease;
}

.field-group input:focus {
  outline: none;
  border-color: #38bdf8;
  box-shadow: 0 0 0 3px rgba(56, 189, 248, 0.2);
}

.input-error {
  border-color: #f87171 !important;
  box-shadow: 0 0 0 3px rgba(248, 113, 113, 0.15) !important;
}

.error-text {
  color: #fca5a5;
  font-size: 0.8rem;
}

.primary-btn {
  margin-top: 6px;
  border: none;
  border-radius: 12px;
  padding: 14px 18px;
  background:  #2563eb;
  color: white;
  font-size: 1rem;
  font-weight: 700;
  cursor: pointer;
  transition: transform 0.18s ease, opacity 0.18s ease;
}

.primary-btn:hover:not(:disabled) {
  transform: translateY(-1px);
}

.primary-btn:disabled {
  opacity: 0.7;
  cursor: not-allowed;
}

@media (max-width: 480px) {
  .auth-card {
    padding: 24px 18px;
  }

  .auth-header h2 {
    font-size: 1.6rem;
  }
}
```

### `src/features/auth/components/RegisterForm.jsx`

This file is currently empty.

### `src/features/auth/components/RegisterForm.css`

This file is currently empty.

### `src/features/ticket/components/ticketCreateForm.jsx`

```jsx
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import './ticketCreateForm.css';

const TICKET_API_URL = 'http://localhost:5164/api/ticket';

const initialForm = {
    issueType: '',
    urgency: 0,
    description: '',
};

const urgencyOptions = [
    { value: 0, label: 'Within 1 hour' },
    { value: 1, label: 'Within 6 hours' },
    { value: 2, label: 'Within 12 hours' },
    { value: 3, label: 'Within 24 hours' },
];

const TicketCreateForm = () => {
    const [formData, setFormData] = useState(initialForm);
    const [errors, setErrors] = useState({});
    const [status, setStatus] = useState({ type: '', message: '' });
    const [isSubmitting, setIsSubmitting] = useState(false);
    const navigate = useNavigate();

    const handleChange = (event) => {
        const { name, value } = event.target;
        setFormData((current) => ({
            ...current,
            [name]: name === 'urgency' ? Number(value) : value,
        }));
        setErrors((current) => ({ ...current, [name]: '' }));
        setStatus({ type: '', message: '' });
    };

    const validate = () => {
        const nextErrors = {};
        const description = formData.description.trim();
        const issueType = formData.issueType.trim();

        if (!issueType) nextErrors.issueType = 'Enter an issue type.';
        if (!description) nextErrors.description = 'Describe the problem.';
        if (description.length > 255) nextErrors.description = 'Use 255 characters or fewer.';

        setErrors(nextErrors);
        return Object.keys(nextErrors).length === 0;
    };

    const handleSubmit = async (event) => {
        event.preventDefault();
        if (!validate()) return;

        const token = localStorage.getItem('token');
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        setIsSubmitting(true);
        setStatus({ type: '', message: '' });

        try {
            const response = await fetch(TICKET_API_URL, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    Authorization: `Bearer ${token}`,
                },
                body: JSON.stringify({
                    issueType: formData.issueType.trim(),
                    urgency: formData.urgency,
                    description: formData.description.trim(),
                }),
            });
            const data = await response.json().catch(() => ({}));

            if (response.status === 401) {
                localStorage.removeItem('token');
                navigate('/login', { replace: true });
                return;
            }

            if (!response.ok) {
                throw new Error(data?.message || 'Unable to create the ticket.');
            }

            setFormData(initialForm);
            setStatus({
                type: 'success',
                message: `Ticket #${data.ticketId} created successfully.`,
            });
        } catch (error) {
            setStatus({ type: 'error', message: error.message });
        } finally {
            setIsSubmitting(false);
        }
    };

    return (
        <section className="ticket-panel" aria-labelledby="create-ticket-title">
            <div className="ticket-panel__intro">
                <p className="ticket-panel__eyebrow">Support desk</p>
                <h2 id="create-ticket-title">Open a new ticket</h2>
                <p>Give us the context we need to get the right person on it.</p>
            </div>

            <form className="ticket-form" onSubmit={handleSubmit} noValidate>
                <div className="ticket-form__row">
                    <div className="ticket-field">
                        <label htmlFor="issueType">Issue type</label>
                        <input
                            id="issueType"
                            name="issueType"
                            value={formData.issueType}
                            onChange={handleChange}
                            placeholder="Issue type"
                            maxLength={100}
                            className={errors.issueType ? 'ticket-input ticket-input--error' : 'ticket-input'}
                            aria-invalid={Boolean(errors.issueType)}
                        />
                        {errors.issueType && <span className="ticket-error">{errors.issueType}</span>}
                    </div>

                    <div className="ticket-field">
                        <label htmlFor="urgency">Required by</label>
                        <select id="urgency" name="urgency" value={formData.urgency} onChange={handleChange} className="ticket-input">
                            {urgencyOptions.map((option) => (
                                <option key={option.value} value={option.value}>{option.label}</option>
                            ))}
                        </select>
                    </div>
                </div>

                <div className="ticket-field">
                    <div className="ticket-label-row">
                        <label htmlFor="description">What is happening?</label>
                        <span>{formData.description.length}/255</span>
                    </div>
                    <textarea
                        id="description"
                        name="description"
                        value={formData.description}
                        onChange={handleChange}
                        placeholder="Tell us what you were trying to do, what happened, and any error message you saw."
                        maxLength={255}
                        rows={6}
                        className={errors.description ? 'ticket-input ticket-input--error' : 'ticket-input'}
                        aria-invalid={Boolean(errors.description)}
                    />
                    {errors.description && <span className="ticket-error">{errors.description}</span>}
                </div>

                <div className="ticket-form__footer">
                    <div aria-live="polite" className={`ticket-status ticket-status--${status.type}`}>
                        {status.message}
                    </div>
                    <button type="submit" className="ticket-submit" disabled={isSubmitting}>
                        {isSubmitting ? 'Creating ticket...' : 'Create ticket'}
                    </button>
                </div>
            </form>
        </section>
    );
};

export default TicketCreateForm;
```

### `src/features/ticket/components/ticketCreateForm.css`

```css
.ticket-panel {
    width: min(100%, 760px);
    margin-top: 32px;
    padding: 32px;
    border: 1px solid #dbe4ee;
    border-radius: 18px;
    background: linear-gradient(135deg, #ffffff 0%, #f4f8fb 100%);
    box-shadow: 0 18px 40px rgba(26, 55, 82, 0.1);
    color: #183247;
}

.ticket-panel__intro {
    border-bottom: 1px solid #dbe4ee;
    padding-bottom: 22px;
}

.ticket-panel__eyebrow {
    margin: 0 0 8px;
    color: #007f82;
    font-size: 0.72rem;
    font-weight: 800;
    letter-spacing: 0.14em;
    text-transform: uppercase;
}

.ticket-panel h2 {
    margin: 0;
    color: #163247;
    font-size: clamp(1.5rem, 3vw, 2rem);
    line-height: 1.1;
}

.ticket-panel__intro > p:last-child {
    margin: 10px 0 0;
    color: #587084;
    line-height: 1.5;
}

.ticket-form {
    display: flex;
    flex-direction: column;
    gap: 22px;
    padding-top: 24px;
}

.ticket-form__row {
    display: grid;
    grid-template-columns: 1.15fr 0.85fr;
    gap: 18px;
}

.ticket-field {
    display: flex;
    flex-direction: column;
    gap: 8px;
}

.ticket-field label,
.ticket-label-row {
    color: #29475d;
    font-size: 0.9rem;
    font-weight: 750;
}

.ticket-label-row {
    display: flex;
    justify-content: space-between;
    gap: 12px;
}

.ticket-label-row span {
    color: #7890a0;
    font-size: 0.78rem;
    font-weight: 500;
}

.ticket-input {
    width: 100%;
    box-sizing: border-box;
    border: 1px solid #c9d8e3;
    border-radius: 10px;
    background: #ffffff;
    color: #183247;
    font: inherit;
    padding: 12px 13px;
    transition: border-color 160ms ease, box-shadow 160ms ease;
}

textarea.ticket-input {
    min-height: 132px;
    resize: vertical;
}

.ticket-input::placeholder {
    color: #8ca0ae;
}

.ticket-input:focus {
    outline: none;
    border-color: #008f91;
    box-shadow: 0 0 0 3px rgba(0, 143, 145, 0.14);
}

.ticket-input--error {
    border-color: #d45b5b;
}

.ticket-error {
    color: #b33b45;
    font-size: 0.78rem;
}

.ticket-form__footer {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 16px;
    min-height: 44px;
}

.ticket-status {
    min-height: 20px;
    font-size: 0.86rem;
}

.ticket-status--success { color: #18734f; }
.ticket-status--error { color: #b33b45; }

.ticket-submit {
    border: 0;
    border-radius: 10px;
    padding: 12px 20px;
    background: #007f82;
    color: #ffffff;
    cursor: pointer;
    font: inherit;
    font-weight: 750;
    transition: background 160ms ease, transform 160ms ease;
}

.ticket-submit:hover:not(:disabled) {
    background: #00696b;
    transform: translateY(-1px);
}

.ticket-submit:disabled {
    cursor: wait;
    opacity: 0.65;
}

@media (max-width: 600px) {
    .ticket-panel {
        padding: 24px 18px;
    }

    .ticket-form__row,
    .ticket-form__footer {
        grid-template-columns: 1fr;
    }

    .ticket-form__footer {
        align-items: stretch;
        flex-direction: column;
    }

    .ticket-submit {
        width: 100%;
    }
}
```

### `src/pages/dashboard.jsx`

```jsx
import { useNavigate } from 'react-router-dom';
import TicketCreateForm from '../features/ticket/components/ticketCreateForm';
import './dashboard.css';

const Dashboard = () => {
    const navigate = useNavigate();

    const handleLogout = () => {
        localStorage.removeItem('token');
        navigate('/login', { replace: true });
    };

    return (
        <main className="dashboard-shell">
            <div className="dashboard-header">
                <div>
                    <p className="dashboard-kicker">IT helpdesk</p>
                    <h1>Dashboard</h1>
                    <p className="dashboard-welcome">Keep an eye on your requests and get support moving.</p>
                </div>
                <div className="dashboard-actions">
                    <button type="button" className="dashboard-tickets-button" onClick={() => navigate('/my-tickets')}>
                        View my tickets
                    </button>
                    <button type="button" className="dashboard-logout-button" onClick={handleLogout}>
                        Log out
                    </button>
                </div>
            </div>

            <TicketCreateForm />
        </main>
    );
};

export default Dashboard;
```

### `src/pages/dashboard.css`

```css
.dashboard-shell {
    min-height: 100vh;
    box-sizing: border-box;
    padding: clamp(24px, 5vw, 64px);
    background: #f5f8f6;
    color: #183247;
}

.dashboard-header {
    display: flex;
    align-items: flex-start;
    justify-content: space-between;
    gap: 24px;
    width: min(100%, 1120px);
    margin: 0 auto;
}

.dashboard-kicker {
    margin: 0 0 10px;
    color: #007f82;
    font-size: 0.72rem;
    font-weight: 800;
    letter-spacing: 0.14em;
    text-transform: uppercase;
}

.dashboard-header h1 {
    margin: 0;
    color: #163247;
    font-size: clamp(2rem, 5vw, 3.5rem);
    line-height: 1;
}

.dashboard-welcome {
    margin: 14px 0 0;
    color: #587084;
    line-height: 1.5;
}

.dashboard-actions {
    display: flex;
    flex-wrap: wrap;
    justify-content: flex-end;
    gap: 10px;
}

.dashboard-actions button {
    border-radius: 10px;
    padding: 11px 16px;
    font: inherit;
    font-weight: 750;
    cursor: pointer;
}

.dashboard-tickets-button {
    border: 1px solid #007f82;
    background: #007f82;
    color: #ffffff;
}

.dashboard-tickets-button:hover { background: #00696b; }

.dashboard-logout-button {
    border: 1px solid #c9d8e3;
    background: transparent;
    color: #29475d;
}

.dashboard-logout-button:hover { background: #e9f0f0; }

.dashboard-shell .ticket-panel {
    margin-right: auto;
    margin-left: auto;
}

@media (max-width: 650px) {
    .dashboard-header { flex-direction: column; }
    .dashboard-actions { justify-content: flex-start; }
}
```

### `src/pages/myTickets.jsx`

```jsx
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import './myTickets.css';

const TICKET_API_URL = 'http://localhost:5164/api/ticket/mine';

const urgencyLabels = ['Within 1 hour', 'Within 6 hours', 'Within 12 hours', 'Within 24 hours'];
const statusLabels = ['Unassigned', 'Assigned', 'Resolved'];

const formatLabel = (value, labels) => {
    if (typeof value === 'number' && labels[value]) return labels[value];
    if (typeof value === 'string') {
        const normalized = value.replaceAll('_', ' ');
        return normalized.charAt(0).toUpperCase() + normalized.slice(1);
    }
    return 'Not specified';
};

const formatDate = (value) => {
    if (!value) return 'Date unavailable';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? 'Date unavailable' : date.toLocaleDateString(undefined, {
        year: 'numeric', month: 'short', day: 'numeric',
    });
};

const MyTickets = () => {
    const [tickets, setTickets] = useState([]);
    const [selectedTicket, setSelectedTicket] = useState(null);
    const [state, setState] = useState({ loading: true, error: '' });
    const navigate = useNavigate();

    useEffect(() => {
        const loadTickets = async () => {
            const token = localStorage.getItem('token');
            if (!token) {
                navigate('/login', { replace: true });
                return;
            }

            try {
                const response = await fetch(TICKET_API_URL, {
                    headers: { Authorization: `Bearer ${token}` },
                });
                const data = await response.json().catch(() => ([]));

                if (response.status === 401) {
                    localStorage.removeItem('token');
                    navigate('/login', { replace: true });
                    return;
                }
                if (!response.ok) throw new Error(data?.message || 'Unable to load your tickets.');

                const loadedTickets = Array.isArray(data) ? data : [];
                setTickets(loadedTickets);
                setSelectedTicket(loadedTickets[0] || null);
            } catch (error) {
                setState({ loading: false, error: error.message || 'Unable to load your tickets.' });
                return;
            }

            setState({ loading: false, error: '' });
        };

        loadTickets();
    }, [navigate]);

    return (
        <main className="tickets-page">
            <header className="tickets-page__header">
                <button type="button" className="back-button" onClick={() => navigate('/')}>
                    Back to dashboard
                </button>
                <p className="tickets-page__kicker">Support desk / Your activity</p>
                <h1>My tickets</h1>
                <p className="tickets-page__summary">A read-only view of every request you have raised.</p>
            </header>

            {state.loading && <div className="tickets-message">Loading your tickets...</div>}
            {!state.loading && state.error && (
                <div className="tickets-message tickets-message--error" role="alert">{state.error}</div>
            )}
            {!state.loading && !state.error && tickets.length === 0 && (
                <div className="tickets-message tickets-message--empty">
                    <strong>No tickets yet</strong>
                    <span>Your submitted requests will appear here.</span>
                </div>
            )}

            {!state.loading && !state.error && tickets.length > 0 && (
                <div className="tickets-layout">
                    <section className="ticket-list" aria-label="Your tickets">
                        <div className="ticket-list__heading">
                            <span>Requests</span>
                            <strong>{tickets.length}</strong>
                        </div>
                        {tickets.map((ticket) => (
                            <button
                                type="button"
                                className={`ticket-list-item ${selectedTicket?.id === ticket.id ? 'ticket-list-item--selected' : ''}`}
                                key={ticket.id}
                                onClick={() => setSelectedTicket(ticket)}
                            >
                                <span className="ticket-list-item__id">Ticket #{ticket.id}</span>
                                <strong>{ticket.issueType || 'General request'}</strong>
                                <span>{formatDate(ticket.createdAt)}</span>
                            </button>
                        ))}
                    </section>

                    {selectedTicket && (
                        <article className="ticket-detail" aria-live="polite">
                            <div className="ticket-detail__topline">
                                <span>Ticket #{selectedTicket.id}</span>
                                <span className="ticket-status-badge">{formatLabel(selectedTicket.status, statusLabels)}</span>
                            </div>
                            <h2>{selectedTicket.issueType || 'General request'}</h2>
                            <p className="ticket-detail__date">Submitted {formatDate(selectedTicket.createdAt)}</p>
                            <div className="ticket-detail__meta">
                                <div><span>Urgency</span><strong>{formatLabel(selectedTicket.urgency, urgencyLabels)}</strong></div>
                                <div><span>Last updated</span><strong>{formatDate(selectedTicket.updatedAt || selectedTicket.createdAt)}</strong></div>
                            </div>
                            <div className="ticket-detail__description">
                                <span>Description</span>
                                <p>{selectedTicket.description || 'No description provided.'}</p>
                            </div>
                        </article>
                    )}
                </div>
            )}
        </main>
    );
};

export default MyTickets;
```

### `src/pages/myTickets.css`

```css
.tickets-page {
    min-height: 100vh;
    box-sizing: border-box;
    padding: clamp(24px, 5vw, 64px);
    background: #f5f8f6;
    color: #183247;
}

.tickets-page__header,
.tickets-layout,
.tickets-message { width: min(100%, 1120px); margin-right: auto; margin-left: auto; }

.back-button {
    margin-bottom: 42px;
    border: 0;
    padding: 0;
    background: transparent;
    color: #007f82;
    font: inherit;
    font-weight: 750;
    cursor: pointer;
}

.back-button:hover { color: #00696b; text-decoration: underline; }

.tickets-page__kicker {
    margin: 0 0 10px;
    color: #007f82;
    font-size: 0.72rem;
    font-weight: 800;
    letter-spacing: 0.14em;
    text-transform: uppercase;
}

.tickets-page h1 { margin: 0; color: #163247; font-size: clamp(2.2rem, 5vw, 4rem); line-height: 1; }
.tickets-page__summary { margin: 14px 0 0; color: #587084; line-height: 1.5; }
.tickets-layout { display: grid; grid-template-columns: minmax(220px, 0.75fr) minmax(0, 1.5fr); gap: 20px; margin-top: 44px; }
.ticket-list, .ticket-detail, .tickets-message { border: 1px solid #dbe4ee; border-radius: 16px; background: #ffffff; box-shadow: 0 18px 40px rgba(26, 55, 82, 0.08); }
.ticket-list { overflow: hidden; }
.ticket-list__heading { display: flex; justify-content: space-between; padding: 18px 20px; border-bottom: 1px solid #e5edf1; color: #587084; font-size: 0.82rem; text-transform: uppercase; letter-spacing: 0.1em; }
.ticket-list__heading strong { color: #007f82; }
.ticket-list-item { display: flex; flex-direction: column; align-items: flex-start; gap: 7px; width: 100%; border: 0; border-bottom: 1px solid #e5edf1; padding: 18px 20px; background: #ffffff; color: #183247; text-align: left; font: inherit; cursor: pointer; }
.ticket-list-item:last-child { border-bottom: 0; }
.ticket-list-item:hover, .ticket-list-item--selected { background: #edf7f5; }
.ticket-list-item--selected { box-shadow: inset 4px 0 #007f82; }
.ticket-list-item strong { font-size: 1rem; }
.ticket-list-item__id, .ticket-list-item > span:last-child { color: #7890a0; font-size: 0.78rem; }
.ticket-detail { padding: clamp(24px, 4vw, 44px); }
.ticket-detail__topline { display: flex; justify-content: space-between; gap: 16px; color: #587084; font-size: 0.84rem; font-weight: 750; }
.ticket-status-badge { border-radius: 999px; padding: 6px 10px; background: #e3f3ec; color: #18734f; font-size: 0.74rem; }
.ticket-detail h2 { margin: 38px 0 8px; color: #163247; font-size: clamp(1.5rem, 3vw, 2.2rem); }
.ticket-detail__date { margin: 0; color: #7890a0; font-size: 0.88rem; }
.ticket-detail__meta { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin: 36px 0; border-top: 1px solid #e5edf1; border-bottom: 1px solid #e5edf1; padding: 20px 0; }
.ticket-detail__meta div { display: flex; flex-direction: column; gap: 7px; }
.ticket-detail__meta span, .ticket-detail__description > span { color: #7890a0; font-size: 0.76rem; font-weight: 800; letter-spacing: 0.08em; text-transform: uppercase; }
.ticket-detail__description p { margin: 12px 0 0; color: #29475d; line-height: 1.7; white-space: pre-wrap; }
.tickets-message { display: flex; flex-direction: column; gap: 8px; margin-top: 44px; padding: 30px; color: #587084; }
.tickets-message strong { color: #163247; font-size: 1.1rem; }
.tickets-message--error { border-color: #e7b9b9; color: #b33b45; }

@media (max-width: 700px) {
    .back-button { margin-bottom: 32px; }
    .tickets-layout { grid-template-columns: 1fr; }
    .ticket-list { max-height: 330px; overflow-y: auto; }
}
```

### `src/reportWebVitals.js`

```javascript
const reportWebVitals = onPerfEntry => {
  if (onPerfEntry && onPerfEntry instanceof Function) {
    import('web-vitals').then(({ getCLS, getFID, getFCP, getLCP, getTTFB }) => {
      getCLS(onPerfEntry);
      getFID(onPerfEntry);
      getFCP(onPerfEntry);
      getLCP(onPerfEntry);
      getTTFB(onPerfEntry);
    });
  }
};

export default reportWebVitals;
```

### `src/setupTests.js`

```javascript
// jest-dom adds custom jest matchers for asserting on DOM nodes.
// allows you to do things like:
// expect(element).toHaveTextContent(/react/i)
// learn more: https://github.com/testing-library/jest-dom
import '@testing-library/jest-dom';
```

### `src/App.test.js`

```javascript
import { render, screen } from '@testing-library/react';
import App from './App';

test('renders learn react link', () => {
  render(<App />);
  const linkElement = screen.getByText(/learn react/i);
  expect(linkElement).toBeInTheDocument();
});
```

### `public/index.html`

```html
<!DOCTYPE html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <link rel="icon" href="%PUBLIC_URL%/favicon.ico" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <meta name="theme-color" content="#000000" />
    <meta name="description" content="Web site created using create-react-app" />
    <link rel="apple-touch-icon" href="%PUBLIC_URL%/logo192.png" />
    <link rel="manifest" href="%PUBLIC_URL%/manifest.json" />
    <title>React App</title>
  </head>
  <body>
    <noscript>You need to enable JavaScript to run this app.</noscript>
    <div id="root"></div>
  </body>
</html>
```

### `public/manifest.json`

```json
{
  "short_name": "React App",
  "name": "Create React App Sample",
  "icons": [
    { "src": "favicon.ico", "sizes": "64x64 32x32 24x24 16x16", "type": "image/x-icon" },
    { "src": "logo192.png", "type": "image/png", "sizes": "192x192" },
    { "src": "logo512.png", "type": "image/png", "sizes": "512x512" }
  ],
  "start_url": ".",
  "display": "standalone",
  "theme_color": "#000000",
  "background_color": "#ffffff"
}
```

### `public/robots.txt`

```text
# https://www.robotstxt.org/robotstxt.html
User-agent: *
Disallow:
```

### `README.md`

The project uses the standard Create React App README. The available commands are:

```bash
npm start
npm test
npm run build
npm run eject
```

### `package-lock.json`

This is the npm-generated lockfile for the dependencies declared in `package.json`. It contains the resolved transitive dependency graph, registry URLs, integrity hashes, and licenses. It is intentionally not duplicated inline here because it is generated dependency metadata rather than application code; the authoritative copy remains `frontend/package-lock.json`.

## Binary assets

The following files are present but are binary and therefore cannot be represented as source code in this Markdown file:

- `public/favicon.ico`
- `public/logo192.png`
- `public/logo512.png`

The text-based React logo at `src/logo.svg` is also present in the source tree and is the standard Create React App logo asset.

## Run and test commands

Run these commands from the `frontend` directory:

```bash
npm install
npm start
npm test
npm run build
```

The development server defaults to `http://localhost:3000`. The backend APIs must be available at the hard-coded localhost ports documented above for login, token validation, ticket creation, and ticket history to work.
