# C4 Architecture Diagrams

## 1. System Context Diagram
This diagram outlines how staff and employees interact with the overarching Helpdesk Platform.

```mermaid
C4Context
    title System Context for IT Helpdesk Platform
    
    Person(employee, "Employee", "Submits and tracks IT support tickets.")
    Person(supportStaff, "IT Support Staff", "Resolves IT issues and reviews SLAs.")
    
    System(helpdeskSystem, "IT Helpdesk Platform", "Allows employees to log hardware/software issues and provides support staff tooling to assign and resolve them.")
    
    Rel(employee, helpdeskSystem, "Logs tickets, checks status using")
    Rel(supportStaff, helpdeskSystem, "Manages, assigns, and resolves tickets using")
```

## 2. Container Diagram
Detailed breakdown of the Microservice topology backing the Helpdesk.

```mermaid
C4Container
    title Container Diagram for IT Helpdesk Platform
    
    Person(user, "User", "Employee or Support Staff")
    
    Container(spa, "React SPA", "JavaScript/React", "Provides the user interface for browser interactions.")
    Container(apiGateway, "WSO2 API Gateway", "WSO2", "Enterprise ingress routing, rate limiting, and core aggregation.")
    
    Boundary(c1, "Backend Microservices (.NET 8)") {
        Container(authApi, "Auth.Api", ".NET 8", "Handles user authentication and JWT provision.")
        Container(ticketApi, "Ticket.Api", ".NET 8", "Manages core ticket workflows.")
        Container(assignApi, "Assignment.Api", ".NET 8", "Handles automated and explicit agent assignments.")
        Container(slaApi, "Sla.Api", ".NET 8", "Monitors and configures SLA priorities and deadlines.")
        Container(notifyApi, "Notification.Api", ".NET 8", "Dispatches user alerts.")
    }
    
    Boundary(c2, "Message Broker") {
        Container(kafka, "Apache Kafka", "Message Bus", "Transmits decoupled asynchronous domain events.")
    }

    Boundary(c3, "Data Stores") {
        ContainerDb(authDb, "auth_db", "MySQL 8", "Stores credentials.")
        ContainerDb(ticketDb, "ticket_db", "MySQL 8", "Stores tickets.")
        ContainerDb(assignDb, "assign_db", "MySQL 8", "Stores logic graphs.")
        ContainerDb(slaDb, "sla_db", "MySQL 8", "Stores metrics.")
        ContainerDb(notifyDb, "notification_db", "MySQL 8", "Stores history.")
    }
    
    Rel(user, spa, "Visits", "HTTPS")
    Rel(spa, apiGateway, "Makes API calls to", "JSON/HTTPS")
    
    Rel(apiGateway, authApi, "Routes traffic to", "HTTPS")
    Rel(apiGateway, ticketApi, "Routes traffic to", "HTTPS")
    Rel(apiGateway, assignApi, "Routes traffic to", "HTTPS")
    Rel(apiGateway, slaApi, "Routes traffic to", "HTTPS")
    Rel(apiGateway, notifyApi, "Routes traffic to", "HTTPS")

    Rel(ticketApi, kafka, "Publishes events to", "TCP")
    Rel(assignApi, kafka, "Consumes events from", "TCP")
    Rel(slaApi, kafka, "Consumes events from", "TCP")
    Rel(notifyApi, kafka, "Consumes events from", "TCP")
    
    Rel(authApi, authDb, "Reads from and writes to", "TCP")
    Rel(ticketApi, ticketDb, "Reads from and writes to", "TCP")
    Rel(assignApi, assignDb, "Reads from and writes to", "TCP")
    Rel(slaApi, slaDb, "Reads from and writes to", "TCP")
    Rel(notifyApi, notifyDb, "Reads from and writes to", "TCP")
```

## 3. Deployment Diagram (Azure Target)
Depicts how the containers map out into the finalized Azure Cloud physical spaces.

```mermaid
C4Deployment
    title Deployment Diagram - Azure Target (Production)
    
    Deployment_Node(cloud, "Microsoft Azure", "Azure Cloud Platform") {
        Deployment_Node(vnet, "Azure Virtual Network", "Secured internal boundary") {
            
            Deployment_Node(aca, "Azure Container Apps", "Serverless orchestration") {
                Container(apiWso2, "WSO2 API Gateway Container", "Docker")
                Container(authApp, "Auth.Api Runtime", ".NET 8 Docker")
                Container(ticketApp, "Ticket.Api Runtime", ".NET 8 Docker")
                Container(assignApp, "Assignment.Api Runtime", ".NET 8 Docker")
                Container(slaApp, "Sla.Api Runtime", ".NET 8 Docker")
                Container(notifyApp, "Notification.Api Runtime", ".NET 8 Docker")
            }
            
            Deployment_Node(azSql, "Azure Database for MySQL flexible server", "Managed PAAS DB") {
                ContainerDb(allDbs, "MySQL 8 Schema instances", "MySQL", "Hosts all decoupled schemas.")
            }
            
            Deployment_Node(kv, "Azure Key Vault", "Secrets Engine") {
                 Container(secrets, "App Secrets", "Tokens / Configs")
            }
        }
    }
    
    Deployment_Node(confluent, "Confluent Cloud / Azure Event Hubs", "Managed Kafka") {
         Container(kafkaProd, "Production Topics", "Kafka")
    }
    
    Rel(apiWso2, authApp, "Routes to")
    Rel(ticketApp, kafkaProd, "Publishes to")
    Rel(slaApp, kafkaProd, "Subscribes to")
    Rel(authApp, allDbs, "Reads/Writes")
    Rel(ticketApp, kv, "Retrieves credentials from")
    
```
