namespace Avalon.Client.SDL;

public class Program
{
    public static void Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console()
            .CreateLogger();

        bottlenoselabs.Interop.SDL.Initialize();
        SDL_image.Initialize();

        using Application app = new("Avalon", 1920, 1080);
        app.Run();
    }
}
