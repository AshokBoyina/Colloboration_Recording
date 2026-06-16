// Handler moved to NICE.Platform.Collaboration.Infrastructure (same relative path).
// Application layer cannot reference Infrastructure — handlers that use CollaborationDbContext
// live in Infrastructure. MediatR scans both assemblies via AddInfrastructureServices().
