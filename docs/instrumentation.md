# Instrumentation

To monitor the performance of the server, you can use the `dotnet-counters` tool, which is part of the .NET Core diagnostics suite.
This tool allows you to collect performance metrics from your application in real-time.

```bash
dotnet tool install --global dotnet-counters
dotnet-counters ps
dotnet-counters monitor -p <PID> System.Runtime Avalon.Server.World
dotnet-counters collect -p <PID> --refresh-interval 1 --format csv -o idle.csv System.Runtime
```
