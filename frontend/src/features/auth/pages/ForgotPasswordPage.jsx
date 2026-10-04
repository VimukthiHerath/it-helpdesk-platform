import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast, ToastContainer } from 'react-toastify';
import '../components/LoginForm.css';

const AUTH_API_URL = `${process.env.REACT_APP_AUTH_API_URL || 'http://localhost:5033'}/api/auth/forgot-password`;

const ForgotPasswordPage = () => {
    const [email, setEmail] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const navigate = useNavigate();

    const handleSubmit = async (e) => {
        e.preventDefault();
        if (!email.trim()) {
            toast.error('Email is required');
            return;
        }

        setIsLoading(true);

        try {
            const response = await fetch(AUTH_API_URL, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                },
                body: JSON.stringify({ email }),
            });

            const data = await response.json().catch(() => ({}));

            toast.success(data.message || 'If an account exists for this email, a password reset link has been sent.', {
                position: 'top-right'
            });

            setEmail('');
        } catch (error) {
            console.error('Error requesting password reset:', error);
            toast.error('Something went wrong. Please try again.', {
                position: 'top-right',
            });
        } finally {
            setIsLoading(false);
        }
    };

    return (
        <div className="auth-shell">
            <ToastContainer />
            <h1 className="auth-brand">IT Helpdesk</h1>
            <div className="auth-card panel">
                <div className="panel__body">
                    <h2>Reset Password</h2>
                    <p>Enter your email address and we'll send you a link to reset your password.</p>

                    <form onSubmit={handleSubmit} className="auth-form" noValidate>
                        <div className="field">
                            <label htmlFor="email">Email</label>
                            <input
                                id="email"
                                type="email"
                                value={email}
                                onChange={(e) => setEmail(e.target.value)}
                                className="input"
                                placeholder="you@example.com"
                                autoComplete="email"
                            />
                        </div>

                        <button type="submit" className="btn btn--primary auth-submit" disabled={isLoading}>
                            {isLoading ? 'Sending...' : 'Request Reset Link'}
                        </button>
                    </form>

                    <div style={{ marginTop: '1rem', textAlign: 'center' }}>
                        <a href="/login" onClick={(e) => { e.preventDefault(); navigate('/login'); }}>Back to Login</a>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default ForgotPasswordPage;
