using hangfire;
using Hangfire;
using hangfire.Model;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHttpClient();
builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseInMemoryStorage()
);
builder.Services.AddHangfireServer();
builder.Services.Configure<HangFireConst>(
    builder.Configuration.GetSection("HangFire")
);
builder.Services.AddSingleton<OnHangFireSettingChange>();
// builder.Services.AddSingleton<StartupService>();
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();


app.UseHangfireDashboard();
app.Services.GetService<OnHangFireSettingChange>();

app.Run();
