# Docker Compose

This is the Visual Studio Docker Compose project for the complete BankSystem stack.

The project is intentionally aligned with the solution root:

```text
BankSystemMicroservices-local/
├── Common/
├── Services/
├── Gateway/
├── AppHost/
└── docker-compose/
    ├── docker-compose.dcproj
    ├── docker-compose.yml
    ├── Properties/
    │   └── launchSettings.json
    └── postgres/
        └── init/
            └── 01-create-databases.sql
```

Each API keeps its own `Dockerfile` inside its API project. This Compose project only orchestrates the containers.

## Start from the repository root

```powershell
docker compose -f docker-compose/docker-compose.yml up -d --build
```

## Stop and reset databases

```powershell
docker compose -f docker-compose/docker-compose.yml down -v
docker compose -f docker-compose/docker-compose.yml up -d --build
```

## Endpoints

- API Gateway: `http://localhost:55837`
- Security: `http://localhost:5153`
- Account: `http://localhost:5069`
- Transaction: `http://localhost:59944`
- Movement: accessible through the API Gateway at `http://localhost:55837/api/v1/movements`
- Notification: accessible through the API Gateway at `http://localhost:55837/api/v1/notifications`
- Reporting: `http://localhost:59943`
- RabbitMQ Management: `http://localhost:15672`

RabbitMQ credentials: `bankuser` / `bankpassword`.
