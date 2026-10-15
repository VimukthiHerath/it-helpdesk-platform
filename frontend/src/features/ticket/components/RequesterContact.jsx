import './RequesterContact.css';

// Who raised the ticket and how to reach them. `contact` is undefined while
// loading or when the lookup failed, which reads as "unavailable" rather than
// leaving the cell blank.
const RequesterContact = ({ contact }) => {
    if (!contact) {
        return <span className="requester-contact__missing">Contact unavailable</span>;
    }

    return (
        <div className="requester-contact">
            <span className="requester-contact__name">{contact.name}</span>
            <a className="requester-contact__link" href={`mailto:${contact.email}`}>{contact.email}</a>
            {contact.phone ? (
                <a className="requester-contact__link" href={`tel:${contact.phone}`}>{contact.phone}</a>
            ) : (
                <span className="requester-contact__missing">No phone on file</span>
            )}
        </div>
    );
};

export default RequesterContact;
