import { ApplicationInsights } from '@microsoft/applicationinsights-web';
import { getUserRole } from './authToken';

// Reset links carry a one-time token in the URL, so anything that looks like one is blanked
// before it leaves the browser.
export const redactUrl = (value) =>
    typeof value === 'string' ? value.replace(/([?&#]token=)[^&#\s"]*/gi, '$1[redacted]') : value;

// Page views only need the path: the query string and fragment are dropped.
const pathOnly = (value) => (typeof value === 'string' ? value.split(/[?#]/)[0] : value);

// Runs on every item before it is sent.
export const sanitizeEnvelope = (envelope) => {
    const data = envelope?.baseData;
    if (!data) return true;

    const isPageView = envelope.name?.endsWith('.PageView');
    ['uri', 'refUri', 'name', 'target', 'data'].forEach((key) => {
        if (typeof data[key] !== 'string') return;
        data[key] = redactUrl(isPageView && (key === 'uri' || key === 'refUri') ? pathOnly(data[key]) : data[key]);
    });

    // Role only, never a name or email.
    const role = getUserRole();
    if (role) {
        data.properties = { ...data.properties, role };
    }
    return true;
};

let started = false;

// Starts browser monitoring when REACT_APP_APPINSIGHTS_CONNECTION_STRING was set at build time;
// otherwise it does nothing, so local runs and tests send nothing.
export const initTelemetry = () => {
    const connectionString = process.env.REACT_APP_APPINSIGHTS_CONNECTION_STRING;
    if (started || !connectionString) return null;
    started = true;

    const appInsights = new ApplicationInsights({
        config: {
            connectionString,
            enableAutoRouteTracking: true,
            disableFetchTracking: false,
            disableAjaxTracking: false,
            // Off on purpose: with it on, the SDK adds traceparent/Request-Id headers to calls to the
            // gateway, and the gateway's CORS list does not allow them, so every API call would fail.
            enableCorsCorrelation: false,
            disableCookiesUsage: true,
            enableRequestHeaderTracking: false,
            enableResponseHeaderTracking: false,
            // The ticket list can repeat the same call; keep the volume down.
            maxAjaxCallsPerView: 50,
        },
    });
    appInsights.loadAppInsights();
    appInsights.addTelemetryInitializer(sanitizeEnvelope);
    appInsights.trackPageView();
    return appInsights;
};
