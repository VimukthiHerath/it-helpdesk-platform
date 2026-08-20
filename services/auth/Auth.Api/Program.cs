using Microsoft.EntityFrameworkCore;
using Auth.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        builder.Configuration.GetConnectionString("AuthDb"),
        ServerVersion.AutoDetect(builder.Configuration.GetConnectionString("AuthDb"))));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "Auth" }));
//Test comment 
app.Run();