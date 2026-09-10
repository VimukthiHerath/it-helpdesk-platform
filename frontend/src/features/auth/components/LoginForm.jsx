import React, { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast, ToastContainer } from 'react-toastify';
import 'react-toastify/dist/ReactToastify.css';
import './LoginForm.css';

const AUTH_API_URL = `${process.env.REACT_APP_AUTH_API_URL}/api/auth/login`;

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
            <div className="auth-card panel">
                <div className="panel__titlebar">
                    <span>IT Helpdesk</span>
                    <span>Sign in</span>
                </div>

                <div className="panel__body">
                    <p className="eyebrow">Authorized users only</p>
                    <h2>Employee login</h2>

                    <form onSubmit={handleSubmit} className="auth-form" noValidate>
                        <div className="field">
                            <label htmlFor="email">Email</label>
                            <input
                                id="email"
                                type="email"
                                name="email"
                                value={formData.email}
                                onChange={handleChange}
                                className={errors.email ? 'input input--error' : 'input'}
                                placeholder="you@example.com"
                                autoComplete="email"
                            />
                            {errors.email && <span className="error-text">{errors.email}</span>}
                        </div>

                        <div className="field">
                            <label htmlFor="password">Password</label>
                            <input
                                id="password"
                                type="password"
                                name="password"
                                value={formData.password}
                                onChange={handleChange}
                                className={errors.password ? 'input input--error' : 'input'}
                                placeholder="Enter your password"
                                autoComplete="current-password"
                            />
                            {errors.password && <span className="error-text">{errors.password}</span>}
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
