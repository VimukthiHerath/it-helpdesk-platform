import React, {useState} from 'react';

const LoginForm = () => {
    const [formdata, setFormdata] = useState({
        email: '',
        password: ''
    });

    const [errors, setErrors] = useState({
        email: '',
        password: ''
    });

    const [isloading, setIsloading] = useState(false);
    

    const handleChange = (e) => {
        setFormdata({
            ...formdata,
            [e.target.name]: e.target.value
        });

        if (errors[e.target.name]) {
            setErrors({
                ...errors,
                [e.target.name]: ''
            });
        }
    }

    const validateForm = () => {
        const newErrors = {};
        if (!formdata.email) {
            newErrors.email = 'Email is required';
        }else if (!/\S+@\S+\.\S+/.test(formdata.email)) {
            newErrors.email = 'Email is invalid';
        }

        if (!formdata.password) {
            newErrors.password = 'Password is required';
        }else if (formdata.password.length < 6) {
            newErrors.password = 'Password must be at least 6 characters';
        }

        setErrors(newErrors);
        return Object.keys(newErrors).length === 0;
    };

    const handleSubmit = async (e) => {
        e.preventDefault();
        if (!validateForm()) return;

        setIsloading(true);
        try {
            // Make API call to login endpoint
            const response = await fetch('http://localhost:5121/api/auth/login', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(formdata),
            });

            if (!response.ok) {
                throw new Error('Login failed');
            }

            if (response.ok) {
                const data = await response.json();
                console.log('Login successful:', data);
            }
        } catch (error) {
            console.error('Error logging in:', error);
        } finally {
            setIsloading(false);
        }
    };


    return (
        <div>
            <form onSubmit={handleSubmit}>
                <div>
                    <label>Email:</label>
                    <input
                        type="email"
                        name="email"
                        value={formdata.email}
                        onChange={handleChange}
                    />
                    {errors.email && <span>{errors.email}</span>}
                </div>
                <div>
                    <label>Password:</label>
                    <input
                        type="password"
                        name="password"
                        value={formdata.password}
                        onChange={handleChange}
                    />
                    {errors.password && <span>{errors.password}</span>}
                </div>
                <button type="submit" disabled={isloading}>
                    {isloading ? 'Logging in...' : 'Login'}
                </button>
            </form>
        </div>
    );
}

export default LoginForm;
