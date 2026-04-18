// TheWatch.AppHost — Aspire Orchestration Entry Point
var builder = DistributedApplication.CreateBuilder(args);

// --- Infrastructure Resources ---
var sqlServer = builder.AddSqlServer("thewatch-sqlserver").AddDatabase("TheWatchDb");
var postgres = builder.AddPostgres("thewatch-postgresql").WithPgAdmin().AddDatabase("TheWatchSpatialDb");
var redis = builder.AddRedis("thewatch-redis").WithRedisInsight();
var cosmos = builder.AddAzureCosmosDB("thewatch-cosmos").RunAsEmulator().AddDatabase("TheWatchAuditDb");
var rabbitmq = builder.AddRabbitMQ("thewatch-rabbitmq").WithManagementPlugin();
var firestoreEmulator = builder.AddContainer("thewatch-firestore", "gcr.io/google.com/cloudsdktool/google-cloud-cli", "latest")
    .WithEntrypoint("gcloud").WithArgs("emulators", "firestore", "start", "--host-port=0.0.0.0:8080", "--project=thewatch-dev")
    .WithHttpEndpoint(port: 8080, targetPort: 8080, name: "firestore");

// --- Original & Agent-Delivered Projects ---
var dashboardApi = builder.AddProject<Projects.TheWatch_Dashboard_Api>("dashboard-api")
    .WithReference(sqlServer).WithReference(postgres).WithReference(redis).WithReference(cosmos).WithReference(rabbitmq)
    .WithReference(firestoreEmulator.GetEndpoint("firestore"))
    .WithEnvironment("DatabaseSettings__UseFirestoreEmulator", "true")
    .WithEnvironment("DatabaseSettings__FirestoreEmulatorHost", firestoreEmulator.GetEndpoint("firestore"))
    .WithEnvironment("DatabaseSettings__FirestoreProjectId", "thewatch-dev")
    .WithExternalHttpEndpoints();

builder.AddProject<Projects.TheWatch_Dashboard_Web>("dashboard-web")
    .WithReference(dashboardApi).WithReference(redis).WithExternalHttpEndpoints();

builder.AddProject<Projects.TheWatch_DocGen>("docgen-worker")
    .WithReference(rabbitmq).WithEnvironment("DocGen__SolutionRoot", builder.AppHostDirectory + "/..");

// --- New Microservices ---
var responseService = builder.AddProject<Projects.TheWatch_ResponseService>("responseservice")
    .WithReference(rabbitmq) // For future event publishing
    .WithReference(dashboardApi) // To call back into the main API
    .WithExternalHttpEndpoints();

builder.Build().Run();
