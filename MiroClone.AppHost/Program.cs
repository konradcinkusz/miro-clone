using MiroClone.AppHost;

var builder = DistributedApplication.CreateBuilder(args);
builder.AddMiroCloneEstate(Path.Combine(builder.AppHostDirectory, ".."));
builder.Build().Run();
