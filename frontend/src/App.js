import React from 'react';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import LoginPage from './features/auth/pages/LoginPage';
import Dashboard from './pages/dashboard';

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
          path="*"
          element={<ProtectedRoute><Navigate to="/" replace /></ProtectedRoute>}
        />
      </Routes>
    </BrowserRouter>
  );
}

export default App;
