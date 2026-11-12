var builder = DistributedApplication.CreateBuilder(args);

// API exposed on port 5085 for Nginx upstream (https://api.worldpinball.local)
var api = builder.AddProject<Projects.WorldPinball_Api>("api")
    .WithHttpEndpoint(port: 5085, name: "http");


// BFF exposed on port 5285 for Nginx upstream (https://bff.worldpinball.local)
var bff = builder.AddProject<Projects.WorldPinball_Bff>("bff")
    .WithHttpEndpoint(port: 5285, name: "http")
    .WithReference(api);

builder.Build().Run();
