import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'react-toastify';
import '../components/LoginForm.css';

const AUTH_API_URL = `${process.env.REACT_APP_AUTH_API_URL || process.env.REACT_APP_API_GATEWAY_URL || 'http://localhost:8280'}/auth/v1/forgot-password`;

const ForgotPasswordPage = () => {
    const [email, setEmail] = useState('');
    const [emailError, setEmailError] = useState('');
    const [isLoading, setIsLoading] = useState(false);
    const navigate = useNavigate();

    const handleSubmit = async (e) => {
        e.preventDefault();
        if (!email.trim()) {
            setEmailError('Email is required.');
            return;
        }
        if (!/\S+@\S+\.\S+/.test(email)) {
            setEmailError('Please enter a valid email address.');
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
            <h1 className="auth-brand">IT Helpdesk</h1>
            <div className="auth-card panel">
                <div className="panel__body">
                    <h2>Reset Password</h2>
                    <p className="auth-help">Enter your email and we will send you a reset link.</p>

                    <form onSubmit={handleSubmit} className="auth-form" noValidate>
                        <div className="field">
                            <label htmlFor="email">Email<span className="required-mark" aria-hidden="true">*</span></label>
                            <input
                                id="email"
                                type="email"
                                value={email}
                                onChange={(e) => { setEmail(e.target.value); setEmailError(''); }}
                                className={emailError ? 'input input--error' : 'input'}
                                placeholder="Enter your email"
                                autoComplete="email"
                                aria-required="true"
                                aria-invalid={Boolean(emailError)}
                            />
                            {emailError && <span className="error-text">{emailError}</span>}
                        </div>

                        <button type="submit" className="btn btn--primary auth-submit" disabled={isLoading}>
                            {isLoading ? 'Sending...' : 'Send reset link'}
                        </button>
                    </form>

                    <div className="auth-footer-link">
                        <a href="/login" onClick={(e) => { e.preventDefault(); void navigate('/login'); }}>back to login</a>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default ForgotPasswordPage;
