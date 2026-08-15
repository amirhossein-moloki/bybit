# Deployment Documentation

This document describes the server requirements and guides you step-by-step through setting up, deploying, and maintaining the **Telegram Signal Trading Bot** and its Next.js Dashboard in both development and production environments.

---

## Server Requirements

* **Operating System**: Linux (Ubuntu 22.04 LTS / Debian 11 recommended), macOS, or Windows Server 2022.
* **Runtime Environments**: .NET SDK 8.0/10.0 runtime and Node.js 20+ (for local dashboard development).
* **Database Requirements**: PostgreSQL 14, 15, or 16.
* **Redis Requirements**: Redis 7.0 or newer.
* **Docker Requirements**: Docker Engine 24.0.0+ and Docker Compose v2.20.0+.

---

## Development Deployment

Follow this sequence to prepare a local development and testing environment:

```
 Clone Repository
        ↓
 Install Dependencies  (dotnet restore & npm install in dashboard)
        ↓
 Configure Environment  (local .env and appsettings.json)
        ↓
Start Database Services (docker-compose postgres and redis containers)
        ↓
   Run Migrations      (dotnet ef database update)
        ↓
  Start Applications   (dotnet run for worker & npm run dev for dashboard)
        ↓
   Verify Health       (curl localhost:5000/health & curl localhost:3000)
```

### 1. Clone Repository
```bash
git clone https://github.com/user/tradingbot.git
cd tradingbot
```

### 2. Install Dependencies
Restore NuGet dependencies across the solution:
```bash
dotnet restore src/TradingBot.sln
```
Install dashboard npm dependencies:
```bash
cd dashboard && npm install && cd ..
```

### 3. Configure Environment
Create local `.env` configurations from the templates:
```bash
cp .env.example .env
cp dashboard/.env.example dashboard/.env
```
Fill out the required API keys and settings inside the `.env` files.

### 4. Start Infrastructure Containers
Bring up local PostgreSQL and Redis servers using Docker:
```bash
docker-compose up -d postgres redis
```

### 5. Run Database Migrations
Apply EF Core database migrations to initialize the schema:
```bash
dotnet ef database update --project src/TradingBot.Persistence/TradingBot.Persistence.csproj --startup-project src/TradingBot.Worker/TradingBot.Worker.csproj
```

### 6. Start the Application
Run the worker process in development mode:
```bash
dotnet run --project src/TradingBot.Worker/TradingBot.Worker.csproj
```

Run the dashboard in local development mode:
```bash
cd dashboard && npm run dev
```

### 7. Verify Health
Verify that the Web host and background services are up:
```bash
curl http://localhost:5000/health
curl http://localhost:3000
```

---

## Production Deployment via Docker Compose

For live trading systems, run the complete 4-service stack inside dedicated Docker containers:

```
  Server Preparation     (Install Docker / Docker Compose / secure firewall)
          ↓
Environment Configuration (Provision production .env keys and encryption)
          ↓
 Database Migration      (Run on container startup or via CI/CD pipelines)
          ↓
  Application Build      (Compile optimized Release Docker containers)
          ↓
   Service Startup       (docker-compose up -d --build)
          ↓
 Health Verification     (Monitor live logs and trigger doctor checks)
```

### Step 1: Server Preparation
Install Docker on the production host and secure the ports:
* Ensure database port `5432` and Redis port `6379` are not exposed to the public internet.
* Open Web port `5000` (Worker API) and `3000` (Dashboard UI) as required for external management.

### Step 2: Environment Configuration
Copy `.env.example` to `.env` on the host server:
```bash
cp .env.example .env
nano .env
```
Set `Application__Environment=Production`, assign a strong `Security__EncryptionKey` (32 characters), set `API_PROXY_TARGET=http://tradingbot-worker:80`, and configure Bybit live API credentials.

### Step 3: Application Build and Startup
Build the container images and launch the orchestration in detached background mode:
```bash
docker-compose up -d --build
```
This command automatically:
* Pulls PostgreSQL and Redis base images.
* Builds the multi-stage C# runtime image for `tradingbot-worker`.
* Builds the multi-stage Node.js runtime image for `tradingbot-dashboard`.
* Mounts database files locally to preserve persistence across service restarts.
* Runs database migrations and seeds initial configurations.

The full stack services will show as:
- `tradingbot-postgres` (Running / Healthy)
- `tradingbot-redis` (Running)
- `tradingbot-worker` (Running / Healthy)
- `tradingbot-dashboard` (Running / Healthy)

### Step 4: Health Verification
Confirm the application is fully operational:
```bash
# Verify worker doctor diagnostic checks
docker-compose exec tradingbot-worker dotnet TradingBot.Worker.dll doctor

# Verify dashboard http status
curl -I http://localhost:3000
```

---

## Operational Procedures

This section details maintenance, backup, and fail-safe recovery commands for DevOps operators.

### 1. Docker Management Commands
* **Start Services**: `docker-compose up -d`
* **Stop Services**: `docker-compose down`
* **Rebuild and Start**: `docker-compose up -d --build`
* **Stop and Delete Volumes**: `docker-compose down -v`

### 2. Migration Commands
To apply manual schema migrations directly on the host server:
```bash
dotnet ef database update --project src/TradingBot.Persistence/TradingBot.Persistence.csproj --startup-project src/TradingBot.Worker/TradingBot.Worker.csproj
```

### 3. Restart Procedures
To restart specific container services safely:
```bash
docker-compose restart tradingbot-worker
docker-compose restart tradingbot-dashboard
```

### 4. Log Inspection
Inspect the live console output or query specific lines using Docker:
```bash
# Follow live container logs for worker and dashboard
docker-compose logs -f tradingbot-worker tradingbot-dashboard

# Inspect error-only events from the persistent log file
tail -f -n 100 logs/tradingbot.log | grep -i "error"
```

### 5. Backup Procedures
To generate a point-in-time snapshot of the PostgreSQL database:
```bash
docker-compose exec -t postgres pg_dumpall -c -U postgres > tradingbot_backup_$(date +%F).sql
```
Save the backup file securely in an offsite container or cloud bucket.

### 6. Recovery Procedures
To restore the database from a backup file in the event of a cluster crash:
```bash
# 1. Stop the application container to block concurrent writes
docker-compose stop tradingbot-worker

# 2. Stream the backup SQL commands back into the Postgres container
cat tradingbot_backup_YYYY-MM-DD.sql | docker-compose exec -T postgres psql -U postgres

# 3. Start the application container
docker-compose start tradingbot-worker
```
