# IT Helpdesk Platform

Welcome to the central repository for the IT Helpdesk Platform.

## Quick Start
To launch the environment locally, execute:
```bash
docker-compose up
```

## System Architecture
- [Architectural Evolution & Final State](docs/architecture/evolution.md)
- [C4 Architecture Diagrams](docs/architecture/diagrams.md)
- [Architectural Decision Records (ADRs)](docs/adr/)

## API Documentation (Swagger UI)
Interactive Swagger definitions are globally available traversing via the WSO2 API Gateway or specific service contexts:
- **Auth Service:** `/api/auth/swagger/index.html` (Local default: `http://localhost:5001/swagger`)
- **Ticket Service:** `/api/ticket/swagger/index.html` (Local default: `http://localhost:5002/swagger`)
- **Assignment Service:** `/api/assignment/swagger/index.html` (Local default: `http://localhost:5003/swagger`)
- **SLA Service:** `/api/sla/swagger/index.html` (Local default: `http://localhost:5004/swagger`)
- **Notification Service:** `/api/notification/swagger/index.html` (Local default: `http://localhost:5005/swagger`)
