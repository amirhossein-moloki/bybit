# Deployment Documentation

This document describes the server requirements and guides you step-by-step through setting up, deploying, and maintaining the **Telegram Signal Trading Bot** in both development and production environments.

---

## Server Requirements

* **Operating System**: Linux (Ubuntu 22.04 LTS / Debian 11 recommended), macOS, or Windows Server 2022.
* **Runtime Environments**: .NET SDK 8.0 or .NET SDK 10.0 runtime.
* **Database Requirements**: PostgreSQL 14, 15, or 16.
* **Redis Requirements**: Redis 7.0 or newer.
* **Docker Requirements**: Docker Engine 24.0.0+ and Docker Compose v2.20.0+.

---

## Development Deployment

Follow this sequence to prepare a local development and testing environment:

```
 Clone Repository
        ↓
 Install Dependencies  (--project parameters on dotnet command)
        ↓
 Configure Environment  (local .env and appsettings.json)
        ↓
Start Database Services (docker-compose postgres and redis containers)
        ↓
   Run Migrations      (dotnet ef database update)
        ↓
  Start Application    (dotnet run)
        ↓
   Verify Health       (curl localhost:5000/health)
```

### 1. Clone Repository
```bash
git clone https://github.com/user/tradingbot.git
cd tradingbot
```

### 2. Install Dependencies
Restore NuGet dependencies across the entire solution:
```bash
dotnet restore src/TradingBot.sln
```

### 3. Configure Environment
Create a local `.env` configuration from the template:
```bash
cp .env.example .env
```
Fill out the required API keys and settings inside the `.env` file.

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

### 7. Verify Health
Verify that the Web host and background services are up:
```bash
curl http://localhost:5000/health
```

---

## Production Deployment

For live trading systems, configure and run the application inside dedicated Docker containers:

```
  Server Preparation     (Install Docker / Docker Compose / secure firewall)
          ↓
Environment Configuration (Provision production .env keys and encryption)
          ↓
 Database Migration      (Run on container startup or via CI/CD pipelines)
          ↓
  Application Build      (Compile optimized Release Docker containers)
          ↓
   Service Startup       (Launch in detached container orchestrations)
          ↓
 Health Verification     (Monitor live logs and trigger doctor checks)
```

### Step 1: Server Preparation
Install Docker on the production host and secure the ports:
* Ensure database port `5432` and Redis port `6379` are not exposed to the public internet.
* Open Web port `5000` only if you need external dashboard access.

### Step 2: Environment Configuration
Copy `.env.example` to `.env` on the host server:
```bash
cp .env.example .env
nano .env
```
Set the `Application__Environment=Production` environment variable, assign a strong `Security__EncryptionKey` (32 characters), and configure Bybit live API credentials.

### Step 3: Application Build and Startup
Build the container images and launch the orchestrations in detached background mode:
```bash
docker-compose up -d --build
```
This command automatically:
* Pulls PostgreSQL and Redis base images.
* Builds the multi-stage C# runtime image.
* Mounts database files locally to preserve persistence across service restarts.
* Runs the database seeder and automatically applies schema migrations.

### Step 4: Health Verification
Confirm the application is fully operational by executing the diagnostic command inside the container:
```bash
docker-compose exec tradingbot-worker dotnet TradingBot.Worker.dll doctor
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
To restart only the trading worker process safely without restarting the database:
```bash
docker-compose restart tradingbot-worker
```

### 4. Log Inspection
Inspect the live console output or query specific lines using the Docker engine:
```bash
# Follow live container logs
docker-compose logs -f tradingbot-worker

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
