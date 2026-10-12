import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { toast } from 'react-toastify';
import './ticketCreateForm.css';

const TICKET_API_URL = `${process.env.REACT_APP_TICKET_API_URL || process.env.REACT_APP_API_GATEWAY_URL}/ticket/v1`;

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

        // AC2: fetch() itself failing (offline, DNS, CORS, server down)
        // means the request never reached the backend at all - a different
        // situation from the backend responding with an error, so it gets
        // its own network-specific message rather than falling through to
        // the same generic wording.
        let response;
        try {
            response = await fetch(TICKET_API_URL, {
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
        } catch {
            setStatus({ type: 'error', message: 'Unable to reach the server. Check your connection and try again.' });
            setIsSubmitting(false);
            return;
        }

        const data = await response.json().catch(() => ({}));

        if (response.status === 401) {
            localStorage.removeItem('token');
            navigate('/login', { replace: true });
            return;
        }

        if (!response.ok) {
            setStatus({ type: 'error', message: data?.message || 'Unable to create the ticket. Please try again.' });
            setIsSubmitting(false);
            return;
        }

        toast.success('Ticket created successfully');
        navigate('/tickets', { replace: true });
    };

    return (
        <section className="ticket-panel panel" aria-labelledby="create-ticket-title">
            <div className="panel__titlebar">
                <span id="create-ticket-title">New ticket</span>
                <span>Support desk</span>
            </div>

            <div className="panel__body">
                <p className="ticket-panel__intro">Give us the context we need to get the right person on it.</p>

                <form className="ticket-form" onSubmit={handleSubmit} noValidate>
                    <div className="ticket-form__row">
                        <div className="field">
                            <label htmlFor="issueType">Issue type</label>
                            <input
                                id="issueType"
                                name="issueType"
                                value={formData.issueType}
                                onChange={handleChange}
                                placeholder="Issue type"
                                maxLength={100}
                                className={errors.issueType ? 'input input--error' : 'input'}
                                aria-invalid={Boolean(errors.issueType)}
                            />
                            {errors.issueType && <span className="error-text">{errors.issueType}</span>}
                        </div>

                        <div className="field">
                            <label htmlFor="urgency">Required by</label>
                            <select id="urgency" name="urgency" value={formData.urgency} onChange={handleChange} className="input">
                                {urgencyOptions.map((option) => (
                                    <option key={option.value} value={option.value}>{option.label}</option>
                                ))}
                            </select>
                        </div>
                    </div>

                    <div className="field">
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
                            className={errors.description ? 'input input--error' : 'input'}
                            aria-invalid={Boolean(errors.description)}
                        />
                        {errors.description && <span className="error-text">{errors.description}</span>}
                    </div>

                    <div className="ticket-form__footer">
                        <div aria-live="polite" className={`ticket-status ticket-status--${status.type}`}>
                            {status.message}
                        </div>
                        <button type="submit" className="btn btn--primary" disabled={isSubmitting}>
                            {isSubmitting ? (
                                <>
                                    <span className="spinner" aria-hidden="true" />
                                    Creating ticket...
                                </>
                            ) : (
                                'Create ticket'
                            )}
                        </button>
                    </div>
                </form>
            </div>
        </section>
    );
};

export default TicketCreateForm;
