import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { getStoredToken, getUserEmail } from '../../../shared/authToken';
import './CreateUserForm.css';

const AUTH_API_URL = `${process.env.REACT_APP_AUTH_API_URL}/api/auth/users`;

const roleOptions = [
    { value: 1, label: 'Employee' },
    { value: 2, label: 'Agent' },
    { value: 3, label: 'Administrator' },
];

const initialForm = { name: '', email: '', password: '', role: 1 };

const CreateUserForm = () => {
    const [formData, setFormData] = useState(initialForm);
    const [errors, setErrors] = useState({});
    const [lastCreated, setLastCreated] = useState(null);
    const [serverError, setServerError] = useState('');
    const [isSubmitting, setIsSubmitting] = useState(false);
    const navigate = useNavigate();

    const handleChange = (event) => {
        const { name, value } = event.target;
        setFormData((current) => ({
            ...current,
            [name]: name === 'role' ? Number(value) : value,
        }));
        setErrors((current) => ({ ...current, [name]: '' }));
        setServerError('');
    };

    const validate = () => {
        const nextErrors = {};
        if (!formData.name.trim()) nextErrors.name = 'Enter a name.';
        if (!/\S+@\S+\.\S+/.test(formData.email)) nextErrors.email = 'Enter a valid email address.';
        if (formData.password.length < 6) nextErrors.password = 'Use at least 6 characters.';

        setErrors(nextErrors);
        return Object.keys(nextErrors).length === 0;
    };

    const handleSubmit = async (event) => {
        event.preventDefault();
        if (!validate()) return;

        const token = getStoredToken();
        if (!token) {
            navigate('/login', { replace: true });
            return;
        }

        setIsSubmitting(true);
        setServerError('');
        setLastCreated(null);

        try {
            const response = await fetch(AUTH_API_URL, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    Authorization: `Bearer ${token}`,
                },
                body: JSON.stringify({
                    name: formData.name.trim(),
                    email: formData.email.trim(),
                    password: formData.password,
                    role: formData.role,
                }),
            });

            if (response.status === 401) {
                navigate('/login', { replace: true });
                return;
            }

            if (response.status === 403) {
                setServerError('Your account no longer has administrator access.');
                return;
            }

            const data = await response.json().catch(() => ({}));

            if (response.status === 409) {
                setErrors({ email: 'That email is already registered.' });
                return;
            }

            if (!response.ok) {
                throw new Error(data?.message || 'Unable to create the account.');
            }

            setLastCreated(data);
            setFormData(initialForm);
        } catch (error) {
            setServerError(error.message || 'Unable to create the account.');
        } finally {
            setIsSubmitting(false);
        }
    };

    return (
        <section className="admin-create-panel panel" aria-labelledby="create-user-title">
            <div className="panel__titlebar">
                <span id="create-user-title">New account</span>
                <span>Administration</span>
            </div>

            <div className="panel__body">
                <p className="admin-create-panel__intro">
                    Onboard an employee, agent, or another administrator onto the platform.
                </p>

                <form className="admin-create-form" onSubmit={handleSubmit} noValidate>
                    <div className="field">
                        <label htmlFor="name">Full name</label>
                        <input
                            id="name"
                            name="name"
                            value={formData.name}
                            onChange={handleChange}
                            placeholder="Jordan Lee"
                            maxLength={100}
                            className={errors.name ? 'input input--error' : 'input'}
                        />
                        {errors.name && <span className="error-text">{errors.name}</span>}
                    </div>

                    <div className="admin-create-form__row">
                        <div className="field">
                            <label htmlFor="email">Email</label>
                            <input
                                id="email"
                                name="email"
                                type="email"
                                value={formData.email}
                                onChange={handleChange}
                                placeholder="jordan@company.com"
                                className={errors.email ? 'input input--error' : 'input'}
                            />
                            {errors.email && <span className="error-text">{errors.email}</span>}
                        </div>

                        <div className="field">
                            <label htmlFor="role">Role</label>
                            <select id="role" name="role" value={formData.role} onChange={handleChange} className="input">
                                {roleOptions.map((option) => (
                                    <option key={option.value} value={option.value}>{option.label}</option>
                                ))}
                            </select>
                        </div>
                    </div>

                    <div className="field">
                        <label htmlFor="password">Temporary password</label>
                        <input
                            id="password"
                            name="password"
                            type="password"
                            value={formData.password}
                            onChange={handleChange}
                            placeholder="At least 6 characters"
                            className={errors.password ? 'input input--error' : 'input'}
                        />
                        {errors.password && <span className="error-text">{errors.password}</span>}
                    </div>

                    <div className="admin-create-form__footer">
                        <div aria-live="polite" className="admin-create-status">
                            {serverError && <span className="error-text">{serverError}</span>}
                            {lastCreated && (
                                <span className="admin-create-status__success">
                                    Created {lastCreated.name} ({roleOptions.find((r) => r.value === lastCreated.role)?.label || lastCreated.role}) — added by {getUserEmail() || 'you'}.
                                </span>
                            )}
                        </div>
                        <button type="submit" className="btn btn--primary" disabled={isSubmitting}>
                            {isSubmitting ? 'Creating account...' : 'Create account'}
                        </button>
                    </div>
                </form>
            </div>
        </section>
    );
};

export default CreateUserForm;
