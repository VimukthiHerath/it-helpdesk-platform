import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'react-toastify';
import './LoginForm.css';

// Every API call, this one included, goes through the WSO2 API Gateway
// (see docs/ARCHITECTURE_AND_END_TO_END_FLOW.md). The gateway only routes the
// request; each service checks the JWT itself.
const AUTH_API_URL = `${process.env.REACT_APP_AUTH_API_URL || process.env.REACT_APP_API_GATEWAY_URL}/auth/v1/login`;

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
            newErrors.email = 'Email is required.';
        } else if (!/\S+@\S+\.\S+/.test(formData.email)) {
            newErrors.email = 'Please enter a valid email address.';
        }

        if (!formData.password) {
            newErrors.password = 'Password is required.';
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    };

    const handleSubmit = async (e) => {
        e.preventDefault();

        if (!validateForm()) return;

        setIsLoading(true);

        try {
            const response = await fetch(AUTH_API_URL, {
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
            toast.success('Login successful.', {
                position: 'top-right',
                autoClose: 1800,
            });

            void navigate('/', { replace: true });

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
            <h1 className="auth-brand">IT Helpdesk</h1>
            <div className="auth-card panel">
                <div className="panel__body">
                    <h2>Welcome Back</h2>

                    <form onSubmit={handleSubmit} className="auth-form" noValidate>
                        <div className="field">
                            <label htmlFor="email">Email<span className="required-mark" aria-hidden="true">*</span></label>
                            <input
                                id="email"
                                type="email"
                                name="email"
                                value={formData.email}
                                onChange={handleChange}
                                className={errors.email ? 'input input--error' : 'input'}
                                placeholder="Enter your email"
                                autoComplete="email"
                                aria-required="true"
                                aria-invalid={Boolean(errors.email)}
                            />
                            {errors.email && <span className="error-text">{errors.email}</span>}
                        </div>

                        <div className="field">
                            <label htmlFor="password">Password<span className="required-mark" aria-hidden="true">*</span></label>
                            <input
                                id="password"
                                type="password"
                                name="password"
                                value={formData.password}
                                onChange={handleChange}
                                className={errors.password ? 'input input--error' : 'input'}
                                placeholder="Enter your password"
                                autoComplete="current-password"
                                aria-required="true"
                                aria-invalid={Boolean(errors.password)}
                            />
                            {errors.password && <span className="error-text">{errors.password}</span>}
                        </div>

                        <div className="field forgot-password-field">
                            <a href="/forgot-password" onClick={(e) => { e.preventDefault(); void navigate('/forgot-password'); }} className="forgot-password-link">
                                forgot password?
                            </a>
                        </div>

                        <button type="submit" className="btn btn--primary auth-submit" disabled={isLoading}>
                            {isLoading ? 'Logging in...' : 'Log in'}
                        </button>
                    </form>
                </div>
            </div>
        </div>
    );
};

export default LoginForm;
