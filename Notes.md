**1) Quick overview** 



This solution is an Online Betting Data Capture \& Analytics backend built with .NET (targeting .NET 8). It is organized into a few projects and pieces:



OT.Assessment.App (API): ASP.NET Web API exposing endpoints (e.g. POST api/player/casinowager, GET api/player/topSpenders) that validate and publish wager events to RabbitMQ. Uses dependency injection, controllers, logging, and OpenAPI metadata.



OT.Assessment.Consumer (Service): A .NET Hosted Service that consumes messages from RabbitMQ, retries using Polly, and writes to SQL Server via repositories.



OT.Assessment.Infrastructure: Dapper-based data access (connection factory), RabbitMQ publisher/consumer wrappers, options classes for configuration, repositories for persistence.



OT.Assessment.Application + OT.Assessment.Core: Business layer (services) and DTOs/entities. The WagerService performs basic validation and calls repository layer.



Database: DatabaseGenerate.sql contains full schema, stored procedures (UpsertProvider, UpsertGame, GetTopSpenders, etc.), indexes and table definitions.



Messaging: RabbitMQ usage with durable exchange/queue, DLQ (dead-letter routing to poison queue), retry policies (Polly) and acknowledgement handling.



Extras: DeadLetterQueueTester, sample data, documentation files, and some tests under test/OT.Assessment.Tester.



High-level flow:



API receives JSON CasinoWager payload.



API validates and publishes the message to RabbitMQ using RabbitMqPublisher.



Consumer (RabbitMqConsumer hosted service) pulls messages, deserializes DTO, calls WagerService.InsertWagerAsync.



WagerRepository persists the wager data via stored procedures / Dapper to SQL Server.



If consumer fails, uses retry policy; persistent failures go to DLQ. There is a DeadLetterConsumer as well.

**2) Notable design choices** 



Messaging-first architecture: Decouples the API from persistent storage and processing via RabbitMQ. Good for scale and resilience.



Dapper + stored procedures: Data access is using Dapper and stored procedures in SQL Server (explicit SQL script provided). This favors explicit control of SQL and performance.



Polly for retries: Retry policies used on transient operations, both in publisher and consumer.



DLQ for poison messages: Messages that fail processing are routed to DLQ/poison queue, with a consumer to inspect / reprocess.



Hosted services: Consumer uses BackgroundService and DI, so it integrates well with .NET hosting.



Separation layers: Clear separation between Core DTOs, Application services, Infrastructure repos and messaging — a reasonably clean vertical layering.



Logging: Uses Microsoft.Extensions.Logging throughout with contextual messages.



Input validation: Some validation in WagerService (argument checks).



Locking / concurrency: Consumer uses a semaphore and BasicQos(0,1,false) for fair dispatch; suggests developer thought about concurrency per consumer instance.


**3) Strengths** 



Clear project structure and layering.



Use of reliable primitives: RabbitMQ, durable exchange/queue, dead-lettering, SQL Server stored procedures.



Use of IOptions<T> and options classes for RabbitMQ config.



Use of async APIs (consumer \& repository use async where appropriate).



Retry policies with Polly and exponential backoff in places.



Transactional/safe DB access patterns through stored procedures and Dapper.



Logging at important lifecycle points (startup/shutdown/processing/errors).



A comprehensive DatabaseGenerate.sql that recreates schema, indexes and stored procedures.



DLQ consumer for failed messages, which is good for observability and recovery.



Good use of DI and Hosted Services for background processing.


**5) Prioritized list of production-ready improvements (Had I had more time I would have Implemented these, but we all need a weekend!)**



Below I prioritize by High / Medium / Low and give practical tips and snippets.



High priority (must have before production)



Health checks \& readiness / liveness



Add ASP.NET Core Health Checks and expose an endpoint /health/ready and /health/live.





Secrets \& configuration management



Move sensitive settings (SQL connection strings, RabbitMQ credentials) out of appsettings.json into environment variables or a secret store (Azure Key Vault, AWS Secrets Manager).



Use IConfiguration to read from env vars in containers/CI.



Idempotency / deduplication



Ensure InsertWagerAsync is idempotent: either use a unique constraint on WagerId in DB and handle violation gracefully, or store a processed message table and skip duplicates.



Example DB-level approach: Wager table primary key on WagerId and UpsertWager stored proc uses IF NOT EXISTS or MERGE to prevent duplication.



Observability: metrics \& distributed tracing



Add OpenTelemetry for traces, metrics, and logs. Export to Jaeger/Zipkin/OTLP and Prometheus for metrics.





Error handling \& structured logging



Return structured API errors (standard error model) and ensure logs include correlation IDs.



Add middleware to generate and propagate traceId/correlationId for each request and include in outgoing RabbitMQ message headers.



Message schema \& contract management



Adopt a clear message contract/versioning strategy (e.g., JSON Schema or Avro) and store versions. Include version in message metadata.



Consider adding MessageId, PublishedAt, and CorrelationId fields in message headers.



Secure RabbitMQ



Use TLS and strong credentials for production RabbitMQ. Avoid guest/guest in prod.



Limit RabbitMQ permissions (least privilege) for the publisher/consumer accounts.



DB migrations



Adopt migration tooling (Flyway, Flyway for SQL Server, or EF Core Migrations if switching to EF) to version and apply database changes automatically in CI/CD.





Containerization \& orchestration



Add Dockerfile for the API and Consumer and a docker-compose.yml for local dev (API + Consumer + RabbitMQ + SQL Server).





Add Compose file with healthchecks for SQL and RabbitMQ.



CI/CD pipeline



Add a YAML pipeline (Azure DevOps or GitHub Actions) that runs linting, unit tests, builds docker images, pushes to registry, and runs DB migration step in staging.



Include environment-specific configuration and gated deploy to prod.



Retry / backoff improvements



Add exponential backoff with jitter to Polly retry policy to avoid thundering herd. E.g. WaitAndRetryAsync(retryCount, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)) + jitter)



Idempotency token \& dedupe TTL



Add a ProcessedMessages table with MessageId primary key and ProcessedAt timestamp. Use it to dedupe messages and optionally clean up old entries.



Input validation framework



Use FluentValidation to centralize DTO validation for the API (and consumer) with clear error messages and automatic ModelState integration.



Add rate-limiting / throttling



Use middleware (e.g. AspNetCoreRateLimit or custom token bucket) to protect the API from abuse.



Add unit and integration tests



Add more tests:



Unit tests for WagerService, WagerRepository (mock Dapper or use in-memory DB).



Integration tests for consumer connecting to local RabbitMQ and SQL in CI via ephemeral containers (Testcontainers).



Contract tests for message schema.



Low priority / nice-to-have



Batching \& bulk insert — for high throughput, consider batching writes to SQL or using a staging table and bulk-copy operations.



Event sourcing / CQRS — if analytics evolve, consider separating write model and read model for fast analytics queries.



Use a mapping library (Mapster or AutoMapper) if DTOs become more complex.



Improve API error responses (ProblemDetails) and OpenAPI docs with examples.



Admin / monitoring UI for DLQ messages, requeue, and replay.


**6) Challenges** 

Kept getting these messages when running the NBomb Tester project, purely because my machine couldn't spawn enough sockets quickly enough.

System.Net.Http.HttpRequestException: No connection could be made because the target machine actively refused it. (localhost:7120)
 ---> System.Net.Sockets.SocketException (10061): No connection could be made because the target machine actively refused it.
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.ThrowException(SocketError error, CancellationToken cancellationToken)
   at System.Net.Sockets.Socket.AwaitableSocketAsyncEventArgs.System.Threading.Tasks.Sources.IValueTaskSource.GetResult(Int16 token)
   at System.Net.Sockets.Socket.<ConnectAsync>g__WaitForConnectWithCancellation|285_0(AwaitableSocketAsyncEventArgs saea, ValueTask connectTask, CancellationToken cancellationToken)
   at System.Net.Http.HttpConnectionPool.ConnectToTcpHostAsync(String host, Int32 port, HttpRequestMessage initialRequest, Boolean async, CancellationToken cancellationToken)
   --- End of inner exception stack trace ---



