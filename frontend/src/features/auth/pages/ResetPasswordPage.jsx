import React, { useState } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { toast } from 'react-toastify';
import '../components/LoginForm.css';

const AUTH_API_URL = `${process.env.REACT_APP_AUTH_API_URL || process.env.REACT_APP_API_GATEWAY_URL || 'http://localhost:8280'}/auth/v1/reset-password`;

const ResetPasswordPage = () => {
    const [searchParams] = useSearchParams();
    const token = searchParams.get('token');
    const [formData, setFormData] = useState({ newPassword: '', confirmPassword: '' });
    const [errors, setErrors] = useState({ newPassword: '', confirmPassword: '' });
    const [isLoading, setIsLoading] = useState(false);
    const [showPassword, setShowPassword] = useState(false);
    const navigate = useNavigate();

    const handleChange = (e) => {
        const { name, value } = e.target;
        setFormData(prev => ({ ...prev, [name]: value }));
        if (errors[name]) setErrors(prev => ({ ...prev, [name]: '' }));
    };

    const validateForm = () => {
        const newErrors = {};
        const passwordRegex = /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[@$!%*?&])[A-Za-z\d@$!%*?&]{8,}$/;

        if (!formData.newPassword) {
            newErrors.newPassword = 'Password is required.';
        } else if (!passwordRegex.test(formData.newPassword)) {
            newErrors.newPassword = 'Use at least 8 characters with an uppercase letter, a lowercase letter, a number and a special character.';
        }

        if (!formData.confirmPassword) {
            newErrors.confirmPassword = 'Please confirm your password.';
        } else if (formData.newPassword !== formData.confirmPassword) {
            newErrors.confirmPassword = 'Passwords do not match.';
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    };

    const handleSubmit = async (e) => {
        e.preventDefault();
        if (!validateForm()) return;

        if (!token) {
            toast.error('Reset token is missing from the URL.');
            return;
        }

        setIsLoading(true);

        try {
            const response = await fetch(AUTH_API_URL, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    token,
                    newPassword: formData.newPassword,
                    confirmPassword: formData.confirmPassword
                }),
            });

            const data = await response.json().catch(() => ({}));

            if (!response.ok) {
                throw new Error(data.message || 'Failed to reset password.');
            }

            toast.success(data.message || 'Password successfully reset.', { position: 'top-right' });

            setTimeout(() => {
                navigate('/login', { replace: true });
            }, 2000);

        } catch (error) {
            console.error('Error resetting password:', error);
            toast.error(error.message || 'Something went wrong.', { position: 'top-right' });
        } finally {
            setIsLoading(false);
        }
    };

    return (
        <div className="auth-shell">
            <h1 className="auth-brand">IT Helpdesk</h1>
            <div className="auth-card panel">
                <div className="panel__body">
                    <h2>Choose New Password</h2>

                    <form onSubmit={handleSubmit} className="auth-form" noValidate>
                        <div className="field">
                            <label htmlFor="newPassword">New password<span className="required-mark" aria-hidden="true">*</span></label>
                            <div style={{ display: 'flex', flexDirection: 'column' }}>
                                <input
                                    id="newPassword"
                                    type={showPassword ? 'text' : 'password'}
                                    name="newPassword"
                                    value={formData.newPassword}
                                    onChange={handleChange}
                                    className={errors.newPassword ? 'input input--error' : 'input'}
                                />
                                {errors.newPassword && <span className="error-text">{errors.newPassword}</span>}
                            </div>
                        </div>

                        <div className="field">
                            <label htmlFor="confirmPassword">Confirm password<span className="required-mark" aria-hidden="true">*</span></label>
                            <input
                                id="confirmPassword"
                                type={showPassword ? 'text' : 'password'}
                                name="confirmPassword"
                                value={formData.confirmPassword}
                                onChange={handleChange}
                                className={errors.confirmPassword ? 'input input--error' : 'input'}
                            />
                            {errors.confirmPassword && <span className="error-text">{errors.confirmPassword}</span>}
                        </div>

                        <div className="field" style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
                            <input
                                type="checkbox"
                                id="showPassword"
                                checked={showPassword}
                                onChange={(e) => setShowPassword(e.target.checked)}
                            />
                            <label htmlFor="showPassword" style={{ margin: 0, fontWeight: 'normal' }}>Show password</label>
                        </div>

                        <button type="submit" className="btn btn--primary auth-submit" disabled={isLoading}>
                            {isLoading ? 'Resetting...' : 'Reset password'}
                        </button>
                    </form>

                    <div className="auth-footer-link">
                        <a href="/login" onClick={(e) => { e.preventDefault(); navigate('/login'); }}>back to login</a>
                    </div>
                </div>
            </div>
        </div>
    );
};

export default ResetPasswordPage;
