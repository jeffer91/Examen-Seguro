using Itsqmet.ExamService;
var builder=Host.CreateApplicationBuilder(args);builder.Services.AddWindowsService(o=>o.ServiceName="ITSQMET Exam Service");builder.Services.AddHostedService<Watchdog>();var host=builder.Build();host.Run();
