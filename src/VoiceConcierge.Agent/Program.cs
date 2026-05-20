using VoiceConcierge.Agent;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddVoiceConciergeAgent(builder.Configuration);
builder.Build().Run();
