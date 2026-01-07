var builder = DistributedApplication.CreateBuilder(args);

// API service
var api = builder.AddProject<Projects.Recommendation_Api>("recommendation-api")
    .WithExternalHttpEndpoints();

// React frontend
builder.AddNpmApp("recommendation-web", "../Recommendation.Web")
    .WithReference(api)
    .WithHttpEndpoint(targetPort: 5173, env: "VITE_PORT")
    .WithExternalHttpEndpoints()
    .PublishAsDockerFile();

builder.Build().Run();
