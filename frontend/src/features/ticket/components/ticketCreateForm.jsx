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
